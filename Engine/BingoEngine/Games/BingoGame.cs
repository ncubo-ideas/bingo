using System;
using System.Collections.Generic;

namespace BingoEngine
{
    public sealed record PendingWinner(long Card, string Figure, int AtDraw);

    public sealed record GameStateSnapshot(
        string State,
        int RoundNumber,
        IReadOnlyList<int> Drawn,
        IReadOnlyList<string> FiguresInPlay,
        IReadOnlyList<string> ClosedFigures,
        string Claiming);

    public sealed class BingoGame
    {
        private static readonly IReadOnlyList<int> NoDraws = new List<int>();
        private static readonly IReadOnlyList<string> NoFigures = new List<string>();
        private static readonly IReadOnlyList<PendingWinner> NoPendingWinners = new List<PendingWinner>();

        private readonly List<CardNumber> _cardsInPlay = new List<CardNumber>();
        private readonly Dictionary<CardNumber, Cardface> _faces = new Dictionary<CardNumber, Cardface>();
        private BingoRound _round;
        private int _roundNumber;

        public string State => _round is null ? GameState.Waiting.ToString() : _round.State.ToString();

        /// <summary>
        /// El pulso de la mesa: todo lo que puede cambiar lo que se ve, en un solo dato.
        ///
        /// Existe para que quien mira la pantalla pregunte BARATO antes de preguntar caro.
        /// Sin esto hay que traerse las bolitas una por una, los cartones uno por uno y el
        /// progreso de cada figura -- decenas de consultas -- solo para descubrir que no
        /// habia pasado nada, que es lo normal: entre bolita y bolita pasan quince segundos
        /// y el reloj late cada uno.
        ///
        /// Lleva los NUMEROS de los cartones y no cuantos son: cambiar un carton por otro
        /// no mueve el conteo, y con una marca que solo contara, la pantalla se quedaria
        /// mostrando el carton que ya no es de esa persona hasta la proxima bolita.
        /// </summary>
        public string Pulse
        {
            get
            {
                if (_round is null) return "0|" + State + "|" + _cardsInPlay.Count;

                List<string> numbers = new List<string>(_cardsInPlay.Count);
                foreach (CardNumber card in _cardsInPlay) numbers.Add(card.Value.ToString());

                return _roundNumber
                    + "|" + State
                    + "|" + _round.DrawnCount
                    + "|" + string.Join(",", numbers)
                    + "|" + string.Join(",", _round.FigureNames)
                    + "|" + _round.PendingWins.Count;
            }
        }

        public int RoundNumber => _roundNumber;

        public int CardsInPlayCount => _cardsInPlay.Count;

        public int DrawnCount => _round is null ? 0 : _round.DrawnCount;

        public IReadOnlyList<int> DrawnNumbers
        {
            get
            {
                if (_round is null) return NoDraws;

                List<int> numbers = new List<int>();
                foreach (Ball ball in _round.Drawn) numbers.Add(ball.Number);
                return numbers;
            }
        }

        /// <summary>
        /// La mesa esta sonando: hay ronda y esta corriendo.
        ///
        /// Existe porque preguntar por State y comparar el texto afuera es leer el nombre de
        /// un enum interno desde otro lado. Quien pregunta quiere saber si puede cantar, no
        /// como se llama el estado.
        /// </summary>
        public bool IsRunning =>
            _round is not null && _round.State == GameState.Running;

        /// <summary>
        /// La mesa se freno sola porque alguien completo su figura.
        ///
        /// Va aparte de IsRunning y no es lo mismo "no esta sonando" que "esta frenada
        /// esperando": el que pregunta tiene que poder decirle a la persona cual de las dos
        /// es. A una se le contesta "arranca la ronda que sigue"; a la otra, arrancar la
        /// ronda siguiente es lo PEOR que puede hacer -- se lleva puesto el premio de
        /// alguien que lo tiene ganado y esta esperando.
        /// </summary>
        public bool IsAwaitingValidation =>
            _round is not null && _round.State == GameState.AwaitingValidation;

        /// <summary>
        /// Si esta bolita se puede cantar AHORA.
        ///
        /// Contesta donde RegisterDraw tira, y para eso existe: cuando el canto llega
        /// reenviado desde el telefono de quien canta, la pregunta se hace ANTES de tomar el
        /// candado de escritura, asi un "no" no deja asiento. Un canto rechazado que dejara
        /// entrada seria una bolita fantasma en el diario de la velada.
        ///
        /// No mira la ronda: la ronda la resuelve el actor al cargar los argumentos, asi que
        /// del lado de aca siempre es la que esta jugando.
        /// </summary>
        public bool CanRegisterDraw(int number)
        {
            if (!IsRunning) return false;
            if (number < Cardface.LowestBall || number > Cardface.HighestBall) return false;

            foreach (Ball ball in _round.Drawn)
                if (ball.Number == number) return false;

            return true;
        }

