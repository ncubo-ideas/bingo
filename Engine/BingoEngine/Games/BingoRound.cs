using System;
using System.Collections.Generic;

namespace BingoEngine
{
    internal enum GameState
    {
        Waiting,
        Running,
        AwaitingValidation,
        Finished
    }

    internal enum DrawOutcome
    {
        Accepted,
        AlreadyDrawn,
        NotDrawn
    }

    internal enum ClaimPolicy
    {
        Automatic,
        OnClaim
    }

    internal sealed record PendingWin(CardNumber Card, string FigureName, int AtDraw);

    internal sealed class BingoRound
    {
        private readonly IReadOnlyList<WinPattern> _figures;
        private readonly DrawnSequence _drawn = new DrawnSequence();
        private readonly Dictionary<CardNumber, Cardface> _faces = new Dictionary<CardNumber, Cardface>();
        private readonly Dictionary<CardNumber, int> _activation = new Dictionary<CardNumber, int>();
        private readonly HashSet<string> _closed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _dismissed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CardNumber>> _winners =
            new Dictionary<string, List<CardNumber>>(StringComparer.Ordinal);
        private readonly HashSet<string> _validatedInThisPause = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<PendingWin> _pending = new List<PendingWin>();

        private readonly ClaimPolicy _claiming;

        private GameState _state = GameState.Running;

        internal BingoRound(
            IReadOnlyList<WinPattern> figures, IEnumerable<CardNumber> cardsInPlay, ClaimPolicy claiming)
        {
            if (figures is null) throw new ArgumentNullException(nameof(figures));
            if (figures.Count == 0) throw new BingoRuleException("A round needs at least one figure.");
            if (cardsInPlay is null) throw new ArgumentNullException(nameof(cardsInPlay));

            _figures = figures;
            _claiming = claiming;
            foreach (WinPattern figure in figures) _winners[figure.Name] = new List<CardNumber>();
            foreach (CardNumber card in cardsInPlay) Enrol(card, 0);
        }

        internal GameState State => _state;

        internal ClaimPolicy Claiming => _claiming;

        internal int DrawnCount => _drawn.Count;

        internal IReadOnlyList<PendingWin> PendingWins => _pending;

        internal IReadOnlyList<Ball> Drawn => _drawn.InOrder;

        internal IReadOnlyList<string> FigureNames
        {
            get
            {
                List<string> names = new List<string>();
                foreach (WinPattern figure in _figures) names.Add(figure.Name);
                return names;
            }
        }

        internal bool IsClosed(string figureName) => _closed.Contains(Figure(figureName).Name);

        internal void AddCard(CardNumber card)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            if (_activation.ContainsKey(card)) return;

            Enrol(card, _drawn.Count);
        }

