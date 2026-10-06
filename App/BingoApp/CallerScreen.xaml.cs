using System.Text;
using System.Text.Json;
using BingoApp.CommunityLeader;

namespace BingoApp;

/// <summary>
/// La noche en marcha. La pantalla es una pagina (caller.html) y este code-behind es el
/// puente: le manda el retrato de la mesa y traduce cada gesto a los metodos de
/// LiveCommunityLeader que ya existian.
/// </summary>
public partial class CallerScreen : ContentPage
{
    private readonly StringBuilder _log = new();
    private readonly string _festivalId;

    private LiveCommunityLeader _communityLeader;
    private IDispatcherTimer _clock;

    /// <summary>La firma de lo ultimo que se mando. El reloj late cada segundo y el
    /// retrato es grande: sin esto la pagina se repinta entera 60 veces por minuto
    /// aunque no haya pasado nada.</summary>
    private string _lastSent;

    /// <summary>El ultimo pulso que se leyo. Mientras no cambie, no hay nada que mirar.</summary>
    private string _lastPulse;

    public CallerScreen(string festivalId)
    {
        InitializeComponent();
        _festivalId = festivalId;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _communityLeader = LiveCommunityLeader.Instance;
        _communityLeader.TalkTo(Say);
        _communityLeader.Changed += OnChanged;

        // Un BINGO se completa en el telefono del jugador y llega por el diario, sin
        // pasar por Changed: hay que ir a mirar.
        _clock = Dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => Refresh();
        _clock.Start();

        Refresh();
    }

    protected override void OnDisappearing()
    {
        _clock?.Stop();
        if (_communityLeader is not null) _communityLeader.Changed -= OnChanged;
        base.OnDisappearing();
    }

    private void OnChanged() => Refresh();

    private void Refresh() => Task.Run(() =>
    {
        try { PushState(); } catch (Exception failure) { Failed(failure); }
    });

    // ── lo que la pagina le pide al telefono ─────────────────────────────────

    private void OnWebMessage(object sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        string[] parts = (e.Message ?? string.Empty).Split('|');

        switch (parts[0])
        {
            case "ready":
                _lastSent = _lastPulse = null;
                PushTextos();
                PushFigures();
                Refresh();
                break;

            case "idioma":
                if (parts.Length > 1) AppLanguage.Choose(parts[1]);
                _lastSent = _lastPulse = null;
                PushTextos();
                PushFigures();
                Refresh();
                PushLog();
                break;

            case "leave":
                OnScreen(async () => await Navigation.PopAsync());
                break;

            case "call":
                if (parts.Length > 1 && int.TryParse(parts[1], out int number)) Call(number);
                break;

            case "correct":
                if (parts.Length > 2
                    && int.TryParse(parts[1], out int wrong)
                    && int.TryParse(parts[2], out int right)) Correct(wrong, right);
                break;

            case "prize":
                if (parts.Length > 2) Verdict(parts[1], parts[2], true);
                break;

            case "wrong":
                if (parts.Length > 2) Verdict(parts[1], parts[2], false);
                break;

            case "settle":
                Settle();
                break;

            case "next":
                NextRound();
                break;

            case "close":
                CloseFestival();
                break;

            case "grant":
                if (parts.Length > 1 && int.TryParse(parts[1], out int toGrant)) Grant(toGrant);
                break;

            case "refuse":
                if (parts.Length > 1 && int.TryParse(parts[1], out int toRefuse)) RefusePlea(toRefuse);
                break;

            case "wantlog":
                PushLog();
                break;
        }
    }