        /// <summary>
        /// Si esta correccion se puede hacer AHORA: la mala salio y la buena todavia no.
        /// Mismo motivo que CanRegisterDraw -- contesta donde CorrectDraw tira.
        /// </summary>
        public bool CanCorrectDraw(int wrongNumber, int rightNumber)
        {
            if (!IsRunning) return false;
            if (wrongNumber == rightNumber) return false;
            if (rightNumber < Cardface.LowestBall || rightNumber > Cardface.HighestBall) return false;

            bool wrongCameOut = false;

            foreach (Ball ball in _round.Drawn)
            {
                if (ball.Number == rightNumber) return false;
                if (ball.Number == wrongNumber) wrongCameOut = true;
            }

            return wrongCameOut;
        }

        public IReadOnlyList<string> FiguresInPlay => _round is null ? NoFigures : _round.FigureNames;

        public int FiguresInPlayCount => FiguresInPlay.Count;

        public string FigureInPlayAt(int position)
        {
            IReadOnlyList<string> figures = FiguresInPlay;

            if (position < 1 || position > figures.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "only " + figures.Count + " figures are in play");
            }

            return figures[position - 1];
        }

        public string Claiming =>
            _round is null ? ClaimPolicy.Automatic.ToString() : _round.Claiming.ToString();

        public IReadOnlyList<PendingWinner> PendingWinners
        {
            get
            {
                if (_round is null) return NoPendingWinners;

                List<PendingWinner> winners = new List<PendingWinner>();
                foreach (PendingWin pending in _round.PendingWins)
                {
                    winners.Add(new PendingWinner(pending.Card.Value, pending.FigureName, pending.AtDraw));
                }

                return winners;
            }
        }

        public bool IsFigureClosed(string figureName) => RequireRound().IsClosed(figureName);

        public bool HasCompleted(long cardNumber, string figureName)
        {
            if (!BingoGame.KnowsFigure(figureName))
            {
                throw new BingoRuleException("Unknown figure '" + figureName + "'.");
            }

            return _round is not null && _round.HasCompleted(new CardNumber(cardNumber), figureName);
        }

        public bool WasDismissed(long cardNumber, string figureName)
        {
            if (!BingoGame.KnowsFigure(figureName))
            {
                throw new BingoRuleException("Unknown figure '" + figureName + "'.");
            }

            return _round is not null && _round.WasDismissed(new CardNumber(cardNumber), figureName);
        }