        internal void RemoveCard(CardNumber card)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            _activation.Remove(card);
            _faces.Remove(card);

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].Card.Equals(card)) _pending.RemoveAt(i);
            }
        }

        internal DrawOutcome Draw(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));

            if (_state != GameState.Running)
            {
                throw new BingoRuleException("Balls are drawn only while the round is running.");
            }

            if (!_drawn.Append(ball)) return DrawOutcome.AlreadyDrawn;

            if (_claiming == ClaimPolicy.Automatic) Detect();

            if (_state == GameState.Running && _drawn.Remaining == 0) _state = GameState.Finished;

            return DrawOutcome.Accepted;
        }

        internal DrawOutcome Correct(Ball wrong, Ball right)
        {
            if (wrong is null) throw new ArgumentNullException(nameof(wrong));
            if (right is null) throw new ArgumentNullException(nameof(right));

            if (_state == GameState.Finished)
            {
                throw new BingoRuleException("A finished round cannot be corrected.");
            }

            if (HasWinners())
            {
                throw new BingoRuleException(
                    "A winner was already validated; this round cannot be corrected.");
            }

            if (!_drawn.Contains(wrong)) return DrawOutcome.NotDrawn;
            if (_drawn.Contains(right)) return DrawOutcome.AlreadyDrawn;

            _drawn.Replace(wrong, right);

            _pending.Clear();
            _validatedInThisPause.Clear();
            _state = GameState.Running;
            if (_claiming == ClaimPolicy.Automatic) Detect();

            if (_state == GameState.Running && _drawn.Remaining == 0) _state = GameState.Finished;

            return DrawOutcome.Accepted;
        }

        internal void End()
        {
            if (_state == GameState.Finished) return;

            _pending.Clear();
            _validatedInThisPause.Clear();
            _state = GameState.Finished;
        }

        internal void Validate(CardNumber card, string figureName)
        {
            WinPattern figure = RequirePending(card, figureName);

            _winners[figure.Name].Add(card);
            _validatedInThisPause.Add(figure.Name);
            ResolvePauseIfDone();
        }

        internal void Dismiss(CardNumber card, string figureName)
        {
            WinPattern figure = RequirePending(card, figureName);

            _dismissed.Add(KeyOf(card, figure.Name));
            ResolvePauseIfDone();
        }

        internal bool ClaimBingo(CardNumber card, string figureName)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            WinPattern figure = Figure(figureName);

            if (!_activation.ContainsKey(card)) return false;
            if (_closed.Contains(figure.Name)) return false;
            if (_dismissed.Contains(KeyOf(card, figure.Name))) return false;
            if (_winners[figure.Name].Contains(card)) return false;

            if (!IsWinner(card, figure)) return false;

            if (_claiming == ClaimPolicy.OnClaim
                && _state != GameState.Finished
                && !AlreadyPending(card, figure.Name))
            {
                _pending.Add(new PendingWin(card, figure.Name, CompletedAt(card, figure)));
                _state = GameState.AwaitingValidation;
            }

            return true;
        }

        private void Enrol(CardNumber card, int activationPoint)
        {
            _activation[card] = activationPoint;
            _faces[card] = Cardface.Of(card);
        }

        private void Detect()
        {
            foreach (WinPattern figure in _figures)
            {
                if (_closed.Contains(figure.Name)) continue;

                foreach (KeyValuePair<CardNumber, int> enrolled in _activation)
                {
                    CardNumber card = enrolled.Key;

                    if (_dismissed.Contains(KeyOf(card, figure.Name))) continue;
                    if (_winners[figure.Name].Contains(card)) continue;
                    if (AlreadyPending(card, figure.Name)) continue;
                    if (!IsWinner(card, figure)) continue;

                    _pending.Add(new PendingWin(card, figure.Name, CompletedAt(card, figure)));
                }
            }

            if (_pending.Count > 0) _state = GameState.AwaitingValidation;
        }

        private int CompletedAt(CardNumber card, WinPattern figure)
        {
            Cardface face = _faces[card];

            for (int upTo = _activation[card] + 1; upTo <= _drawn.Count; upTo++)
            {
                if (figure.CompletesWithin(face, _drawn, upTo)) return upTo;
            }

            return _drawn.Count;
        }

        internal bool WasDismissed(CardNumber card, string figureName)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            return _dismissed.Contains(KeyOf(card, Figure(figureName).Name));
        }

        internal bool HasCompleted(CardNumber card, string figureName)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            WinPattern figure = Figure(figureName);

            if (!_activation.ContainsKey(card)) return false;

            return IsWinner(card, figure);
        }

        private bool IsWinner(CardNumber card, WinPattern figure)
        {
            Cardface face = _faces[card];

            if (!figure.Completes(face, _drawn)) return false;

            return !figure.CompletesWithin(face, _drawn, _activation[card]);
        }

        private bool AlreadyPending(CardNumber card, string figureName)
        {
            foreach (PendingWin pending in _pending)
            {
                if (pending.Card.Equals(card) && string.Equals(pending.FigureName, figureName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private WinPattern RequirePending(CardNumber card, string figureName)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            if (_state != GameState.AwaitingValidation)
            {
                throw new BingoRuleException("There is no pending validation.");
            }

            WinPattern figure = Figure(figureName);

            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Card.Equals(card)
                    && string.Equals(_pending[i].FigureName, figure.Name, StringComparison.Ordinal))
                {
                    _pending.RemoveAt(i);
                    return figure;
                }
            }

            throw new BingoRuleException(
                "Card " + card + " is not a pending winner of " + figure.Name + ".");
        }

        private void ResolvePauseIfDone()
        {
            if (_pending.Count > 0) return;

            foreach (string figureName in _validatedInThisPause) _closed.Add(figureName);
            _validatedInThisPause.Clear();

            if (_closed.Count == _figures.Count || _drawn.Remaining == 0)
            {
                _state = GameState.Finished;
                return;
            }

            _state = GameState.Running;
        }

        internal WinPattern Figure(string figureName)
        {
            if (string.IsNullOrWhiteSpace(figureName))
            {
                throw new ArgumentException("A figure name is required.", nameof(figureName));
            }

            foreach (WinPattern figure in _figures)
            {
                if (string.Equals(figure.Name, figureName, StringComparison.Ordinal)) return figure;
            }

            throw new BingoRuleException("Figure '" + figureName + "' is not in play this round.");
        }

        private bool HasWinners()
        {
            foreach (KeyValuePair<string, List<CardNumber>> figure in _winners)
            {
                if (figure.Value.Count > 0) return true;
            }

            return false;
        }

        private static string KeyOf(CardNumber card, string figureName) => card.Value + "|" + figureName;
    }
}
