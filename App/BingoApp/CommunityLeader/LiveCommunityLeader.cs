using System.Globalization;
using System.Reflection;
using System.Text.Json;
using BingoEngine;
using Choreography.Theater;
using Choreography.Told;
using Choreography.StageManager;
using Choreography.Transport;
using Choreography.Transport.Brokered;
using Choreography.Transport.Iroh;
using Puppeteer;
using Puppeteer.EventSourcing.Follower;
using Puppeteer.Tell;
using BingoApp.Transport;
using BingoApp.Contract;
using static BingoApp.Contract.JournalAnswer;
using Village;

namespace BingoApp.CommunityLeader;

/// <summary>
/// El organizador en vivo: el village que administra UN DIARIO POR BINGO. Cada evento
/// tiene su carpeta, su Elder, su Festival, su mesa y su reaccion; las carpetas
/// SON la cartelera. Un solo telefono en el 8080 atiende a todas.
/// </summary>
public sealed class LiveCommunityLeader : IDisposable
{
    private static readonly Assembly VillageAssembly = typeof(Elder).Assembly;
    private static readonly Assembly GameAssembly = typeof(BingoGame).Assembly;

    public const int CardsByDefault = 30;

    private const int CardsPerSeller = 3;
    private const int MyPort = 8080;

    private static LiveCommunityLeader _instance;

    /// <summary>El organizador es uno solo por telefono: las pantallas comparten esta instancia.</summary>
    public static LiveCommunityLeader Instance => _instance ??= new LiveCommunityLeader();

    /// <summary>Una mano levantada: alguien canto BINGO desde su telefono y espera
    /// que el organizador mire su carton.</summary>
    public sealed class Shout
    {
        public string Who { get; init; }

        public string Mark { get; init; }

        public long Card { get; init; }

        public string Figure { get; init; }

        public DateTime When { get; init; }
    }

    /// <summary>Un bingo abierto: su diario y todo lo que cuelga de el.</summary>
    private sealed class LiveFestival
    {
        public string Id { get; init; }

        public FestivalJournal Journal { get; init; }

        public TellBindingTable Bindings { get; init; }

        public BrokerTellTransport Transport { get; init; }
        public IrohMessageBroker Bus { get; init; }
        public IrohTransport BusWire { get; init; }

        public List<Attendee> Attendees { get; } = new();

        public Dictionary<string, List<long>> SoldTo { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Quienes cantaron BINGO desde su telefono y todavia no se resolvieron.
        /// NO viven en el diario a proposito: cantar es levantar la mano, y una mano
        /// levantada no es un asiento de la noche -- el asiento lo escribe el organizador
        /// al entregar o rechazar. Por eso tampoco sobreviven al reinicio: la persona
        /// sigue ahi parada y vuelve a cantar.</summary>
        public List<Shout> Shouts { get; } = new();

        /// <summary>Todos los cartones que el bingo puso en venta, esten donde esten.</summary>
        public List<long> ForSale { get; } = new();

        /// <summary>
        /// Los cartones que siguen en el libro, en el orden del libro. Se llena al abrir
        /// el bingo leyendo el diario, y de aca sale cada talonario.
        /// Antes esto era un contador que se usaba como indice sobre ForSale, y solo
        /// funcionaba porque nada volvia nunca: el dia que un carton regresa, el contador
        /// se desalinea y el siguiente vendedor recibe uno que ya esta en otra mano.
        /// </summary>
        public List<long> InTheBook { get; } = new();

        /// <summary>Hasta que asiento del diario estan puestos al dia los espejos de
        /// arriba. Sirve para no releer el diario en cada pintada: si el diario no se
        /// movio, lo que hay en memoria ya es lo que dice el diario.</summary>
        public long MirrorsAtEntry { get; set; } = -1;

        /// <summary>Cuantas entregas tienen cartones APARTADOS del libro y todavia sin
        /// asentar. Mientras haya una sola, el libro en memoria esta adelantado a
        /// proposito respecto del diario y NO se puede rearmar leyendolo: hacerlo
        /// devolveria al libro los que ya estan apartados, y la entrega siguiente se
        /// llevaria los mismos cartones.</summary>
        public int HandoutsInFlight { get; set; }
    }

    private readonly object _guard = new();

    private readonly Dictionary<string, LiveFestival> _live = new(StringComparer.Ordinal);

    private readonly List<string> _order = new();

    private Action<string> _say = _ => { };
    private PhoneToPhone _phone;
    private CancellationTokenSource _life;
    private string _myAddress;
    private string _root;

    public event Action Changed;

    public bool Started => _phone is not null;

    // BINGO_PUBLIC_URL: la URL con la que los demas llegan a este organizador cuando corre detras
    // de un puente (emulador en la PC). Si no esta, la IP de la LAN de siempre.
    public string MyUrl => PublicUrl ?? $"http://{_myAddress}:{MyPort}";

    private static string PublicUrl
    {
        get
        {
            string url = Environment.GetEnvironmentVariable("BINGO_PUBLIC_URL");
            if (string.IsNullOrWhiteSpace(url))
            {
                string file = Path.Combine(FileSystem.AppDataDirectory, "url-publica.txt");
                if (File.Exists(file)) url = File.ReadAllText(file);
            }
            return string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');
        }
    }

    /// <summary>La identidad del organizador, con llave privada: firma los codigos de
    /// colaborador. Null si no se pudo cargar, y en ese caso los codigos van sin firma.</summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2 Certificate { get; private set; }

    public void TalkTo(Action<string> say) => _say = say ?? (_ => { });

    // ── arrancar el village ──────────────────────────────────────────────────

    /// <summary>Abre el village: enciende el telefono y abre el diario de cada
    /// carpeta que encuentre. Idempotente.</summary>
    public void Start(CancellationToken life)
    {
        if (Started) return;

        _life = CancellationTokenSource.CreateLinkedTokenSource(life);
        _myAddress = Huddle.NetworkAddress.Mine(_say);
        if (PublicUrl is not null) _say(Texts.T("organizador.log.url-publica", ("url", PublicUrl)));

        // La llave con la que se firman los codigos de colaborador: el mismo certificado
        // del telefono que usa el corrillo, asi una sola huella identifica al organizador
        // en el transporte y en los papelitos. Si no se puede cargar, la funcion sigue
        // andando SIN firmar -- los codigos sin firma son validos por diseno.
        try
        {
            Certificate = Huddle.PhoneCertificate.MineOrNew("organizador", _myAddress, _say);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.sin-certificado", ("motivo", Texts.Explain(failure))));
        }

        _root = Path.Combine(FileSystem.AppDataDirectory, "festivals");
        Directory.CreateDirectory(_root);

        _phone = new PhoneToPhone("organizador", MyPort,
            Path.Combine(_root, "bandeja"), line => _say("    " + line));

        if (Certificate is not null)
            _phone.PublicCertDer = Certificate.Export(
                System.Security.Cryptography.X509Certificates.X509ContentType.Cert);

        _phone.OnEnlist = Enlist;
        _phone.OnSale = AcceptSale;
        _phone.OnCall = AcceptCall;
        _phone.OnExchange = AcceptExchange;
        _phone.OnHandover = AcceptHandover;
        _phone.OnGiveBack = AcceptGiveBack;
        _phone.OnPlea = AcceptPlea;
        _phone.OnBingoClaim = AcceptBingoClaim;
        _phone.OnWhoIs = AnswerWhoIs;
        _phone.Listen();

        string[] folders = Directory.GetDirectories(_root);
        Array.Sort(folders, StringComparer.Ordinal);

        int opened = 0;

        foreach (string folder in folders)
        {
            string id = Path.GetFileName(folder);

            if (string.Equals(id, "bandeja", StringComparison.Ordinal)) continue;

            try
            {
                Open(id, null, null);
                opened++;
            }
            catch (Exception failure)
            {
                _say(Texts.T("organizador.log.carpeta-sin-bingo", ("id", id), ("motivo", Texts.Explain(failure))));
            }
        }

                _say(opened == 0
            ? Texts.T("organizador.log.village-vacio")
            : Texts.Plural(opened, "organizador.log.village-abierto.uno", "organizador.log.village-abierto.varios"));
    }

    // ── crear y abrir bingos ────────────────────────────────────────────────

    public OpenFestival CreateFestival(string name, IReadOnlyList<RoundPlan> rounds,
        int howManyCards, long cardPrice = 0, DateTime? playsOn = null,
        string doorsOpenAt = null)
    {
        if (!Started) throw new InvalidOperationException(Texts.T("organizador.no.village-no-arrancado"));
        if (howManyCards <= 0)
            throw new InvalidOperationException(Texts.T("organizador.no.dime-cuantos-cartones"));
        if (rounds is null or { Count: 0 })
                        throw new InvalidOperationException(
                Texts.T("organizador.no.dime-que-se-juega"));

        foreach (RoundPlan plan in rounds) MustKnowThePlan(plan);

        string id = DateTime.Now.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string clean = Clean(string.IsNullOrWhiteSpace(name) ? "Bingo" : name);

        Open(id, clean, rounds, howManyCards, cardPrice, playsOn, doorsOpenAt);

        _say(Texts.T("organizador.log.bingo-nuevo", ("nombre", clean), ("id", id), ("rondas", rounds.Count.ToString()), ("cartones", howManyCards.ToString())));
        _say(Texts.T("organizador.log.diario-en", ("id", id)));

        Changed?.Invoke();

        return QrFor(id);
    }

    /// <summary>Las figuras se validan ACA, contra el catalogo de la mesa, porque mas
    /// adelante van pegadas al texto del comando que arranca la ronda. El premio vive
    /// en cada figura: una ronda es un punado de promesas.</summary>
    private static void MustKnowThePlan(RoundPlan plan)
    {
        if (plan is null || plan.Promises.Count == 0)
                        throw new InvalidOperationException(
                Texts.T("organizador.no.ronda-sin-promesa"));

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (PromisePlan promise in plan.Promises)
        {
            string figure = (promise?.Figure ?? string.Empty).Trim();

            if (figure.Length == 0)
                throw new InvalidOperationException(Texts.T("organizador.no.promesa-sin-figura"));

            if (!BingoGame.KnowsFigure(figure))
                throw new InvalidOperationException(Texts.T("organizador.no.figura-desconocida", ("figura", figure)));

            if (!seen.Add(figure))
                            throw new InvalidOperationException(
                    Texts.T("organizador.no.figura-repetida", ("figura", figure)));

            if (string.IsNullOrWhiteSpace(promise.Prize))
                            throw new InvalidOperationException(
                    Texts.T("organizador.no.dime-premio-figura", ("figura", figure)));
        }
    }

    /// <summary>Abre (o crea) el diario de un bingo. El upgrade es idempotente,
    /// asi que la misma llamada sirve para nacer y para rehidratar.</summary>
    private void Open(string id, string nameIfNew, IReadOnlyList<RoundPlan> roundsIfNew,
        int howManyCards = 0, long cardPrice = 0, DateTime? playsOn = null,
        string doorsOpenAt = null)
    {
        lock (_guard)
        {
            if (_live.ContainsKey(id)) return;
        }

        string dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);

        FestivalJournal journal = FestivalJournal.Open("funcion-" + id, _say, VillageAssembly, GameAssembly);
        journal.ConfigureStorage(DatabaseType.FileSystem, dir);

        // El logger va ANTES del Start: la rehidratacion pasa adentro de Start, y es ahi
        // donde el sustrato se queja de un asiento que no pudo volver a aplicar.
        journal.Logger(new JournalVoice(_say));

        journal.Start();

        // Solo el bingo nuevo trae nombre y agenda; al rehidratar el diario ya
        // los tiene y el upgrade no vuelve a correr.
        // El upgrade YA NO arranca la ronda: la agenda se anuncia con comandos aparte
        // y la ronda 1 se abre igual que las que siguen. Cambiarle el cuerpo a un
        // upgrade grabado revienta, pero este solo corre al nacer: los diarios viejos
        // rehidratan su upgrade viejo (con el StartRound adentro) y quedan como
        // bingos sin agenda de rondas.
        if (nameIfNew is not null)
        {
            DateTime today = DateTime.Now;

            // El dia que se juega lo dice quien lo crea. Antes se daba por sentado que
            // era hoy, y por eso ningun bingo podia decir "faltan tres dias". Si no lo
            // dijeron, sigue siendo hoy: un bingo sin fecha es uno que arranca ya.
            DateTime plays = playsOn?.Date ?? today.Date;

            // El pueblo se niega a anunciar para un dia que ya paso, y tiene razon. Pero
            // negarse ACA seria dejar al organizador sin bingo por un dia mal tecleado:
            // se corre a hoy y se sigue.
            if (plays < today.Date) plays = today.Date;

            journal.Using($@"
upgrade('festival') {{
    elder  = Elder(@communityLeader);
    dia    = CalendarDay(@anio, @mes, @diaMes);
    hoy    = CalendarDay(@hoyAnio, @hoyMes, @hoyDia);
    festival = elder.CreateFestival(@comoSeLlama, dia, hoy);
    mesa   = BingoGame();
    tells to {WireNames.PlayerRole(id)};
}}")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("communityLeader", "Bartolo");
                    p.UserParameter<int>("anio", plays.Year);
                    p.UserParameter<int>("mes", plays.Month);
                    p.UserParameter<int>("diaMes", plays.Day);
                    p.UserParameter<int>("hoyAnio", today.Year);
                    p.UserParameter<int>("hoyMes", today.Month);
                    p.UserParameter<int>("hoyDia", today.Day);
                    p.UserParameter<string>("comoSeLlama", nameIfNew);
                })
                .PerformCommand();

            // La hora va aparte del upgrade: el calendario del pueblo llega hasta el dia,
            // y la hora es lo que uno le dice al vecino ademas del dia.
            (int hour, int minute) = HourIn(doorsOpenAt);

            if (hour >= 0)
            {
                journal.Using("doorsTime = ClockTime(@hora, @minuto); elder.OpenTheDoorsAt(festival, doorsTime);")
                    .WithParameters(p =>
                    {
                        p.UserParameter<int>("hora", hour);
                        p.UserParameter<int>("minuto", minute);
                    })
                    .PerformCommand();
            }

            // Tipo declarado interfaz y valor List: la regla de las dos reglas que esta
            // escrita entera arriba de HandTo. Aca tambien, aunque hoy solo corra al
            // nacer el bingo: el dia que alguien reordene esto, la firma ya esta bien.
            journal.Using("elder.PutUpForSale(festival, @cartones);")
                .WithParameters(p =>
                    p.UserParameter<IEnumerable<long>>("cartones", ARun(1, howManyCards)))
                .PerformCommand();

            // El precio, si el wizard lo trajo. Va asentado en el diario como todo lo
            // que sobrevive a un reinicio: el precio de la noche no puede vivir en RAM.
            if (cardPrice > 0)
            {
                journal.Using("cardPrice = Price(@precio); elder.PriceCardsAt(festival, cardPrice);")
                    .WithParameters(p => p.UserParameter<long>("precio", cardPrice))
                    .PerformCommand();
            }

            foreach (RoundPlan plan in roundsIfNew) Announce(journal, plan);

            // La ronda 1 arranca con el bingo, como siempre: el umbral operativo
            // sigue siendo la primera bolita.
            BeginNextRound(journal);
        }

        // Si la carpeta no tenia un bingo de verdad, esto revienta y el que
        // llama la deja de lado en vez de tumbar el arranque.
        string name = Read(journal.PerformQry("print festival.Name n;"), "n")
            ?? throw new InvalidOperationException(Texts.T("organizador.no.diario-sin-bingo"));

        TellBindingTable bindings = new TellBindingTable();
        bindings.Bind(WireNames.PlayerRole(id), WireNames.CallTopic(id));

        IrohTransport busWire = journal.Wire;
        IrohMessageBroker bus = new IrohMessageBroker(busWire);
        BrokerTellTransport transport =
            new BrokerTellTransport(bus, bindings, "organizador-" + id);
        journal.UseTellTransport(transport);
        IrohDoor.Serve(bus, id, Enlist, _life.Token, _say);

        LiveFestival live = new LiveFestival
        {
            Id = id,
            Journal = journal,
            Bindings = bindings,
            Bus = bus,
            BusWire = busWire,
            Transport = transport,
        };

        lock (_guard)
        {
            _live[id] = live;
            if (!_order.Contains(id)) _order.Add(id);
        }

        ArmTheReaction(live);
        Rehydrate(live);