        public bool IsPendingWinner(long cardNumber, string figureName)
        {
            foreach (PendingWinner pending in PendingWinners)
            {
                if (pending.Card == cardNumber
                    && string.Equals(pending.Figure, figureName, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        public int WinningDrawOf(long cardNumber, string figureName)
        {
            foreach (PendingWinner pending in PendingWinners)
            {
                if (pending.Card == cardNumber
                    && string.Equals(pending.Figure, figureName, StringComparison.Ordinal)) return pending.AtDraw;
            }

            return 0;
        }

        public IReadOnlyList<int> CardColumn(long cardNumber, int band) =>
            new List<int>(FaceInPlay(cardNumber).Column(band));

        public IReadOnlyList<int> CardMarks(long cardNumber)
        {
            Cardface face = FaceInPlay(cardNumber);
            List<int> marks = new List<int>();

            if (_round is null) return marks;

            foreach (Ball ball in _round.Drawn)
            {
                if (face.Has(ball.Number)) marks.Add(ball.Number);
            }

            return marks;
        }

        public static IReadOnlyList<string> KnownFigures
        {
            get
            {
                List<string> names = new List<string>();
                foreach (WinPattern pattern in WinPatterns.Catalog.ByPopularity) names.Add(pattern.Name);

                return names;
            }
        }

        public static bool KnowsFigure(string figureName)
        {
            foreach (string known in KnownFigures)
            {
                if (string.Equals(known, figureName, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        public static int AlternativeCount(string figureName) =>
            WinPatterns.Catalog.ByName(figureName).Alternatives.Count;

        public static IReadOnlyList<int> ClosestFor(
            long cardNumber, string figureName, IEnumerable<int> drawn)
        {
            Cardface face = Cardface.Of(new CardNumber(cardNumber));

            HashSet<int> alreadyDrawn = new HashSet<int>();
            if (drawn != null) foreach (int ball in drawn) alreadyDrawn.Add(ball);

            IReadOnlyList<int> best = null;
            int bestCovered = -1;

            foreach (IReadOnlyList<Cell> alternative in WinPatterns.Catalog.ByName(figureName).Alternatives)
            {
                List<int> numbers = new List<int>();
                int covered = 0;

                foreach (Cell cell in alternative)
                {
                    if (cell.IsFreeCenter) continue;

                    int number = face.NumberAt(cell);
                    numbers.Add(number);
                    if (alreadyDrawn.Contains(number)) covered++;
                }

                if (covered > bestCovered)
                {
                    bestCovered = covered;
                    best = numbers;
                }
            }

            return best ?? new List<int>();
        }

        public int MarkedCount(long cardNumber) => CardMarks(cardNumber).Count;

        public int DrawnAt(int position)
        {
            IReadOnlyList<int> drawn = DrawnNumbers;

            if (position < 1 || position > drawn.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "only " + drawn.Count + " balls have been drawn");
            }

            return drawn[position - 1];
        }

        public long CardInPlayAt(int position)
        {
            if (position < 1 || position > _cardsInPlay.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position,
                    "only " + _cardsInPlay.Count + " cards are in play");
            }

            return _cardsInPlay[position - 1].Value;
        }

        public IReadOnlyList<long> NumbersInPlay()
        {
            List<long> playing = new List<long>(_cardsInPlay.Count);

            foreach (CardNumber card in _cardsInPlay) playing.Add(card.Value);

            return playing;
        }

        public GameStateSnapshot Snapshot()
        {
            if (_round is null)
            {
                return new GameStateSnapshot(
                    GameState.Waiting.ToString(), 0, NoDraws, NoFigures, NoFigures, Claiming);
            }

            List<string> closed = new List<string>();
            foreach (string figureName in _round.FigureNames)
            {
                if (_round.IsClosed(figureName)) closed.Add(figureName);
            }

            return new GameStateSnapshot(
                _round.State.ToString(), _roundNumber, DrawnNumbers, _round.FigureNames, closed, Claiming);
        }

        public bool IsInPlay(long cardNumber) => _cardsInPlay.Contains(new CardNumber(cardNumber));

        public void PutCardInPlay(long cardNumber)
        {
            CardNumber card = new CardNumber(cardNumber);

            if (_cardsInPlay.Contains(card)) return;

            _cardsInPlay.Add(card);
            _faces[card] = Cardface.Of(card);

            if (_round is null) return;
            if (_round.State == GameState.Finished) return;

            _round.AddCard(card);
        }

        public CardNumber CardInPlay(long cardNumber)
        {
            CardNumber card = new CardNumber(cardNumber);

            if (!_cardsInPlay.Contains(card))
                throw new BingoRuleException("Card " + card + " is not in play.");

            return card;
        }

        public WinPattern FigureInPlay(string figureName) => RequireRound().Figure(figureName);

        public void TakeCardOutOfPlay(CardNumber card)
        {
            if (card is null) throw new ArgumentNullException(nameof(card));

            if (!_cardsInPlay.Contains(card))
                throw new BingoRuleException("Card " + card + " is not in play.");

            if (_round is not null && _round.DrawnCount > 0)
            {
                throw new BingoRuleException(
                    "Card " + card + " cannot leave a round that has already started: "
                    + _round.DrawnCount + " ball(s) have been drawn.");
            }

            _cardsInPlay.Remove(card);
            _faces.Remove(card);

            _round?.RemoveCard(card);
        }

        public void StartRound(IEnumerable<string> figureNames) =>
            StartRound(figureNames, ClaimPolicy.Automatic.ToString());

        public void StartRound(IEnumerable<string> figureNames, string claimPolicy)
        {
            if (figureNames is null) throw new ArgumentNullException(nameof(figureNames));

            if (_round is not null && _round.State != GameState.Finished)
            {
                throw new BingoRuleException("Finish the current round before starting another.");
            }

            ClaimPolicy claiming = PolicyFrom(claimPolicy);

            List<WinPattern> figures = new List<WinPattern>();
            foreach (string figureName in figureNames)
            {
                WinPattern figure = WinPatterns.Catalog.ByName(figureName);
                if (!figures.Contains(figure)) figures.Add(figure);
            }

            _round = new BingoRound(figures, _cardsInPlay, claiming);
            _roundNumber++;
        }

        public void MoveOnToRound(string playing, string claimPolicy)
        {
            if (string.IsNullOrWhiteSpace(playing))
                throw new BingoRuleException("A round needs at least one figure.");

            List<string> figureNames = new List<string>();
            foreach (string piece in playing.Split(','))
            {
                string name = piece.Trim();
                if (name.Length != 0) figureNames.Add(name);
            }

            foreach (string name in figureNames) WinPatterns.Catalog.ByName(name);
            PolicyFrom(claimPolicy);

            _round?.End();
            StartRound(figureNames, claimPolicy);
        }

        private static ClaimPolicy PolicyFrom(string claimPolicy)
        {
            if (string.IsNullOrWhiteSpace(claimPolicy))
            {
                throw new ArgumentException("A claim policy is required.", nameof(claimPolicy));
            }

            if (!Enum.TryParse(claimPolicy, false, out ClaimPolicy claiming)
                || !Enum.IsDefined(typeof(ClaimPolicy), claiming))
            {
                throw new BingoRuleException("Unknown claim policy '" + claimPolicy + "'.");
            }

            return claiming;
        }

        public void EndRound() => RequireRound().End();

        public void RegisterDraw(int number) => RequireRound().Draw(new Ball(number));

        /// <summary>
        /// La misma bolita, diciendo a que ronda pertenece. No es adorno: una ronda nueva
        /// empieza con la bolsa limpia, asi que el 7 de la ronda 2 es un evento distinto
        /// del 7 de la ronda 1, y sin la ronda los dos asientos quedan identicos. Aguas
        /// abajo eso se lee como si alguien repitiera lo mismo dos veces, y la segunda se
        /// descarta sin decir nada.
        /// De paso ataja lo que antes entraba mudo: un canto que sale de una pantalla
        /// vieja, cuando la ronda que se veia ahi ya se cerro.
        /// </summary>
        public void RegisterDraw(int number, int round)
        {
            if (round != _roundNumber)
            {
                throw new BingoRuleException(
                    "Ball " + number + " belongs to round " + round
                    + ", but round " + _roundNumber + " is the one playing.");
            }

            RequireRound().Draw(new Ball(number));
        }

        public void CorrectDraw(int wrongNumber, int rightNumber)
        {
            DrawOutcome outcome = RequireRound().Correct(new Ball(wrongNumber), new Ball(rightNumber));

            if (outcome == DrawOutcome.NotDrawn)
            {
                throw new BingoRuleException("Ball " + wrongNumber + " never came out.");
            }

            if (outcome == DrawOutcome.AlreadyDrawn)
            {
                throw new BingoRuleException("Ball " + rightNumber + " already came out.");
            }
        }

        /// <summary>
        /// La misma correccion, diciendo en que ronda se hizo. Por lo mismo que la bolita:
        /// equivocarse dos veces igual --teclear el 46 en vez del 45 en la ronda 1 y volver
        /// a hacerlo en la ronda 3-- son dos hechos distintos, y sin la ronda los dos
        /// asientos quedan identicos. Aguas abajo el segundo se lee como una repeticion del
        /// primero, se descarta, y a la gente le queda el numero malo marcado en el carton.
        /// </summary>
        public void CorrectDraw(int wrongNumber, int rightNumber, int round)
        {
            if (round != _roundNumber)
            {
                throw new BingoRuleException(
                    "The correction belongs to round " + round
                    + ", but round " + _roundNumber + " is the one playing.");
            }

            CorrectDraw(wrongNumber, rightNumber);
        }

        public bool ClaimBingo(CardNumber card, WinPattern figure) =>
            RequireRound().ClaimBingo(card, NameOf(figure));

        public void ValidateWinner(CardNumber card, WinPattern figure) =>
            RequireRound().Validate(card, NameOf(figure));

        public void DismissWinner(CardNumber card, WinPattern figure) =>
            RequireRound().Dismiss(card, NameOf(figure));

        private static string NameOf(WinPattern figure) =>
            figure?.Name ?? throw new ArgumentNullException(nameof(figure));

        private Cardface FaceInPlay(long cardNumber)
        {
            CardNumber card = new CardNumber(cardNumber);

            if (!_faces.TryGetValue(card, out Cardface face))
            {
                throw new BingoRuleException("Card " + card + " is not in play.");
            }

            return face;
        }

        private BingoRound RequireRound()
        {
            if (_round is null) throw new BingoRuleException("No round has been started.");

            return _round;
        }
    }
}
