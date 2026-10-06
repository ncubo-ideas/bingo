using System.Globalization;
using System.Reflection;
using System.Text.Json;
using BingoEngine;
using Choreography.Theater;
using Choreography.Told;
using Choreography.StageManager;
using Choreography.Transport.Brokered;
using Choreography.Transport.Iroh;
using Puppeteer;
using BingoApp.Transport;
using BingoApp.CommunityLeader;
using BingoApp.Contract;
using static BingoApp.Contract.JournalAnswer;

namespace BingoApp.Player;

/// <summary>
/// El jugador en vivo: UN journal por telefono con un motor de bingo POR bingo
/// (juego_&lt;id&gt;). Puede estar metido en varias bingos a la vez; cada tell llega
/// por el topico de SU bingo y cae en el motor de ese bingo, nunca cruzado.
/// </summary>
public sealed class LivePlayer : IDisposable
{
    private static readonly Assembly GameOnly = typeof(BingoGame).Assembly;

    private const string SavedKey = "jugador.funciones";

    private const string ActiveKey = "jugador.funcion.activa";

    private const string MineKey = "jugador.minumero";

    private const string MyNameKey = "jugador.minombre";

    private static LivePlayer _instance;

    /// <summary>El jugador es uno solo por telefono: las pantallas comparten esta instancia.</summary>
    public static LivePlayer Instance => _instance ??= new LivePlayer();

    /// <summary>Lo que se sabe de la ronda para PINTAR: llega por la respuesta del
    /// enlist y por los avisos de RoundNotice. La mecanica no pasa por aca. El premio
    /// vive en cada figura, asi que lo que se guarda es la linea entera.</summary>
    private sealed class RoundShown
    {
        public int Position;
        public string PrizeLine = string.Empty;
        public bool Between;
        public bool Over;

        /// <summary>Como se anuncian los bingos aca: 'OnClaim' (lo canta la gente, y por
        /// eso el megafono hace falta) o 'Automatic' (la mesa se frena sola). Se arranca
        /// en OnClaim porque es lo de siempre y es el modo donde cantar SI importa: ante
        /// la duda, el megafono queda util y no muerto.</summary>
        public string Announce = "OnClaim";
    }

    private readonly HashSet<string> _alreadyCalled = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlayerEntry> _festivals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RoundShown> _rounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _roundSeen = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _listeners = new();

    /// <summary>El buzon de cartones que hay escuchando por cada bingo, guardado aparte
    /// para poder SOLTARLO. El buzon se llama por la marca, y la marca puede cambiar dentro
    /// de un mismo bingo: sin soltar el viejo y atar el nuevo, el organizador publicaria a
    /// un buzon que nadie escucha y el carton pagado se perderia sin un solo error.</summary>
    private readonly Dictionary<string, (string Role, IDisposable Ear)> _cardEars =
        new(StringComparer.Ordinal);

    private readonly object _guard = new();

    private Action<string> _say = _ => { };
    private PerformanceV2 _journal;
    private PhoneToPhone _phone;
    private TellBindingTable _bindings;
    private IrohTransport _wire;
    private IrohMessageBroker _bus;
    private readonly HashSet<string> _linkedBuses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wantBus = new(StringComparer.Ordinal);
    private DateTime _lastRelink = DateTime.MinValue;
    private int _relinking;
    private CancellationTokenSource _life;
    private string _myAddress;
    private string _active;
    private string _lastSignature;

    /// <summary>El ultimo pulso leido. Mientras no cambie no hay nada que mirar, y mirar
    /// cuesta una consulta por carton y otra por bolita.</summary>
    private string _lastPulse;

    public void TalkTo(Action<string> say) => _say = say ?? (_ => { });

    public event Action<PlayerProgress> Changed;

    public event Action<string> Bingo;

    public event Action Entered;

    /// <summary>Arranco otra ronda en ese bingo: el tablero quedo limpio y el letrero
    /// de BINGO, si estaba prendido, ya no dice nada de esta ronda.</summary>
    public event Action<string> RoundChanged;

    public string Active => _active;

    /// <summary>Por que no pudo entrar la ultima vez. Va a la pantalla, no solo al registro:
    /// un "no" que solo se ve en el registro es un "no" que la persona no lee.</summary>
    public string LastTrouble { get; private set; } = string.Empty;

    public IReadOnlyList<PlayerEntry> MyFestivals
    {
        get { lock (_guard) return _festivals.Values.ToArray(); }
    }

    public PlayerEntry InFestival(string id)
    {
        lock (_guard) return _festivals.TryGetValue(id, out PlayerEntry entry) ? entry : null;
    }

    /// <summary>El bingo que este telefono completo y todavia no canto: bingo, carton y
    /// figura. Se llena solo cuando el motor local dice que completo.</summary>
    private (string Festival, long Card, string Figure) _iWon;

    /// <summary>
    /// Si hay algo que cantar ahora mismo en el bingo abierto.
    ///
    /// Con 'Automatic' es que NO: la mesa se frena sola en cuanto un carton completa y el
    /// organizador ve el letrero sin que nadie reclame, asi que el megafono seria un boton
    /// que no cambia nada. Cantar solo hace falta cuando lo canta la gente -- ahi la mesa
    /// no busca ganadores, y quien no canta no gana.
    /// </summary>
    public bool HaveSomethingToShout
    {
        get
        {
            lock (_guard)
            {
                if (_iWon.Card <= 0) return false;
                if (!string.Equals(_iWon.Festival, _active, StringComparison.Ordinal)) return false;

                return !TheHouseAnnouncesItself(_iWon.Festival);
            }
        }
    }

    /// <summary>Si en este bingo los premios se anuncian solos. Hay que llamarlo con el
    /// candado tomado: lee la ronda que este telefono tiene guardada.</summary>
    private bool TheHouseAnnouncesItself(string festivalId) =>
        _rounds.TryGetValue(festivalId, out RoundShown shown)
        && string.Equals(shown.Announce, "Automatic", StringComparison.OrdinalIgnoreCase);

    /// <summary>Si ya se canto y el organizador lo tomo: la pantalla lo dice para que
    /// la persona no toque el megafono diez veces.</summary>
    public bool AlreadyShouted { get; private set; }