        _say(Texts.T("organizador.log.bingo-abierto", ("nombre", name), ("id", id)));
    }

    /// <summary>El reparto de bolitas vuelve a ser del sustrato: con una sola mesa
    /// por diario el patron ya no es ambiguo.</summary>
    private void ArmTheReaction(LiveFestival live)
    {
        string toWhom = WireNames.PlayerRole(live.Id);

        // El 'once' le pone nombre propio a cada canto. Sin el, el sustrato le calcula la
        // identidad al tell con un hash de su contenido --mensaje, destinatario, valores--
        // y como lo unico que varia ahi es el numero, el 7 de la ronda 2 hashea igual que
        // el de la ronda 1 y se descarta ANTES de armar el sobre: sin excepcion, sin ruta,
        // sin nada en la bandeja. Lo que viaja no cambia (sigue siendo Bolita con numero),
        // asi que del lado del jugador no hay que tocar nada.
        // El destinatario va sin instancia: quien reparte a los N telefonos es el
        // transporte, que liga el ROL a un topico. La instancia no participa de ese ruteo
        // ni de la toma del lado del jugador, y escrita quedaba en el acuse como si
        // hubiera alguien llamado asi.
        // La primera sentencia parece de adorno y NO LO ES: es lo unico que evita que el
        // sustrato elida el par {aviso, acuse} de cada bolita.
        //
        // El sustrato marca elegible para elidir solo cuando el cuerpo tiene EXACTAMENTE
        // una sentencia y esa sentencia es un tell (Program.HasSingleTellStatement). Al
        // elidir la cola del diario, el contador de entradas se siembra por debajo de la
        // cabeza fisica y las entradas siguientes nacen con ids ya usados; cuando a uno de
        // esos ids le toca la DEFINICION de una accion, esa accion queda huerfana para
        // siempre y toda invocacion suya se descarta en silencio. Lo medimos: una
        // devolucion de cartones que el diario acepto y que al releerse no existia.
        //
        // Que una entrada de mas de una sentencia NO se elida es comportamiento buscado
        // del sustrato, no un descuido nuestro: lo fija su propio test
        // Ack_MultiStatementEntry_NotElided ("the tell entry stays in the journal so the
        // upgrade alongside it survives"). Lo que dejamos de usar es la optimizacion.
        //
        // Se revierte a una sola sentencia el dia que Puppeteer siembre el contador desde
        // LastJournaledEntryId(); esta reportado como 'entryid-tail-elision'.
        // DirectorOnly y no Company, y ahora si importa. Mientras el diario fue de un solo
        // nodo daba igual: no habia replicas donde duplicar. Desde que el colaborador es Cast,
        // el actor esta replicado, y bajo Company la reaccion corre en LOS DOS papeles -- o
        // sea que el dia que alguien arme reacciones del lado del Cast, cada colaboradora le
        // mandaria la MISMA bolita a todos los jugadores. Hoy no pasa solo porque el Cast no
        // llama DefineReaction; esta a salvo por omision, no por diseño.
        live.Journal.Reactions.DefineReaction("Cantar")
            .Cue().DirectorOnly().WithSharedHydration()
            .Seek("Bolita").OnMatch("[_:BingoGame].RegisterDraw($numero, $ronda)")
            .Causation.Continue(
                "ultimaCantada = @numero; "
                + $"tell Bolita with @numero, @ronda to {toWhom} "
                + "once 'bolita-' + @ronda + '-' + @numero;");

        // La correccion viaja igual que la bolita: el organizador se equivoco de tecla y
        // los cartones de todos tienen que enterarse, o el 46 les queda tapado para
        // siempre. Los nombres de ligadura son los MISMOS que los parametros del comando
        // -- si no coinciden, el match no se cumple y no avisa: se corrige aca y en los
        // telefonos no cambia nada.
        // Y lleva su propio 'once' por la misma razon que la bolita: la identidad por
        // contenido no distingue dos veces el mismo error en rondas distintas, y la segunda
        // correccion se descartaria dejandole a la gente el numero malo pintado.
        live.Journal.Reactions.DefineReaction("Corregir")
            .Cue().DirectorOnly().WithSharedHydration()
            .Seek("Correccion").OnMatch("[_:BingoGame].CorrectDraw($malo, $bueno, $ronda)")
            .Causation.Continue(
                "ultimaCorregida = @bueno; "
                + $"tell Correccion with @malo, @bueno to {toWhom} "
                + "once 'correccion-' + @ronda + '-' + @malo + '-' + @bueno;");

        // CADA reaccion necesita su propio pulso. `Reactions.Execute()` sin nombre corre
        // UNA sola, asi que mientras hubo una sola definida parecio andar; al agregar la
        // segunda, la correccion se journaleaba y a los telefonos no les llegaba nada, sin
        // un solo error. El sustrato lo dice en su propia forma: Reactions[nombre].Execute
        // (SaleTellMessaging hace exactamente esto, un pulso por reaccion).
        Pulse(live, "Cantar");
        Pulse(live, "Corregir");
        WatchForWritesFromTheHuddle(live);
    }

    /// <summary>Cada cuanto se mira si el diario crecio por mano ajena.</summary>
    private static readonly TimeSpan HuddleWatchBeat = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Avisa que la pantalla tiene que repintarse cuando el diario avanzo sin que la app
    /// haya escrito.
    ///
    /// Todo lo que el organizador hace por su cuenta termina en Changed?.Invoke(), asi que
    /// la pantalla se entera. Lo que entra por el corrillo --el comando que una
    /// colaboradora le reenvia al Director-- no pasa por ninguno de esos caminos: mueve el
    /// diario y a nadie le avisa. Sin esto, la devolucion aparece bien recien la proxima
    /// vez que el organizador toque algo, porque ahi la pantalla se rearma sola.
    ///
    /// Es el companero de SyncMirrorsWithTheJournal: aquel hace que lo que se lee sea
    /// verdad, y este hace que se lea. Los dos hacen falta.
    /// </summary>
    private void WatchForWritesFromTheHuddle(LiveFestival live)
    {
        Thread watch = new Thread(() =>
        {
            long seen = -1;

            while (!_life.IsCancellationRequested)
            {
                try
                {
                    long now = live.Journal.CurrentEntryId;

                    // La primera vuelta solo toma la marca: al abrir el bingo la pantalla
                    // ya se pinto con lo que corresponde y no hay nada que avisar.
                    if (seen >= 0 && now != seen) Changed?.Invoke();

                    seen = now;
                }
                catch
                {
                    // El diario se esta cerrando o todavia no esta. No es motivo para
                    // matar el vigia: se vuelve a mirar en el latido siguiente.
                }

                try { Thread.Sleep(HuddleWatchBeat); } catch { return; }
            }
        })
        {
            IsBackground = true,
            Name = "corrillo-" + live.Id,
        };

        watch.Start();
    }

    /// <summary>Cuantas veces se levanta un pulso caido antes de darse por vencido. Con
    /// techo a proposito: si lo que falla es permanente, un reintento sin fin lo unico
    /// que hace es llenar el registro y gastar bateria toda la noche.</summary>
    private const int PulseLives = 5;

    /// <summary>
    /// El latido de UNA reaccion, en su propio hilo, y que se levanta si se cae.
    ///
    /// Lo de levantarse no es precaucion de mas: las dos reacciones comparten hidratacion
    /// y arrancan a la vez, asi que una le mueve la coleccion a la otra mientras la
    /// enumera ("Collection was modified"). La que perdia se moria ahi, para siempre, y
    /// con ella el reparto -- el diario seguia asentando bolitas y a los cartones de la
    /// gente no les llegaba ninguna, sin un solo error a la vista.
    /// </summary>
    private void Pulse(LiveFestival live, string reaction)
    {
        Thread pulse = new Thread(() =>
        {
            for (int life = 1; life <= PulseLives; life++)
            {
                try
                {
                    // En Continuous esto no vuelve solo: si volvio, lo cancelaron... o la
                    // reaccion no era para este papel. Desde que estan en DirectorOnly, el
                    // sustrato mira el papel UNA vez, al entrar, y si no le toca vuelve
                    // callado -- sin excepcion y sin gastar ninguna de las vidas de abajo.
                    // Tratar eso como cancelacion es quedarse sin reparto de bolitas para
                    // siempre, en silencio, que es la peor forma de romperse que tiene esto.
                    live.Journal.Reactions[reaction]
                        .Execute(ReactionExecutionMode.Continuous, CancellationToken.None);

                    if (life == 1)
                    {
                                                _say(Texts.T("organizador.log.reaccion-volvio", ("reaccion", reaction), ("id", live.Id)));
                    }

                    return;
                }
                catch (Exception failure)
                {
                                        _say(life < PulseLives
                        ? Texts.T("organizador.log.reaccion-se-cayo", ("reaccion", reaction), ("id", live.Id), ("vez", life.ToString()), ("veces", PulseLives.ToString()), ("motivo", Texts.Explain(failure)))
                        : Texts.T("organizador.log.reaccion-la-dejo", ("reaccion", reaction), ("id", live.Id), ("veces", PulseLives.ToString()), ("motivo", Texts.Explain(failure))));

                    // Un respiro antes de volver: si dos hilos se estan pisando, entrar de
                    // nuevo en el mismo instante es pisarse otra vez.
                    Thread.Sleep(250 * life);
                }
            }
        })
        {
            IsBackground = true,
            Name = reaction.ToLowerInvariant() + "-" + live.Id,
        };

        pulse.Start();
    }

    /// <summary>Lee del diario lo que el organizador tenia en memoria: quien tiene
    /// cartones en la mano y a quien se le vendio que.</summary>
    private void Rehydrate(LiveFestival live)
    {
        List<long> upForSale = NumbersIn(live.Journal.PerformQry(
            "foreach (n in festival.NumbersUpForSale()) { print n 'c'; }"));

        // Los que siguen sueltos los NOMBRA el bingo, no los cuenta: contarlos solo
        // alcanzaba mientras nada volviera al libro.
        List<long> stillInTheBook = NumbersIn(live.Journal.PerformQry(
            "foreach (n in festival.NumbersInTheBook()) { print n 'c'; }"));

        lock (_guard)
        {
            live.ForSale.Clear();
            live.ForSale.AddRange(upForSale);

            live.InTheBook.Clear();
            live.InTheBook.AddRange(stillInTheBook);
        }

        int soldCount = AsInt(live.Journal.PerformQry("print festival.SoldCount n;"), "n");

        for (int position = 1; position <= soldCount; position++)
        {
            long card = AsLong(live.Journal.Using("print festival.SoldNumberAt(@puesto) n;")
                .WithParameters(p => p.UserParameter<int>("puesto", position))
                .PerformQuery(), "n");

            string who = Read(live.Journal.Using("print festival.ParticipationNumbered(@carton).Buyer.Name q;")
                .WithParameters(p => p.UserParameter<long>("carton", card))
                .PerformQuery(), "q");

            if (who is null) continue;

            lock (_guard)
            {
                if (!live.SoldTo.TryGetValue(who, out List<long> theirs))
                    live.SoldTo[who] = theirs = new List<long>();

                if (!theirs.Contains(card)) theirs.Add(card);
            }
        }

        RememberWhoWasHere(live);
    }

    /// <summary>
    /// Volver a poner en la lista a las colaboradoras que ya estaban adentro, leyendolas del
    /// diario.
    ///
    /// Sin esto, un reinicio de la app a mitad de bingo dejaba al organizador con la
    /// pantalla diciendo que no llego nadie, y a ninguna colaboradora se le podia vender ni
    /// entregar hasta que volviera a presentarse: todo lo que el organizador puede HACER
    /// pasa por esta lista, y el diario tenia todo lo necesario para armarla.
    ///
    /// Lo unico que NO se puede recuperar es la direccion del telefono: eso no vive en el
    /// diario y solo lo dice la persona al presentarse. Por eso vuelven SIN direccion, que
    /// es la verdad -- estan adentro del bingo, pero todavia no se les puede hablar.
    /// </summary>
    private void RememberWhoWasHere(LiveFestival live)
    {
        foreach (HandedOut errand in ErrandsOf(live))
        {
            if (!errand.InSomebodysHands) continue;
            if (string.IsNullOrWhiteSpace(errand.TakenBy)) continue;
            if (string.IsNullOrWhiteSpace(errand.Mark)) continue;

            List<long> inHand = NumbersUnder(live, errand.Mark, "NumbersWith");
            List<long> sold = NumbersUnder(live, errand.Mark, "NumbersSoldBy");

            lock (_guard)
            {
                if (live.Attendees.Any(a => string.Equals(a.Mark, errand.Mark,
                    StringComparison.Ordinal))) continue;

                Attendee back = new Attendee
                {
                    FestivalId = live.Id,
                    Name = errand.TakenBy,
                    Duty = errand.Duty,
                    Mark = errand.Mark,
                };

                back.Cards.AddRange(inHand);
                back.Cards.Sort();
                back.Sold.AddRange(sold);

                live.Attendees.Add(back);
            }
        }
    }

    /// <summary>Saca del libro los primeros que queden, hasta cuantos se pidan; devuelve
    /// menos si el libro se acabo. Hay que llamarlo con el _guard tomado: no toca la red
    /// ni el diario.</summary>
    private static List<long> TakeFromTheBook(LiveFestival live, int howMany)
    {
        List<long> taken = live.InTheBook.Take(howMany).ToList();

        live.InTheBook.RemoveRange(0, taken.Count);

        return taken;
    }

    // ── la cartelera, derivada del diario ────────────────────────────────────

    public IReadOnlyList<string> Festivals
    {
        get { lock (_guard) return _order.ToArray(); }
    }

    private LiveFestival Live(string id)
    {
        lock (_guard)
        {
            return _live.TryGetValue(id, out LiveFestival live)
                ? live
                : throw new Refusal(Refusal.NoSuchFestival, Texts.T("organizador.no.sin-bingo-abierto", ("id", id)));
        }
    }

    public OpenFestival QrFor(string id) => QrFor(id, Duties.Plays);

    public OpenFestival QrFor(string id, string duty)
    {
        LiveFestival live = Live(id);

        return new OpenFestival
        {
            Name = Read(live.Journal.PerformQry("print festival.Name n;"), "n") ?? "Bingo",
            FestivalId = id,
            CommunityLeaderUrl = MyUrl,
            IrohTicket = TicketFor(live),
            Figures = string.Join(",", FiguresOf(id)),
            Duty = Duties.Clean(duty),
        };
    }

    // ── los papelitos: uno por persona, y rotan ──────────────────────────────

    private static string NewMark() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>El QR con la marca vigente para ese papel. Si no hay ninguna en la mesa,
    /// aca nace la siguiente: por eso el QR rota solo en cuanto alguien usa la anterior.
    /// QrFor se queda puro a proposito -- lo llaman la pantalla, el boton de compartir y
    /// CodeFor en CADA venta, y si cortar papelitos fuera efecto suyo cada venta le
    /// rotaria el pase al colaborador.</summary>
    public OpenFestival MarkedQrFor(string id, string duty)
    {
        OpenFestival open = QrFor(id, duty);

        if (!Duties.IsCollaborator(open.Duty)) return open;

        open.Mark = MarkOnOfferFor(Live(id), open.Duty);

        return open;
    }

    /// <summary>
    /// Cortar un papelito A NOMBRE de una persona, con los cartones que se le apartan, y
    /// devolver SU QR.
    ///
    /// Se pueden cortar varios seguidos sin esperar a que nadie escanee: antes habia UNO
    /// en la mesa por oficio y el siguiente nacia recien cuando alguien usaba el anterior,
    /// asi que el organizador tenia que quedarse al lado de cada persona mirandola
    /// escanear. Ahora escribe la lista de quienes lo ayudan y le pasa a cada una la suya.
    ///
    /// Si esa persona ya tiene uno en la mesa se le devuelve ESE y no se corta otro: el
    /// organizador que vuelve a la lista a compartirlo de nuevo no quiere un codigo nuevo,
    /// quiere el que ya le prometio.
    /// </summary>
    public OpenFestival EntrustTo(string id, string duty, string name, int howManyCards)
    {
        if (!Duties.IsCollaborator(duty))
            throw new InvalidOperationException(Texts.T("organizador.no.papelito-vende-o-canta"));

        string clean = (name ?? string.Empty).Trim();

        if (clean.Length == 0)
            throw new InvalidOperationException(Texts.T("organizador.no.dime-como-se-llama"));

        LiveFestival live = Live(id);

        MustStillBeGoing(live);

        // La marca directa y no el encargo: si no hay ninguno, ErrandOnOfferMeantFor
        // devuelve nada y pedirle el .Mark a esa nada revienta adentro del query.
        string already = Read(live.Journal.Using("print festival.MarkOnOfferMeantFor(@para) m;")
            .WithParameters(p => p.UserParameter<string>("para", clean))
            .PerformQuery(), "m");

        if (!string.IsNullOrWhiteSpace(already))
        {
            _say(Texts.T("organizador.log.codigo-sin-usar-mismo", ("nombre", clean)));

            return QrForMark(id, already);
        }

        string mark = NewMark();
        int cards = Duties.IsSeller(duty) && howManyCards > 0 ? howManyCards : 0;

        live.Journal.Using("elder.Entrust(festival, @oficio, @marca, @para, @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("oficio", Duties.AsChore(duty));
                p.UserParameter<string>("marca", mark);
                p.UserParameter<string>("para", clean);
                p.UserParameter<int>("cartones", cards);
            })
            .PerformCommand();

                _say(cards > 0
            ? Texts.T("organizador.log.corte-codigo-con-cartones", ("nombre", clean), ("oficio", Duties.Describe(duty)), ("n", cards.ToString()))
            : Texts.T("organizador.log.corte-codigo", ("nombre", clean), ("oficio", Duties.Describe(duty))));

        Changed?.Invoke();

        return QrForMark(id, mark);
    }

    /// <summary>El QR de UN papelito ya cortado. Es lo que deja compartirle a cada persona
    /// el suyo cuando el organizador quiera, sin cortar nada nuevo.</summary>
    public OpenFestival QrForMark(string id, string mark)
    {
        (string standing, string doing) = WhatIsThisMark(Live(id), mark);

        if (standing is null)
            throw new InvalidOperationException(Texts.T("organizador.no.codigo-desconocido"));

        OpenFestival open = QrFor(id, Duties.FromChore(doing));
        open.Mark = mark;
        open.MeantFor = MeantForOf(Live(id), mark);

        return open;
    }

    private string MeantForOf(LiveFestival live, string mark) =>
        Read(live.Journal.Using("print festival.ErrandMarked(@marca).MeantFor f;")
            .WithParameters(p => p.UserParameter<string>("marca", mark))
            .PerformQuery(), "f") ?? string.Empty;

    /// <summary>Los cartones que el organizador aparto para el que use este papelito. Cero
    /// si no aparto ninguno, o si el papelito es de antes de que se pudieran apartar.
    /// </summary>
    private int CardsMeantFor(LiveFestival live, string mark)
    {
        if (string.IsNullOrWhiteSpace(mark)) return 0;

        try
        {
            return AsInt(live.Journal.Using("print festival.ErrandMarked(@marca).CardsMeant c;")
                .WithParameters(p => p.UserParameter<string>("marca", mark))
                .PerformQuery(), "c");
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private string MarkOnOfferFor(LiveFestival live, string duty)
    {
        string chore = Duties.AsChore(duty);

        string offered = Read(live.Journal.Using("print festival.MarkOnOfferFor(@oficio) m;")
            .WithParameters(p => p.UserParameter<string>("oficio", chore))
            .PerformQuery(), "m");

        if (!string.IsNullOrWhiteSpace(offered)) return offered;

        string fresh = NewMark();

        live.Journal.Using("elder.Entrust(festival, @oficio, @marca);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("oficio", chore);
                p.UserParameter<string>("marca", fresh);
            })
            .PerformCommand();

        _say(Texts.T("organizador.log.corte-codigo-nuevo", ("oficio", Duties.Describe(duty)), ("codigo", fresh)));

        return fresh;
    }

    /// <summary>Lo que el diario sabe de un papelito. Vacio si no lo conoce: ErrandMarked
    /// revienta con una marca que el organizador nunca dio, y aca eso no es una falla.
    /// La marca y el nombre van como parametros y no pegados al texto: son entrada de
    /// afuera, y una consulta no se asienta, asi que parametrizarla es gratis.</summary>
    /// <summary>El numero con que se nombra a un colaborador delante de los demas:
    /// el orden en que el organizador le corto el papel. NO es la marca -- la marca
    /// autoriza, y por eso nunca sale del telefono de quien la recibio. Este numero
    /// no autoriza nada, asi que se puede decir en voz alta.</summary>
    private int NumberOf(LiveFestival live, string mark)
    {
        if (string.IsNullOrWhiteSpace(mark)) return 0;

        try
        {
            return AsInt(live.Journal.Using("print festival.ErrandMarked(@marca).Position n;")
                .WithParameters(p => p.UserParameter<string>("marca", mark))
                .PerformQuery(), "n");
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Mover cartones de la mesa solo antes de la primera bolita. Uno que entra despues
    /// nace mutilado y uno que sale deja al comprador sin lo que ya venia marcando.
    ///
    /// La guarda va en su PROPIO cuerpo con PerformCheckThenCommand, no adentro del
    /// comando. Escrita adentro es letra muerta: el comando corre igual, y asi estuvo
    /// la del cambio de carton desde que se escribio. Aca el rechazo pasa bajo el read
    /// lock, no deja asiento en el diario, y se detecta porque el retorno viene con
    /// texto -- no tira excepcion.
    /// </summary>
    private void MustBeBeforeTheFirstBall(LiveFestival live, string command,
        Action<Parameters> values, string andAlso = null)
    {
        string refused = live.Journal.Using(
            "Check(mesa.DrawnCount == 0) "
                        + "Error '" + Texts.ForDsl("organizador.no.ronda-empezada") + "';"
            + (andAlso ?? string.Empty),
            command)
            .WithParameters(values)
            .PerformCheckThenCommand();

        if (!string.IsNullOrWhiteSpace(refused))
            throw new InvalidOperationException(WhyItWasRefused(refused));
    }

    // ── las rondas: la agenda del padre ──────────────────────────────────────

    /// <summary>Anunciar una ronda, al crear el bingo o en el vuelo: los MISMOS
    /// comandos, con texto constante y todo por parametros. La ronda se anuncia vacia
    /// y cada promesa cae en ella (la ultima anunciada que siga en la mesa); si una
    /// promesa fallara a mitad, la ronda queda con menos promesas y se ve en la
    /// agenda -- no hay estado a medias escondido.</summary>
    private static void Announce(FestivalJournal journal, RoundPlan plan)
    {
        journal.Using("elder.AnnounceRound(festival);").PerformCommand();

        foreach (PromisePlan promise in plan.Promises)
        {
            journal.Using("elder.Promise(festival, @figura, @forma, @premio);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("figura", promise.Figure.Trim());
                    p.UserParameter<string>("forma", promise.IsCash ? "Cash" : "Goods");
                    p.UserParameter<string>("premio", promise.Prize.Trim());
                })
                .PerformCommand();
        }
    }

    /// <summary>
    /// Arranca la primera ronda anunciada que quede: el pueblo autoriza primero
    /// (BeginRound revienta si otra suena) y la mesa arranca despues.
    /// Las figuras van como parametro con la regla de HandTo: tipo declarado
    /// IEnumerable, valor List.
    /// </summary>
    private void BeginNextRound(FestivalJournal journal)
    {
        string playing = NextPlannedPlaying(journal)
            ??             throw new InvalidOperationException(
                Texts.T("organizador.no.sin-ronda-anunciada"));

        List<string> inPlay = playing.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0)
            .ToList();

        // El modo con que arranca es el elegido en la configuracion, leido del diario
        // en ESTE momento: elegirlo a mitad de una ronda vale desde la que sigue, que
        // es cuando StartRound vuelve a correr.
        //
        // Sin eleccion va 'Automatic', y el cambio de default no es un gusto. Con
        // 'OnClaim' la mesa no busca ganadores: los anota de a uno, cuando alguien canta.
        // Y el organizador nunca canta -- toca un boton que manda ClaimBingo y
        // ValidateWinner JUNTOS -- asi que el carton entra y sale de la lista de
        // pendientes en el mismo instante, la lista nunca llega a tener dos, y la figura
        // se cierra con el primero. El segundo que completo LA MISMA fila CON LA MISMA
        // BOLITA deja de poder ganar sin haber tenido turno.
        //
        // Con 'Automatic' la mesa los encuentra a todos al caer la bolita, se frena, y no
        // cierra la figura mientras quede alguien esperando al padre. Esta escrito en
        // BingoEngineTests/Games/TwoWinnersTests: las dos politicas, la misma
        // escena.
        //
        // El precio de esto es que la mesa SE FRENA de verdad: hasta que el padre entregue
        // o rechace, cantar otra bolita tira. La pantalla tiene que decirlo.
        string how = Read(journal.PerformQry("print festival.WinsAnnounced w;"), "w");

        if (string.IsNullOrWhiteSpace(how)) how = "Automatic";

        journal.Using("elder.BeginRound(festival); mesa.StartRound(@figurasEnJuego, @modo);")
            .WithParameters(p =>
            {
                p.UserParameter<IEnumerable<string>>("figurasEnJuego", inPlay);
                p.UserParameter<string>("modo", how);
            })
            .PerformCommand();
    }

    private static string NextPlannedPlaying(FestivalJournal journal)
    {
        int howMany = AsInt(journal.PerformQry("print festival.RoundCount n;"), "n");

        for (int position = 1; position <= howMany; position++)
        {
            string read = journal.Using(
                "{ print festival.RoundAt(@posicion).HowItStands s; "
                + "print festival.RoundAt(@posicion).Playing f; }")
                .WithParameters(p => p.UserParameter<int>("posicion", position))
                .PerformQuery();

            if (string.Equals(Read(read, "s"), "Planned", StringComparison.Ordinal))
                return Read(read, "f");
        }

        return null;
    }

    /// <summary>La agenda entera, leida del diario: sobrevive cualquier reinicio.</summary>
    public IReadOnlyList<RoundStatus> RoundsOf(string id)
    {
        LiveFestival live;

        lock (_guard)
        {
            if (!_live.TryGetValue(id, out live)) return Array.Empty<RoundStatus>();
        }

        int howMany = AsInt(live.Journal.PerformQry("print festival.RoundCount n;"), "n");
        List<RoundStatus> rounds = new();

        for (int position = 1; position <= howMany; position++)
        {
            string read = live.Journal.Using(
                "{ print festival.RoundAt(@posicion).Playing f; "
                + "print festival.RoundAt(@posicion).HowItStands s; "
                + "print festival.RoundAt(@posicion).PromiseCount q; }")
                .WithParameters(p => p.UserParameter<int>("posicion", position))
                .PerformQuery();

            int promiseCount = AsInt(read, "q");
            List<PromiseStatus> promises = new();

            for (int promise = 1; promise <= promiseCount; promise++)
            {
                string one = live.Journal.Using(
                    "{ print festival.RoundAt(@posicion).PromiseAt(@promesa).Playing f; "
                    + "print festival.RoundAt(@posicion).PromiseAt(@promesa).Prize z; "
                    + "print festival.RoundAt(@posicion).PromiseAt(@promesa).IsCash c; }")
                    .WithParameters(p =>
                    {
                        p.UserParameter<int>("posicion", position);
                        p.UserParameter<int>("promesa", promise);
                    })
                    .PerformQuery();

                promises.Add(new PromiseStatus
                {
                    Position = promise,
                    Playing = Read(one, "f") ?? string.Empty,
                    Prize = Read(one, "z") ?? string.Empty,
                    IsCash = AsBool(one, "c"),
                    Winners = NumbersIn(live.Journal.Using(
                        "foreach (n in festival.RoundAt(@posicion).PromiseAt(@promesa)"
                        + ".NumbersThatWon()) { print n 'c'; }")
                        .WithParameters(p =>
                        {
                            p.UserParameter<int>("posicion", position);
                            p.UserParameter<int>("promesa", promise);
                        })
                        .PerformQuery()),
                });
            }

            rounds.Add(new RoundStatus
            {
                Position = position,
                Playing = Read(read, "f") ?? string.Empty,
                Standing = Read(read, "s") ?? string.Empty,
                Promises = promises,
            });
        }

        return rounds;
    }

    /// <summary>La ronda que suena, o null si ninguna (entre rondas, o bingo viejo
    /// sin agenda).</summary>
    public RoundStatus CurrentRoundOf(string id) =>
        RoundsOf(id).FirstOrDefault(r => r.IsPlaying);

    /// <summary>Anunciar otra ronda en el vuelo, sin frenar la que suena.</summary>
    public void AnnounceAnotherRound(string festivalId, RoundPlan plan)
    {
        MustKnowThePlan(plan);

        LiveFestival live = Live(festivalId);

        Announce(live.Journal, plan);

                _say(Texts.T("organizador.log.anuncie-otra-ronda", ("que", string.Join("  ·  ", plan.Promises.Select(x => OnOffer.Called(x.Figure.Trim()) + " " + (x.IsCash ? "$" : string.Empty) + x.Prize.Trim())))));

        Changed?.Invoke();
    }

    public async Task BeginTheNextRoundAsync(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        BeginNextRound(live.Journal);

        RoundStatus current = CurrentRoundOf(festivalId)
            ?? throw new InvalidOperationException(Texts.T("organizador.no.ronda-no-encontrada"));

        _say(Texts.T("organizador.log.arranca-la-ronda", ("n", current.Position.ToString()), ("premios", current.PrizeLine)));

        await TellTheNewRoundAsync(live, current);

        Changed?.Invoke();
    }

    /// <summary>
    /// La ronda nueva les llega a los jugadores por DOS caminos con papeles distintos:
    /// el tell "Ronda" entra al diario de cada jugador por el MISMO topico que las
    /// bolitas, asi el corte queda ordenado con ellas -- una bolita vieja nunca cae en
    /// la ronda nueva; y el aviso de RoundNotice es solo para las pantallas (tambien la
    /// de quien canta), sin diario.
    /// </summary>
    private async Task TellTheNewRoundAsync(LiveFestival live, RoundStatus round)
    {
        try
        {
            Parameters payload = new Parameters();
            payload["figuras", typeof(string)] = round.Playing;

            await live.Transport.SendAsync(new TellEnvelope(
                Id: $"ronda-{WireNames.Safe(live.Id)}-{round.Position}",
                MessageName: "Ronda",
                Addressee: WireNames.PlayerRole(live.Id),
                AddresseeInstanceId: null,
                CausalEventId: null,
                ReactionName: null,
                Check: null,
                Values: payload));
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.ronda-sin-avisar", ("motivo", Texts.Explain(failure))));
        }

        await BroadcastTheRoundAsync(live,
            RoundNotice.BodyStarting(round.Position, round.Playing,
                round.Promises.Select(x => (x.Playing, x.Prize, x.IsCash))));
    }

    private async Task BroadcastTheRoundAsync(LiveFestival live, string body)
    {
        try
        {
            await _phone.ProduceAsync(RoundNotice.TopicFor(live.Id), "ronda", null, body);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.aviso-ronda-no-repartido", ("motivo", Texts.Explain(failure))));
        }
    }

    /// <summary>
    /// El padre la da por resuelta, con o sin ganadores. El pueblo decide como quedo
    /// (jugada o cancelada) y la mesa deja de aceptar bolitas. A los jugadores no se
    /// les corta nada aca: su ronda local se corta cuando arranque la siguiente, y
    /// mientras tanto no llegan bolitas porque la mesa del organizador esta parada.
    /// </summary>
    public async Task SettleTheRoundAsync(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        RoundStatus current = CurrentRoundOf(festivalId)
            ?? throw new InvalidOperationException(Texts.T("organizador.no.sin-ronda-que-cerrar"));

        live.Journal.Using("elder.SettleRound(festival); mesa.EndRound();").PerformCommand();

        // Figura por figura: cual entrego y cual quedo debiendo su premio.
                string tale = string.Join("; ", current.Promises.Select(x => x.WasKept
            ? Texts.T("organizador.log.figura-entregada", ("figura", OnOffer.Called(x.Playing)), ("premio", x.PrizeText), ("quienes", string.Join(", ", x.Winners)))
            : Texts.T("organizador.log.figura-sin-entregar", ("figura", OnOffer.Called(x.Playing)), ("premio", x.PrizeText))));

        _say(current.WinnerCount > 0
            ? Texts.T("organizador.log.cerre-la-ronda", ("n", current.Position.ToString()), ("cuento", tale))
            : Texts.T("organizador.log.cancele-la-ronda", ("n", current.Position.ToString()), ("cuento", tale)));

        await BroadcastTheRoundAsync(live, RoundNotice.BodySettled(current.Position));

        Changed?.Invoke();
    }

    /// <summary>Terminar todo. Las rondas que nunca se jugaron quedan anunciadas y nada
    /// mas: la historia es honesta. A los jugadores solo se les avisa para la pantalla:
    /// su mesa local queda como este, que ya no le llega nada.</summary>
    public async Task CloseTheFestivalAsync(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        live.Journal.Using("elder.CloseFestival(festival);").PerformCommand();

        _say(Texts.T("organizador.log.se-termino"));

        await BroadcastTheRoundAsync(live, RoundNotice.BodyOver());

        Changed?.Invoke();
    }

    /// <summary>
    /// Sembrar publico DE ENSAYO: una vendedora de la casa toma un papelito, recibe
    /// un carton por persona y se los vende, todo asentado con los MISMOS comandos
    /// que usa la venta real -- papelito, take, entrega, venta y carton en juego.
    /// Es lo que le da publico al que ensaya a cantar: con el anuncio en 'Automatic',
    /// la mesa se frena sola cuando uno de estos cartones completa la figura.
    /// Solo tiene sentido en un ensayo; un bingo real vende por sus colaboradores.
    /// </summary>
    public IReadOnlyList<(string Buyer, long Card)> SeedTheAudience(
        string festivalId, IReadOnlyList<string> buyers)
    {
        List<(string, long)> seeded = new();

        if (buyers is null || buyers.Count == 0) return seeded;

        LiveFestival live = Live(festivalId);

        string mark = NewMark();

        live.Journal.Using("elder.Entrust(festival, @oficio, @marca, @para, @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("oficio", Duties.AsChore(Duties.Sells));
                p.UserParameter<string>("marca", mark);
                p.UserParameter<string>("para", "La casa");
                p.UserParameter<int>("cartones", 0);
            })
            .PerformCommand();

        live.Journal.Using("errand = festival.ErrandMarked(@marca); elder.HandTheErrandTo(festival, errand, @nombre);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", mark);
                p.UserParameter<string>("nombre", "La casa");
            })
            .PerformCommand();

        List<long> taken;

        lock (_guard)
        {
            taken = TakeFromTheBook(live, buyers.Count);
        }

        if (taken.Count < buyers.Count)
            _say(Texts.T("organizador.log.libro-dio-pocos", ("n", taken.Count.ToString()), ("cuantos", buyers.Count.ToString())));

        for (int at = 0; at < taken.Count; at++)
        {
            string buyer = buyers[at];
            long card = taken[at];

            live.Journal.Using(
                "elder.HandTo(festival, festival.MerchantMarked(@marca), @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", mark);
                    p.UserParameter<IEnumerable<long>>("cartones", new List<long> { card });
                })
                .PerformCommand();

            live.Journal.Using(@"
{
    comprador = Villager(@nombre, @contacto, @marcaComprador);
    festival.MerchantMarked(@marca).SellThemTo(festival, @cartones, comprador);
    foreach (numero in @cartones) {
        mesa.PutCardInPlay(numero);
    }
}")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", mark);
                    p.UserParameter<string>("nombre", buyer);
                    p.UserParameter<string>("contacto", "en el bingo");
                    p.UserParameter<string>("marcaComprador", BuyerMark.Mint());
                    p.UserParameter<IEnumerable<long>>("cartones", new List<long> { card });
                })
                .PerformCommand();

            lock (_guard)
            {
                if (!live.SoldTo.TryGetValue(buyer, out List<long> theirs))
                    live.SoldTo[buyer] = theirs = new List<long>();

                if (!theirs.Contains(card)) theirs.Add(card);
            }

            _say(Texts.T("organizador.log.sembre-carton", ("carton", card.ToString()), ("nombre", buyer)));

            seeded.Add((buyer, card));
        }

        Changed?.Invoke();

        return seeded;
    }

    /// <summary>Si la mesa esta frenada esperando que alguien resuelva un bingo. Es lo
    /// que el modo 'Automatic' hace solo cuando un carton completa su figura.</summary>
    public bool TableAwaitsValidation(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        string state = Read(live.Journal.PerformQry("print mesa.State s;"), "s");

        return string.Equals(state, "AwaitingValidation", StringComparison.Ordinal);
    }

    /// <summary>
    /// Desmontar un bingo DEL TODO: parar su diario y borrar su carpeta, como se
    /// desarma la escenografia al terminar la obra. Solo tiene sentido para un ENSAYO:
    /// un bingo de verdad se cierra con CloseTheFestivalAsync y su diario queda, que
    /// es el registro de la noche. El diario se apaga ANTES de tocar el disco --
    /// borrarle la carpeta con el Stage vivo es corromperlo a medio morir.
    /// </summary>
    public void StrikeTheFestival(string festivalId)
    {
        LiveFestival live;

        lock (_guard)
        {
            _live.TryGetValue(festivalId, out live);
            _live.Remove(festivalId);
            _order.Remove(festivalId);
        }

        if (live is not null)
        {
            try { live.Journal.Dispose(); }
            catch (Exception failure)
            {
                _say(Texts.T("organizador.log.ensayo-no-se-apago", ("motivo", Texts.Explain(failure))));
            }
        }

        // La carpeta se busca aunque el village nunca haya arrancado en este proceso:
        // con _root nulo el ensayo quedaba borrado de la lista pero VIVO en el disco,
        // y en el proximo arranque volvia a abrirse como un fantasma.
        string root = _root ?? Path.Combine(FileSystem.AppDataDirectory, "festivals");
        string folder = Path.Combine(root, festivalId);

        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.ensayo-carpeta-no-borrada", ("motivo", Texts.Explain(failure))));
        }

        _say(Texts.T("organizador.log.bingo-desmontado", ("id", festivalId)));

        Changed?.Invoke();
    }

    /// <summary>
    /// El BINGO validado, de verdad y journaleado: la mesa primero -- es la autoridad de
    /// si el bingo es real, y revienta si el carton no completo la figura -- y el pueblo
    /// despues, que escribe quien se lleva el premio de ESA figura. Dos comandos:
    /// si el pueblo no pudiera escribir al ganador, la figura ya quedo cerrada igual.
    /// </summary>
    public void GiveThePrize(string festivalId, long card, string figure)
    {
        LiveFestival live = Live(festivalId);

        live.Journal.Using(
            "tableCard = mesa.CardInPlay(@carton); figure = mesa.FigureInPlay(@figura); "
            + "mesa.ClaimBingo(tableCard, figure); mesa.ValidateWinner(tableCard, figure);")
            .WithParameters(p =>
            {
                p.UserParameter<int>("carton", (int)card);
                p.UserParameter<string>("figura", figure);
            })
            .PerformCommand();

        // Solo si hay ronda sonando (un bingo de antes de las rondas no tiene a
        // quien anotarle el premio), y sin nombrar dos veces al mismo carton en la
        // misma figura.
        int position = AsInt(live.Journal.PerformQry("print festival.CurrentRoundPosition n;"), "n");

        bool alreadyNamed = position > 0 && AsBool(live.Journal.Using(
            "print festival.RoundAt(@posicion).PromiseOn(@figura).Won(@carton) g;")
            .WithParameters(p =>
            {
                p.UserParameter<int>("posicion", position);
                p.UserParameter<string>("figura", figure);
                p.UserParameter<long>("carton", card);
            })
            .PerformQuery(), "g");

        if (position > 0 && !alreadyNamed)
        {
            live.Journal.Using("card = festival.ParticipationNumbered(@carton); figurePromise = festival.PromiseInPlayOn(@figura); elder.NameWinner(festival, card, figurePromise);")
                .WithParameters(p =>
                {
                    p.UserParameter<int>("carton", (int)card);
                    p.UserParameter<string>("figura", figure);
                })
                .PerformCommand();
        }

                _say(Texts.T("organizador.log.bingo-validado", ("carton", card.ToString()), ("figura", OnOffer.Called(figure))));

        LowerTheHand(live, card, figure);

        Changed?.Invoke();
    }

    /// <summary>El carton estaba mal: la mesa lo deja anotado (WasDismissed) para que el
    /// letrero no se vuelva a prender con el mismo papel.</summary>
    public void TheCardWasWrong(string festivalId, long card, string figure)
    {
        LiveFestival live = Live(festivalId);

        live.Journal.Using(
            "tableCard = mesa.CardInPlay(@carton); figure = mesa.FigureInPlay(@figura); "
            + "mesa.ClaimBingo(tableCard, figure); mesa.DismissWinner(tableCard, figure);")
            .WithParameters(p =>
            {
                p.UserParameter<int>("carton", (int)card);
                p.UserParameter<string>("figura", figure);
            })
            .PerformCommand();

        _say(Texts.T("organizador.log.carton-no-gana", ("carton", card.ToString()), ("figura", OnOffer.Called(figure))));

        LowerTheHand(live, card, figure);

        Changed?.Invoke();
    }

    public bool IsOver(string id) =>
        AsBool(Live(id).Journal.PerformQry("print festival.IsOver b;"), "b");

    /// <summary>Cuantos cartones se vendieron, segun el diario. NO es la suma de lo que
    /// tienen los asistentes: quien compro y nunca escaneo su codigo no esta en esa
    /// lista, y ya pago.</summary>
    public int SoldCount(string id) =>
        AsInt(Live(id).Journal.PerformQry("print festival.SoldCount n;"), "n");

    /// <summary>Un bingo cerrado no acepta mas verbos. La consulta va antes del
    /// _guard, como MustBeAlive.</summary>
    private void MustStillBeGoing(LiveFestival live)
    {
        if (MustReadWhether(live.Journal.PerformQry("print festival.IsOver b;"), "b"))
            throw new Refusal(Refusal.Closed, Texts.T("rechazo.funcion_terminada"));
    }

    /// <summary>
    /// El motivo, en cristiano. El sustrato contesta el rechazo como JSON --
    /// {"EWI":[{"Error":"ya empezo la ronda"}]} -- y eso no se le puede poner enfrente
    /// a una senora en la puerta de la iglesia.
    /// </summary>
    private static string WhyItWasRefused(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    foreach (JsonProperty reason in item.EnumerateObject())
                    {
                        if (reason.Value.ValueKind != JsonValueKind.String) continue;

                        string said = reason.Value.GetString();

                        if (!string.IsNullOrWhiteSpace(said)) return said;
                    }
                }
            }
        }
        catch (Exception)
        {
        }

        return json;
    }

    private (string Standing, string Doing) WhatIsThisMark(LiveFestival live, string mark)
    {
        try
        {
            string read = live.Journal.Using(
                "{ print festival.ErrandMarked(@marca).HowItStands s; "
                + "print festival.ErrandMarked(@marca).Doing d; }")
                .WithParameters(p => p.UserParameter<string>("marca", mark))
                .PerformQuery();

            return (Read(read, "s"), Read(read, "d"));
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// El papelito con el que dice venir tiene que ser el suyo y tiene que estar vivo. Es
    /// lo que cierra la puerta de atras: hasta ahora /vendi/ y /cante/ le creian el NOMBRE
    /// y nada mas, asi que cualquiera que supiera el id del bingo y como se llama la
    /// vendedora podia vender por ella.
    /// Se llama ANTES de tomar el _guard: son consultas al diario, y bajo el lock que la
    /// pantalla tambien toma eso congelaria la UI. La ventana que queda es de milisegundos
    /// y lo peor que deja pasar es una operacion en el instante exacto de la revocacion.
    /// </summary>
    /// <summary>
    /// Cual colaboradora es esta, entre las que estan adentro.
    ///
    /// Por el papelito, que es lo que MustBeAlive acaba de validar una linea antes, y no por
    /// el nombre. Con dos Marias, buscar por nombre le entrega a la segunda el asistente de
    /// la primera: la venta se asienta bajo el papelito equivocado y, peor, el buzon de
    /// custodia se le desvia al telefono de la segunda -- la primera deja de recibir sus
    /// cartones sin un solo error a la vista.
    ///
    /// El nombre queda de respaldo para los bingos de antes de los papelitos, donde no hay
    /// otra cosa con que distinguir.
    /// </summary>
    private Attendee WhoIsThisCollaborator(LiveFestival live, string name, string mark)
    {
        lock (_guard)
        {
            if (!string.IsNullOrWhiteSpace(mark))
            {
                Attendee byMark = live.Attendees.FirstOrDefault(a => a.IsCollaborator
                    && string.Equals(a.Mark, mark, StringComparison.Ordinal));

                if (byMark is not null) return byMark;
            }

            return live.Attendees.FirstOrDefault(a => a.IsCollaborator
                && string.IsNullOrWhiteSpace(a.Mark)
                && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void MustBeAlive(LiveFestival live, string name, string mark)
    {
        // Bingo de antes de los papelitos: entra como entraba, sin codigo. Se pregunta con
        // MustRead y no con AsInt porque ese cero es la llave de esta puerta: si viniera de
        // una lectura fallida, la puerta se abriria sola.
        if (MustRead(live.Journal.PerformQry("print festival.ErrandCount n;"), "n") == 0) return;

        if (string.IsNullOrWhiteSpace(mark))
                        throw new Refusal(Refusal.NoMark,
                Texts.T("organizador.no.sin-codigo"));

        (string standing, _) = WhatIsThisMark(live, mark);

        if (standing is null)
            throw new Refusal(Refusal.NoMark, Texts.T("organizador.no.codigo-no-es-mio"));

        if (string.Equals(standing, "SentHome", StringComparison.Ordinal))
            throw new Refusal(Refusal.SentHome, Texts.T("organizador.no.te-saque"));

        if (!MarkBelongsTo(live, mark, name))
            throw new Refusal(Refusal.NotYourMark, Texts.T("rechazo.no_es_tuya"));
    }

    private bool MarkBelongsTo(LiveFestival live, string mark, string name)
    {
        try
        {
            string read = live.Journal.Using(
                "print festival.ErrandBelongsTo(@marca, @nombre) g;")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", mark);
                    p.UserParameter<string>("nombre", name);
                })
                .PerformQuery();

            return AsBool(read, "g");
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<string> FiguresOf(string id)
    {
        LiveFestival live = Live(id);

        int howMany = AsInt(live.Journal.PerformQry("print mesa.FiguresInPlayCount n;"), "n");
        List<string> figures = new();

        for (int position = 1; position <= howMany; position++)
        {
            string figure = Read(live.Journal.Using("print mesa.FigureInPlayAt(@posicion) f;")
                .WithParameters(p => p.UserParameter<int>("posicion", position))
                .PerformQuery(), "f");

            if (figure is not null) figures.Add(figure);
        }

        return figures;
    }

    public IReadOnlyList<FestivalStatus> Listing()
    {
        List<FestivalStatus> rows = new();

        foreach (string id in Festivals)
        {
            LiveFestival live;

            try
            {
                live = Live(id);
            }
            catch (Exception)
            {
                continue;
            }

            string name = "Bingo";
            string state = "?";
            int draws = 0, sold = 0, rounds = 0, current = 0, cards = 0;
            long price = 0;
            string playsOn = string.Empty, doorsOpenAt = string.Empty;

            try
            {
                string read = live.Journal.PerformQry(
                    "{ print festival.Name n; print mesa.State s; "
                    + "print mesa.DrawnCount b; print festival.SoldCount v; "
                    + "print festival.ParticipationCount c; "
                    + "print festival.CardPrice cp; "
                    + "print festival.Day.Year dy; print festival.Day.Month dm; "
                    + "print festival.Day.Day dd; print festival.DoorsOpenAt ho; "
                    + "print festival.RoundCount r; print festival.CurrentRoundPosition p; "
                    + "print festival.IsOver o; }");

                name = Read(read, "n") ?? name;
                state = AsBool(read, "o") ? "Over" : Read(read, "s") ?? state;
                draws = AsInt(read, "b");
                sold = AsInt(read, "v");
                cards = AsInt(read, "c");
                price = AsLong(read, "cp");

                // El dia se arma de sus tres numeros y no de un texto: el diario no
                // serializa objetos, y una fecha partida en tres no depende del formato
                // con que este puesto el telefono.
                int year = AsInt(read, "dy");
                if (year > 0)
                {
                    playsOn = new DateTime(year, AsInt(read, "dm"), AsInt(read, "dd"))
                        .ToString("yyyy-MM-dd");
                }

                doorsOpenAt = Read(read, "ho") ?? string.Empty;
                rounds = AsInt(read, "r");
                current = AsInt(read, "p");
            }
            catch (Exception failure)
            {
                _say(Texts.T("organizador.log.no-pude-leer-bingo", ("id", id), ("motivo", Texts.Explain(failure))));
            }

            rows.Add(new FestivalStatus
            {
                Id = id,
                Name = name,
                Figures = string.Join(", ", FiguresOf(id)),
                CreatedAt = BornAt(id),
                State = state,
                Draws = draws,
                SoldCards = sold,
                Cards = cards,
                CardPrice = price,
                PlaysOn = playsOn,
                DoorsOpenAt = doorsOpenAt,
                People = AttendeesOf(id).Count,
                Rounds = rounds,
                CurrentRound = current,
            });
        }

        return rows;
    }

    /// <summary>El id es un sello de tiempo: la fecha de creacion no hay que guardarla.</summary>
    private static string BornAt(string id) =>
        DateTime.TryParseExact(id, "yyMMdd-HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime when)
            ? when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : id;

    // ── la puerta: alistarse ─────────────────────────────────────────────────

    private string Enlist(string whatTheySay)
    {
        JoinRequest who = JoinRequest.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(who.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.sin-bingo-de-donde"));
        if (string.IsNullOrWhiteSpace(who.Name))
            throw new InvalidOperationException(Texts.T("organizador.no.sin-nombre"));
        if (string.IsNullOrWhiteSpace(who.ListensAt))
            throw new InvalidOperationException(Texts.T("organizador.no.sin-donde-escucha"));

        LiveFestival live = Live(who.FestivalId);

        MustStillBeGoing(live);

        return who.IsCollaborator ? EnlistCollaborator(live, who) : EnlistPlayer(live, who);
    }

    /// <summary>La puerta antes de la puerta: el papelito. NO exige que este sin usar --
    /// quien vuelve a entrar tocando su bingo guardada manda el MISMO papelito que ya
    /// reclamo, y tiene que pasar, porque ese es el camino de vuelta cuando al
    /// organizador se le reinicio la app. Devuelve la marca con la que entro.</summary>
    private string CheckTheMark(LiveFestival live, JoinRequest who, string duty)
    {
        (string standing, string doing) = WhatIsThisMark(live, who.Mark);

        if (standing is null)
        {
            // El diario no conoce esa marca: codigo viejo, o el organizador se reinicio y
            // perdio los encargos. Si el diario igual reconoce a la persona la dejo pasar:
            // dejar afuera a quien tiene cartones en la mano es peor que un pase vencido.
            if (Duties.IsSeller(duty)
                && (CustodyOf(live, who.Name, who.Mark).Count > 0
                    || SoldAny(live, who.Name, who.Mark)))
            {
                                _say(Texts.T("organizador.log.codigo-viejo-la-dejo-pasar", ("nombre", who.Name)));
                return null;
            }

                        throw new Refusal(Refusal.NoMark,
                Texts.T("organizador.no.codigo-no-es-mio-pide-otro"));
        }

        if (string.Equals(standing, "SentHome", StringComparison.Ordinal))
            throw new Refusal(Refusal.SentHome, Texts.T("rechazo.mandado_a_casa"));

        if (!string.Equals(doing, Duties.AsChore(duty), StringComparison.Ordinal))
                        throw new Refusal(Refusal.OtherDuty,
                Texts.T("organizador.no.codigo-de-otro-oficio", ("oficio", Duties.Describe(Duties.FromChore(doing))), ("tuyo", Duties.Describe(duty))));

        if (string.Equals(standing, "Taken", StringComparison.Ordinal))
        {
            if (MarkBelongsTo(live, who.Mark, who.Name)) return who.Mark;

                        throw new Refusal(Refusal.MarkTaken,
                Texts.T("rechazo.marca_tomada"));
        }

        // Estaba en la mesa: lo toma. En cuanto lo toma, el que pida el QR se lleva otro.
        live.Journal.Using("errand = festival.ErrandMarked(@marca); elder.HandTheErrandTo(festival, errand, @nombre);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", who.Mark);
                p.UserParameter<string>("nombre", who.Name);
            })
            .PerformCommand();

        _say(Texts.T("organizador.log.uso-el-codigo", ("nombre", who.Name), ("codigo", who.Mark)));

        return who.Mark;
    }

    /// <summary>La ronda vigente y las bolitas que ya salieron, para la respuesta del
    /// enlist: la palabra del organizador vale mas que el QR, que se imprimio cuando
    /// el bingo era joven y la ronda era otra. El premio va figura por figura.</summary>
    private (int Ronda, string SeJuega, List<object> Premios, IReadOnlyList<int> Bolitas,
        string Anuncio) RoundForTheWire(LiveFestival live)
    {
        RoundStatus current = CurrentRoundOf(live.Id);

        List<object> premios = current is null
            ? new List<object>()
            : current.Promises
                .Select(x => (object)new { figura = x.Playing, premio = x.Prize, esCash = x.IsCash })
                .ToList();

        return (
            current?.Position ?? 0,
            current?.Playing ?? string.Join(",", FiguresOf(live.Id)),
            premios,
            Drawn(live.Id),
            HowWinsAreAnnounced(live));
    }

    /// <summary>
    /// Como se anuncia un bingo en esta funcion, para quien juega.
    ///
    /// Importa del lado del jugador: con 'Automatic' la mesa se frena sola en cuanto un
    /// carton completa, asi que cantar no hace falta y el megafono seria un boton que no
    /// cambia nada. Cuando lo canta la gente ('OnClaim', que es lo de siempre), la mesa
    /// NO busca ganadores -- quien no canta no gana --, y ahi el megafono es la unica
    /// forma que tiene el jugador de reclamar desde el telefono.
    /// </summary>
    private string HowWinsAreAnnounced(LiveFestival live)
    {
        try
        {
            string how = Read(live.Journal.PerformQry("print festival.WinsAnnounced w;"), "w");

            return string.IsNullOrWhiteSpace(how) ? "OnClaim" : how;
        }
        catch (Exception)
        {
            // Un bingo de antes de que esto existiera se comporta como siempre: lo canta
            // la gente. Es el modo en el que cantar SI hace falta, asi que ante la duda
            // el megafono queda encendido y no apagado.
            return "OnClaim";
        }
    }


    /// <summary>
    /// Le abre la puerta del corrillo a quien acaba de alistarse y devuelve su pase.
    ///
    /// Se acuña ACA y no antes: la direccion de una invitacion lleva la IP adentro, asi que
    /// un pase repartido de vispera apunta a donde el organizador ya no esta. Y como la
    /// invitacion es de un solo uso, cada quien se lleva la suya.
    ///
    /// La espera queda corriendo sola: si ella no se pega, no pasa nada -- el pase caduca
    /// con la app y el circuito de HTTP sigue funcionando igual que hoy.
    /// </summary>
    private string PassForTheHuddle(LiveFestival live)
    {
        try
        {
            Huddle.HuddleStage.OpenDoor door =
                live.Journal.OpenTheDoorAsync(_life.Token).GetAwaiter().GetResult();

            _ = Task.Run(async () =>
            {
                try { await live.Journal.WelcomeAsync(door); }
                catch (Exception failure) { _say(Texts.T("organizador.log.no-pude-pegarme", ("motivo", Texts.Explain(failure)))); }
            });

            return door.Pass.ToLink();
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.puerta-corrillo", ("motivo", Texts.Explain(failure))));
            return null;
        }
    }

    private string EnlistCollaborator(LiveFestival live, JoinRequest who)
    {
        string duty = Duties.Clean(who.Duty);
        string mark = CheckTheMark(live, who, duty);
        Attendee attendee;

        attendee = WhoIsThisCollaborator(live, who.Name, who.Mark);

        if (attendee is not null)
        {
            if (!string.Equals(attendee.Duty, duty, StringComparison.Ordinal))
            {
                                throw new InvalidOperationException(
                    Texts.T("organizador.no.entro-con-otro-oficio", ("nombre", attendee.Name), ("oficio", Duties.Describe(attendee.Duty)), ("otro", Duties.Describe(duty))));
            }

            // El topico propio se le rutea a cualquier colaborador, tambien a quien canta:
            // es el unico camino de vuelta que el organizador tiene hacia su telefono.
            _phone.RouteTopicTo(CustodyNotice.TopicFor(live.Id, attendee.Handle), who.ListensAt);
            _phone.RouteTopicTo(RoundNotice.TopicFor(live.Id), who.ListensAt);
            _phone.RouteTopicTo(DrawNotice.TopicFor(live.Id), who.ListensAt);

            // Aca se rellena la direccion de quien volvio del diario sin ninguna, y se
            // refresca la de quien vuelve desde otra: es lo que la vuelve alcanzable.
            attendee.ListensAt = who.ListensAt;

            _say(Texts.T("organizador.log.se-volvio-a-presentar", ("nombre", attendee.Name), ("n", attendee.Cards.Count.ToString())));
            Changed?.Invoke();

            (int ronda, string sejuega, List<object> premios, IReadOnlyList<int> bolitas,
                string anuncio) = RoundForTheWire(live);

            return new
            {
                cartones = attendee.Cards,
                custodia = true,
                rol = attendee.Duty,
                numero = NumberOf(live, attendee.Mark),
                vendidos = SoldOf(live, attendee),
                ronda,
                sejuega,
                premios,
                bolitas,
                anuncio,
                pase = PassForTheHuddle(live),
            }.ToText();
        }

        // El diario puede conocerlo aunque la memoria no (el organizador se reinicio).
        // Solo se le pregunta a quien vende: el papelito de quien canta no tiene
        // comerciante detras, y pedirle sus cartones al pueblo es pedirle algo que no
        // existe -- se niega, con razon, y con eso se cae la entrada entera.
        bool sells = Duties.IsSeller(duty);

        List<long> fromJournal = sells ? CustodyOf(live, who.Name, mark) : new List<long>();
        bool alreadyKnown = sells
            && (fromJournal.Count > 0 || SoldAny(live, who.Name, mark));
        List<long> handed = new();

        lock (_guard)
        {
            attendee = new Attendee
            {
                FestivalId = live.Id,
                Name = who.Name,
                ListensAt = who.ListensAt,
                Duty = duty,
                Mark = mark,
            };

            if (alreadyKnown)
            {
                attendee.Cards.AddRange(fromJournal);
            }
            else if (Duties.IsSeller(duty))
            {
                // Solo quien vende recibe talonario. El que canta no toca cartones.
                // Aca se RESERVAN nomas: salen del libro bajo el lock para que dos que
                // entren a la vez no se lleven los mismos, pero no se le anotan a nadie
                // hasta que el diario los haya aceptado.
                //
                // Cuantos: los que el organizador le aparto al cortarle el codigo, y si no
                // le aparto ninguno, el lote de siempre. Asi la cantidad se decide cuando
                // se arma la lista de quienes ayudan, que es cuando el organizador sabe a
                // quien le va a tocar vender mas.
                int apart = CardsMeantFor(live, mark);

                handed.AddRange(TakeFromTheBook(live, apart > 0 ? apart : CardsPerSeller));

                if (handed.Count > 0) live.HandoutsInFlight++;
            }

            live.Attendees.Add(attendee);
        }

        if (alreadyKnown)
        {
            _say(Texts.T("organizador.log.volvio-recordado", ("nombre", attendee.Name), ("n", attendee.Cards.Count.ToString())));
        }
        else if (attendee.IsSeller)
        {
            // El asiento va ANTES de anotarselos y antes de contestarle al telefono, y se
            // espera. Al reves -- que era como estaba -- una entrega que el diario rechaza
            // deja su taco lleno en la pantalla y los cartones fuera del libro: al primer
            // reinicio vuelven al libro, se los llevan otros, y dos personas terminan con
            // el mismo carton en la mano.
            if (handed.Count > 0)
            {
                bool wroteItDown = WriteDownTheHandover(live, attendee, handed);

                lock (_guard)
                {
                    if (!wroteItDown) live.InTheBook.InsertRange(0, handed);

                    // La reserva termino: el diario la acepto, o los cartones volvieron
                    // al libro. En cualquiera de los dos casos el libro en memoria ya
                    // vuelve a poder rearmarse leyendo el diario.
                    live.HandoutsInFlight--;
                }

                if (!wroteItDown) handed.Clear();
            }

            attendee.Cards.AddRange(handed);

                        _say(handed.Count > 0
                ? Texts.T("organizador.log.llego-a-vender", ("nombre", attendee.Name), ("cuales", string.Join(", ", handed)))
                : Texts.T("organizador.log.llego-sin-cartones", ("nombre", attendee.Name)));
        }
        else
        {
            _say(Texts.T("organizador.log.llego-a-cantar", ("nombre", attendee.Name)));
        }

        _phone.RouteTopicTo(CustodyNotice.TopicFor(live.Id, attendee.Handle), attendee.ListensAt);
        _phone.RouteTopicTo(RoundNotice.TopicFor(live.Id), attendee.ListensAt);
        _phone.RouteTopicTo(DrawNotice.TopicFor(live.Id), attendee.ListensAt);

        Changed?.Invoke();

        (int laRonda, string queSeJuega, List<object> losPremios, IReadOnlyList<int> lasBolitas,
            string elAnuncio) = RoundForTheWire(live);

        return new
        {
            cartones = attendee.Cards,
            custodia = true,
            rol = attendee.Duty,
            numero = NumberOf(live, attendee.Mark),
            vendidos = SoldOf(live, attendee),
            ronda = laRonda,
            sejuega = queSeJuega,
            premios = losPremios,
            bolitas = lasBolitas,
            pase = PassForTheHuddle(live),
        }.ToText();
    }

    /// <summary>
    /// Gana el primero: un codigo de compra vale para UN aparato.
    ///
    /// Presentarse de nuevo desde el MISMO telefono es normal y tiene que seguir andando --
    /// pasa cada vez que se abre la app --, por eso no alcanza con contar presentaciones: hay
    /// que mirar de que aparato viene. Si viniera de otro, los dos verian el mismo carton y
    /// los dos creerian haber ganado.
    ///
    /// Sin numero de aparato no se puede decidir nada, y se deja pasar: es un jugador de
    /// antes de los numeros, y negarle la entrada seria peor que el riesgo.
    /// </summary>
    private static void MustBeTheSameHandset(Attendee attendee, JoinRequest who)
    {
        if (string.IsNullOrWhiteSpace(attendee.Handset)) return;
        if (string.IsNullOrWhiteSpace(who.Handset)) return;
        if (string.Equals(attendee.Handset, who.Handset, StringComparison.Ordinal)) return;

                throw new Refusal(Refusal.AnotherHandset,
            Texts.T("organizador.no.otro-aparato-de", ("nombre", attendee.Name)));
    }

    private string EnlistPlayer(LiveFestival live, JoinRequest who)
    {
        Attendee attendee;
        bool isNew;

        CardsAtTheDoor showing;

        // Una sola vez y arriba de todo: abajo se compara letra por letra en cuatro lugares
        // distintos, y si uno normalizara y otro no, la misma persona seria dos.
        who.Mark = BuyerMark.Normalize(who.Mark);

        // Lo que el diario le tiene comprado a esa marca, preguntado ANTES del lock porque
        // bajo el lock no se le habla al diario. Es la misma pregunta que la puerta de la
        // colaboradora hace dos metodos mas arriba; sin ella, a quien le vendieron con el
        // telefono cerrado no hay con que notarle la falta: no lo trae, y anotado ya esta.
        List<long> whatTheJournalSays = who.Mark.Length == 0
            ? new List<long>()
            : CardsBoughtBy(live, who.Mark);

        lock (_guard)
        {
            // Por la marca, no por el nombre: dos personas pueden llamarse igual, pero
            // cada una trae la suya. El nombre solo sirve de respaldo para los codigos de
            // antes de las marcas.
            string mark = who.Mark;

            attendee = mark.Length == 0
                ? live.Attendees.FirstOrDefault(a => !a.IsCollaborator
                    && string.IsNullOrWhiteSpace(a.Mark)
                    && string.Equals(a.Name, who.Name, StringComparison.OrdinalIgnoreCase))
                : live.Attendees.FirstOrDefault(a => !a.IsCollaborator
                    && string.Equals(a.Mark, mark, StringComparison.Ordinal));

            isNew = attendee is null;

            if (!isNew)
            {
                MustBeTheSameHandset(attendee, who);

                attendee.ListensAt = who.ListensAt;

                // El aparato se anota tarde si el jugador venia de una version sin numero de
                // aparato: el primero que lo diga se queda con el codigo.
                if (string.IsNullOrWhiteSpace(attendee.Handset)) attendee.Handset = who.Handset;
            }

            if (isNew)
            {
                // Sin nombre propio se toma el que el diario ya tiene para SU ID. La
                // identidad es el numero, no el nombre -- asi lo dice el carnet -- y el
                // nombre es una etiqueta que ya escribio quien le vendio. La pagina del
                // jugador no lo exige para entrar, asi que un telefono recien instalado
                // llegaba vacio y el organizador lo veia sin nombre teniendolo escrito.
                string named = string.IsNullOrWhiteSpace(who.Name) && whatTheJournalSays.Count > 0
                    ? NameOfWhoeverBought(live, whatTheJournalSays[0]) ?? string.Empty
                    : who.Name;

                attendee = new Attendee
                {
                    FestivalId = live.Id,
                    Name = named,
                    ListensAt = who.ListensAt,
                    Duty = Duties.Plays,
                    Mark = mark,
                    Handset = who.Handset,
                };

                // Al jugador lo mete quien le vendio: su codigo trae los cartones
                // que compro. Sin cartones no hay a quien reconocer.
                if (who.Cards is null or { Count: 0 })
                {
                                        throw new InvalidOperationException(
                        Texts.T("organizador.no.codigo-sin-cartones"));
                }
            }

            showing = CardsAtTheDoor.Sort(
                attendee.Cards, who.Cards, card => WasSoldTo(live, card, who), whatTheJournalSays);

            if (isNew && showing.New.Count == 0 && showing.Missing.Count == 0)
            {
                                throw new InvalidOperationException(
                    Texts.T("organizador.no.cartones-no-vendidos-a-ti"));
            }

            attendee.Cards.AddRange(showing.New);
            // Lo que el diario le tiene y su telefono no trae entra igual que lo nuevo: si
            // ya estaba anotado no se duplica, y si no, esta es la unica oportunidad de
            // ponerselo.
            foreach (long card in showing.Missing)
                if (!attendee.Cards.Contains(card)) attendee.Cards.Add(card);

            attendee.Cards.Sort();

            if (isNew) live.Attendees.Add(attendee);
        }

        // El ruteo se rehace SIEMPRE, no solo la primera vez. Quien se vuelve a presentar
        // puede venir desde otra direccion -- se le cayo la app, cambio de red, le prestaron
        // el telefono a otra --, y con la direccion vieja el organizador le seguiria hablando
        // a un aparato que ya no es el suyo, sin un solo error de por medio.
        live.Bindings.Bind(attendee.CardRole, attendee.CardTopic);
        _phone.RouteTopicTo(WireNames.CallTopic(live.Id), attendee.ListensAt);
        _phone.RouteTopicTo(attendee.CardTopic, attendee.ListensAt);
        _phone.RouteTopicTo(RoundNotice.TopicFor(live.Id), attendee.ListensAt);

        if (isNew)
        {
                    _say(Texts.T("organizador.log.llego-desde-con", ("nombre", attendee.Name), ("donde", attendee.ListensAt), ("cuales", string.Join(", ", attendee.Cards))));
        }
        else
        {
                        _say(showing.AnythingNew
                ? Texts.T("organizador.log.volvio-con-nuevos", ("nombre", attendee.Name), ("n", showing.New.Count.ToString()), ("cuales", string.Join(", ", showing.New)))
                : Texts.T("organizador.log.volvio-sin-nuevos", ("nombre", attendee.Name)));
        }

        if (showing.Missing.Count > 0)
        {
                        _say(Texts.T("organizador.log.le-faltaban", ("nombre", attendee.Name), ("n", showing.Missing.Count.ToString()), ("cuales", string.Join(", ", showing.Missing))));
        }

        if (showing.Refused.Count > 0)
        {
                        _say(Texts.T("organizador.log.no-le-anote", ("nombre", attendee.Name), ("cuales", string.Join(", ", showing.Refused))));
        }

        Attendee theOneWhoCame = attendee;
        IReadOnlyList<long> newlyShown = isNew
            ? attendee.Cards.ToArray()
            : showing.New.Concat(showing.Missing).OrderBy(card => card).ToArray();

        if (newlyShown.Count > 0)
            _ = Task.Run(() => TellThemTheseCardsAsync(live, theOneWhoCame, newlyShown));

        Changed?.Invoke();

        (int ronda, string sejuega, List<object> premios, IReadOnlyList<int> bolitas,
            string anuncio) = RoundForTheWire(live);

        return new
        {
            cartones = attendee.Cards,
            ronda,
            sejuega,
            premios,
            bolitas,
            anuncio,
            iroh = OfferIrohLink(live, string.IsNullOrWhiteSpace(who.Mark) ? who.Name : who.Mark),
        }.ToText();
    }

    // ── la venta ─────────────────────────────────────────────────────────────

    // El bus de tells del bingo va por iroh: al que se alista se le ofrece un enlace de un solo
    // uso (ticket del bingo + nonce) y el bus queda esperando ese nonce un rato. La pagina sin
    // app ignora el campo y sigue con su sondeo.
    // Una oferta viva por persona: la pagina sin app se alista cada 3 s y no se engancha nunca, asi
    // que sin esto cada sondeo dejaba una espera de 3 minutos abierta en el transporte.
    private readonly Dictionary<string, (Guid Nonce, string Ticket, DateTime Until)> _irohOffers = new(StringComparer.Ordinal);

    private object OfferIrohLink(LiveFestival live, string who)
    {
        if (live.Bus is null || live.BusWire is null) return null;
        string key = live.Id + "|" + (who ?? string.Empty);

        lock (_irohOffers)
        {
            if (_irohOffers.TryGetValue(key, out (Guid Nonce, string Ticket, DateTime Until) open) && open.Until > DateTime.UtcNow)
                return new { ticket = open.Ticket, nonce = open.Nonce.ToString("N") };
        }

        Guid nonce = Guid.NewGuid();
        string ticket = TicketFor(live);
        lock (_irohOffers) _irohOffers[key] = (nonce, ticket, DateTime.UtcNow.AddMinutes(2.5));

        CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(_life.Token);
        patience.CancelAfter(TimeSpan.FromMinutes(3));
        _ = Task.Run(async () =>
        {
            try
            {
                await live.Bus.AcceptLinkAsync(nonce, patience.Token);
                _say(Texts.T("organizador.log.bus-enlace", ("bingo", live.Id), ("cuantos", live.Bus.LinkCount.ToString())));
            }
            catch (OperationCanceledException) { }
            catch (Exception failure) { _say(Texts.T("organizador.log.bus-enlace-fallo", ("motivo", failure.Message))); }
            finally
            {
                lock (_irohOffers)
                    if (_irohOffers.TryGetValue(key, out (Guid Nonce, string Ticket, DateTime Until) mine) && mine.Nonce == nonce)
                        _irohOffers.Remove(key);
                patience.Dispose();
            }
        });

        _say(Texts.T("organizador.log.ticket-bus", ("quien", who), ("ticket", ticket)));
        return new { ticket, nonce = nonce.ToString("N") };
    }

    // El ticket iroh de ahora: LocalTicket es el del ultimo refresco, y pedir una invitacion lo
    // renueva con el relay y las direcciones vigentes. Null si el bingo no tiene bus.
    private static string TicketFor(LiveFestival live)
    {
        if (live?.BusWire is null) return null;
        try
        {
            live.BusWire.CreateInvitationAsync(ChannelPurpose.Usher).GetAwaiter().GetResult();
            return live.BusWire.LocalTicket;
        }
        catch (Exception) { return null; }
    }

    private string AcceptSale(string whatTheySay)
    {
        SaleNotice sale = SaleNotice.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(sale.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.venta-sin-bingo"));
        if (string.IsNullOrWhiteSpace(sale.Buyer))
            throw new InvalidOperationException(Texts.T("organizador.no.venta-sin-comprador"));
        if (sale.Cards is null or { Count: 0 })
            throw new InvalidOperationException(Texts.T("organizador.no.venta-sin-cartones"));

        LiveFestival live = Live(sale.FestivalId);

        MustStillBeGoing(live);
        MustBeAlive(live, sale.Seller, sale.Mark);
        Attendee seller;

        seller = WhoIsThisCollaborator(live, sale.Seller, sale.Mark)
            ?? throw new InvalidOperationException(Texts.T("organizador.no.no-conozco-a", ("nombre", sale.Seller)));

        lock (_guard)
        {
            if (!seller.IsSeller)
                            throw new InvalidOperationException(
                    Texts.T("organizador.no.entro-no-a-vender", ("nombre", seller.Name), ("oficio", Duties.Describe(seller.Duty))));

            foreach (long card in sale.Cards)
            {
                if (!seller.Cards.Contains(card))
                    throw new InvalidOperationException(Texts.T("organizador.no.carton-no-en-custodia", ("carton", card.ToString())));
            }
        }

        // La marca del comprador la trae la venta cuando la persona ya compro antes; si no,
        // se acuna aca y viaja en su codigo. Es lo que hace que dos Ritas no se mezclen, y
        // lo que permite reconocerla en su segunda compra sin depender del nombre.
        // Normalizar ACA y nunca en el dominio: una guarda adentro de Villager no falla
        // ruidosa, hace desaparecer en silencio las ventas de los bingos viejas, porque la
        // rehidratacion se saltea el asiento que revienta y el bingo vuelve sin vender.
        string buyerMark = BuyerMark.Normalize(sale.BuyerMark);

        if (buyerMark.Length == 0) buyerMark = BuyerMark.Mint();

        live.Journal.Using(@"
{
    comprador = Villager(@nombre, @contacto, @marcaComprador);
    festival.MerchantMarked(@marca).SellThemTo(festival, @cartones, comprador);
    foreach (numero in @cartones) {
        mesa.PutCardInPlay(numero);
    }
}")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(seller));
                p.UserParameter<string>("nombre", sale.Buyer);
                p.UserParameter<string>("contacto", ReachableAt(sale.Contact));
                p.UserParameter<string>("marcaComprador", buyerMark);
                p.UserParameter<IEnumerable<long>>("cartones", sale.Cards);
            })
            .PerformCommand();

        lock (_guard)
        {
            foreach (long card in sale.Cards)
            {
                seller.Cards.Remove(card);
                seller.Sold.Add(card);
            }
        }

        List<long> everythingOfTheirs;

        lock (_guard)
        {
            if (!live.SoldTo.TryGetValue(sale.Buyer, out everythingOfTheirs))
            {
                everythingOfTheirs = new List<long>();
                live.SoldTo[sale.Buyer] = everythingOfTheirs;
            }

            foreach (long card in sale.Cards)
                if (!everythingOfTheirs.Contains(card)) everythingOfTheirs.Add(card);
        }

        // Lo que va en su codigo sale del DIARIO, no de la memoria: la memoria agrupa por
        // nombre y meteria los cartones de la otra Rita en el papel de esta.
        List<long> hersByMark = CardsBoughtBy(live, buyerMark);

                _say(Texts.T("organizador.log.vendio-a", ("vendedor", sale.Seller), ("cuales", string.Join(", ", sale.Cards)), ("comprador", sale.Buyer))
            + (hersByMark.Count > sale.Cards.Count
                ? " " + Texts.T("organizador.log.ya-lleva-en-total", ("n", hersByMark.Count.ToString())) : string.Empty));

        HandOverTheNewCards(live, sale.Buyer, buyerMark, sale.Cards);

        Changed?.Invoke();

        return CodeFor(live.Id, sale.Buyer, buyerMark, hersByMark);
    }

    /// <summary>
    /// De quien es este numero, ANTES de cobrar.
    ///
    /// Es la unica defensa contra el teclazo. Sin esto, un caracter cambiado a media luz no
    /// da ningun error: la plata se cobra, la venta queda asentada a un numero que no es de
    /// nadie, y el carton no le llega nunca a nadie -- en un diario que no se puede
    /// corregir. Con esto, quien vende ve "es Rita, ya lleva 3" o "comprador nuevo" y sabe
    /// cual de las dos cosas esta haciendo.
    ///
    /// La respuesta sale del DIARIO, que es lo unico que sabe distinguir dos personas del
    /// mismo nombre.
    /// </summary>
    private string AnswerWhoIs(string whatTheyAsk)
    {
        WhoIsQuestion asked = WhoIsQuestion.FromText(whatTheyAsk);

        if (string.IsNullOrWhiteSpace(asked.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.pregunta-sin-bingo"));

        LiveFestival live = Live(asked.FestivalId);

        MustBeAlive(live, asked.Collaborator, asked.Mark);

        string looking = BuyerMark.Normalize(asked.Buyer);

        if (looking.Length == 0)
            return new { marca = string.Empty, conocido = false, quien = string.Empty, cuantos = 0 }
                .ToText();

        List<long> theirs = CardsBoughtBy(live, looking);

        // Del diario y no de la memoria: live.SoldTo agrupa por nombre y no sobrevive a un
        // reinicio del organizador, que es justo cuando esta pregunta hace mas falta.
        string name = theirs.Count == 0
            ? string.Empty
            : NameOfWhoeverBought(live, theirs[0]);

        return new
        {
            marca = looking,
            conocido = theirs.Count > 0,
            quien = name,
            cuantos = theirs.Count,
        }.ToText();
    }

    /// <summary>Donde ubicar a quien compro. El dominio exige uno porque un premio hay que
    /// entregarlo, pero en un bingo presencial casi nadie deja telefono: el campo llega
    /// vacio, y vacio no es lo mismo que nulo segun cuantas veces se toco la pantalla.
    /// Los dos casos son la misma cosa -- esta persona esta aca -- y asi se anota.</summary>
    private static string ReachableAt(string contact) =>
                string.IsNullOrWhiteSpace(contact) ? Texts.T("organizador.contacto.en-el-bingo") : contact.Trim();

    private string AcceptExchange(string whatTheySay)
    {
        ExchangeRequest asked = ExchangeRequest.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(asked.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.cambio-sin-bingo"));
        if (asked.CardLeaving == asked.CardComingIn)
            throw new InvalidOperationException(Texts.T("organizador.no.mismo-carton"));

        LiveFestival live = Live(asked.FestivalId);

        MustStillBeGoing(live);
        MustBeAlive(live, asked.Collaborator, asked.Mark);

        Attendee collaborator = WhoIsThisCollaborator(live, asked.Collaborator, asked.Mark)
            ??             throw new InvalidOperationException(
                Texts.T("organizador.no.no-conozco-vendiendo", ("nombre", asked.Collaborator)));

        // La tenencia YA NO se pregunta aca. Se preguntaba contra live.Attendees, que es un
        // espejo en memoria, y un espejo puede estar viejo -- lo estuvo, cuando la escritura
        // por corrillo empezo a mover cartones sin pasar por estas listas. Ahora la pregunta
        // baja al Check, que corre contra el diario, que es el que sabe.
        string buyer = WhoBought(live, asked.CardLeaving)
            ??             throw new InvalidOperationException(
                Texts.T("organizador.no.carton-no-vendido-en-bingo", ("carton", asked.CardLeaving.ToString())));

        MustBeBeforeTheFirstBall(live,
            "seller = festival.MerchantMarked(@marca); soldCard = festival.ParticipationNumbered(@sale); "
            + "incomingCard = festival.ParticipationNumbered(@entra); seller.ExchangeFor(festival, soldCard, incomingCard); "
            + "leavingCard = mesa.CardInPlay(@sale); mesa.TakeCardOutOfPlay(leavingCard); "
            + "mesa.PutCardInPlay(@entra);",
            p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(collaborator));
                p.UserParameter<int>("sale", (int)asked.CardLeaving);
                p.UserParameter<int>("entra", (int)asked.CardComingIn);
            },
            // La tenencia se preguntaba arriba contra la lista en memoria, y el pueblo la
            // volvia a exigir TIRANDO adentro del comando -- un asiento de intento fallido
            // por cada cambio que no daba. Preguntada aca, el "no" no deja nada escrito.
            "Check(festival.MerchantCanExchange(@marca, @sale, @entra)) "
                        + "Error '" + Texts.ForDsl("organizador.no.cambio-no-se-puede") + "';");

        List<long> everythingOfTheirs;

        lock (_guard)
        {
            foreach (Attendee someone in live.Attendees)
            {
                someone.Sold.Remove(asked.CardLeaving);
                someone.Cards.Remove(asked.CardLeaving);
            }

            collaborator.Cards.Remove(asked.CardComingIn);
            collaborator.Cards.Add(asked.CardLeaving);
            collaborator.Sold.Add(asked.CardComingIn);

            if (!live.SoldTo.TryGetValue(buyer, out everythingOfTheirs))
                live.SoldTo[buyer] = everythingOfTheirs = new List<long>();

            everythingOfTheirs.Remove(asked.CardLeaving);
            if (!everythingOfTheirs.Contains(asked.CardComingIn))
                everythingOfTheirs.Add(asked.CardComingIn);
        }

                _say(Texts.T("organizador.log.le-cambio", ("nombre", collaborator.Name), ("comprador", buyer), ("sale", asked.CardLeaving.ToString()), ("entra", asked.CardComingIn.ToString())));

        _ = Task.Run(() => TellTheExchangeAsync(live, buyer, asked));

        Changed?.Invoke();

        string buyerMark = MarkOfWhoeverBought(live, asked.CardComingIn);

        return CodeFor(live.Id, buyer, buyerMark,
            string.IsNullOrWhiteSpace(buyerMark) ? everythingOfTheirs : CardsBoughtBy(live, buyerMark));
    }

    private string AcceptHandover(string whatTheySay)
    {
        HandoverRequest asked = HandoverRequest.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(asked.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.traspaso-sin-bingo"));
        if (asked.Cards is null or { Count: 0 })
            throw new InvalidOperationException(Texts.T("organizador.no.traspaso-sin-cartones"));

        LiveFestival live = Live(asked.FestivalId);

        MustStillBeGoing(live);
        MustBeAlive(live, asked.From, asked.Mark);

        Attendee from;
        Attendee to;

        lock (_guard)
        {
            from = live.Attendees.FirstOrDefault(a => a.IsSeller
                && string.Equals(a.Mark, asked.Mark, StringComparison.Ordinal))
                ??             throw new InvalidOperationException(
                Texts.T("organizador.no.no-conozco-vendiendo", ("nombre", asked.From)));

            if (asked.ToNumber <= 0)
                                throw new InvalidOperationException(
                    Texts.T("organizador.no.dime-numero-colaboradora"));

            to = live.Attendees.FirstOrDefault(a => a.IsSeller
                && NumberOf(live, a.Mark) == asked.ToNumber)
                ??                 throw new InvalidOperationException(
                    Texts.T("organizador.no.sin-colaboradora-numero", ("n", asked.ToNumber.ToString())));

            if (ReferenceEquals(from, to))
                throw new InvalidOperationException(Texts.T("organizador.no.pasarte-a-ti-misma"));

            foreach (long card in asked.Cards)
            {
                if (!from.Cards.Contains(card))
                    throw new InvalidOperationException(Texts.T("organizador.no.carton-no-en-custodia", ("carton", card.ToString())));
            }
        }

        live.Journal.Using(
            "festival.MerchantMarked(@marca).PassTo("
            + "festival, festival.MerchantMarked(@marcaDeElla), @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(from));
                p.UserParameter<string>("marcaDeElla", MustHaveAMark(to));
                p.UserParameter<IEnumerable<long>>("cartones", asked.Cards.ToList());
            })
            .PerformCommand();

        lock (_guard)
        {
            foreach (long card in asked.Cards)
            {
                from.Cards.Remove(card);
                if (!to.Cards.Contains(card)) to.Cards.Add(card);
            }
        }

        _say(Texts.T("organizador.log.le-paso", ("de", from.Name), ("cuales", string.Join(", ", asked.Cards)), ("para", to.Name)));

        _ = Task.Run(() => TellTheHandoverAsync(live, from, to, asked.Cards));

        Changed?.Invoke();

        return new { cartones = asked.Cards, traspaso = true }.ToText();
    }

    /// <summary>
    /// Ella suelta lo que no vendio y vuelve al libro, para que el padre se lo pueda dar
    /// a otra. Sigue alistada: devolver no es irse.
    /// El gesto lo inicia quien tiene los cartones, que es lo que pide el invariante --
    /// el organizador no los recoge por detras. Cuando el los recoge es porque la mando
    /// a la casa, y ahi ya dejo de ser colaboradora de este bingo.
    /// </summary>
    private string AcceptGiveBack(string whatTheySay)
    {
        GiveBackRequest asked = GiveBackRequest.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(asked.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.devolucion-sin-bingo"));
        if (asked.Cards is null or { Count: 0 })
            throw new InvalidOperationException(Texts.T("organizador.no.devolucion-sin-cartones"));

        LiveFestival live = Live(asked.FestivalId);

        MustStillBeGoing(live);
        MustBeAlive(live, asked.From, asked.Mark);

        Attendee from;

        lock (_guard)
        {
            from = live.Attendees.FirstOrDefault(a => a.IsSeller
                && string.Equals(a.Mark, asked.Mark, StringComparison.Ordinal))
                ??             throw new InvalidOperationException(
                Texts.T("organizador.no.no-conozco-vendiendo", ("nombre", asked.From)));

            foreach (long card in asked.Cards)
            {
                if (!from.Cards.Contains(card))
                    throw new InvalidOperationException(Texts.T("organizador.no.carton-no-en-custodia", ("carton", card.ToString())));
            }
        }

        live.Journal.Using("festival.MerchantMarked(@marca).GiveBack(festival, @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(from));
                p.UserParameter<IEnumerable<long>>("cartones", asked.Cards.ToList());
            })
            .PerformCommand();

        lock (_guard)
        {
            foreach (long card in asked.Cards) from.Cards.Remove(card);

            live.InTheBook.AddRange(asked.Cards);
            live.InTheBook.Sort();
        }

        _say(Texts.T("organizador.log.me-devolvio", ("nombre", from.Name), ("cuales", string.Join(", ", asked.Cards))));

        Attendee sheWho = from;
        _ = Task.Run(() => TellTheGiveBackAsync(live, sheWho, asked.Cards));

        Changed?.Invoke();

        return new { cartones = asked.Cards, devolucion = true }.ToText();
    }

    /// <summary>
    /// Ella plantea que una venta se deshaga. NO se deshace aca: queda esperando al
    /// organizador, que es quien le da efecto.
    /// El Check de la ronda va en los dos momentos, y no es de mas: entre que ella lo
    /// plantea y el organizador lo resuelve pueden pasar bolitas. Aca evita que mande
    /// algo que ya nace muerto; al conceder es lo que de verdad protege al comprador,
    /// porque lo que importa es cuando el carton SALE de la mesa.
    /// </summary>
    private string AcceptPlea(string whatTheySay)
    {
        PleaRequest asked = PleaRequest.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(asked.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.pedido-sin-bingo"));
        if (asked.Card <= 0)
            throw new InvalidOperationException(Texts.T("organizador.no.pedido-sin-carton"));

        LiveFestival live = Live(asked.FestivalId);

        MustStillBeGoing(live);
        MustBeAlive(live, asked.From, asked.Mark);

        Attendee who;

        lock (_guard)
        {
            who = live.Attendees.FirstOrDefault(a => a.IsSeller
                && string.Equals(a.Mark, asked.Mark, StringComparison.Ordinal))
                ??             throw new InvalidOperationException(
                Texts.T("organizador.no.no-conozco-vendiendo", ("nombre", asked.From)));
        }

        MustBeBeforeTheFirstBall(live,
            "seller = festival.MerchantMarked(@marca); card = festival.ParticipationNumbered(@numero); seller.Plead(festival, card);",
            p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(who));
                p.UserParameter<int>("numero", (int)asked.Card);
            });

        _say(Texts.T("organizador.log.pide-anular", ("nombre", who.Name), ("carton", asked.Card.ToString())));

        Changed?.Invoke();

        return new { carton = asked.Card, planteado = true }.ToText();
    }

    // ── cantar bingo ─────────────────────────────────────────────────────────

    /// <summary>
    /// Alguien canto BINGO desde su telefono.
    ///
    /// NO decide y NO escribe en el diario. El organizador ya sabe por su cuenta que
    /// cartones completaron -- se lo pregunta a la mesa carton por carton en Progress --,
    /// asi que esto no viene a descubrir un ganador: viene a decir QUIEN esta reclamando,
    /// que es lo unico que la pantalla no podia saber. Con dos cartones completos, el
    /// letrero mostraba al primero del tablero; ahora muestra a quien esta parado.
    ///
    /// Tampoco frena la mesa: si frenara, cualquiera con un telefono podria detener la
    /// noche cantando en falso.
    /// </summary>
    private string AcceptBingoClaim(string whatTheySay)
    {
        BingoClaim shouted = BingoClaim.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(shouted.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.canto-sin-bingo"));
        if (shouted.Card <= 0)
            throw new InvalidOperationException(Texts.T("organizador.no.canto-sin-carton"));

        LiveFestival live = Live(shouted.FestivalId);

        MustStillBeGoing(live);

        // Que el carton sea suyo se pregunta al DIARIO y no a la lista de asistentes:
        // quien compro y todavia no escaneo su codigo tambien juega, y tiene el mismo
        // derecho a cantar. Sin esto, cualquiera podria cantar con el carton de otro.
        string mine = WhoBought(live, shouted.Card);

        if (mine is null)
            throw new InvalidOperationException(Texts.T("organizador.no.carton-no-vendido", ("carton", shouted.Card.ToString())));

        string who = string.IsNullOrWhiteSpace(shouted.Who) ? mine : shouted.Who.Trim();

        lock (_guard)
        {
            // Cantar dos veces con el mismo carton y la misma figura es una sola mano
            // levantada: la persona toca de nuevo porque nadie le hizo caso, no porque
            // haya ganado otra vez.
            live.Shouts.RemoveAll(s => s.Card == shouted.Card
                && string.Equals(s.Figure, shouted.Figure, StringComparison.OrdinalIgnoreCase));

            live.Shouts.Add(new Shout
            {
                Who = who,
                Mark = shouted.Mark,
                Card = shouted.Card,
                Figure = shouted.Figure,
                When = DateTime.Now,
            });
        }

                _say(Texts.T("organizador.log.canta-bingo-con", ("nombre", who), ("carton", shouted.Card.ToString()))
            + (string.IsNullOrWhiteSpace(shouted.Figure)
                ? string.Empty
                : " (" + OnOffer.Called(shouted.Figure) + ")"));

        Changed?.Invoke();

        return new { canto = true, quien = who }.ToText();
    }

    /// <summary>Las manos levantadas, la primera arriba. El organizador las resuelve
    /// entregando o rechazando, que es lo que ya hacia.</summary>
    public IReadOnlyList<Shout> ShoutsWaiting(string festivalId)
    {
        LiveFestival live;

        lock (_guard)
        {
            if (!_live.TryGetValue(festivalId, out live)) return Array.Empty<Shout>();

            return live.Shouts.OrderBy(s => s.When).ToArray();
        }
    }

    /// <summary>La mano se baja cuando el organizador resuelve ese carton, sin importar
    /// como: entregado o rechazado, ya lo miro.</summary>
    private void LowerTheHand(LiveFestival live, long card, string figure)
    {
        lock (_guard)
        {
            live.Shouts.RemoveAll(s => s.Card == card
                && string.Equals(s.Figure, figure, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Lo que sigue esperando al organizador, con quien lo planteo.</summary>
    public IReadOnlyList<(int Plea, long Card, string Who)> PleasWaiting(string festivalId)
    {
        LiveFestival live = Live(festivalId);
        List<(int, long, string)> waiting = new();

        int howMany = AsInt(live.Journal.PerformQry("print festival.PleaCount n;"), "n");

        for (int position = 1; position <= howMany; position++)
        {
            string read = live.Journal.Using(
                "{ print festival.PleaAt(@posicion).HowItStands s; "
                + "print festival.PleaAt(@posicion).Number c; "
                + "print festival.PleaAt(@posicion).RaisedBy q; }")
                .WithParameters(p => p.UserParameter<int>("posicion", position))
                .PerformQuery();

            if (!string.Equals(Read(read, "s"), "Raised", StringComparison.Ordinal)) continue;

            waiting.Add((position, AsLong(read, "c"), Read(read, "q")));
        }

        return waiting;
    }

    /// <summary>Darle efecto: la venta se deshace, el carton sale de la mesa y vuelve al
    /// libro, y al comprador se le quita de la pantalla.</summary>
    public async Task GrantAsync(string festivalId, int plea)
    {
        LiveFestival live = Live(festivalId);

        (int _, long card, string who) = PleasWaiting(festivalId).FirstOrDefault(p => p.Plea == plea);

        if (card == 0) throw new InvalidOperationException(Texts.T("organizador.no.pedido-ya-no-espera"));

        string buyer = WhoBought(live, card);

        // A quien avisarle se resuelve ANTES de deshacer la venta: despues el diario ya no
        // tiene comprador para ese carton, y no habria como saber a cual de las dos personas
        // del mismo nombre hay que quitarselo de la pantalla.
        Attendee itsBuyer = WhoeverHasThisCard(live, card, buyer);

        MustBeBeforeTheFirstBall(live,
            "plea = festival.PleaAt(@pedido); elder.Grant(festival, plea); "
            + "tableCard = mesa.CardInPlay(@numero); mesa.TakeCardOutOfPlay(tableCard);",
            p =>
            {
                p.UserParameter<int>("pedido", plea);
                p.UserParameter<int>("numero", (int)card);
            });

        lock (_guard)
        {
            live.InTheBook.Add(card);
            live.InTheBook.Sort();

            foreach (List<long> theirs in live.SoldTo.Values) theirs.Remove(card);
            foreach (Attendee anybody in live.Attendees) anybody.Cards.Remove(card);
        }

        _say(Texts.T("organizador.log.anule-la-venta", ("carton", card.ToString())));

        if (itsBuyer is not null)
        {
            try
            {
                await TellThemToDropOneCardAsync(live, itsBuyer, card);
            }
            catch (Exception failure)
            {
                _say(Texts.T("organizador.log.anulacion-sin-avisar", ("nombre", buyer), ("motivo", Texts.Explain(failure))));
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Decir que no. La venta queda como estaba y se puede volver a plantear.</summary>
    public void Refuse(string festivalId, int plea)
    {
        LiveFestival live = Live(festivalId);

        live.Journal.Using("plea = festival.PleaAt(@pedido); elder.Refuse(festival, plea);")
            .WithParameters(p => p.UserParameter<int>("pedido", plea))
            .PerformCommand();

        _say(Texts.T("organizador.log.no-acepte-pedido", ("pedido", plea.ToString())));

        Changed?.Invoke();
    }

    private async Task TellTheGiveBackAsync(LiveFestival live, Attendee from, List<long> cards)
    {
        try
        {
            await _phone.ProduceAsync(
                CustodyNotice.TopicFor(live.Id, from.Handle), from.Name, null,
                CustodyNotice.BodyTaking(cards));
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.devolucion-sin-avisar", ("motivo", Texts.Explain(failure))));
        }
    }

    private async Task TellTheHandoverAsync(
        LiveFestival live, Attendee from, Attendee to, List<long> cards)
    {
        try
        {
            await _phone.ProduceAsync(
                CustodyNotice.TopicFor(live.Id, to.Handle), to.Name, null,
                CustodyNotice.Body(cards));

            await _phone.ProduceAsync(
                CustodyNotice.TopicFor(live.Id, from.Handle), from.Name, null,
                CustodyNotice.BodyTaking(cards));
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.traspaso-sin-avisar", ("motivo", Texts.Explain(failure))));
        }
    }

    private string WhoBought(LiveFestival live, long card)
    {
        lock (_guard)
        {
            foreach (KeyValuePair<string, List<long>> theirs in live.SoldTo)
            {
                if (theirs.Value.Contains(card)) return theirs.Key;
            }
        }

        return null;
    }

    private string CodeFor(string id, string who, string mark, IEnumerable<long> cards)
    {
        OpenFestival open = QrFor(id, Duties.Plays);

        var withOwner = new
        {
            v = open.Version,
            n = open.Name,
            r = open.FestivalId,
            e = open.CommunityLeaderUrl,
            i = open.IrohTicket,
            f = open.Figures,
            k = Duties.Plays,
            yo = who,
            m = mark,
            c = cards.ToArray(),
        };

        string link = OpenFestival.Scheme + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(withOwner)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // El codigo del comprador tambien va firmado cuando hay llave. Va como cola del
        // link y no como llave del JSON: los telefonos viejos lo leen igual (el payload
        // termina donde empieza el primer '&').
        return Certificate is null ? link : link + FestivalSeal.SignedTail(link, Certificate);
    }

    // ── el canto ─────────────────────────────────────────────────────────────

    private string AcceptCall(string whatTheySay)
    {
        CallNotice call = CallNotice.FromText(whatTheySay);

        if (string.IsNullOrWhiteSpace(call.FestivalId))
            throw new InvalidOperationException(Texts.T("organizador.no.canto-sin-bingo"));
        if (call.Number is < 1 or > 75)
            throw new InvalidOperationException(Texts.T("organizador.no.bolita-no-existe", ("numero", call.Number.ToString())));

        LiveFestival live = Live(call.FestivalId);

        MustStillBeGoing(live);
        MustBeAlive(live, call.Who, call.Mark);
        Attendee who;

        who = WhoIsThisCollaborator(live, call.Who, call.Mark)
            ??             throw new InvalidOperationException(
                Texts.T("organizador.no.no-conozco-a", ("nombre", call.Who)));

        if (!who.IsCaller)
                        throw new InvalidOperationException(
                Texts.T("organizador.no.entro-no-a-cantar", ("nombre", who.Name), ("oficio", Duties.Describe(who.Duty))));

        if (call.Instead > 0)
        {
            _say(Texts.T("organizador.log.se-equivoco", ("nombre", who.Name), ("malo", call.Instead.ToString()), ("bueno", call.Number.ToString())));
            CorrectTheDraw(live.Id, call.Instead, call.Number);

            return null;
        }

        _say(Texts.T("organizador.log.canta-el", ("nombre", who.Name), ("numero", call.Number.ToString())));
        Call(live.Id, call.Number);

        return null;
    }

    /// <summary>Journalea la bolita y ya: el reparto lo hace la reaccion de ese diario.</summary>
    public void Call(string id, int number)
    {
        LiveFestival live = Live(id);

        // El motor tambien frena, pero en ingles; esto es lo que ve la persona.
        //
        // Y hay DOS motivos para no poder cantar que no se pueden decir igual. Uno es que la
        // ronda termino, y ahi si hay que arrancar la siguiente. El otro es que la mesa se
        // freno sola porque alguien completo su figura -- que es lo que hace de propio el modo
        // automatico -- y ahi arrancar la ronda siguiente es lo PEOR que puede hacer: se
        // llevaria puesto el premio de alguien que lo tiene ganado y esta esperando.
        //
        // Decia lo mismo para los dos. En una velada eso es mandar al padre a cerrar la ronda
        // justo cuando lo que tenia que hacer era entregar.
        string state = Read(live.Journal.PerformQry("print mesa.State s;"), "s");

        if (string.Equals(state, "AwaitingValidation", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                Texts.T("organizador.no.mesa-frenada-entrega"));

        if (!string.Equals(state, "Running", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                Texts.T("organizador.no.sin-ronda-para-cantar"));

        // La ronda viaja con la bolita. El sustrato identifica un tell por su contenido, y
        // el contenido de "salio el 7" es el mismo en la ronda 1 que en la ronda 2: sin la
        // ronda, el segundo 7 se toma por una repeticion del primero y no se reparte.
        //
        // La ronda la dice la mesa, no este metodo: con Eval la resuelve el actor al cargar
        // los argumentos --antes del cuerpo-- y queda congelada en el asiento, asi que al
        // releerlo dice la misma ronda que dijo aquella noche. Traerla antes con un print y
        // parsearla aca era peor de lo que parecia: esa lectura ADIVINA, y si fallara la
        // ronda llegaria en cero y la guarda del dominio tumbaria la bolita entera --
        // exactamente el sintoma que este arreglo vino a curar.
        live.Journal.Using("mesa.RegisterDraw(@numero, @ronda);")
            .WithParameters(p =>
            {
                p.UserParameter<int>("numero", number);
                p[Parameter.Eval, "ronda", typeof(int)] = "mesa.RoundNumber";
            })
            .PerformCommand();

        _say(Texts.T("organizador.log.cante-el", ("numero", number.ToString()), ("id", id)));
        TellTheDraw(live, DrawNotice.Body(number));
        WarnIfNobodyIsDelivering(live, number);
        Changed?.Invoke();
    }

    /// <summary>Esa bolita no era. Cambia una por otra, que es lo que el motor sabe hacer
    /// -- no existe quitarla y dejar nada.
    ///
    /// Se puede corregir CUALQUIERA de la ronda y no solo la ultima: el error se descubre
    /// tres bolitas despues, o lo descubre alguien mirando su carton. El motor la
    /// reemplaza EN SU POSICION, asi que el orden de la ronda no se mueve y la ultima
    /// sigue siendo la que era.
    ///
    /// No se pregunta si alguien ya iba ganando con la bolita mala: los premios se
    /// entregan al cerrar la ronda, asi que un BINGO que dependia de ella deja de serlo y
    /// ya. Lo unico que esto NO deshace es un premio ya entregado -- ese asiento esta
    /// escrito y no se reescribe --, y de eso avisa el motor negandose.</summary>
    public void CorrectTheDraw(string id, int wrong, int right)
    {
        LiveFestival live = Live(id);

        string state = Read(live.Journal.PerformQry("print mesa.State s;"), "s");

        // Misma razon que en el canto: frenada por un ganador no es lo mismo que terminada, y
        // aca importa mas todavia -- si el que esta esperando el premio lo gano por una bolita
        // MAL TECLEADA, corregirla es exactamente lo que hay que hacer, y el mensaje tiene que
        // decirle donde esta parado en vez de mandarlo a otro lado.
        if (string.Equals(state, "AwaitingValidation", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                Texts.T("organizador.no.mesa-frenada-corregir"));

        if (!string.Equals(state, "Running", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                Texts.T("organizador.no.nada-que-corregir"));

        // El motor tambien frena, pero en ingles; esto es lo que ve la persona.
        RoundStatus current = CurrentRoundOf(id);

        if (current is not null && current.WinnerCount > 0)
                        throw new InvalidOperationException(
                Texts.T("organizador.no.premio-ya-entregado"));

        // La ronda va igual que en la bolita, y por lo mismo: sin ella, equivocarse dos
        // veces con el mismo par --el 46 por el 45 en la ronda 1 y otra vez en la ronda 3--
        // se lee como la misma correccion dicha dos veces, y la segunda no sale.
        live.Journal.Using("mesa.CorrectDraw(@malo, @bueno, @ronda);")
            .WithParameters(p =>
            {
                p.UserParameter<int>("malo", wrong);
                p.UserParameter<int>("bueno", right);
                p[Parameter.Eval, "ronda", typeof(int)] = "mesa.RoundNumber";
            })
            .PerformCommand();

        _say(Texts.T("organizador.log.corregi", ("malo", wrong.ToString()), ("bueno", right.ToString())));
        TellTheDraw(live, DrawNotice.BodyCorrecting(wrong, right));
        Changed?.Invoke();
    }

    /// <summary>La bolita, para las pantallas de los colaboradores. No se espera a que
    /// salga: la bolita ya quedo asentada en el diario y esto es solo pintura -- si el
    /// aviso se pierde, la colaboradora ve su tombola desactualizada, no un juego roto.
    /// </summary>
    private void TellTheDraw(LiveFestival live, string body) => _ = Task.Run(async () =>
    {
        try
        {
            await _phone.ProduceAsync(DrawNotice.TopicFor(live.Id), "bolita", null, body);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.bolita-sin-avisar", ("motivo", Texts.Explain(failure))));
        }
    });
    /// <summary>
    /// A la bolita la reparte una reaccion, y una reaccion que no se armo NO SE QUEJA: la
    /// mesa cuenta bien, el diario tiene todo, y al jugador no le llega nada. Ese silencio
    /// ya costo media tarde una vez.
    /// No se imprimen contadores en cada canto --seria ruido de programador en 'lo que va
    /// pasando', que lo lee el organizador--: se habla solo cuando no hay quien reparta,
    /// que es la falla que de otro modo no deja rastro en ninguna parte.
    /// </summary>
    private void WarnIfNobodyIsDelivering(LiveFestival live, int number)
    {
        try
        {
            foreach (Puppeteer.EventSourcing.Follower.Reaction r in live.Journal.Reactions)
                if (r.Name == "Cantar") return;

            _say("    " + Texts.T("organizador.log.reaccion-cantar-no-armada", ("numero", number.ToString())));
        }
        catch (Exception failure)
        {
            _say("    " + Texts.T("organizador.log.no-pude-mirar-reaccion", ("motivo", Texts.Explain(failure))));
        }
    }

    /// <summary>La hora escrita "HH:mm", partida. Hora -1 cuando no dijeron ninguna:
    /// no hay hora cero que sirva de "sin hora", porque medianoche es una hora real.
    /// </summary>
    private static (int Hour, int Minute) HourIn(string said)
    {
        string[] parts = (said ?? string.Empty).Split(':');

        if (parts.Length != 2) return (-1, 0);

        if (!int.TryParse(parts[0], out int hour) || hour < 0 || hour > 23) return (-1, 0);
        if (!int.TryParse(parts[1], out int minute) || minute < 0 || minute > 59) return (-1, 0);

        return (hour, minute);
    }

    /// <summary>Ponerle precio al carton, o cambiarselo. Es UN numero para todo el
    /// bingo -- no una historia por venta --, asi que cambiarlo a mitad de noche cambia
    /// tambien la cuenta de lo ya vendido. La pantalla lo advierte; aca solo se asienta.
    /// </summary>
    public void PriceTheCards(string festivalId, long price)
    {
        LiveFestival live = Live(festivalId);

        MustStillBeGoing(live);

        live.Journal.Using("cardPrice = Price(@precio); elder.PriceCardsAt(festival, cardPrice);")
            .WithParameters(p => p.UserParameter<long>("precio", price))
            .PerformCommand();

        _say(Texts.T("organizador.log.carton-vale", ("precio", price.ToString())));
        Changed?.Invoke();
    }

    /// <summary>A que hora abren las puertas, dicho o corregido despues de crear. El
    /// wizard ya lo preguntaba, pero un bingo que nacio sin hora se quedaba sin ella para
    /// siempre: el pueblo sabia recibirla y no habia camino para decirsela.</summary>
    public void OpenTheDoorsAt(string festivalId, int hour, int minute)
    {
        LiveFestival live = Live(festivalId);

        MustStillBeGoing(live);

        live.Journal.Using("doorsTime = ClockTime(@hora, @minuto); elder.OpenTheDoorsAt(festival, doorsTime);")
            .WithParameters(p =>
            {
                p.UserParameter<int>("hora", hour);
                p.UserParameter<int>("minuto", minute);
            })
            .PerformCommand();

        _say(Texts.T("organizador.log.abre-a-las", ("hora", $"{hour:00}:{minute:00}")));
        Changed?.Invoke();
    }

    /// <summary>Como se anuncia el bingo: 'OnClaim' (la gente lo canta con la voz y el
    /// organizador verifica con su cartel) o 'Automatic' (la mesa se frena sola al
    /// completarse un carton, hasta que el organizador entregue o rechace). Vale desde
    /// la proxima ronda, porque el modo se fija cuando una ronda arranca.</summary>
    public void AnnounceWinsBy(string festivalId, string how)
    {
        LiveFestival live = Live(festivalId);

        MustStillBeGoing(live);

        live.Journal.Using("winsAnnouncement = WinsAnnouncement(@modo); elder.AnnounceWinsBy(festival, winsAnnouncement);")
            .WithParameters(p => p.UserParameter<string>("modo", how))
            .PerformCommand();

                _say(Texts.T(string.Equals(how, "Automatic", StringComparison.Ordinal)
            ? "organizador.log.se-anuncia-solo"
            : "organizador.log.lo-canta-la-gente"));

        Changed?.Invoke();
    }

    /// <summary>La configuracion del bingo, para la pantalla del detalle. Del diario y
    /// no de memoria: es lo unico que la tiene despues de un reinicio.</summary>
    public (long CardPrice, string WinsAnnounced, string PlaysOn, string DoorsOpenAt)
        SettingsOf(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        string read = live.Journal.PerformQry(
            "{ print festival.CardPrice p; print festival.WinsAnnounced w; "
            + "print festival.Day.Year dy; print festival.Day.Month dm; "
            + "print festival.Day.Day dd; print festival.DoorsOpenAt ho; }");

        // El dia se arma de sus tres numeros: el diario no serializa objetos, y partido
        // en tres no depende del formato con que este puesto el telefono.
        int year = AsInt(read, "dy");
        string plays = year > 0
            ? new DateTime(year, AsInt(read, "dm"), AsInt(read, "dd")).ToString("yyyy-MM-dd")
            : string.Empty;

        return (AsLong(read, "p"), Read(read, "w") ?? string.Empty,
            plays, Read(read, "ho") ?? string.Empty);
    }

    public (int ForSale, int Left) HowManyForSale(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        lock (_guard) return (live.ForSale.Count, live.InTheBook.Count);
    }

    /// <summary>Los NUMEROS que quedan en el libro, no cuantos. El organizador reparte por
    /// tramos -- "a esta del 7 al 20" --, y para decir el tramo hacen falta los numeros.
    /// Van en orden porque asi se reparten: del principio del libro.</summary>
    public IReadOnlyList<long> StillInTheBook(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        SyncMirrorsWithTheJournal(live);

        lock (_guard) return live.InTheBook.ToArray();
    }

    /// <summary>Cuantos cartones hay de verdad en la mesa. La pantalla lo usa para saber
    /// si el tablero cambio: una venta no mueve ni las bolitas ni la cantidad de gente,
    /// asi que sin esto COMO VAN se quedaba pegado en "todavia nadie compro" aunque ya
    /// hubiera cartones jugando. Sale del diario y no de memoria, para que siga siendo
    /// cierto despues de reabrir la app.</summary>
    public int HowManyOnTheTable(string festivalId)
    {
        LiveFestival live = Live(festivalId);

        return AsInt(live.Journal.PerformQry("print mesa.CardsInPlayCount n;"), "n");
    }

    /// <summary>Los numeros de una tanda. No salen de ninguna lista guardada: se
    /// nombran, y la cara de las 25 casillas se deriva del numero.</summary>
    private static List<long> ARun(long from, int howMany)
    {
        List<long> run = new(howMany);

        for (int i = 0; i < howMany; i++) run.Add(from + i);

        return run;
    }

    public IReadOnlyList<long> PutMoreUpForSale(string festivalId, int howMany)
    {
        if (howMany <= 0) throw new InvalidOperationException(Texts.T("organizador.no.dime-cuantos-mas"));

        LiveFestival live = Live(festivalId);

        // Esta y HandMoreAsync las dispara el organizador desde su propia pantalla y no
        // llegan por HTTP: por eso se habian quedado sin la guarda que si tienen los ocho
        // manejadores. El pueblo ahora tambien se niega, pero el "no" en cristiano sale de
        // aca.
        MustStillBeGoing(live);

        List<long> fresh;

        lock (_guard)
        {
            fresh = ARun(live.ForSale.Count == 0 ? 1 : live.ForSale.Max() + 1, howMany);
        }

        live.Journal.Using("elder.PutUpForSale(festival, @cartones);")
            .WithParameters(p => p.UserParameter<IEnumerable<long>>("cartones", fresh))
            .PerformCommand();

        lock (_guard)
        {
            live.ForSale.AddRange(fresh);
            live.InTheBook.AddRange(fresh);
        }

        _say(Texts.T("organizador.log.puse-mas-en-venta", ("n", fresh.Count.ToString()), ("cuales", string.Join(", ", fresh))));
        Changed?.Invoke();

        return fresh;
    }

    /// <summary>Por el papelito y no por el nombre: la lista de la pantalla sale de los
    /// papelitos, y dos colaboradoras que se llaman igual traen uno cada una.</summary>
    /// <summary>
    /// El organizador le recoge cartones a alguien y vuelven al libro.
    ///
    /// El invariante de siempre dice que el gesto lo inicia QUIEN TIENE los cartones, y no
    /// es capricho: si el organizador se los quita y el telefono de ella no se entera, la
    /// app dice que no tiene nada y ella sigue con veinte papeles en la mano. Por eso esto
    /// solo se puede con ella CONECTADA -- la misma regla que entregarle mas --, y el
    /// aviso le sale antes de que la pantalla del organizador se refresque.
    ///
    /// Se recogen los ULTIMOS que se le entregaron: si le llegaron del 7 al 20 y devuelve
    /// cinco, lo natural es que suelte del 16 al 20 y siga vendiendo el tramo de abajo.
    /// </summary>
    public async Task TakeBackAsync(string festivalId, string mark, int howMany)
    {
        LiveFestival live = Live(festivalId);

        MustStillBeGoing(live);

        Attendee from = await MustBeSellingAndReachableAsync(live, mark, Texts.T("organizador.accion.recogerle-cartones"));

        List<long> taken;

        lock (_guard)
        {
            if (howMany <= 0 || howMany > from.Cards.Count)
                            throw new InvalidOperationException(
                Texts.T("organizador.no.dime-cuantos-le-recojo", ("nombre", from.Name), ("n", from.Cards.Count.ToString())));

            taken = from.Cards.Skip(from.Cards.Count - howMany).ToList();
        }

        live.Journal.Using("festival.MerchantMarked(@marca).GiveBack(festival, @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(from));
                p.UserParameter<IEnumerable<long>>("cartones", taken);
            })
            .PerformCommand();

        lock (_guard)
        {
            foreach (long card in taken) from.Cards.Remove(card);

            live.InTheBook.AddRange(taken);
            live.InTheBook.Sort();
        }

        _say(Texts.T("organizador.log.le-recogi", ("nombre", from.Name), ("cuales", string.Join(", ", taken))));

        await TellTheGiveBackAsync(live, from, taken);

        Changed?.Invoke();
    }

    /// <summary>
    /// El organizador le pasa cartones de una persona a otra, sin que ninguna de las dos
    /// toque su telefono. Las DOS tienen que estar conectadas: si la que recibe no lo
    /// esta, los cartones le llegarian a un telefono que no escucha y quedarian en un
    /// limbo del que no vuelven.
    /// </summary>
    public async Task MoveCardsAsync(string festivalId, string fromMark, string toMark,
        int howMany)
    {
        LiveFestival live = Live(festivalId);

        MustStillBeGoing(live);

        Attendee from = await MustBeSellingAndReachableAsync(live, fromMark, Texts.T("organizador.accion.moverle-cartones"));
        Attendee to = await MustBeSellingAndReachableAsync(live, toMark, Texts.T("organizador.accion.pasarle-cartones"));

        if (ReferenceEquals(from, to))
            throw new InvalidOperationException(Texts.T("organizador.no.ya-son-de-esa-persona"));

        List<long> moved;

        lock (_guard)
        {
            if (howMany <= 0 || howMany > from.Cards.Count)
                            throw new InvalidOperationException(
                Texts.T("organizador.no.dime-cuantos-le-paso", ("nombre", from.Name), ("n", from.Cards.Count.ToString())));

            moved = from.Cards.Skip(from.Cards.Count - howMany).ToList();
        }

        live.Journal.Using(
            "festival.MerchantMarked(@marca).PassTo("
            + "festival, festival.MerchantMarked(@marcaDeElla), @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(from));
                p.UserParameter<string>("marcaDeElla", MustHaveAMark(to));
                p.UserParameter<IEnumerable<long>>("cartones", moved);
            })
            .PerformCommand();

        lock (_guard)
        {
            foreach (long card in moved)
            {
                from.Cards.Remove(card);
                if (!to.Cards.Contains(card)) to.Cards.Add(card);
            }
        }

        _say(Texts.T("organizador.log.pase-de-a", ("de", from.Name), ("para", to.Name), ("cuales", string.Join(", ", moved))));

        await TellTheHandoverAsync(live, from, to, moved);

        Changed?.Invoke();
    }

    /// <summary>
    /// Quien vende y ademas ESCUCHA. Las dos cosas: mover cartones de un lado a otro sin que
    /// el telefono de la persona se entere deja a la app diciendo una cosa y a la mano de
    /// ella teniendo otra.
    ///
    /// Y desde que ella puede cobrar sin el organizador, esto pesa mas: si le sacan de la
    /// mano un carton que ella ya vendio y tiene anotado, esa venta va a rebotar cuando se
    /// la cuente -- con la plata ya cobrada. Por eso no alcanza con que ALGUNA VEZ haya
    /// dicho donde escucha, que es lo unico que se miraba: hay que tocarle la puerta AHORA.
    /// </summary>
    /// <summary>Donde escucha el papelito, o vacio si no hay nadie anotado con esa marca.</summary>
    private string Where(LiveFestival live, string mark)
    {
        lock (_guard)
        {
            return live.Attendees.FirstOrDefault(a => a.IsCollaborator
                && string.Equals(a.Mark, mark, StringComparison.Ordinal))?.ListensAt
                ?? string.Empty;
        }
    }

    private async Task<Attendee> MustBeSellingAndReachableAsync(
        LiveFestival live, string mark, string forWhat)
    {
        Attendee who = WhoIsThisCollaborator(live, null, mark)
                        ?? throw new InvalidOperationException(
                Texts.T("organizador.no.no-se-presento"));

        if (!who.IsSeller)
            throw new InvalidOperationException(Texts.T("organizador.no.no-entro-a-vender", ("nombre", who.Name)));

        if (string.IsNullOrWhiteSpace(who.ListensAt))
                        throw new InvalidOperationException(
                Texts.T("organizador.no.no-conectada-para", ("nombre", who.Name), ("accion", forWhat)));

        if (!await _phone.CanReachAsync(who.ListensAt, _life.Token))
        {
                        throw new InvalidOperationException(
                Texts.T("organizador.no.no-contesta-para", ("nombre", who.Name), ("accion", forWhat)));
        }

        return who;
    }

    public async Task HandMoreAsync(string festivalId, string mark, int howMany)
    {
        LiveFestival live = Live(festivalId);

        MustStillBeGoing(live);

        List<long> batch = new();

        Attendee attendee = WhoIsThisCollaborator(live, null, mark)
                        ?? throw new InvalidOperationException(
                Texts.T("organizador.no.colaboradora-no-presentada"));

        if (!attendee.IsSeller)
            throw new InvalidOperationException(Texts.T("organizador.no.no-entro-a-vender", ("nombre", attendee.Name)));

        // Se reservan bajo el lock (para que dos entregas no se lleven los mismos) pero no
        // se le anotan hasta que el diario los acepte: si se anotaran antes y el asiento
        // rebotara, el error se veria en la pantalla pero el libro quedaria debitado y ella
        // con cartones que el diario no le dio.
        lock (_guard)
        {
            batch.AddRange(TakeFromTheBook(live, howMany));

            if (batch.Count == 0)
                            throw new InvalidOperationException(
                Texts.T("organizador.no.sin-cartones-en-venta"));

            live.HandoutsInFlight++;
        }

        try
        {
            HandTo(live, attendee, batch);
        }
        catch (Exception)
        {
            lock (_guard)
            {
                live.InTheBook.InsertRange(0, batch);
                live.HandoutsInFlight--;
            }
            Changed?.Invoke();
            throw;
        }

        lock (_guard)
        {
            attendee.Cards.AddRange(batch);
            live.HandoutsInFlight--;
        }

        _say(Texts.T("organizador.log.le-entregue-mas", ("n", batch.Count.ToString()), ("nombre", attendee.Name), ("cuales", string.Join(", ", batch))));

        if (batch.Count < howMany)
            _say(Texts.T("organizador.log.ojo-solo-quedaban", ("pedidos", howMany.ToString()), ("n", batch.Count.ToString())));

        await _phone.ProduceAsync(
            CustodyNotice.TopicFor(live.Id, attendee.Handle),
            attendee.Name,
            null,
            CustodyNotice.Body(batch));

        Changed?.Invoke();
    }

    // ── los avisos que salen ─────────────────────────────────────────────────

    /// <summary>
    /// Entregarle cartones a un colaborador. UN solo comando para el primer talonario y
    /// para todos los que siguen, con texto CONSTANTE: el papelito es el id, asi que el
    /// script no nombra a nadie.
    /// Antes el nombre del comerciante iba interpolado (v_maria), y eso escribia una
    /// plantilla de Action por colaborador -- el mismo problema que el q_&lt;guid&gt; del
    /// comprador. Y ademas era un global declarado por un comando y referenciado por
    /// otro, que es lo que al rehidratar dejaba la Action sin registrar.
    /// </summary>
    // ── COMO SE PASA UNA LISTA DE CARTONES AL DIARIO ─────────────────────────
    //
    // Dos reglas, las dos medidas con un repro entre procesos, y las dos silenciosas
    // si se rompen. Valen para los cuatro sitios que mandan @cartones (la venta, el
    // traspaso, la entrega y la devolucion).
    //
    // 1. El TIPO DECLARADO va IEnumerable<long>, nunca List<long>.
    //    El diario guarda la firma como arreglo (cartones:long[]) sin importar lo que
    //    le pases. En la sesion que define el comando la firma sale del mismo valor y
    //    coincide; en TODA sesion posterior sale del archivo, y ofrecer un List<long>
    //    contra una firma de arreglo revienta con "The provided parameters are not
    //    compatible with the Program's parameter signature". Una interfaz si es
    //    asignable desde el arreglo, asi que pasa siempre.
    //
    // 2. El VALOR tiene que seguir siendo una List<long>, nunca un arreglo.
    //    Con un valor de arreglo el comando pasa el chequeo y se asienta, pero cada
    //    rehidratacion posterior lo descarta con un NullReferenceException y
    //    CatchUpFailed queda en False: la entrega desaparece sin que nadie se entere.
    //    O sea que un .ToArray() de mas en cualquiera de esos cuatro sitios cambia un
    //    error visible por perdida de datos muda.
    private void HandTo(LiveFestival live, Attendee seller, List<long> cards)
    {
        live.Journal.Using("elder.HandTo(festival, festival.MerchantMarked(@marca), @cartones);")
            .WithParameters(p =>
            {
                p.UserParameter<string>("marca", MustHaveAMark(seller));
                p.UserParameter<IEnumerable<long>>("cartones", cards);
            })
            .PerformCommand();
    }

    /// <summary>El papelito con el que entro. Quien entro por gracia no tiene, y sin el no
    /// hay como nombrarlo en el diario.</summary>
    private static string MustHaveAMark(Attendee who) =>
        string.IsNullOrWhiteSpace(who.Mark)
                        ? throw new Refusal(Refusal.NoMark,
                Texts.T("organizador.no.codigo-que-el-diario-no-reconoce", ("nombre", who.Name)))
            : who.Mark;

    /// <summary>
    /// Asienta la entrega y dice si el diario la acepto. Devuelve falso en vez de tirar
    /// porque quien llama tiene que poder deshacer la reserva del libro y dejar entrar a la
    /// persona igual, sin cartones: quedarse afuera por esto seria peor.
    ///
    /// No hace falta deshacer nada del lado del diario. Festival.HandTo valida el LOTE
    /// ENTERO antes de tocar el primer carton, asi que si rebota, el diario quedo como
    /// estaba -- que es la unica forma de trabajar con un sustrato sin rollback.
    /// </summary>
    private bool WriteDownTheHandover(LiveFestival live, Attendee seller, List<long> cards)
    {
        // Quien llega con el talonario agotado igual queda registrada, y sin asentar
        // nada: el comerciante nacio cuando tomo el papelito, no cuando recibio
        // cartones, asi que se le pueden entregar despues sin haberla nombrado hoy.
        if (cards.Count == 0)
        {
            _say(Texts.T("organizador.log.alistada-sin-cartones", ("nombre", seller.Name)));
            return true;
        }

        try
        {
            HandTo(live, seller, cards);

            _say(Texts.T("organizador.log.le-entregue-en-custodia", ("n", cards.Count.ToString()), ("nombre", seller.Name)));
            return true;
        }
        catch (Exception failure)
        {
            // Fuerte y con las consecuencias, no una linea que se pierde entre las bolitas:
            // de esto depende que la persona sepa que NO puede vender todavia.
                        _say(Texts.T("organizador.log.no-pude-anotar-entrega", ("nombre", seller.Name), ("motivo", Texts.Explain(failure))));

            return false;
        }
    }

    private Task TellThemTheirCardsAsync(LiveFestival live, Attendee who) =>
        TellThemTheseCardsAsync(live, who, who.Cards.ToArray());

    private async Task TellThemTheseCardsAsync(
        LiveFestival live, Attendee who, IReadOnlyList<long> which)
    {
        foreach (long card in which)
        {
            try
            {
                await TellThemOneCardAsync(live, who, card);
            }
            catch (Exception failure)
            {
                _say(Texts.T("organizador.log.no-pude-avisar-del-carton", ("nombre", who.Name), ("carton", card.ToString()), ("motivo", Texts.Explain(failure))));
            }
        }
    }

    private async Task TellTheExchangeAsync(LiveFestival live, string buyer, ExchangeRequest asked)
    {
        // Por la marca del carton que SALE, no por el nombre. Con dos Ritas adentro -- que es
        // justo para lo que existe la marca -- buscar por nombre devuelve la primera de la
        // lista: a esa le llegaria un carton que no compro y la orden de soltar uno que nunca
        // tuvo, mientras la verdadera se queda marcando un carton que ya no vale.
        Attendee who = WhoeverHasThisCard(live, asked.CardLeaving, buyer);

        if (who is null) return;

        try
        {
            await TellThemOneCardAsync(live, who, asked.CardComingIn);
            await TellThemToDropOneCardAsync(live, who, asked.CardLeaving);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.no-pude-avisar-del-cambio", ("nombre", who.Name), ("motivo", Texts.Explain(failure))));
        }
    }

    /// <summary>
    /// A quien hay que hablarle por este carton. La marca sale del diario, que es lo unico
    /// que sabe cual de las dos personas del mismo nombre lo compro; el nombre queda de
    /// respaldo para los bingos de antes de las marcas, donde no hay otra cosa.
    /// </summary>
    private Attendee WhoeverHasThisCard(LiveFestival live, long card, string name)
    {
        string mark = MarkOfWhoeverBought(live, card);

        lock (_guard)
        {
            if (!string.IsNullOrWhiteSpace(mark))
            {
                return live.Attendees.FirstOrDefault(a => !a.IsCollaborator
                    && string.Equals(a.Mark, mark, StringComparison.Ordinal));
            }

            return live.Attendees.FirstOrDefault(a => !a.IsCollaborator
                && string.IsNullOrWhiteSpace(a.Mark)
                && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task TellThemToDropOneCardAsync(LiveFestival live, Attendee who, long card)
    {
        string game = "juego_" + WireNames.Safe(live.Id);

        Parameters payload = new Parameters();
        payload["numero", typeof(int)] = (int)card;

        await live.Transport.SendAsync(new TellEnvelope(
            Id: $"quitar-{WireNames.Safe(live.Id)}-{who.Name.ToLowerInvariant()}-{card}",
            MessageName: "QuitarCarton",
            Addressee: who.CardRole,
            AddresseeInstanceId: who.Name,
            CausalEventId: null,
            ReactionName: null,
            Check: $"Check({game}.IsInPlay(@numero)) Error '{Texts.ForDsl("organizador.no.carton-ya-no-era-mio")}';",
            Values: payload));

        _say(Texts.T("organizador.log.avise-ya-no-es-suyo", ("nombre", who.Name), ("carton", card.ToString())));
    }

    private async Task TellThemOneCardAsync(LiveFestival live, Attendee who, long card)
    {
        string game = "juego_" + WireNames.Safe(live.Id);

        Parameters payload = new Parameters();
        payload["numero", typeof(int)] = (int)card;

        await live.Transport.SendAsync(new TellEnvelope(
            Id: $"carton-{WireNames.Safe(live.Id)}-{who.Name.ToLowerInvariant()}-{card}",
            MessageName: "Carton",
            Addressee: who.CardRole,
            AddresseeInstanceId: who.Name,
            CausalEventId: null,
            ReactionName: null,
            Check: $"Check(!{game}.IsInPlay(@numero)) Error '{Texts.ForDsl("organizador.no.ya-tenia-ese-carton")}';",
            Values: payload));

        _say(Texts.T("organizador.log.avise-que-tiene", ("nombre", who.Name), ("carton", card.ToString())));
    }

    // ── lo que las pantallas preguntan ───────────────────────────────────────

    /// <summary>Los papelitos que el organizador dio en este bingo, leidos del diario.
    /// Del diario y no de la memoria a proposito: asi se pueden administrar aunque la app
    /// se haya reiniciado y la lista de asistentes este vacia, que es justo cuando hace
    /// falta poder mandar a alguien a la casa.</summary>
    public IReadOnlyList<HandedOut> ErrandsOf(string id)
    {
        LiveFestival live;

        lock (_guard)
        {
            if (!_live.TryGetValue(id, out live)) return Array.Empty<HandedOut>();
        }

        return ErrandsOf(live);
    }

    private IReadOnlyList<HandedOut> ErrandsOf(LiveFestival live)
    {
        int howMany = AsInt(live.Journal.PerformQry("print festival.ErrandCount n;"), "n");
        List<HandedOut> given = new();

        for (int position = 1; position <= howMany; position++)
        {
            string read = live.Journal.Using(
                "{ print festival.ErrandAt(@posicion).Mark m; "
                + "print festival.ErrandAt(@posicion).Doing d; "
                + "print festival.ErrandAt(@posicion).HowItStands s; "
                + "print festival.ErrandAt(@posicion).TakenBy w; "
                + "print festival.ErrandAt(@posicion).MeantFor f; "
                + "print festival.ErrandAt(@posicion).CardsMeant c; }")
                .WithParameters(p => p.UserParameter<int>("posicion", position))
                .PerformQuery();

            given.Add(new HandedOut
            {
                Mark = Read(read, "m"),
                Duty = Duties.FromChore(Read(read, "d")),
                Standing = Read(read, "s"),
                TakenBy = Read(read, "w") ?? string.Empty,
                MeantFor = Read(read, "f") ?? string.Empty,
                CardsMeant = AsInt(read, "c"),
            });
        }

        return given;
    }

    /// <summary>
    /// Mandar a la casa a quien tiene un papelito: le rompe el papel y le recoge lo que le
    /// quede en la mano, para que el organizador se lo pueda dar a otra persona.
    /// Son DOS comandos y no uno: el sustrato ejecuta las sentencias de un comando una tras
    /// otra sin rollback, asi que si la devolucion fallara a mitad quedaria grabada a
    /// medias. Separados, cada uno es atomico por su cuenta.
    /// </summary>
    public async Task SendHomeAsync(string festivalId, string mark)
    {
        LiveFestival live = Live(festivalId);

        (string standing, string doing) = WhatIsThisMark(live, mark);

        if (standing is null)
            throw new InvalidOperationException(Texts.T("organizador.no.codigo-desconocido"));

        if (string.Equals(standing, "SentHome", StringComparison.Ordinal)) return;

        string who = Read(live.Journal.Using("print festival.ErrandMarked(@marca).TakenBy w;")
            .WithParameters(p => p.UserParameter<string>("marca", mark))
            .PerformQuery(), "w") ?? string.Empty;

        // Primero los cartones, despues el papel: si la devolucion fallara, preferimos que
        // siga autorizada y sin cartones que revocada con cartones que nadie puede recoger.
        //
        // Se le pregunta al PAPELITO que le queda en la mano, no al NOMBRE: aca ya tenemos
        // la marca, y buscarla de vuelta por nombre traeria la de otro papel el dia que la
        // misma persona tenga dos (se reinstalo la app, o volvio despues de una revocacion).
        // Cuando eso pasaba, el GiveBack se hacia sobre un comerciante distinto del que
        // tiene los cartones y el pueblo lo rechazaba entero: o los cartones se quedaban
        // trabados en una mano que ya nadie puede nombrar, o la revocacion no se podia
        // hacer nunca.
        bool sells = string.Equals(doing, "Selling", StringComparison.Ordinal);

        List<long> stillHers = who.Length == 0 || !sells
            ? new List<long>()
            : NumbersUnder(live, mark, "NumbersWith");

        // Sacarla NO se bloquea, y es a proposito: si el telefono se le murio de verdad, el
        // organizador TIENE que poder recuperar esos cartones para venderlos. Lo que no puede
        // pasar es que lo haga sin saber lo que arriesga.
        //
        // Es el unico camino que le saca cartones a alguien sin preguntarle al telefono, y
        // desde que ella puede cobrar sin el organizador eso tiene precio: cada carton que
        // vuelve al libro es una venta suya que va a rebotar cuando por fin la cuente, con la
        // plata ya en la mano de ella. El organizador no puede saber si vendio -- por eso se
        // le dice lo que se sabe, que es que no contesta, y lo demas lo averigua hablando.
        if (stillHers.Count > 0
            && !string.IsNullOrWhiteSpace(who)
            && !await _phone.CanReachAsync(Where(live, mark), _life.Token))
        {
                        _say(Texts.T("organizador.log.ojo-recogiendo-sin-contestar", ("nombre", who), ("n", stillHers.Count.ToString())));
        }

        if (stillHers.Count > 0)
        {
            live.Journal.Using("festival.MerchantMarked(@marca).GiveBack(festival, @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", mark);
                    p.UserParameter<IEnumerable<long>>("cartones", stillHers);
                })
                .PerformCommand();

            lock (_guard)
            {
                live.InTheBook.AddRange(stillHers);
                live.InTheBook.Sort();
            }

            _say(Texts.T("organizador.log.le-recogi-n", ("n", stillHers.Count.ToString()), ("nombre", who), ("cuales", string.Join(", ", stillHers))));
        }

        live.Journal.Using("errand = festival.ErrandMarked(@marca); elder.SendHome(festival, errand);")
            .WithParameters(p => p.UserParameter<string>("marca", mark))
            .PerformCommand();

        Attendee gone;

        lock (_guard)
        {
            gone = live.Attendees.FirstOrDefault(a =>
                string.Equals(a.Mark, mark, StringComparison.Ordinal));

            if (gone is not null) live.Attendees.Remove(gone);
        }

                _say(who.Length == 0
            ? Texts.T("organizador.log.invalide-codigo", ("codigo", mark))
            : Texts.T("organizador.log.mande-a-casa", ("nombre", who)));

        // Avisarle antes de cortarle el canal, en ese orden: primero que se le vacie el
        // taco en el telefono, y despues dejar de rutearle.
        if (gone is not null)
        {
            string topic = CustodyNotice.TopicFor(live.Id, gone.Handle);

            if (stillHers.Count > 0)
                await _phone.ProduceAsync(
                    topic, gone.Name, null, CustodyNotice.BodyTaking(stillHers));

            _phone.StopRouting(topic);
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<Attendee> AttendeesOf(string id)
    {
        LiveFestival live;

        lock (_guard)
        {
            if (!_live.TryGetValue(id, out live)) return Array.Empty<Attendee>();
        }

        SyncMirrorsWithTheJournal(live);

        lock (_guard) return live.Attendees.ToArray();
    }

    /// <summary>
    /// Cada comprador con sus cartones, como el diario lo sabe. Es la mitad que le
    /// faltaba a "quienes juegan": el que compro y todavia no entro con su telefono
    /// tambien juega esta noche -- la lista de alistados solo tiene a los que ya
    /// escanearon su codigo. Sale de SoldTo, que las ventas alimentan en vivo y la
    /// rehidratacion rearma del diario, asi que sobrevive a un reinicio.
    /// </summary>
    public IReadOnlyList<(string Buyer, IReadOnlyList<long> Cards)> BuyersOf(string id)
    {
        LiveFestival live;

        lock (_guard)
        {
            if (!_live.TryGetValue(id, out live))
                return Array.Empty<(string, IReadOnlyList<long>)>();

            return live.SoldTo
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => (pair.Key,
                    (IReadOnlyList<long>)pair.Value.OrderBy(card => card).ToList()))
                .ToList();
        }
    }

    /// <summary>
    /// Vuelve a leer del diario lo que la pantalla del organizador muestra de custodia y
    /// de libro.
    ///
    /// Existe porque el organizador tiene un read-model paralelo en C# --Attendee.Cards,
    /// Attendee.Sold, live.InTheBook-- y ese espejo se corregia A MANO en cada camino
    /// local: primero PerformCommand, y despues la lista. Mientras la unica puerta de
    /// escritura fue la HTTP eso alcanzo. Con el corrillo hay una SEGUNDA puerta --el
    /// comando que un Cast le reenvia al Director-- que asienta en el diario sin pasar
    /// por aca, y nadie le avisaba al espejo: la colaboradora devolvia sus cartones, el
    /// diario lo aceptaba, y el organizador seguia viendolos en su mano hasta reiniciar
    /// la app. Lo medimos con la devolucion; le habria pasado igual al traspaso, a la
    /// anulacion y a la venta.
    ///
    /// Por eso el espejo se rearma del diario ANTES de entregarselo a quien pinta, y no
    /// se le pide a cada camino de escritura que se acuerde de corregirlo. Un camino
    /// nuevo que no sepa de esta lista ya no puede dejar la pantalla mintiendo.
    ///
    /// No cuesta una lectura por pintada: si el diario no avanzo desde la ultima vez, no
    /// hay nada que releer.
    /// </summary>
    private void SyncMirrorsWithTheJournal(LiveFestival live)
    {
        long at;

        try
        {
            at = live.Journal.CurrentEntryId;
        }
        catch
        {
            // Sin diario a mano no hay nada que sincronizar; se sigue con lo que haya en
            // memoria, que es lo que la app hacia antes de que esto existiera.
            return;
        }

        lock (_guard)
        {
            if (live.MirrorsAtEntry == at) return;

            // Con una entrega a medio camino el libro en memoria esta adelantado al
            // diario a proposito. Se deja como esta y se rearma en la pintada siguiente.
            if (live.HandoutsInFlight > 0) return;
        }

        List<long> upForSale = NumbersIn(live.Journal.PerformQry(
            "foreach (n in festival.NumbersUpForSale()) { print n 'c'; }"));

        List<long> stillInTheBook = NumbersIn(live.Journal.PerformQry(
            "foreach (n in festival.NumbersInTheBook()) { print n 'c'; }"));

        List<string> marks;

        lock (_guard)
        {
            // SOLO quien VENDE. Lo que se relee aca es custodia, y custodia solo tiene quien
            // vende: al papelito de quien canta no se le puede pedir su comerciante, igual
            // que al carnet de quien compra. Y eso no devuelve vacio, TIRA.
            //
            // Empezo filtrando por jugador --alcanzaba con que UNO entrara para que este
            // rearmado dejara de correr, y con el se caian la lista de gente y los numeros del
            // libro-- y quien canta se colo por el mismo agujero: es colaborador, asi que
            // pasaba el filtro. Con el try de abajo el espejo ya no se moria, pero quedaba
            // quejandose en cada bolita.
            marks = live.Attendees
                .Where(a => a.IsSeller && !string.IsNullOrWhiteSpace(a.Mark))
                .Select(a => a.Mark)
                .ToList();
        }

        // Las consultas van FUERA del candado: son del diario, no de estas listas, y
        // tenerlo tomado mientras se consulta el actor traba la pantalla entera.
        Dictionary<string, (List<long> InHand, List<long> Sold)> fromTheJournal = new(StringComparer.Ordinal);

        foreach (string mark in marks)
        {
            try
            {
                fromTheJournal[mark] = (
                    NumbersUnder(live, mark, "NumbersWith"),
                    NumbersUnder(live, mark, "NumbersSoldBy"));
            }
            catch (Exception failure)
            {
                // Un papelito que el diario no reconoce -- revocado y vuelto a repartir, o de
                // un bingo que se reabrio -- no puede dejar sin espejo a las demas.
                _say(Texts.T("organizador.log.no-pude-releer-papelito", ("codigo", mark), ("motivo", Texts.Explain(failure))));
            }
        }

        List<(long Card, string Buyer, string Mark)> sales = WhoBoughtWhat(live);

        List<(Attendee Who, List<long> Cards)> toHandOver = new();

        lock (_guard)
        {
            live.ForSale.Clear();
            live.ForSale.AddRange(upForSale);

            live.InTheBook.Clear();
            live.InTheBook.AddRange(stillInTheBook);

            foreach (Attendee who in live.Attendees)
            {
                if (string.IsNullOrWhiteSpace(who.Mark)) continue;
                if (!fromTheJournal.TryGetValue(who.Mark, out var mine)) continue;

                who.Cards.Clear();
                who.Cards.AddRange(mine.InHand);
                who.Cards.Sort();

                who.Sold.Clear();
                who.Sold.AddRange(mine.Sold);
            }

            live.SoldTo.Clear();

            foreach ((long card, string buyer, string _) in sales)
            {
                if (string.IsNullOrWhiteSpace(buyer)) continue;

                if (!live.SoldTo.TryGetValue(buyer, out List<long> theirs))
                    live.SoldTo[buyer] = theirs = new List<long>();

                if (!theirs.Contains(card)) theirs.Add(card);
            }

            // Lo que el diario le vendio a quien YA esta adentro y su telefono todavia no
            // tiene. Por la puerta esto lo hacia HandOverTheNewCards al pie de la venta; una
            // venta que entra por el corrillo no pasa por ahi, y sin esto la segunda compra
            // se le quedaba en el aire hasta que volviera a escanear.
            foreach (Attendee who in live.Attendees)
            {
                if (who.IsCollaborator || string.IsNullOrWhiteSpace(who.Mark)) continue;

                List<long> missing = new();

                foreach ((long card, string _, string mark) in sales)
                {
                    if (!string.Equals(mark, who.Mark, StringComparison.Ordinal)) continue;
                    if (who.Cards.Contains(card)) continue;

                    who.Cards.Add(card);
                    missing.Add(card);
                }

                if (missing.Count == 0) continue;

                who.Cards.Sort();
                toHandOver.Add((who, missing));
            }

            live.MirrorsAtEntry = at;
        }

        foreach ((Attendee who, List<long> cards) in toHandOver)
        {
                        _say(Texts.T("organizador.log.ya-estaba-le-mando", ("nombre", who.Name), ("cuales", string.Join(", ", cards))));

            Attendee theOneWhoBought = who;
            List<long> theirs = cards;
            _ = Task.Run(() => TellThemTheseCardsAsync(live, theOneWhoBought, theirs));
        }
    }

    /// <summary>
    /// Quien compro que, TODO de una sola lectura.
    ///
    /// Preguntarlo carton por carton -- que es como se armaba al abrir el bingo -- son
    /// doscientos viajes al actor cada vez que el diario avanza, y avanza en cada bolita.
    /// El diario sabe darlo junto: una vuelta por venta y tres cosas impresas en cada una.
    ///
    /// Va el carnet ademas del nombre porque el nombre no distingue a dos Ritas, y lo que
    /// se decide con esto -- a que telefono le caen los cartones -- no se puede decidir mal.
    /// </summary>
    private List<(long Card, string Buyer, string Mark)> WhoBoughtWhat(LiveFestival live)
    {
        List<(long, string, string)> sales = new();

        string read;

        try
        {
            read = live.Journal.PerformQry(
                "foreach (n in festival.NumbersSold()) { print n 'c';"
                + " print festival.ParticipationNumbered(n).Buyer.Name 'q';"
                + " print festival.ParticipationNumbered(n).Buyer.Mark 'k'; }");
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.no-pude-releer-ventas", ("motivo", Texts.Explain(failure))));
            return sales;
        }

        if (string.IsNullOrWhiteSpace(read)) return sales;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(read);

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    if (!item.TryGetProperty("c", out JsonElement card)) continue;
                    if (!card.TryGetInt64(out long number)) continue;

                    string buyer = item.TryGetProperty("q", out JsonElement q)
                        ? q.GetString() ?? string.Empty : string.Empty;
                    string mark = item.TryGetProperty("k", out JsonElement k)
                        ? k.GetString() ?? string.Empty : string.Empty;

                    sales.Add((number, buyer, mark));
                }
            }
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.ventas-formato", ("motivo", Texts.Explain(failure))));
        }

        return sales;
    }

    public IReadOnlyList<int> Drawn(string id)
    {
        LiveFestival live = Live(id);

        // UNA consulta, no una por bolita. Esto se llama en cada repintada, y con setenta
        // cantadas eran setenta y un viajes al diario cada vez que algo cambiaba en la
        // pantalla -- justo cuando mas cambia, que es cantando. La mesa las sabe dar todas
        // juntas desde siempre; lo que faltaba era pedirselas asi.
        List<long> drawn = NumbersIn(live.Journal.PerformQry(
            "foreach (n in mesa.DrawnNumbers) { print n 'c'; }"));

        List<int> asBalls = new(drawn.Count);
        foreach (long n in drawn) asBalls.Add((int)n);

        return asBalls;
    }

    public string State(string id) => Live(id).Journal.PerformQry("print mesa.State s;");

    /// <summary>
    /// Lo minimo que hay que preguntar para saber si vale la pena preguntar el resto: UNA
    /// consulta que trae el pulso de los dos dominios.
    ///
    /// Contra las decenas que cuesta el retrato completo -- una por bolita, el progreso de
    /// cada carton por cada figura --, que hasta ahora se pagaban SIEMPRE, cada segundo,
    /// para descubrir al final que nada habia cambiado. Y lo normal es que nada haya
    /// cambiado: entre bolita y bolita pasan quince segundos.
    /// </summary>
    public string Pulse(string id)
    {
        LiveFestival live = Live(id);

        try
        {
            return live.Journal.PerformQry("{ print mesa.Pulse m; print festival.Pulse f; }");
        }
        catch (Exception)
        {
            // Sin pulso legible no se adivina: se contesta algo distinto cada vez para que
            // quien mira siga haciendo el trabajo completo, que es lo que hacia siempre.
            return Guid.NewGuid().ToString();
        }
    }

    /// <summary>
    /// Como va cada carton QUE ESTA EN LA MESA, con el nombre de quien lo compro.
    /// Antes se armaba recorriendo los telefonos conectados y sus cartones, y por eso
    /// mostraba papel que no juega -- los que la colaboradora todavia tiene en la mano,
    /// con un progreso que nunca podia completarse -- y escondia papel que si juega:
    /// quien compro y no escaneo su codigo no tenia fila, asi que podia ganar sin que
    /// el letrero de BINGO se prendiera. Y al reiniciar la app quedaba en blanco, porque
    /// esa lista es memoria.
    /// </summary>
    public IReadOnlyList<(string Who, long Card, string Figure, int Have, int Need,
        bool Done, bool Dismissed, bool FigureClosed)> Progress(
        string id, IReadOnlyCollection<int> drawn)
    {
        LiveFestival live = Live(id);
        List<(string, long, string, int, int, bool, bool, bool)> rows = new();
        IReadOnlyList<string> figures = FiguresOf(id);

        if (figures.Count == 0) return rows;

        string prints = "print n 'c'";

        for (int i = 0; i < figures.Count; i++)
        {
            prints += $", mesa.HasCompleted(n, @figura{i}) 'g{i}'";
            prints += $", mesa.WasDismissed(n, @figura{i}) 'x{i}'";
        }

        void TheFigures(Parameters p)
        {
            for (int i = 0; i < figures.Count; i++)
                p.UserParameter<string>("figura" + i, figures[i]);
        }

        string board = live.Journal.Using(
            $"foreach (n in mesa.NumbersInPlay()) {{ {prints}; }}")
            .WithParameters(TheFigures)
            .PerformQuery();

        string closedPrints = "{ " + string.Join(" ", figures.Select(
            (_, i) => $"print mesa.IsFigureClosed(@figura{i}) k{i};")) + " }";

        string closedRead = live.Journal.Using(closedPrints)
            .WithParameters(TheFigures)
            .PerformQuery();
        bool[] closed = figures.Select((_, i) => AsBool(closedRead, "k" + i)).ToArray();

        Dictionary<long, string> whoBought = new();

        lock (_guard)
        {
            foreach (KeyValuePair<string, List<long>> theirs in live.SoldTo)
                foreach (long card in theirs.Value) whoBought[card] = theirs.Key;
        }

        foreach ((long card, bool[] done, bool[] dismissed) in ReadTheBoard(board, figures.Count))
        {
            string who = whoBought.TryGetValue(card, out string buyer) ? buyer : Texts.T("general.sin-nombre");

            for (int i = 0; i < figures.Count; i++)
            {
                IReadOnlyList<int> needs = BingoGame.ClosestFor(card, figures[i], drawn);
                int have = needs.Count(n => drawn.Contains(n));

                rows.Add((who, card, figures[i], have, needs.Count,
                    done[i], dismissed[i], closed[i]));
            }
        }

        return rows;
    }

    // ── consultas de apoyo ───────────────────────────────────────────────────

    private static string Clean(string name) => name.Replace("'", " ").Trim();


    /// <summary>
    /// El tablero entero, tal como lo devuelve UN solo foreach del DSL:
    /// {"n":[{"c":1,"g0":false,"g1":true}, ...]}
    /// Antes era una consulta por carton POR FIGURA: con 300 cartones y 3 figuras, 900
    /// consultas cada vez que la pantalla se repintaba.
    /// </summary>
    private static List<(long Card, bool[] Done, bool[] Dismissed)> ReadTheBoard(
        string json, int howManyFigures)
    {
        List<(long, bool[], bool[])> board = new();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    if (!item.TryGetProperty("c", out JsonElement number)) continue;
                    if (!number.TryGetInt64(out long card)) continue;

                    bool[] done = new bool[howManyFigures];
                    bool[] dismissed = new bool[howManyFigures];

                    for (int i = 0; i < howManyFigures; i++)
                    {
                        done[i] = item.TryGetProperty("g" + i, out JsonElement flag)
                            && flag.ValueKind == JsonValueKind.True;
                        dismissed[i] = item.TryGetProperty("x" + i, out JsonElement gone)
                            && gone.ValueKind == JsonValueKind.True;
                    }

                    board.Add((card, done, dismissed));
                }
            }
        }
        catch (Exception)
        {
            return board;
        }

        return board;
    }

    /// <summary>Los numeros que devuelve un foreach del DSL. El dominio entrega la
    /// coleccion y la proyeccion vive en el print, que es lo que pide la guia: nada
    /// de serializar adentro del dominio.</summary>
    /// <summary>El papelito de venta que esa persona tomo, leido del diario; null si no
    /// tomo ninguno. Es lo que reemplaza al viejo global con el nombre pegado: la
    /// identidad del comerciante vive en el papel, y para llegar a el desde un nombre
    /// hay que buscar cual papel tiene ese nombre escrito.
    /// Una persona puede llegar a tener MAS DE UNO (se reinstalo la app y escaneo otro QR,
    /// o volvio despues de que la mandaran a la casa), y cada papel es un comerciante
    /// distinto con su propia mano de cartones. Se elige el que esta VIVO y el ultimo:
    /// es el que el asistente lleva puesto y con el que va a vender, asi que es el unico
    /// con el que la memoria y el diario pueden estar de acuerdo. Los revocados solo se
    /// miran si no quedo ninguno vivo, para no perder de vista lo que hizo.</summary>
    private string SellingMarkOf(LiveFestival live, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        string alive = null;
        string anyOfHers = null;

        foreach (HandedOut one in ErrandsOf(live))
        {
            if (one.OnTheTable || !Duties.IsSeller(one.Duty)) continue;

            if (!string.Equals(one.TakenBy, name, StringComparison.OrdinalIgnoreCase)) continue;

            anyOfHers = one.Mark;

            if (one.InSomebodysHands) alive = one.Mark;
        }

        return alive ?? anyOfHers;
    }

    /// <summary>Los cartones que el diario le reconoce a esa persona: una sola consulta,
    /// que es lo que gana pedirle la lista al bingo en vez de barrer el libro carton
    /// por carton.</summary>
    private List<long> NumbersOf(LiveFestival live, string name, string verb) =>
        NumbersUnder(live, SellingMarkOf(live, name), verb);

    /// <summary>Lo mismo pero preguntado por el papelito, que es como el diario lo tiene
    /// guardado. La marca va como parametro; lo unico pegado al texto es el verbo, que
    /// la gramatica no deja pasar de otra forma.</summary>
    /// <summary>
    /// Los cartones de un papelito. El verbo que se le pide contesta VACIO cuando la marca no
    /// es de vender --quien canta no tiene cartones-- en vez de reventar.
    ///
    /// Antes se le pedia el comerciante a la marca, y eso tira. No devolvia vacio: se caia el
    /// query entero, y con el todo lo que estuviera recorriendo. Mordio tres veces: el espejo
    /// del organizador con un jugador adentro, la lectura de la replica de quien canta, y el
    /// arranque -- ahi el organizador perdio un bingo entero y dijo que el village estaba
    /// vacio, con siete cartones vendidos y una bolita cantada adentro.
    /// </summary>
    private List<long> NumbersUnder(LiveFestival live, string mark, string verb)
    {
        if (string.IsNullOrWhiteSpace(mark)) return new List<long>();

        string asking = string.Equals(verb, "NumbersSoldBy", StringComparison.Ordinal)
            ? "NumbersSoldUnder"
            : "NumbersHeldBy";

        return NumbersIn(live.Journal
            .Using($"foreach (n in festival.{asking}(@marca)) {{ print n 'c'; }}")
            .WithParameters(p => p.UserParameter<string>("marca", mark))
            .PerformQuery());
    }

    /// <summary>
    /// Lo que el diario le reconoce a esta colaboradora. Por el papelito si lo trae, y por el
    /// nombre solo cuando no hay ninguno.
    ///
    /// Este es el camino que corre cuando al organizador se le reinicio la app y su memoria
    /// esta vacia: es justo ahi donde el nombre hace mas dano, porque las dos Marias se
    /// llevan cada una los cartones de la otra sin que nadie vea un error.
    /// </summary>
    private List<long> CustodyOf(LiveFestival live, string name, string mark) =>
        string.IsNullOrWhiteSpace(mark)
            ? NumbersOf(live, name, "NumbersWith")
            : NumbersUnder(live, mark, "NumbersWith");

    private bool SoldAny(LiveFestival live, string name, string mark) =>
        SoldBy(live, name, mark).Count > 0;

    private List<long> SoldBy(LiveFestival live, string name, string mark) =>
        string.IsNullOrWhiteSpace(mark)
            ? NumbersOf(live, name, "NumbersSoldBy")
            : NumbersUnder(live, mark, "NumbersSoldBy");

    /// <summary>Lo que vendio, para la respuesta del enlist. A quien vino a cantar se le
    /// contesta una lista vacia sin preguntarle nada al diario: su papelito no tiene
    /// comerciante detras.</summary>
    private List<object> SoldOf(LiveFestival live, Attendee attendee) =>
        attendee.IsSeller
            ? SoldWhat(live, attendee.Name, attendee.Mark)
                .Select(s => (object)new { quien = s.Who, marca = s.Mark, carton = s.Card })
                .ToList()
            : new List<object>();

    private List<(string Who, string Mark, long Card)> SoldWhat(
        LiveFestival live, string name, string mark)
    {
        List<(string Who, string Mark, long Card)> sold = new();

        foreach (long card in SoldBy(live, name, mark))
        {
            try
            {
                string read = live.Journal.Using(
                    "{ print festival.ParticipationNumbered(@carton).Buyer.Name c; "
                    + "print festival.ParticipationNumbered(@carton).Buyer.Mark m; }")
                    .WithParameters(p => p.UserParameter<long>("carton", card))
                    .PerformQuery();

                sold.Add((Read(read, "c"), Read(read, "m") ?? string.Empty, card));
            }
            catch (Exception failure)
            {
                // Callarse aca es perderle una venta a alguien sin que nadie se entere: esta
                // lista es la que la colaboradora ve al volver a presentarse.
                _say(Texts.T("organizador.log.no-pude-leer-comprador", ("carton", card.ToString()), ("motivo", Texts.Explain(failure))));
            }
        }

        return sold;
    }

    /// <summary>La marca de quien compro este carton. Vacia en los bingos de antes de las
    /// marcas.</summary>
    private string MarkOfWhoeverBought(LiveFestival live, long card)
    {
        try
        {
            return Read(live.Journal.Using("print festival.ParticipationNumbered(@carton).Buyer.Mark m;")
                .WithParameters(p => p.UserParameter<long>("carton", card))
                .PerformQuery(), "m") ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Como se llama quien compro este carton, segun el diario.</summary>
    private string NameOfWhoeverBought(LiveFestival live, long card)
    {
        try
        {
            return Read(live.Journal.Using("print festival.ParticipationNumbered(@carton).Buyer.Name q;")
                .WithParameters(p => p.UserParameter<long>("carton", card))
                .PerformQuery(), "q") ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Los cartones de quien trae esta marca, leidos del diario. El diario es el
    /// unico que sabe distinguir a dos compradoras del mismo nombre.</summary>
    private List<long> CardsBoughtBy(LiveFestival live, string mark)
    {
        List<long> theirs = new();

        if (string.IsNullOrWhiteSpace(mark)) return theirs;

        // La marca va por parametro y NO pegada al texto. Es la regla del sustrato, y aca
        // ademas hace falta: lo que la colaboradora teclea llega crudo si no son ocho
        // alfanumericos, asi que un apostrofe romperia la consulta, el catch se lo comeria,
        // y el codigo del comprador saldria sin un solo carton para una venta ya cobrada.
        try
        {
            int howMany = AsInt(live.Journal.Using("print festival.CountBoughtBy(@marca) n;")
                .WithParameters(p => p.UserParameter<string>("marca", mark))
                .PerformQuery(), "n");

            for (int position = 1; position <= howMany; position++)
            {
                theirs.Add(AsLong(live.Journal
                    .Using("print festival.BoughtByAt(@marca, @puesto) n;")
                    .WithParameters(p =>
                    {
                        p.UserParameter<string>("marca", mark);
                        p.UserParameter<int>("puesto", position);
                    })
                    .PerformQuery(), "n"));
            }
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.no-pude-leer-cartones-marca", ("codigo", mark), ("motivo", Texts.Explain(failure))));
        }

        return theirs;
    }

    /// <summary>Lo que hace que la segunda compra aparezca sola en su telefono: si quien
    /// compro ya esta adentro, se le anota y se le avisa en el momento, sin que tenga que
    /// volver a escanear nada. Si todavia no entro, no pasa nada: los recibe al entrar.</summary>
    private void HandOverTheNewCards(
        LiveFestival live, string buyer, string buyerMark, IReadOnlyList<long> justSold)
    {
        Attendee who;
        List<long> toTell = new();

        lock (_guard)
        {
            who = live.Attendees.FirstOrDefault(a => !a.IsCollaborator
                && string.Equals(a.Mark, buyerMark, StringComparison.Ordinal));

            if (who is null) return;

            foreach (long card in justSold)
            {
                if (who.Cards.Contains(card)) continue;

                who.Cards.Add(card);
                toTell.Add(card);
            }

            who.Cards.Sort();
        }

        if (toTell.Count == 0) return;

        _say(Texts.T("organizador.log.ya-estaba-le-mando", ("nombre", buyer), ("cuales", string.Join(", ", toTell))));

        Attendee theOneWhoBought = who;
        _ = Task.Run(() => TellThemTheseCardsAsync(live, theOneWhoBought, toTell));
    }

    /// <summary>La prueba de que un carton es de quien dice traerlo. Con marca se compara
    /// la marca, que es lo unico que distingue a dos personas del mismo nombre; sin marca
    /// se cae al nombre, que es como se conocen los bingos de antes.</summary>
    private bool WasSoldTo(LiveFestival live, long card, JoinRequest who) =>
        string.IsNullOrWhiteSpace(who.Mark)
            ? WasSoldTo(live, card, who.Name)
            : WasSoldToTheMarked(live, card, who.Mark);

    private bool WasSoldToTheMarked(LiveFestival live, long card, string mark)
    {
        try
        {
            string read = live.Journal.Using(
                "{ print festival.ParticipationNumbered(@carton).IsSold sold; "
                + "print festival.ParticipationNumbered(@carton).Buyer.Mark who; }")
                .WithParameters(p => p.UserParameter<long>("carton", card))
                .PerformQuery();

            return AsBool(read, "sold")
                && string.Equals(Read(read, "who"), mark, StringComparison.Ordinal);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.no-pude-verificar-carton", ("carton", card.ToString()), ("motivo", Texts.Explain(failure))));
            return false;
        }
    }

    private bool WasSoldTo(LiveFestival live, long card, string buyer)
    {
        try
        {
            string read = live.Journal.Using(
                "{ print festival.ParticipationNumbered(@carton).IsSold sold; "
                + "print festival.ParticipationNumbered(@carton).Buyer.Name who; }")
                .WithParameters(p => p.UserParameter<long>("carton", card))
                .PerformQuery();

            return AsBool(read, "sold")
                && string.Equals(Read(read, "who"), buyer, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception failure)
        {
            _say(Texts.T("organizador.log.no-pude-verificar-carton", ("carton", card.ToString()), ("motivo", Texts.Explain(failure))));
            return false;
        }
    }

    public void Dispose()
    {
        try { _life?.Cancel(); } catch { }
        try { _phone?.Dispose(); } catch { }
        try { _life?.Dispose(); } catch { }
    }
}
