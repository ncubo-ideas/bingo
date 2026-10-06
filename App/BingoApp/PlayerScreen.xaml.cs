using System.Globalization;
using System.Text;
using System.Text.Json;
using BingoEngine;
using BingoApp.Player;
using BingoApp.Contract;

namespace BingoApp;

public partial class PlayerScreen : ContentPage
{
    /// <summary>Un solo Start() a la vez. Sin esto, llegar por deep link CON bingos
    /// guardadas arranca el transporte dos veces en paralelo (Rehydrate y Enter), una de
    /// las dos muere con NullReference y el accept loop del listener queda tocado: el
    /// telefono dice "escucho en 8081" pero ya no le llega ningun tell.</summary>
    private static readonly object _startGate = new();

    private readonly StringBuilder _log = new();
    private readonly string _arrivedCode;

    private LivePlayer _player;
    private PlayerProgress _progress;
    private bool _rehydrated;
    private bool _showDoor = true;

    /// <summary>Si la puerta tiene que pedir el nombre. Arranca en false porque el codigo
    /// del vendedor lo trae adentro (la llave 'yo'): preguntarselo era pedirle lo mismo que
    /// estaba por recibir. Se prende solo si un codigo llega sin nombre.</summary>
    private bool _askName;
    private string _bingoText;
    private string _trouble = string.Empty;
    /// <summary>Vacio hasta que la persona escriba: un telefono recien instalado no se
    /// puede llamar Rita, porque ese nombre sale despues en su carnet.</summary>
    private string _webName = string.Empty;

    public PlayerScreen()
    {
        InitializeComponent();

        _webName = LivePlayer.MyOwnName();
    }

    public PlayerScreen(string code) : this() => _arrivedCode = code;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Fullscreen.HideBars();

        _player = LivePlayer.Instance;
        _player.TalkTo(Say);
        _player.Changed += OnChanged;
        _player.Bingo += OnBingo;
        _player.Entered += OnEnteredFestival;
        _player.RoundChanged += OnRoundChanged;

        QrInbox.Arrived += OnCodeArrived;

        Rehydrate();

        if (_arrivedCode is not null && !_rehydrated) OnCodeArrived(_arrivedCode);
        else if (QrInbox.Collect() is string pending) OnCodeArrived(pending);