    /// <summary>
    /// Cantar BINGO: avisarle al organizador que ESTA persona esta reclamando.
    ///
    /// No decide nada -- el organizador ya sabe que cartones completaron y la mesa es
    /// quien valida al entregar. Esto es levantar la mano, que es exactamente lo que la
    /// persona hace en la vida real cuando grita.
    /// </summary>
    public async Task<bool> ShoutBingoAsync(CancellationToken life)
    {
        string festivalId;
        long card;
        string figure;

        lock (_guard)
        {
            (festivalId, card, figure) = _iWon;
        }

        if (card <= 0)
        {
            _say(Texts.T("jugador.log.nada-que-cantar"));
            return false;
        }

        lock (_guard)
        {
            if (TheHouseAnnouncesItself(festivalId))
            {
                _say(Texts.T("jugador.log.premios-solos"));
                return false;
            }
        }

        PlayerEntry entry = InFestival(festivalId);

        if (entry is null)
        {
            _say(Texts.T("jugador.log.no-estoy-en-ese-bingo"));
            return false;
        }

        BingoClaim claim = new()
        {
            FestivalId = festivalId,
            Who = MyOwnName(),
            Mark = entry.MyMark,
            Card = card,
            Figure = figure,
        };

        PhoneToPhone.Reply reply = await _phone.ShoutBingoAsync(
            entry.Festival.CommunityLeaderUrl, claim.ToText(), life).ConfigureAwait(false);

        if (!reply.Ok)
        {
            AlreadyShouted = false;
            _say(Texts.T("jugador.log.no-pude-cantar-grita",
                ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
            return false;
        }

        AlreadyShouted = true;
        _say(Texts.T("jugador.log.cante-bingo", ("carton", card.ToString())));

        return true;
    }

    // ── arranque (una vez por telefono) ──────────────────────────────────────

    public bool Started => _journal is not null;

    public void Start()
    {
        if (_journal is not null) return;

        _life = new CancellationTokenSource();
        _myAddress = Huddle.NetworkAddress.Mine(_say);

        string gameDir = Path.Combine(FileSystem.AppDataDirectory, "jugador");
        string inboxDir = Path.Combine(FileSystem.AppDataDirectory, "jugador-bandeja");

        _journal = new PerformanceV2("jugador", new[] { GameOnly });
        _journal.ConfigureStorage(DatabaseType.FileSystem, gameDir);

        // El diario del jugador estaba MUDO: si el oyente no sabia aplicar una bolita, el
        // sustrato lo decia por la consola de Puppeteer y no por 'lo que va pasando', asi
        // que en el telefono no quedaba rastro. El organizador ya tenia su voz; sin esta,
        // una bolita que se cae del lado del jugador es indistinguible de una que nunca salio.
        _journal.Logger(new JournalVoice(_say));

        _journal.Start();

        _phone = new PhoneToPhone("jugador", 8081, inboxDir, line => _say("    " + line));
        _bindings = new TellBindingTable();
        _phone.Listen();
        _wire = IrohWire.ForStage(PlayerIdentity(gameDir), gameDir, new JournalVoice(_say), _say);
        _bus = new IrohMessageBroker(_wire);
        _ = _wire.StartAsync(_life.Token);

        _ = Task.Run(() => WatchAsync(_life.Token));
    }

    // Quien soy en la red iroh: una identidad por aparato, guardada junto al diario del jugador.
    private static PerformerId PlayerIdentity(string dir)
    {
        string path = Path.Combine(dir, "identity.txt");
        if (File.Exists(path))
            return new PerformerId(Guid.ParseExact(File.ReadAllText(path).Trim(), "N"));
        PerformerId fresh = PerformerId.New();
        File.WriteAllText(path, fresh.ToString());
        return fresh;
    }

    // El organizador ofrece en la respuesta del alta un enlace iroh de un solo uso al bus de tells
    // del bingo (ticket + nonce). Un enlace por bingo: si ya estoy enganchado y vivo, no repito.
    private async Task LinkToTheBusAsync(string id, string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("iroh", out JsonElement offer)
                || offer.ValueKind != JsonValueKind.Object) return;
            string ticket = offer.GetProperty("ticket").GetString();
            string nonce = offer.GetProperty("nonce").GetString();
            if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(nonce)) return;
            lock (_guard) { if (_linkedBuses.Contains(id) && _bus.LinkCount > 0) return; }
            await _wire.StartAsync(_life.Token);
            await _bus.LinkAsync(IrohMessageBroker.LinkAddress(ticket, Guid.ParseExact(nonce, "N")), _life.Token);
            lock (_guard) _linkedBuses.Add(id);
            _say(Texts.T("jugador.log.bus-enganchado"));
        }
        catch (Exception failure)
        {
            _say(Texts.T("jugador.log.bus-no-enganche", ("motivo", failure.Message)));
        }
    }

    // Si el codigo trae el ticket iroh del organizador, el alta va por el bus: sirve desde otra red
    // o con datos moviles, donde la direccion HTTP del organizador no existe. Null = no hubo
    // respuesta por iroh, y el que llama prueba por HTTP como siempre.
    private async Task<PhoneToPhone.Reply> EnlistThroughIrohAsync(string id, PlayerEntry entry, string joinText)
    {
        string ticket = entry.Festival.IrohTicket;
        if (string.IsNullOrWhiteSpace(ticket)) return null;

        try
        {
            await _wire.StartAsync(_life.Token);

            bool linked;
            lock (_guard) linked = _linkedBuses.Contains(id) && _bus.LinkCount > 0;
            if (!linked)
            {
                await IrohDoor.LinkAsync(_bus, ticket, id, _life.Token, _say);
                lock (_guard) _linkedBuses.Add(id);
            }

            IrohDoor.Answer answer = await IrohDoor.EnlistAsync(_bus, id, joinText, TimeSpan.FromSeconds(30), _life.Token);
            if (answer is null)
            {
                _say(Texts.T("jugador.log.iroh-sin-respuesta"));
                return null;
            }

            if (answer.Ok) _say(Texts.T("jugador.log.alistado-iroh"));
            return answer.Ok
                ? new PhoneToPhone.Reply { Ok = true, Body = answer.Body }
                : new PhoneToPhone.Reply { Ok = false, Why = answer.Why, Because = answer.Because };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception failure)
        {
            _say(Texts.T("jugador.log.no-alistado-iroh", ("motivo", failure.Message)));
            return null;
        }
    }

    // ── entrar a un bingo (cuantas quiera) ─────────────────────────────────

    public async Task<bool> EnterAsync(PlayerEntry entry)
    {
        if (_journal is null) throw new InvalidOperationException(Texts.T("jugador.log.arranca-primero"));

        // La firma se verifica SOLO cuando el codigo es nuevo en este telefono: es el
        // momento de aceptarlo o no. Los ya guardados no re-verifican, porque este metodo
        // corre tambien al reabrir la app -- a veces sin red y ANTES del organizador -- y
        // exigirle el certificado en cada reapertura romperia el carton sin senal, que es
        // un derecho del jugador. Y va antes del journal: un codigo forjado no deja huella.
        if (!string.IsNullOrWhiteSpace(entry.Festival.Signature)
            && !SavedCodes().Contains(entry.Code)
            && !await SealChecksOutAsync(entry.Festival))
        {
            return false;
        }

        string id = entry.Festival.FestivalId;
        bool wasAlready;

        lock (_guard)
        {
            wasAlready = _festivals.ContainsKey(id);
            if (wasAlready) _festivals[id] = entry;
        }

        if (wasAlready) _say(Texts.T("jugador.log.ya-estaba", ("bingo", entry.Festival.Name)));

        string game = entry.MyGame;

        // La firma de un upgrade es el texto de su cuerpo. Los telefonos que ya tenian
        // la mesa la armaron con las figuras pegadas en el texto, y reenviarles este
        // cuerpo reventaria por "differs from the one previously applied". Si la mesa
        // ya existe, el upgrade no tiene nada que hacer: no se manda.
        if (!AlreadyHasTheGame(game))
        {
            _journal.Using($"upgrade('{entry.MyUpgradeName()}') {{ {game} = BingoGame(); "
                + $"{game}.StartRound(@figurasEnJuego, 'OnClaim'); }}")
                .WithParameters(p => p.UserParameter<IEnumerable<string>>(
                    "figurasEnJuego", entry.Festival.FigureList().ToList()))
                .PerformCommand();
        }

        // La ranura de la ronda se estrena en 1 y se pisa apenas el organizador diga otra
        // cosa. Tiene que existir ANTES del primer aviso: el 'if' que decide si una bolita es
        // de esta ronda la lee, y leer una global que nadie escribio no es comparar, es
        // reventar. Va fuera del upgrade a proposito -- el upgrade corre una sola vez en la
        // vida del telefono, y esto tiene que estar cada vez que se abre la app.
        RememberTheRound(entry, RoundTheyToldMe(entry.Festival.FestivalId));

        _say(Texts.T("jugador.log.soy-en-el-bingo", ("nombre", entry.MyName), ("bingo", entry.Festival.Name)));
        _say(Texts.T("jugador.log.bingo-se-juega", ("id", id), ("figuras", string.Join(", ", entry.Festival.FigureList()))));

        if (!wasAlready)
        {
            _bindings.Bind(entry.PlayerRole, entry.CallTopic);
            _phone.SendAcksOf(entry.CallTopic, entry.Festival.CommunityLeaderUrl);

            // La ronda nueva entra por el MISMO listener que las bolitas, asi el corte queda
            // ordenado con ellas. Eso alcanza para lo que viaja EN VIVO y no para lo que se
            // reentrega: un aviso que quedo parqueado --el telefono en el fondo, la wifi
            // cortada-- sale de la bandeja cuando el jugador vuelve, y sale en el orden en que
            // estaba guardado, no en el que pasaron las cosas. Una bolita vieja despues del
            // corte entraba a la ronda nueva sin que nada la frenara, y el carton se pintaba
            // con numeros que en esa ronda nunca salieron.
            //
            // Por eso viaja CON SU RONDA y se asienta con el verbo de dos argumentos, que es
            // el que compara; el de uno solo aplica lo que le den.
            //
            // Y el descarte se decide con un if y no dejando que el motor tire. Una excepcion
            // no mata al oyente --la atrapa-- pero deja el aviso SIN ACUSAR, y entonces el que
            // lo mando lo reintenta cada segundo, toda la noche, fallando siempre. Un descarte
            // tiene que acusar recibo: llego, lo miramos, no iba.
            //
            // La tardia se anota en dos globales para que la pantalla pueda contarlo. Es el
            // mismo recurso que usa el organizador con 'ultimaCantada', y hace falta porque
            // este cuerpo es DSL y no le puede hablar al C#.
            _listeners.Add(_journal.ListenAs(entry.PlayerRole, _bindings, _bus)
                // Se compara contra LA RONDA DEL ORGANIZADOR, guardada en su propia ranura, y
                // NO contra {game}.RoundNumber: el contador del motor del jugador sube solo
                // cada vez que rehace el tablero --y lo rehace cada vez que le reponen un
                // carton-- asi que se adelanta sin que en el bingo haya pasado nada. Comparar
                // contra el descarta bolitas buenas, medido.
                //
                // Y una vez que la ronda ya se comprobo, se asienta con la version de UN
                // argumento: significa "va en la ronda que estoy jugando", que es justo lo que
                // acabamos de verificar. La de dos volveria a comparar contra el contador
                // equivocado.
                .Told("Bolita").With<int>("numero").With<int>("ronda")
                    .Command($"if ({entry.MyRoundSlot} == @ronda) {{ {game}.RegisterDraw(@numero); }}"
                        + " else { bolitaTardia = @numero; rondaTardia = @ronda; };")
                .Told("Correccion").With<int>("malo").With<int>("bueno")
                    .Command($"{game}.CorrectDraw(@malo, @bueno);")
                .Told("Ronda").With<string>("figuras")
                    .Command($"{game}.MoveOnToRound(@figuras, 'OnClaim');")
                .Start());

            _listeners.Add(_phone.Subscribe(RoundNotice.TopicFor(id),
                notice => OnRoundNotice(id, notice)));
        }

        ListenToMyCards(id, entry, game);

        // En la puerta se declara lo que este telefono TIENE, no lo que decia el codigo con
        // el que entro la primera vez: asi el organizador puede ver que le falta y, de
        // paso, no le reavisa uno que ya esta en juego (ese aviso rebota).
        JoinRequest join = entry.ToJoinRequest(_myAddress, MyOwnNumber());
        List<long> alreadyMine = CardsInPlay(game);

        if (alreadyMine.Count > 0)
        {
            List<long> declared = new(join.Cards ?? new List<long>());
            foreach (long card in alreadyMine) if (!declared.Contains(card)) declared.Add(card);
            declared.Sort();
            join.Cards = declared;
        }

        PhoneToPhone.Reply reply = await EnlistThroughIrohAsync(id, entry, join.ToText())
            ?? await _phone.EnlistAndListenAsync(
                entry.Festival.CommunityLeaderUrl,
                join.ToText(),
                _life.Token);

        if (!reply.Ok)
        {
            // El "no" se dice en el idioma de ESTE telefono: el codigo cruza el cable y se
            // traduce aca; el texto del organizador es solo el respaldo del "no" sin codigo.
            bool sinRazon = string.IsNullOrWhiteSpace(reply.Why)
                && (string.IsNullOrWhiteSpace(reply.Because) || reply.Because == Refusal.Plain);
            LastTrouble = sinRazon
                ? Texts.T("jugador.log.no-logre-entrar")
                : Refusal.Dicho(reply.Because, reply.Why);

            _say(LastTrouble);
            return false;
        }

        LastTrouble = string.Empty;

        CatchUpTheRound(entry, reply.Body);
        await LinkToTheBusAsync(id, reply.Body);

        lock (_guard)
        {
            _festivals[id] = entry;
            _active ??= id;
            if (!string.IsNullOrWhiteSpace(entry.Festival.IrohTicket)) _wantBus.Add(id);
        }

        Remember();

        _say(Texts.T("jugador.log.esperando-mi-carton"));
        Entered?.Invoke();

        return true;
    }

    /// <summary>
    /// Ponerse a escuchar el buzon de MIS cartones en este bingo, y volver a hacerlo si el
    /// buzon se mudo.
    ///
    /// El buzon se llama por la marca del comprador, y la marca puede cambiar dentro de una
    /// mismo bingo: alcanza con que en una compra quien vende deje el numero vacio, y el
    /// organizador acune otro. Antes esto se ataba una sola vez, al entrar, asi que a partir
    /// de ahi el organizador publicaba a un buzon que este telefono ya no escuchaba: el
    /// carton pagado se perdia sin un solo error, ni de un lado ni del otro.
    /// </summary>
    private void ListenToMyCards(string id, PlayerEntry entry, string game)
    {
        lock (_guard)
        {
            if (_cardEars.TryGetValue(id, out (string Role, IDisposable Ear) already))
            {
                if (string.Equals(already.Role, entry.MyCardRole, StringComparison.Ordinal))
                    return;

                _say(Texts.T("jugador.log.me-cambio-el-numero"));
                try { already.Ear.Dispose(); } catch (Exception) { }
            }
        }

        _bindings.Bind(entry.MyCardRole, entry.MyCardTopic);
        _phone.SendAcksOf(entry.MyCardTopic, entry.Festival.CommunityLeaderUrl);

        IDisposable ear = _journal.ListenAs(entry.MyCardRole, _bindings, _bus)
            .Told("Carton").With<int>("numero").Command($"{game}.PutCardInPlay(@numero);")
            .Told("QuitarCarton").With<int>("numero")
                .Command($"tableCard = {game}.CardInPlay(@numero); {game}.TakeCardOutOfPlay(tableCard);")
            .Start();

        lock (_guard) _cardEars[id] = (entry.MyCardRole, ear);
    }

    /// <summary>
    /// La palabra del organizador vale mas que el QR: la ronda vigente y sus bolitas
    /// vienen en la respuesta del enlist, asi que quien entra tarde (o vuelve) se pone
    /// al dia. Si las figuras locales ya son las de la ronda vigente, NO se corta nada
    /// -- solo se reponen las bolitas que falten; cortar aca borraria lo ya marcado.
    /// Un organizador viejo no manda nada de esto y el juego queda como el QR lo armo.
    /// </summary>
    private void CatchUpTheRound(PlayerEntry entry, string body)
    {
        string sejuega = Read(body, "sejuega");

        lock (_guard)
        {
            if (!_rounds.TryGetValue(entry.Festival.FestivalId, out RoundShown shown))
                _rounds[entry.Festival.FestivalId] = shown = new RoundShown();

            shown.Position = AsInt(body, "ronda");
            shown.PrizeLine = RoundNotice.PrizeLine(body);

            // Como se anuncian los bingos en esta funcion. Decide si el megafono sirve
            // de algo: con 'Automatic' la mesa se frena sola y cantar no cambia nada;
            // cuando lo canta la gente, cantar es lo unico que hace ganar.
            string how = Read(body, "anuncio");
            if (!string.IsNullOrWhiteSpace(how)) shown.Announce = how;
        }

        RememberTheRound(entry, RoundTheyToldMe(entry.Festival.FestivalId));

        // Lo primero: reponer los cartones que el organizador dice que son mios y este
        // telefono no tiene. Es el punto de reparacion mas barato que hay -- si un aviso se
        // perdio en el camino, el proximo ingreso lo arregla sin que nadie haga nada.
        int recovered = 0;

        {
            string game = entry.MyGame;
            HashSet<long> have = new(CardsInPlay(game));

            foreach (long card in CardsIn(body))
            {
                if (have.Contains(card)) continue;

                // El MISMO texto y el MISMO tipo de parametro que journalea el oyente de
                // los cartones: la firma queda escrita en el diario y se re-ejecuta en cada
                // sesion posterior.
                _journal.Using($"{game}.PutCardInPlay(@numero);")
                    .WithParameters(p => p.UserParameter<int>("numero", (int)card))
                    .PerformCommand();

                recovered++;
            }

            if (recovered > 0)
                _say(Texts.Plural(recovered, "jugador.log.me-faltaban.uno", "jugador.log.me-faltaban.varios"));
        }

        if (string.IsNullOrWhiteSpace(sejuega)) return;

        string juego = entry.MyGame;
        string current = string.Join(",", FiguresInPlay(juego));
        string told = string.Join(",", sejuega.Split(',')
            .Select(f => f.Trim()).Where(f => f.Length > 0));

        // Un carton que entra a mitad de ronda se enrola en la bolita de ese momento: no
        // trae marcadas las que ya salieron. Por eso, si se repuso alguno, la ronda se
        // rehace y se reponen TODAS las bolitas, no solo las que faltaban.
        bool startOver = recovered > 0
            || !string.Equals(current, told, StringComparison.OrdinalIgnoreCase);

        if (startOver)
        {
            _journal.Using($"{juego}.MoveOnToRound(@figuras, 'OnClaim');")
                .WithParameters(p => p.UserParameter<string>("figuras", told))
                .PerformCommand();

            _say(recovered > 0
                ? Texts.T("jugador.log.rehago-el-tablero")
                : Texts.T("jugador.log.ronda-vigente-juega", ("figuras", told)));
        }

        int already = AsInt(_journal.PerformQry($"print {juego}.DrawnCount n;"), "n");
        HashSet<int> known = startOver ? new HashSet<int>() : new(DrawOrder(juego, already));
        int caught = 0;

        foreach (int bolita in BallsIn(body))
        {
            if (known.Contains(bolita)) continue;

            _journal.Using($"{juego}.RegisterDraw(@numero);")
                .WithParameters(p => p.UserParameter<int>("numero", bolita))
                .PerformCommand();

            caught++;
        }

        if (caught > 0) _say(Texts.T("jugador.log.me-puse-al-dia", ("n", caught.ToString())));
    }

    private List<string> FiguresInPlay(string game)
    {
        int howMany = AsInt(_journal.PerformQry($"print {game}.FiguresInPlayCount n;"), "n");
        List<string> playing = new();

        for (int position = 1; position <= howMany; position++)
        {
            string figure = Read(
                Ask($"print {game}.FigureInPlayAt(@posicion) f;",
                    p => p.UserParameter<int>("posicion", position)), "f");

            if (figure is not null) playing.Add(figure);
        }

        return playing;
    }

    /// <summary>Una lectura con sus valores como parametros: el nombre de la mesa es lo unico
    /// que va pegado al texto, porque es un nombre del diario y no un valor.</summary>
    private string Ask(string query, Action<Parameters> values) =>
        _journal.Using(query).WithParameters(values).PerformQuery();

    private bool AlreadyHasTheGame(string game)
    {
        try
        {
            _journal.PerformQry($"print {game}.RoundNumber r;");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>La ronda que el organizador dijo por ultima vez, o 1 si todavia no dijo nada.
    /// Es lo que se ve en pantalla, y desde ahora tambien lo que decide si una bolita es de
    /// esta ronda o llego tarde.</summary>
    private int RoundTheyToldMe(string id)
    {
        lock (_guard)
            return _rounds.TryGetValue(id, out RoundShown shown) && shown.Position > 0
                ? shown.Position
                : 1;
    }

    /// <summary>Deja escrita en el diario la ronda que dice el organizador, para que el aviso
    /// de la proxima bolita la pueda comparar. Si no cambio, no se escribe: seria un asiento
    /// por cada repintada.</summary>
    private void RememberTheRound(PlayerEntry entry, int round)
    {
        if (entry is null || round <= 0) return;

        try
        {
            string slot = entry.MyRoundSlot;

            _journal.Using($"{slot} = @ronda;")
                .WithParameters(p => p.UserParameter<int>("ronda", round))
                .PerformCommand();
        }
        catch (Exception failure)
        {
            _say(Texts.T("jugador.log.no-pude-anotar-ronda", ("motivo", Texts.Explain(failure))));
        }
    }

    private void OnRoundNotice(string id, BrokerRecord notice)
    {
        string what = RoundNotice.What(notice.Value);
        bool roundChanged = false;

        lock (_guard)
        {
            if (!_rounds.TryGetValue(id, out RoundShown shown))
                _rounds[id] = shown = new RoundShown();

            if (string.Equals(what, RoundNotice.Starting, StringComparison.Ordinal))
            {
                shown.Position = RoundNotice.Position(notice.Value);
                shown.PrizeLine = RoundNotice.PrizeLine(notice.Value);
                shown.Between = false;
                shown.Over = false;
                roundChanged = true;
            }
            else if (string.Equals(what, RoundNotice.Settled, StringComparison.Ordinal))
            {
                shown.Between = true;
            }
            else if (string.Equals(what, RoundNotice.Over, StringComparison.Ordinal))
            {
                shown.Over = true;
            }

            _lastSignature = _lastPulse = null;
        }

        // Se anota la ronda nueva ANTES de que llegue su primera bolita. Si llegara antes, esa
        // bolita se leeria como de la ronda anterior y se descartaria siendo buena.
        if (roundChanged) RememberTheRound(InFestival(id), RoundTheyToldMe(id));

        if (string.Equals(what, RoundNotice.Over, StringComparison.Ordinal))
            _say(Texts.T("jugador.log.termino-gracias"));
        else if (string.Equals(what, RoundNotice.Settled, StringComparison.Ordinal))
            _say(Texts.T("jugador.log.ronda-cerrada"));
    }

    public void SwitchActive(string id)
    {
        lock (_guard)
        {
            if (!_festivals.ContainsKey(id)) return;
            _active = id;
            _lastSignature = _lastPulse = null;
        }

        Remember();
    }

    /// <summary>
    /// La firma del codigo, contra el certificado que el propio organizador sirve en
    /// /certificado/. El rechazo va por LastTrouble, que la puerta ya sabe mostrar.
    /// Sin pin y sin memoria: se coteja en el momento, contra quien atiende AHORA.
    /// </summary>
    private async Task<bool> SealChecksOutAsync(OpenFestival festival)
    {
        using System.Security.Cryptography.X509Certificates.X509Certificate2 cert =
            await PhoneToPhone.FetchPeerCertAsync(festival.CommunityLeaderUrl);

        if (cert is null)
        {
            LastTrouble = Texts.T("jugador.log.firma.sin-certificado");
            _say(LastTrouble);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(festival.SignerFingerprint)
            && !string.Equals(Huddle.PhoneCertificate.Fingerprint(cert),
                festival.SignerFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            LastTrouble = Texts.T("jugador.log.firma.certificado-cambio");
            _say(LastTrouble);
            return false;
        }

        if (!FestivalSeal.Verify(festival.SignedPayload, festival.Signature, cert))
        {
            LastTrouble = Texts.T("jugador.log.firma.no-es-del-organizador");
            _say(LastTrouble);
            return false;
        }

        _say(Texts.T("jugador.log.firma.verificada"));
        return true;
    }

    // ── la memoria del jugador ───────────────────────────────────────────────

    /// <summary>
    /// Lo que este telefono se lleva a disco: la UNION de lo que ya estaba guardado y lo
    /// que esta vivo, no solo lo vivo.
    ///
    /// Escribir desde _festivals a secas BORRABA. _festivals tiene unicamente los bingos
    /// que CONTESTARON -- EnterAsync se va con return false antes de meter ahi al que no
    /// contesta --, asi que reescribir la lista desde el se llevaba del disco, sin un solo
    /// aviso, todo bingo cuyo organizador estuviera apagado. Y no hacia falta entrar a
    /// nada: Rehydrate() recorre los guardados al abrir la app, y con que UNO contestara,
    /// ese primer Remember() borraba a los demas. Medido en el 5558: dos bingos guardados,
    /// uno con el organizador vivo y otro apagado, y al abrir la app quedaba uno. Lo que
    /// la persona leia era "no pude alistarme", que suena a que la red anda mal hoy.
    ///
    /// Ahora el disco dice lo mismo que MyBingos() ya muestra en pantalla. Lo vivo va
    /// primero para que, cuando los dos tengan el mismo bingo, gane el codigo de esta
    /// sesion: puede traer cartones que el guardado todavia no sabe.
    /// </summary>
    private void Remember()
    {
        List<PlayerEntry> live;
        string active;

        lock (_guard)
        {
            live = _festivals.Values.Where(v => v.Code is not null).ToList();
            active = _active ?? string.Empty;
        }

        List<string> codes = new();
        HashSet<string> already = new(StringComparer.Ordinal);

        foreach (PlayerEntry one in live)
            if (already.Add(one.Festival.FestivalId)) codes.Add(one.Code);

        foreach (string code in SavedCodes())
        {
            string id;

            try
            {
                id = PlayerEntry.FromCode(code).Festival.FestivalId;
            }
            catch (Exception)
            {
                // Un codigo que hoy no se puede leer se CONSERVA. Perderlo es definitivo y
                // callado; conservarlo cuesta, en el peor caso, una fila repetida. Es la
                // misma prudencia que MyBingos ya tiene al pintar, y la unica defensa si
                // algun dia sube la version del sobre.
                codes.Add(code);
                continue;
            }

            if (already.Add(id)) codes.Add(code);
        }

        Preferences.Default.Set(SavedKey, JsonSerializer.Serialize(codes));
        Preferences.Default.Set(ActiveKey, active);
    }

    public static IReadOnlyList<string> SavedCodes()
    {
        try
        {
            string raw = Preferences.Default.Get(SavedKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

            return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>En cual estaba cuando cerro la app. Sin esto, al reabrirla el jugador cae
    /// en el bingo mas vieja que recuerde -- vuelve a ver el carton de la semana pasada,
    /// sin bolitas, mientras la de esta noche corre sin el.</summary>
    public static string SavedActive() => Preferences.Default.Get(ActiveKey, string.Empty);

    // ── el numero de este telefono ───────────────────────────────────────────

    /// <summary>
    /// El numero propio del aparato. Se acuna la primera vez que alguien lo pide y no se
    /// vuelve a tocar, asi que existe ANTES de entrar a ningun bingo: es lo que el
    /// comprador ensena cuando le van a cobrar.
    /// </summary>
    public static string MyOwnNumber()
    {
        string mine = Preferences.Default.Get(MineKey, string.Empty);

        if (!string.IsNullOrWhiteSpace(mine)) return mine;

        mine = BuyerMark.Mint();
        Preferences.Default.Set(MineKey, mine);

        return mine;
    }

    /// <summary>Como se llama, para que quien vende no lo tenga que escribir. Se recuerda
    /// del ultimo nombre que uso.</summary>
    public static string MyOwnName() => Preferences.Default.Get(MyNameKey, string.Empty);

    public static void RememberMyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        Preferences.Default.Set(MyNameKey, name.Trim());
    }

    /// <summary>
    /// El numero que este telefono ENSENA ahora mismo: el del bingo en la que esta si esa
    /// bingo ya lo conoce por uno, y el propio mientras no lo conozca nadie.
    /// </summary>
    public string NumberIShow()
    {
        string inThisFestival = null;

        lock (_guard)
        {
            if (_active is not null && _festivals.TryGetValue(_active, out PlayerEntry entry))
                inThisFestival = entry.MyMark;
        }

        return BuyerBadge.Showing(MyOwnNumber(), inThisFestival);
    }

    /// <summary>El nombre con el que lo conoce el bingo en la que esta; si no esta en
    /// ninguna, el que dejo escrito la ultima vez.</summary>
    public string NameIShow()
    {
        lock (_guard)
        {
            if (_active is not null && _festivals.TryGetValue(_active, out PlayerEntry entry)
                && !string.IsNullOrWhiteSpace(entry.MyName))
                return entry.MyName;
        }

        return MyOwnName();
    }

    // ── el pulso: mirar como voy en el bingo activa ─────────────────────────

    private async Task WatchAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, ct);
                Check();
                KeepTheBusAlive();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception failure)
            {
                _say(Texts.T("jugador.log.se-cayo-el-ciclo", ("motivo", Texts.Explain(failure))));
                await Task.Delay(2000, ct);
            }
        }
    }

    // Si el enlace iroh con el organizador se cayo (pantalla apagada, otra app encima, corte de red),
    // el telefono se vuelve a presentar solo: mismo camino que tocar el bingo en la lista, que
    // re-enlaza por una puerta y trae las bolitas que se perdio. Cada 10 s mientras siga caido.
    private void KeepTheBusAlive()
    {
        if (_bus is null || _bus.LinkCount > 0) return;
        if (DateTime.UtcNow - _lastRelink < TimeSpan.FromSeconds(10)) return;

        PlayerEntry entry;
        lock (_guard)
        {
            entry = _active is not null && _festivals.TryGetValue(_active, out PlayerEntry e) ? e : null;
            if (entry is null || string.IsNullOrWhiteSpace(entry.Festival.IrohTicket)) return;
            if (!_wantBus.Contains(entry.Festival.FestivalId)) return;
        }

        if (Interlocked.Exchange(ref _relinking, 1) == 1) return;
        _lastRelink = DateTime.UtcNow;
        lock (_guard) _linkedBuses.Remove(entry.Festival.FestivalId);
        _say(Texts.T("jugador.log.perdi-enlace-iroh"));

        _ = Task.Run(async () =>
        {
            try
            {
                await EnterAsync(entry);
            }
            catch (Exception failure)
            {
                _say(Texts.T("jugador.log.no-reenganche", ("motivo", failure.Message)));
            }
            finally { Interlocked.Exchange(ref _relinking, 0); }
        });
    }

    private void Check()
    {
        PlayerEntry entry;
        lock (_guard)
            entry = _active is not null && _festivals.TryGetValue(_active, out PlayerEntry e) ? e : null;

        if (entry is null) return;

        string id = entry.Festival.FestivalId;
        string game = entry.MyGame;

        // Lo barato primero, y que CORTE. Esta consulta ya se hacia, pero solo para traer
        // los numeros: el trabajo caro --las figuras, las bolitas una por una, un carton
        // por consulta-- se pagaba igual, y recien despues se armaba la firma para
        // descubrir que nada habia cambiado. Aca late cada medio segundo, en el telefono
        // mas barato de la mesa, y entre bolita y bolita pasan quince.
        string read = _journal.PerformQry(
            $"{{ print {game}.CardsInPlayCount cards; print {game}.DrawnCount draws; "
            + $"print {game}.RoundNumber round; print {game}.Pulse pulse; }}");

        int cardCount = AsInt(read, "cards");
        int draws = AsInt(read, "draws");
        int round = AsInt(read, "round");

        string pulse = id + "|" + Read(read, "pulse");

        if (cardCount == 0) return;

        // El pulso trae los NUMEROS de los cartones y las figuras, no solo cuantos: cambiar
        // un carton por otro no mueve ningun conteo, y con una marca que solo contara la
        // pantalla se quedaria mostrando el que ya no es suyo hasta la proxima bolita.
        if (string.Equals(pulse, _lastPulse, StringComparison.Ordinal)) return;

        _lastPulse = pulse;

        // Arranco otra ronda: los bingos cantados eran de la anterior, y el letrero
        // de la pantalla tambien.
        bool freshRound;
        lock (_guard)
        {
            freshRound = _roundSeen.TryGetValue(id, out int seen) && seen != round;
            _roundSeen[id] = round;
        }

        if (freshRound)
        {
            foreach (string called in _alreadyCalled
                .Where(c => c.StartsWith(id + "|", StringComparison.Ordinal)).ToArray())
            {
                _alreadyCalled.Remove(called);
            }

            // Lo cantado era de la ronda que se fue: la mano se baja sola.
            lock (_guard) { _iWon = default; }
            AlreadyShouted = false;

            _say(Texts.T("jugador.log.tablero-limpio"));
            RoundChanged?.Invoke(id);
        }

        List<string> playing = FiguresInPlay(game);

        List<int> order = DrawOrder(game, draws);
        HashSet<int> drawn = new(order);

        List<long> mine = new();
        for (int position = 1; position <= cardCount; position++)
            mine.Add(AsLong(Ask($"print {game}.CardInPlayAt(@posicion) n;",
                p => p.UserParameter<int>("posicion", position)), "n"));

        // Los NUMEROS, no cuantos: un cambio de un carton por otro no mueve el
        // conteo ni las bolitas, asi que con la firma vieja la pantalla se
        // quedaba mostrando el carton que ya no es suyo hasta la proxima bolita.
        string signature = id
            + "|" + round
            + "|" + string.Join(",", playing)
            + "|" + string.Join(",", mine.OrderBy(n => n))
            + "|" + string.Join(",", drawn.OrderBy(n => n));

        if (signature == _lastSignature) return;

        bool firstTime = _lastSignature is null
            || !_lastSignature.StartsWith(id + "|", StringComparison.Ordinal);

        // Una correccion deja la MISMA cantidad de bolitas y otras distintas: reemplaza
        // una por otra. Sirve para no decir "salio el 18" cuando lo que paso es que
        // corrigieron la tercera y la ultima sigue siendo la de antes.
        bool corrected = !firstTime
            && HowManyBalls(_lastSignature) == HowManyBalls(signature)
            && !string.Equals(_lastSignature, signature, StringComparison.Ordinal);

        _lastSignature = signature;

        int last = draws > 0 ? DrawAt(game, draws) : 0;

        foreach (string called in _alreadyCalled
            .Where(c => c.StartsWith(id + "|", StringComparison.Ordinal)).ToArray())
        {
            string[] parts = called.Split('|');

            if (AsBool(Ask($"print {game}.HasCompleted(@carton, @figura) g;", p =>
                {
                    p.UserParameter<long>("carton", long.Parse(parts[1], CultureInfo.InvariantCulture));
                    p.UserParameter<string>("figura", parts[2]);
                }), "g")) continue;

            _alreadyCalled.Remove(called);
            _say(Texts.T("jugador.log.se-me-cayo-el-numero", ("numero", parts[2]), ("carton", parts[1])));
        }

        List<CardProgress> cards = new();

        foreach (long card in mine)
        {
            List<(string, int, int, bool, IReadOnlyList<int>)> figures = new();

            foreach (string figure in playing)
            {
                IReadOnlyList<int> needs = BingoGame.ClosestFor(card, figure, drawn);
                int have = needs.Count(n => drawn.Contains(n));
                List<int> waiting = needs.Where(n => !drawn.Contains(n)).ToList();

                string key = id + "|" + card + "|" + figure;

                if (!_alreadyCalled.Contains(key)
                    && AsBool(Ask($"print {game}.HasCompleted(@carton, @figura) g;", p =>
                    {
                        p.UserParameter<long>("carton", card);
                        p.UserParameter<string>("figura", figure);
                    }), "g"))
                {
                    _alreadyCalled.Add(key);
                    _say(Texts.T("jugador.log.bingo-completo", ("carton", card.ToString()), ("figura", figure),
                        ("bolita", draws.ToString()), ("bingo", entry.Festival.Name)));
                    // Por su nombre de la casa: es lo que la persona lee en la pantalla
                    // de la celebracion, y ahi decirle 'AnySquare' es hablarle en motor
                    // justo en el momento que mas le importa.
                    Bingo?.Invoke(Texts.T("jugador.bingo-con-carton", ("figura", OnOffer.Called(figure)), ("carton", card.ToString())));

                    // Lo que hace falta para poder CANTARLO: la celebracion es un texto
                    // para leer, y para avisarle al organizador hay que decirle carton y
                    // figura en nombre de motor.
                    lock (_guard) { _iWon = (id, card, figure); }
                }

                figures.Add((figure, have, needs.Count, _alreadyCalled.Contains(key), waiting));
            }

            cards.Add(new CardProgress { Number = card, Figures = figures });
        }

        if (firstTime)
            _say(Texts.T("jugador.log.me-llegaron-cartones", ("cuales", string.Join(", ", mine))));
        else if (corrected)
            _say(Texts.T("jugador.log.corrigieron-bolita"));
        else if (last > 0)
            _say(Texts.T("jugador.log.salio-el", ("numero", last.ToString())));

        int shownPosition = 0;
        string prizeLine = string.Empty;
        bool between = false, over = false;

        lock (_guard)
        {
            if (_rounds.TryGetValue(id, out RoundShown shown))
            {
                shownPosition = shown.Position;
                prizeLine = shown.PrizeLine ?? string.Empty;
                between = shown.Between;
                over = shown.Over;
            }
        }

        Changed?.Invoke(new PlayerProgress
        {
            FestivalId = id,
            Drawn = drawn,
            DrawOrder = order,
            LastDraw = last,
            Cards = cards,
            Playing = playing,
            RoundNumber = round,
            RoundPosition = shownPosition,
            PrizeLine = prizeLine,
            BetweenRounds = between,
            FestivalOver = over,
        });

        SayIfABallCameInTooLate(id);
    }

    /// <summary>La ultima tardia de la que ya se hablo, por bingo, para no repetirla en cada
    /// repintada. Cada bingo lleva su propia ronda.</summary>
    private readonly Dictionary<string, string> _lateAlreadySaid = new(StringComparer.Ordinal);

    /// <summary>
    /// Contar que llego una bolita de una ronda que ya cerro.
    ///
    /// Pasa cuando el telefono estuvo un rato sin recibir --se fue al fondo, se corto la
    /// wifi-- y el aviso quedo parqueado. Al volver, la bandeja le larga lo atrasado, y si
    /// entre medio cambio la ronda esa bolita ya no va a ningun lado.
    ///
    /// Decirlo importa. Sin esto la persona escucho cantar un numero, no lo ve en su carton,
    /// y no tiene como saber que fue porque volvio tarde y no porque la app se equivoco.
    /// </summary>
    private void SayIfABallCameInTooLate(string id)
    {
        int number;
        int round;

        try
        {
            string read = _journal.PerformQry("{ print bolitaTardia n; print rondaTardia r; }");

            number = AsInt(read, "n");
            round = AsInt(read, "r");
        }
        catch (Exception)
        {
            // Todavia no llego ninguna tardia, asi que esas globales no existen. Es el caso
            // normal, no un problema.
            return;
        }

        if (number <= 0 || round <= 0) return;

        string what = round + "-" + number;

        lock (_guard)
        {
            if (_lateAlreadySaid.TryGetValue(id, out string already)
                && string.Equals(already, what, StringComparison.Ordinal)) return;

            _lateAlreadySaid[id] = what;
        }

        _say(Texts.T("jugador.log.llego-tarde", ("numero", number.ToString()), ("ronda", round.ToString())));
    }

    private List<int> DrawOrder(string game, int howMany)
    {
        List<int> drawn = new();

        for (int position = 1; position <= howMany; position++) drawn.Add(DrawAt(game, position));

        return drawn;
    }

    private int DrawAt(string game, int position)
    {
        try
        {
            return AsInt(Ask($"print {game}.DrawnAt(@posicion) n;",
                p => p.UserParameter<int>("posicion", position)), "n");
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Cuantas bolitas dice una firma. La firma es id|ronda|figuras|cartones|
    /// bolitas, y las bolitas van separadas por coma: contarlas es lo que distingue una
    /// bolita nueva (una mas) de una correccion (las mismas, otras).</summary>
    private static int HowManyBalls(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return 0;

        string[] parts = signature.Split('|');
        if (parts.Length < 5 || parts[4].Length == 0) return 0;

        return parts[4].Split(',').Length;
    }

    private static List<int> BallsIn(string body)
    {
        List<int> balls = new();

        if (string.IsNullOrWhiteSpace(body)) return balls;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("bolitas", out JsonElement list)) return balls;
            if (list.ValueKind != JsonValueKind.Array) return balls;

            foreach (JsonElement one in list.EnumerateArray())
            {
                if (one.ValueKind == JsonValueKind.Number && one.TryGetInt32(out int ball))
                    balls.Add(ball);
            }
        }
        catch (Exception)
        {
            return new List<int>();
        }

        return balls;
    }

    /// <summary>Los cartones que el organizador dice que son mios, en la respuesta de la
    /// puerta. Es lo que permite repararse solo: si un aviso se perdio, el proximo ingreso
    /// lo repone.</summary>
    private static List<long> CardsIn(string body)
    {
        List<long> cards = new();

        if (string.IsNullOrWhiteSpace(body)) return cards;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("cartones", out JsonElement list)) return cards;
            if (list.ValueKind != JsonValueKind.Array) return cards;

            foreach (JsonElement one in list.EnumerateArray())
            {
                if (one.ValueKind == JsonValueKind.Number && one.TryGetInt64(out long card))
                    cards.Add(card);
            }
        }
        catch (Exception)
        {
            return new List<long>();
        }

        return cards;
    }

    /// <summary>Los cartones que este telefono TIENE en juego. Es lo que se le declara al
    /// organizador en la puerta: el codigo de compra solo trae los de la primera compra, y
    /// decir de menos hace que le reavisen algo que ya tiene, que rebota del otro lado.
    /// </summary>
    private List<long> CardsInPlay(string game)
    {
        List<long> mine = new();

        try
        {
            int howMany = AsInt(_journal.PerformQry($"print {game}.CardsInPlayCount n;"), "n");

            for (int position = 1; position <= howMany; position++)
            {
                mine.Add(AsLong(Ask($"print {game}.CardInPlayAt(@posicion) n;",
                    p => p.UserParameter<int>("posicion", position)), "n"));
            }
        }
        catch (Exception)
        {
            return mine;
        }

        return mine;
    }

    public void Dispose()
    {
        try { _life?.Cancel(); } catch { }
        foreach (IDisposable listener in _listeners) { try { listener.Dispose(); } catch { } }
        foreach ((string _, IDisposable ear) in _cardEars.Values) { try { ear.Dispose(); } catch { } }
        try { _phone?.Dispose(); } catch { }
        try { _life?.Dispose(); } catch { }
    }
}