    private void Call(int number) => Task.Run(() =>
    {
        try
        {
            _communityLeader.Call(_festivalId, number);
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-cantar-el", ("numero", number.ToString()), ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void Correct(int wrong, int right) => Task.Run(() =>
    {
        try
        {
            _communityLeader.CorrectTheDraw(_festivalId, wrong, right);
            PushToast(Texts.T("organizador.log.listo-era-el", ("numero", right.ToString())));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-corregir-el", ("numero", wrong.ToString()), ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void Verdict(string card, string figure, bool giveIt) => Task.Run(() =>
    {
        if (!long.TryParse(card, out long number)) return;

        try
        {
            if (giveIt) _communityLeader.GiveThePrize(_festivalId, number, figure);
            else _communityLeader.TheCardWasWrong(_festivalId, number, figure);

            _lastSent = _lastPulse = null;
            PushState();
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-resolver-bingo", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void Settle() => Task.Run(async () =>
    {
        try
        {
            await _communityLeader.SettleTheRoundAsync(_festivalId);
            _lastSent = _lastPulse = null;
            PushState();
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-cerrar-ronda", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void NextRound() => Task.Run(async () =>
    {
        try
        {
            await _communityLeader.BeginTheNextRoundAsync(_festivalId);
            _lastSent = _lastPulse = null;
            PushState();
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-arrancar-ronda", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void CloseFestival() => Task.Run(async () =>
    {
        try
        {
            await _communityLeader.CloseTheFestivalAsync(_festivalId);
            _lastSent = _lastPulse = null;
            PushState();
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-terminar-bingo", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void Grant(int plea) => Task.Run(async () =>
    {
        try
        {
            await _communityLeader.GrantAsync(_festivalId, plea);
            PushToast(Texts.T("organizador.log.anulada"));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-anular", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void RefusePlea(int plea) => Task.Run(() =>
    {
        try
        {
            _communityLeader.Refuse(_festivalId, plea);
            PushToast(Texts.T("organizador.log.venta-queda"));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-resolverlo", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

    private void PushState()
    {
        // Primero lo barato. El reloj late cada segundo, pero entre bolita y bolita pasan
        // quince: lo normal es que no haya pasado nada. Antes se armaba el retrato ENTERO
        // --las bolitas una por una, el progreso de cada carton por cada figura-- y recien
        // al final se comparaba con el anterior para tirarlo a la basura. Cuanto mas
        // avanzaba la noche, mas caro salia ese descubrimiento, y justo en el telefono del
        // que esta cantando.
        string pulse = _communityLeader.Pulse(_festivalId);

        if (string.Equals(pulse, _lastPulse, StringComparison.Ordinal)) return;

        _lastPulse = pulse;

        IReadOnlyList<int> drawn = _communityLeader.Drawn(_festivalId);
        IReadOnlyList<RoundStatus> rounds = _communityLeader.RoundsOf(_festivalId);
        RoundStatus current = rounds.FirstOrDefault(r => r.IsPlaying);
        bool over = _communityLeader.IsOver(_festivalId);
        bool legacy = rounds.Count == 0;

        var board = _communityLeader.Progress(_festivalId, drawn);

        IReadOnlyList<(int Plea, long Card, string Who)> pleas;
        try { pleas = _communityLeader.PleasWaiting(_festivalId); }
        catch (Exception) { pleas = Array.Empty<(int, long, string)>(); }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "state");
            json.WriteString("name", NameOf());

            json.WriteString("roundLine", over ? Texts.T("cantar.cabecera.termino")
                : current is not null
                    ? Texts.T("cantar.cabecera.ronda", ("n", current.Position.ToString())) + "  ·  " + current.PrizeLine
                    : legacy ? Texts.T("cantar.cabecera.se-juega", ("figuras", string.Join(", ", _communityLeader.FiguresOf(_festivalId))))
                    : Texts.T("cantar.cabecera.entre-rondas"));

            // La ronda con su total, para la cabecera. NO va "juego X de Y" como en la
            // maqueta: en este bingo los juegos de una ronda corren A LA VEZ sobre la
            // misma tombola, asi que numerarlos diria algo que no pasa. Lo que si es
            // cierto es cuantos siguen abiertos.
            json.WriteNumber("roundAt", current?.Position ?? 0);
            json.WriteNumber("roundCount", rounds.Count);

            json.WriteBoolean("playing", (current is not null || legacy) && !over);
            json.WriteBoolean("over", over);
            json.WriteBoolean("anyPlanned", rounds.Any(r => r.IsPlanned));
            json.WriteBoolean("anyWinner", current is not null && current.WinnerCount > 0);

            json.WriteString("owed", current is null || current.EveryPrizeWasGiven
                ? null
                : string.Join(", ", current.Promises
                    .Where(p => !p.WasKept)
                    .Select(p => OnOffer.Called(p.Playing) + " (" + p.PrizeText + ")")));

            // Lo que se esta jugando AHORA, figura por figura. La pantalla dibuja cada
            // una con su premio, asi que no sirve la linea ya armada: van las partes.
            // Al ganador se le dice por su nombre y no por el numero de carton -- el
            // numero lo tiene el tablero de abajo, el nombre es lo que se grita.
            json.WriteStartArray("games");
            foreach (PromiseStatus promise in current?.Promises ?? Array.Empty<PromiseStatus>())
            {
                json.WriteStartObject();
                json.WriteString("figure", promise.Playing);
                json.WriteString("prize", promise.Prize);
                json.WriteBoolean("cash", promise.IsCash);

                json.WriteStartArray("winners");
                foreach (long card in promise.Winners)
                {
                    string who = board.FirstOrDefault(r => r.Card == card).Who;
                    json.WriteStringValue(string.IsNullOrWhiteSpace(who) ? Texts.T("cantar.ganador.carton", ("n", card.ToString())) : who);
                }
                json.WriteEndArray();

                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("drawn");
            foreach (int n in drawn) json.WriteNumberValue(n);
            json.WriteEndArray();

            json.WriteNumber("last", drawn.Count > 0 ? drawn[^1] : 0);

            json.WriteStartArray("board");
            foreach ((string who, long card, string figure, int have, int need,
                bool done, bool dismissed, bool closed) in board)
            {
                json.WriteStartObject();
                json.WriteString("who", who);
                json.WriteNumber("card", card);
                json.WriteString("figure", figure);
                json.WriteNumber("have", have);
                json.WriteNumber("need", need);
                json.WriteBoolean("done", done);
                json.WriteBoolean("dismissed", dismissed);
                json.WriteBoolean("closed", closed);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            // Los que siguen esperando al padre: ni los ya rechazados ni una figura que ya
            // entrego. Es lo que prende el letrero.
            //
            // Se cuentan TODOS aunque el letrero muestre uno. Dos cartones que completan la
            // misma fila con la misma bolita es lo normal en una parroquia, y hasta ahora el
            // segundo no se veia en ningun lado: el padre entregaba el premio creyendo que
            // habia un solo ganador. Ahora al menos sabe que hay mas y que despues de este le
            // va a aparecer el siguiente.
            var everyoneWaiting = board
                .Where(r => r.Done && !r.Dismissed && !r.FigureClosed)
                .ToList();

            // Quien CANTO desde su telefono pasa al frente. El tablero ya sabia quienes
            // completaron, pero no cual de ellos esta parado reclamando: con dos cartones
            // completos el letrero mostraba al primero de la lista, que podia ser alguien
            // que ni se dio cuenta. La mano levantada manda; si nadie canto, el orden es
            // el de siempre.
            IReadOnlyList<LiveCommunityLeader.Shout> shouts =
                _communityLeader.ShoutsWaiting(_festivalId);

            var waiting = everyoneWaiting
                .OrderByDescending(r => shouts.Any(s => s.Card == r.Card
                    && string.Equals(s.Figure, r.Figure, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault();

            json.WriteNumber("waiting", everyoneWaiting.Count);

            if (waiting.Figure is null) json.WriteNull("claim");
            else
            {
                LiveCommunityLeader.Shout shouted = shouts.FirstOrDefault(s =>
                    s.Card == waiting.Card
                    && string.Equals(s.Figure, waiting.Figure, StringComparison.OrdinalIgnoreCase));

                json.WriteStartObject("claim");
                json.WriteString("who", waiting.Who);
                json.WriteNumber("card", waiting.Card);
                json.WriteString("figure", waiting.Figure);
                json.WriteString("prize", current?.Promises
                    .FirstOrDefault(p => string.Equals(p.Playing, waiting.Figure,
                        StringComparison.OrdinalIgnoreCase))?.PrizeText ?? string.Empty);

                // La diferencia entre "lo vi yo" y "me estan cantando": la pantalla lo
                // dice distinto, porque es una persona esperando y no un hallazgo.
                json.WriteBoolean("shouted", shouted is not null);
                json.WriteEndObject();
            }

            // Los que cantaron y todavia no llegaron al letrero: si alguien canta con un
            // carton que la mesa NO da por completo, esto es lo unico que lo hace visible
            // -- y es justo el caso que hay que poder mirar de frente.
            json.WriteStartArray("shouts");
            foreach (LiveCommunityLeader.Shout one in shouts)
            {
                bool onTheBoard = everyoneWaiting.Any(r => r.Card == one.Card
                    && string.Equals(r.Figure, one.Figure, StringComparison.OrdinalIgnoreCase));

                json.WriteStartObject();
                json.WriteString("who", one.Who);
                json.WriteNumber("card", one.Card);
                json.WriteString("figure", one.Figure ?? string.Empty);
                json.WriteBoolean("completed", onTheBoard);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("pleas");
            foreach ((int plea, long card, string who) in pleas)
            {
                json.WriteStartObject();
                json.WriteNumber("plea", plea);
                json.WriteNumber("card", card);
                json.WriteString("who", who);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("history");
            foreach (RoundStatus round in rounds)
            {
                if (round.IsPlaying) continue;

                json.WriteStringValue(Texts.T("cantar.historia.ronda", ("n", round.Position.ToString())) + "  ·  " + (round.IsPlanned
                    ? round.PrizeLine + "  ·  " + Texts.T("cantar.historia.en-la-fila")
                    : string.Join("  ·  ", round.Promises.Select(p => p.WasKept
                        ? Texts.T("cantar.historia.para", ("figura", OnOffer.Called(p.Playing)), ("premio", p.PrizeText),
                            ("quienes", string.Join(", ", p.Winners)))
                        : Texts.T("cantar.historia.sin-entregar", ("figura", OnOffer.Called(p.Playing)), ("premio", p.PrizeText))))));
            }
            json.WriteEndArray();

            json.WriteEndObject();
        }

        string state = Encoding.UTF8.GetString(buffer.ToArray());

        // El reloj late cada segundo; si nada cambio, no se molesta a la pagina.
        if (string.Equals(state, _lastSent, StringComparison.Ordinal)) return;

        _lastSent = state;
        PushRaw(state);
    }

    private string NameOf()
    {
        try
        {
            return _communityLeader.QrFor(_festivalId).Name;
        }
        catch (Exception)
        {
            return Texts.T("cantar.nombre-por-defecto");
        }
    }

    private void PushToast(string text) => PushOneLiner("toast", text);

    private void PushLog()
    {
        string all;
        lock (_log) all = _log.ToString();

        PushOneLiner("log", all);
    }

    private void PushOneLiner(string kind, string text)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", kind);
            json.WriteString("text", text);
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Como se dice cada figura. Aca no se ofrecen para elegir -- eso pasa en
    /// el wizard --, pero la pagina las nombra en el tablero y en el bingo, y el nombre
    /// no puede salir en el idioma del motor.</summary>
    private void PushFigures() => PushRaw(OnOffer.AsMessage());

    /// <summary>El catalogo del idioma de la persona. Va ANTES que las figuras y el
    /// estado: la pagina no adivina el idioma, se lo dice el telefono.</summary>
    private void PushTextos() => PushRaw(Texts.AsMessage());

    private void PushRaw(string json) => OnScreen(() => Web.SendRawMessage(json));

    private void Failed(Exception failure)
    {
        Say(Texts.T("general.log.se-cayo", ("error", failure.GetType().Name + ": " + Texts.Explain(failure))));

        Exception root = failure;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
            Say("  " + Texts.T("general.log.causa", ("error", root.GetType().Name + ": " + root.Message)));
        }
    }

    private void Say(string line)
    {
        Console.WriteLine("[BINGO] " + line);

        lock (_log)
        {
            _log.AppendLine(line);
            if (_log.Length > 30000) _log.Remove(0, _log.Length - 20000);
        }

        PushLog();
    }

    private static void OnScreen(Action what)
    {
        if (MainThread.IsMainThread) what();
        else MainThread.BeginInvokeOnMainThread(what);
    }
}