        _rehydrated = true;
    }

    protected override void OnDisappearing()
    {
        Fullscreen.ShowBars();

        QrInbox.Arrived -= OnCodeArrived;

        if (_player is not null)
        {
            _player.Changed -= OnChanged;
            _player.Bingo -= OnBingo;
            _player.Entered -= OnEnteredFestival;
            _player.RoundChanged -= OnRoundChanged;
        }

        base.OnDisappearing();
    }

    // ── lo que la pagina le pide al telefono ─────────────────────────────────

    private void OnWebMessage(object sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        string[] parts = (e.Message ?? string.Empty).Split('|', 3);

        switch (parts[0])
        {
            case "ready":
                PushTextos();
                PushFigures();
                PushState();
                PushLog();
                break;
            case "idioma":
                if (parts.Length > 1) AppLanguage.Choose(parts[1]);
                PushTextos();
                PushFigures();
                PushState();
                PushLog();
                break;
            case "scan":
                if (parts.Length > 1) MyNameIs(parts[1]);
                OnScreen(async () => await Scan());
                break;
            case "enter":
                if (parts.Length == 3)
                {
                    MyNameIs(parts[1]);
                    EnterByHand(parts[2]);
                }
                break;
            case "myname":
                if (parts.Length > 1) MyNameIs(parts[1]);
                break;
            case "badge":
                PushState();
                break;
            case "switch":
                if (parts.Length > 1) SwitchFestival(parts[1]);
                break;
            case "another":
                _showDoor = true;
                Say(Texts.T("jugador.log.elige-a-cual-volver"));
                PushState();
                break;
            case "back":
                _showDoor = false;
                PushState();
                break;
            case "bingo":
                ShoutBingo();
                break;
            case "wantlog":
                PushLog();
                break;
        }
    }

    /// <summary>Cantar BINGO: el megafono avisa al organizador que esta persona esta
    /// reclamando. Antes solo repetia la animacion de la celebracion y nada salia del
    /// telefono; quien completaba tenia que gritar y esperar que alguien lo mirara.</summary>
    private void ShoutBingo() => Task.Run(async () =>
    {
        try
        {
            await _player.ShoutBingoAsync(CancellationToken.None);
        }
        catch (Exception failure)
        {
            Say(Texts.T("jugador.log.no-pude-cantar", ("motivo", Texts.Explain(failure))));
        }

        OnScreen(PushState);
    });

    /// <summary>El nombre queda guardado en el telefono: viaja en el carnet para que quien
    /// vende no lo tenga que escribir, y sigue estando el proximo bingo.</summary>
    private void MyNameIs(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        _webName = name.Trim();
        LivePlayer.RememberMyName(_webName);
    }

    private async Task Scan()
    {
        ScanScreen camera = new();
        await Navigation.PushModalAsync(camera);

        string read = await camera.Scanned;

        if (read is not null) OnCodeArrived(read);
    }

    private void EnterByHand(string code)
    {
        try
        {
            PlayerEntry entry = PlayerEntry.FromCode(code);

            if (string.IsNullOrWhiteSpace(entry.MyName))
                entry.MyName = _webName;

            Enter(entry);
        }
        catch (Exception failure)
        {
            Say(Texts.T("jugador.log.codigo-no-sirve", ("motivo", Texts.Explain(failure))));
        }
    }

    private void OnCodeArrived(string code)
    {
        if (Huddle.HuddlePass.IsPassLink(code))
        {
            OnScreen(async () => await Navigation.PushAsync(new HuddleDemoScreen(code)));
            return;
        }

        try
        {
            PlayerEntry entry = PlayerEntry.FromCode(code);

            if (string.IsNullOrWhiteSpace(entry.MyName))
                entry.MyName = _webName;

            OnScreen(() => Enter(entry));
        }
        catch (Exception failure)
        {
            Say(Texts.T("jugador.log.codigo-no-entiendo", ("motivo", Texts.Explain(failure))));
        }
    }

    /// <summary>Entrar SIEMPRE se presenta ante el organizador, aunque ya este en esa
    /// bingo: un codigo nuevo de el mismo bingo trae cartones nuevos (la segunda compra
    /// de la noche viaja en su propio codigo), y antes la re-entrada se cortaba en "ya
    /// estoy" y esos cartones no llegaban nunca.</summary>
    private void Enter(PlayerEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.MyName))
        {
            // Recien ACA se sabe que falta: el nombre viene en el codigo y este no lo trajo.
            // Se destapa el campo y se deja el codigo donde esta, asi con escribir y volver
            // a tocar entrar alcanza -- no hay que pegarlo de nuevo.
            _askName = true;
            PushState();
            Say(Texts.T("jugador.log.codigo-sin-nombre"));
            return;
        }

        _askName = false;

        string id = entry.Festival.FestivalId;

        if (_player.InFestival(id) is not null)
            Say(Texts.T("jugador.log.ya-estoy-me-presento", ("bingo", entry.Festival.Name)));

        _showDoor = false;
        PushState();

        Task.Run(async () =>
        {
            try
            {
                lock (_startGate) if (!_player.Started) _player.Start();

                bool entered = await _player.EnterAsync(entry);

                if (entered)
                {
                    _trouble = string.Empty;
                    _player.SwitchActive(id);
                    PushState();
                    return;
                }

                // Volver a abrir la puerta con el motivo a la vista. Sin esto, un "no" deja
                // la pantalla del juego vacia y la persona no tiene como saber que paso.
                // El motivo tecnico ("Connection refused...") va al registro; a la pildora
                // va que hacer, que es lo unico accionable para quien esta en la puerta.
                Say(Texts.T("jugador.log.no-entre", ("motivo", _player.LastTrouble)));
                _trouble = Texts.T("jugador.puerta.no-contesta-todavia");
                _showDoor = true;
                PushState();
            }
            catch (Exception failure)
            {
                _trouble = Texts.Explain(failure);
                _showDoor = true;
                PushState();

                Say(Texts.T("jugador.log.no-pude-entrar", ("error", failure.GetType().Name + ": " + Texts.Explain(failure))));
            }
        });
    }

    /// <summary>Al abrir la pantalla, vuelve a entrar a los bingos guardadas:
    /// la lista del jugador sobrevive a cerrar la app.</summary>
    private void Rehydrate()
    {
        if (_rehydrated) return;

        IReadOnlyList<string> saved = LivePlayer.SavedCodes();
        if (saved.Count == 0) return;

        // Se lee ANTES del ciclo: volver a entrar a la primer bingo guardada ya deja
        // esa como activa y pisa el recuerdo, asi que despues seria tarde para preguntar.
        string wasIn = LivePlayer.SavedActive();

        // La puerta se queda ABIERTA: con bingos guardados, la lista es la primera
        // pantalla -- ver los suyos y cual fue el ultimo, y entrar es UN toque. Antes se
        // saltaba directo al carton del activo, y elegir otro obligaba a encontrar la
        // carpa de la barra de abajo. Las reconexiones siguen corriendo aca atras, asi
        // que las filas se van prendiendo solas mientras mira.

        Task.Run(async () =>
        {
            try
            {
                lock (_startGate) if (!_player.Started) _player.Start();

                string refused = string.Empty;

                foreach (string code in saved)
                {
                    try
                    {
                        PlayerEntry entry = PlayerEntry.FromCode(code);

                        if (string.IsNullOrWhiteSpace(entry.MyName))
                            entry.MyName = "Jugador";

                        if (_player.InFestival(entry.Festival.FestivalId) is null
                            && !await _player.EnterAsync(entry))
                            refused = _player.LastTrouble;
                    }
                    catch (Exception failure)
                    {
                        refused = failure.Message;
                        Say(Texts.T("jugador.log.no-pude-volver", ("motivo", Texts.Explain(failure))));
                    }
                }

                // El motivo tecnico va al registro; a la pantalla va que hacer. La
                // pildora cruda ("Connection refused (10.0.2.16:8080)") era lo primero
                // que leia quien abria la app con el organizador apagado.
                if (_player.MyFestivals.Count == 0 && refused.Length > 0)
                {
                    Say(Texts.T("jugador.log.ninguno-contesto", ("cuales", refused)));
                    _trouble = Texts.T("jugador.puerta.tus-bingos-no-contestan");
                }

                BackToWhereTheyWere(wasIn);

                PushState();
            }
            catch (Exception failure)
            {
                Say(Texts.T("jugador.log.no-pude-arrancar", ("error", failure.GetType().Name + ": " + Texts.Explain(failure))));
            }
        });
    }

    /// <summary>Devolverlo al bingo en la que estaba. Al volver a entrar a todas las
    /// guardadas gana la primera, que es la mas vieja: sin esto, quien reabre la app en
    /// media bingo se encuentra el carton de otra noche y cree que perdio sus bolitas.</summary>
    private void BackToWhereTheyWere(string wasIn)
    {
        if (!string.IsNullOrWhiteSpace(wasIn) && _player.InFestival(wasIn) is not null)
        {
            _player.SwitchActive(wasIn);
            return;
        }

        // Sin memoria de cual era, la mas reciente: el id es la fecha y hora en que se
        // creo, asi que la ultima en orden es la de esta noche.
        string newest = _player.MyFestivals
            .Select(v => v.Festival?.FestivalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .LastOrDefault();

        if (newest is not null) _player.SwitchActive(newest);
    }

    /// <summary>
    /// Mis bingos: los que el telefono tiene guardados, contestara el organizador o no.
    ///
    /// Antes esta lista era MyFestivals, que se llena al ENTRAR -- y entrar necesita que el
    /// organizador conteste. Con el organizador apagado la persona veia cero bingos aunque
    /// tuviera sus codigos guardados y sus cartones comprados. Y no hace falta la red para
    /// eso: el nombre y los cartones viajan dentro del codigo.
    ///
    /// El de memoria le gana al guardado cuando los dos estan, porque el de memoria trae lo
    /// que el diario fue reconociendo durante la noche.
    /// </summary>
    private static string IdIn(string code)
    {
        try { return PlayerEntry.FromCode(code).Festival.FestivalId; }
        catch (Exception) { return null; }
    }

    /// <summary>En cual bingo estaba: el activo si hay uno, y si no, el que el telefono
    /// recuerda de la ultima vez. El fallback importa con el organizador apagado -- ahi
    /// nada esta "activo", pero la persona igual tiene un ultimo lugar.</summary>
    private string LastBingoId()
    {
        string live = _player?.Active;
        return string.IsNullOrWhiteSpace(live) ? LivePlayer.SavedActive() : live;
    }

    private IReadOnlyList<PlayerEntry> MyBingos()
    {
        List<PlayerEntry> all = new();
        HashSet<string> already = new(StringComparer.Ordinal);

        foreach (PlayerEntry live in _player?.MyFestivals ?? Array.Empty<PlayerEntry>())
            if (already.Add(live.Festival.FestivalId)) all.Add(live);

        foreach (string code in LivePlayer.SavedCodes())
        {
            try
            {
                PlayerEntry saved = PlayerEntry.FromCode(code);
                if (already.Add(saved.Festival.FestivalId)) all.Add(saved);
            }
            catch (Exception)
            {
                // Un codigo guardado ilegible no puede vaciar la lista de los demas.
            }
        }

        // El ultimo en que estuvo va PRIMERO: la lista es ahora la primera pantalla, y
        // la pregunta que la persona trae al abrir la app es "en cual estaba yo".
        string last = LastBingoId();
        int at = all.FindIndex(one => string.Equals(one.Festival.FestivalId, last, StringComparison.Ordinal));
        if (at > 0)
        {
            PlayerEntry first = all[at];
            all.RemoveAt(at);
            all.Insert(0, first);
        }

        return all;
    }

    private void SwitchFestival(string id)
    {
        // Todavia no entrado: se intenta con su codigo guardado. Antes se salia en silencio,
        // asi que la fila de un bingo cuyo organizador no habia contestado era un boton
        // muerto -- y "no pasa nada al tocar" no le dice a nadie que falta la red.
        if (_player.InFestival(id) is null)
        {
            string code = LivePlayer.SavedCodes().FirstOrDefault(one => IdIn(one) == id);

            if (code is null)
            {
                Say(Texts.T("jugador.log.bingo-ya-no-guardado"));
                return;
            }

            EnterByHand(code);
            return;
        }

        _player.SwitchActive(id);
        _progress = null;
        _bingoText = null;
        _showDoor = false;
        PushState();
    }

    // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

    private void OnChanged(PlayerProgress progress)
    {
        if (!string.Equals(progress.FestivalId, _player.Active, StringComparison.Ordinal)) return;

        _progress = progress;
        PushState();
    }

    private void OnEnteredFestival() => PushState();

    private void OnBingo(string whatIWon)
    {
        _bingoText = Texts.T("jugador.bingo-y-que", ("que", whatIWon));
        PushRaw(OneLiner("celebrate", whatIWon));
    }

    /// <summary>Arranco otra ronda: el bingo celebrado era de la anterior.</summary>
    private void OnRoundChanged(string festivalId)
    {
        if (!string.Equals(festivalId, _player.Active, StringComparison.Ordinal)) return;

        _bingoText = null;
        PushState();
    }

    private void PushState() => PushRaw(StateJson());

    private void PushLog()
    {
        string all;
        lock (_log) all = _log.ToString();

        PushRaw(OneLiner("log", all));
    }

    /// <summary>Como se dice cada figura. El jugador solo la nombra cuando la ronda no
    /// prometio premio; sin esto ahi le sale "L" en vez de "La ele".</summary>
    private void PushFigures() => PushRaw(OnOffer.AsMessage());

    /// <summary>El catalogo del idioma de la persona. Va ANTES que las figuras y el
    /// estado: la pagina no adivina el idioma, se lo dice el telefono.</summary>
    private void PushTextos() => PushRaw(Texts.AsMessage());

    private void PushRaw(string json) => OnScreen(() => Web.SendRawMessage(json));

    private static string OneLiner(string kind, string text)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", kind);
            json.WriteString("text", text);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// El carnet: el numero que esta persona le ENSENA a quien le vende, con su QR ya
    /// dibujado. Va en cada estado y no solo en la puerta, porque la segunda compra pasa en
    /// medio del juego -- con las bolitas cayendo -- y ahi tambien lo tiene que poder
    /// mostrar.
    ///
    /// El QR viaja incrustado como data URI: la pagina no puede pedir nada por red, y el
    /// numero no tiene por que salir del aparato.
    /// </summary>
    private void WriteMyBadge(Utf8JsonWriter json)
    {
        string number = _player is null
            ? LivePlayer.MyOwnNumber()
            : _player.NumberIShow();

        string name = _player is null
            ? LivePlayer.MyOwnName()
            : _player.NameIShow();

        if (string.IsNullOrWhiteSpace(name)) name = _webName;

        json.WriteStartObject("badge");
        json.WriteString("number", number);
        json.WriteString("pretty", BuyerMark.Pretty(number));
        json.WriteString("name", name ?? string.Empty);

        try
        {
            json.WriteString("qr", QrImage.AsDataUri(BuyerBadge.For(number, name)));
        }
        catch (Exception)
        {
            // Sin dibujo se sigue pudiendo teclear el numero, que es justo para lo que se
            // hizo corto. Quedarse sin carnet entero por no poder pintar seria peor.
            json.WriteString("qr", string.Empty);
        }

        json.WriteEndObject();
    }

    /// <summary>El estado entero del jugador como lo necesita la pagina. La matriz del
    /// carton viaja resuelta desde aca: la cara sale de Cardface (HMAC por numero) y el
    /// JS no tiene como derivarla. El centro libre viaja como 0.</summary>
    private string StateJson()
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "state");
            json.WriteBoolean("door", _showDoor);
            json.WriteBoolean("canGoBack", _player?.MyFestivals.Count > 0);
            json.WriteBoolean("askName", _askName);
            json.WriteString("trouble", _trouble ?? string.Empty);

            PlayerEntry active = _player?.Active is null ? null : _player.InFestival(_player.Active);
            json.WriteString("who", active?.MyName ?? _webName);
            json.WriteString("festivalName", active?.Festival.Name ?? string.Empty);

            WriteMyBadge(json);

            json.WriteStartArray("festivals");
            if (_player is not null)
            {
                foreach (PlayerEntry entry in MyBingos())
                {
                    json.WriteStartObject();
                    json.WriteString("id", entry.Festival.FestivalId);
                    json.WriteString("name", entry.Festival.Name);
                    json.WriteBoolean("active",
                        string.Equals(entry.Festival.FestivalId, LastBingoId(), StringComparison.Ordinal));

                    // Si el organizador de ESE bingo contesto. La fila se muestra igual:
                    // el nombre y los cartones estan en el codigo guardado, no en la red.
                    json.WriteBoolean("connected",
                        _player.InFestival(entry.Festival.FestivalId) is not null);

                    // Cuantos cartones tengo alla. Es lo unico que distingue un bingo de
                    // otro cuando los dos se llaman parecido -- y se llaman parecido casi
                    // siempre, porque el organizador los nombra por el mes.
                    json.WriteNumber("cards", entry.MyCards.Count);
                    json.WriteEndObject();
                }
            }
            json.WriteEndArray();

            json.WriteStartObject("round");
            json.WriteNumber("position", _progress?.RoundPosition ?? 0);
            json.WriteStartArray("playing");
            foreach (string figure in _progress?.Playing ?? [])
                json.WriteStringValue(figure);
            json.WriteEndArray();
            json.WriteString("prizeLine", _progress?.PrizeLine ?? string.Empty);
            json.WriteBoolean("between", _progress?.BetweenRounds ?? false);
            json.WriteBoolean("over", _progress?.FestivalOver ?? false);
            json.WriteEndObject();

            json.WriteStartArray("drawn");
            foreach (int n in _progress?.Drawn ?? [])
                json.WriteNumberValue(n);
            json.WriteEndArray();

            json.WriteStartArray("drawOrder");
            foreach (int n in _progress?.DrawOrder ?? [])
                json.WriteNumberValue(n);
            json.WriteEndArray();

            json.WriteNumber("lastDraw", _progress?.LastDraw ?? 0);

            json.WriteStartArray("cards");
            foreach (CardProgress card in _progress?.Cards ?? [])
            {
                Cardface face = Cardface.Of(new CardNumber(card.Number));

                json.WriteStartObject();
                json.WriteNumber("number", card.Number);

                json.WriteStartArray("grid");
                for (int band = 0; band < 5; band++)
                {
                    json.WriteStartArray();
                    IReadOnlyList<int> column = face.Column(band);

                    for (int row = 0; row < 5; row++)
                    {
                        if (band == 2 && row == 2) json.WriteNumberValue(0);
                        else json.WriteNumberValue(column[band == 2 && row > 2 ? row - 1 : row]);
                    }

                    json.WriteEndArray();
                }
                json.WriteEndArray();

                json.WriteStartArray("figures");
                foreach ((string figure, int have, int need, bool called,
                    IReadOnlyList<int> waiting) in card.Figures)
                {
                    json.WriteStartObject();
                    json.WriteString("name", figure);
                    json.WriteNumber("have", have);
                    json.WriteNumber("need", need);
                    json.WriteBoolean("called", called);

                    json.WriteStartArray("waiting");
                    foreach (int n in waiting) json.WriteNumberValue(n);
                    json.WriteEndArray();

                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteEndObject();
            }
            json.WriteEndArray();

            if (_bingoText is null) json.WriteNull("bingo");
            else json.WriteString("bingo", _bingoText);

            // Si el megafono tiene algo que mandar, y si ya se mando: sin esto la persona
            // toca y no sabe si el organizador se entero.
            json.WriteBoolean("canShout", _player?.HaveSomethingToShout ?? false);
            json.WriteBoolean("shouted", _player?.AlreadyShouted ?? false);

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void Say(string line)
    {
        Console.WriteLine("[BINGO] " + line);

        lock (_log)
        {
            _log.AppendLine(line);
            if (_log.Length > 20000) _log.Remove(0, _log.Length - 15000);
        }

        PushLog();
    }

    private static void OnScreen(Action what)
    {
        if (MainThread.IsMainThread) what();
        else MainThread.BeginInvokeOnMainThread(what);
    }
}
