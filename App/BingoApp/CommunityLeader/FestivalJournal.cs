using System.Reflection;
using Choreography.StageManager;
using Choreography.Transport;
using Puppeteer;
using Puppeteer.EventSourcing;
using Puppeteer.EventSourcing.Follower;
using BingoApp.Huddle;
using BingoApp.Transport;
using Choreography.Transport.Iroh;

namespace BingoApp.CommunityLeader;

/// <summary>
/// El diario del bingo, con una sola puerta.
///
/// Adentro hay un StageV2 y el organizador es su Director. Todo comando pasa por aca, y esa
/// es la razon de que exista: un Cast que le pida Using(...) al actor por adentro escribe
/// LOCAL y se saltea el reenvio al Director, que es justo lo que el corrillo viene a dar.
///
/// La forma fluida es la que ya usaba LiveCommunityLeader --Using(...).WithParameters(...)
/// .PerformCommand()-- a proposito: asi los ciento y pico de sitios que le hablan al diario
/// no se tocaron cuando el sustrato cambio debajo.
/// </summary>
public sealed class FestivalJournal : IDisposable
{

    private readonly StageV2 _stage;
    private readonly string _who;
    private readonly Action<string> _say;

    private string _dir;
    private IrohTransport _wire;

    private FestivalJournal(StageV2 stage, string who, Action<string> say)
    {
        _stage = stage;
        _who = who;
        _say = say;
    }

    public static FestivalJournal Open(string actorName, Action<string> say, params Assembly[] libraries) =>
        new FestivalJournal(new StageV2(WhoAmI(), actorName, libraries), actorName, say);

    public FestivalJournal ConfigureStorage(DatabaseType kind, string connectionString)
    {
        _dir = connectionString.StartsWith("path=", StringComparison.Ordinal)
            ? connectionString[5..]
            : connectionString;

        _stage.ConfigureStorage(kind, "path=" + _dir);
        return this;
    }

    public FestivalJournal Logger(IPuppeteerLogger logger)
    {
        _stage.Logger(logger);
        return this;
    }

    public FestivalJournal UseTellTransport(Puppeteer.Tell.ITransport transport)
    {
        _stage.UseTellTransport(transport);
        return this;
    }

    /// <summary>
    /// Levanta el diario y se hace Director.
    ///
    /// El orden importa y no es negociable: el logger ya quedo puesto antes (Stage tira si
    /// se invierte), el transporte se configura antes de arrancar, y el ascenso a Director
    /// va ANTES del primer comando -- sin el, el Stage nace Isolated y EnsureCanWrite tumba
    /// la velada en la primera escritura.
    /// </summary>
    public void Start()
    {
        _wire = IrohWire.ForStage(_stage.Id, _dir, new JournalVoice(_say), _say);
        _stage.ConfigureTransport(_wire);

        Block(() => _stage.StartAsync());
        Block(() => _wire.StartAsync());

        _stage.PromoteToDirector(force: true);

        _say(Texts.T("organizador.log.diario-escucha-iroh", ("id", IrohWire.Short(_wire.LocalEndpointId))));
    }

    public Reactions Reactions => _stage.Reactions;

    public IrohTransport Wire => _wire;


    public long CurrentEntryId => _stage.CurrentEntryId;

    /// <summary>
    /// Si este diario se cree el que manda.
    ///
    /// En el organizador es siempre true y no dice nada. Donde importa es del lado del
    /// colaborador, y ahi es una MALA noticia: quiere decir que el sustrato lo ascendio.
    /// Cuando el organizador deja de latir, el vigia de cada Cast llama a una eleccion, y
    /// como el bus de coordinacion es una estrella --cada Cast solo ve al organizador-- cada
    /// uno se cuenta a si mismo, alcanza el quorum de uno y se corona.
    ///
    /// Ascendida, ella deja de reenviarle nada a nadie: sus comandos se asientan en SU copia,
    /// que despues se aparta al reengancharse. Escribir ahi es escribir en el aire.
    /// </summary>
    public bool IAmTheOneInCharge => _stage.IsDirector;

    /// <summary>
    /// Avisa cuando se cayo el organizador, que es lo que el sustrato SI sabe y no cuenta
    /// solo.
    ///
    /// El vigia del sustrato mira los latidos del Director y cuando pasan DirectorTimeout
    /// sin ninguno --15 segundos, revisados cada 5-- limpia el Director y dispara esto. Lo
    /// importante es DONDE dispara: justo antes de StartElection(), sincronico, en el mismo
    /// hilo. O sea que a esta reaccion la corren PRIMERO, antes de que este Cast pueda
    /// coronarse solo. No hay carrera contra el reloj: somos los primeros en enterarnos.
    ///
    /// Ojo con lo que se haga adentro: bloquear aca demora el hilo del vigia. Va lo minimo,
    /// y lo caro por otro lado.
    /// </summary>
    public void WhenTheLeaderIsGone(Action say)
    {
        if (say is null) return;

        _stage.OnDirectorLost += _ => say();
    }

    /// <summary>
    /// Quienes estaban en el corrillo la ultima vez, aunque la app se haya reiniciado.
    ///
    /// El sustrato persiste la MEMBRESIA (peers.bin) y la devuelve al arrancar, antes de que
    /// exista un solo canal vivo. Lo que NO devuelve son las direcciones: esas son del
    /// anfitrion. Nos sirve para lo que mas dolia -- que el organizador volviera creyendo que
    /// nunca entro nadie.
    /// </summary>
    public IReadOnlyList<PerformerId> WhoWasInTheHuddle() => _stage.RecallKnownPeers();

    public string PerformQry(string script) => _stage.PerformQry(script, new Parameters());

    public Invocation Using(string script) => new Invocation(this, script, null);

    public Invocation Using(string scriptForCheck, string scriptForCommand) =>
        new Invocation(this, scriptForCommand, scriptForCheck);

    public void Dispose() => Block(async () =>
    {
        await _stage.DisposeAsync();
        if (_wire is not null) await _wire.DisposeAsync();
    });



    /// <summary>
    /// Abre MI copia del diario sin pegarse a nadie.
    ///
    /// Es para cuando el organizador no esta: no hay Director al que pedirle nada, pero el
    /// diario que ya replique esta entero en este telefono y se puede leer. No se levanta
    /// transporte -- no hay con quien hablar -- y por lo tanto tampoco se puede escribir.
    /// </summary>
    public static async Task<FestivalJournal> OpenMineAsync(
        string actorName, string dir, Action<string> say, CancellationToken life,
        params Assembly[] libraries)
    {
        FestivalJournal journal = new FestivalJournal(
            new StageV2(WhoAmI(), actorName, libraries), actorName, say);

        journal._dir = dir;
        journal._stage.ConfigureStorage(DatabaseType.FileSystem, "path=" + dir);
        // El Stage exige transporte aunque no haya con quien hablar. Se lo damos: queda
        // con su identidad iroh y no se pega a nadie. El dia que el organizador vuelva a
        // estar, esa puerta ya esta lista.
        journal._wire = IrohWire.ForStage(journal._stage.Id, dir, new JournalVoice(say), say);
        journal._stage.ConfigureTransport(journal._wire);

        await journal._stage.StartAsync(life);
        await journal._wire.StartAsync(life);

        say(Texts.T("colaborador.log.abri-copia-sin-corrillo"));
        return journal;
    }

    /// <summary>
    /// Abre el diario como CAST y se pega al corrillo del organizador con el pase que le
    /// dieron en la puerta.
    ///
    /// No se asciende a Director: un Cast lee local y reenvia lo que escribe. Y el catch-up
    /// no es automatico -- el organizador se lo manda del otro lado, en WelcomeAsync.
    /// </summary>
    public static async Task<FestivalJournal> JoinAsCastAsync(
        string actorName, string dir, HuddlePass pass, Action<string> say, CancellationToken life,
        params Assembly[] libraries)
    {
        FestivalJournal journal = new FestivalJournal(
            new StageV2(WhoAmI(), actorName, libraries), actorName, say);

        journal._dir = dir;
        Directory.CreateDirectory(dir);
        journal._stage.ConfigureStorage(DatabaseType.FileSystem, "path=" + dir);
        // Antes de ConfigureTransport y no despues: el transporte se construye CON el logger
        // adentro, y volver a cablearlo tira. Sin esto la replica se queja contra el logger
        // por defecto y en el telefono no lo oye nadie.
        journal._stage.Logger(new JournalVoice(say));

        journal._wire = IrohWire.ForStage(journal._stage.Id, dir, new JournalVoice(say), say);
        journal._stage.ConfigureTransport(journal._wire);

        await journal._stage.StartAsync(life);
        await journal._wire.StartAsync(life);

        PerformerId director = new PerformerId(Guid.Parse(pass.InvitedBy));

        IStageChannel coordination = await journal._stage.AcceptInvitationAsync(
            new ConnectionInvitation(director, ChannelPurpose.Coordination, pass.CoordinationAddress));
        await journal._stage.JoinCoordination(director, coordination);

        IStageChannel replication = await journal._stage.AcceptInvitationAsync(
            new ConnectionInvitation(director, ChannelPurpose.Replication, pass.ReplicationAddress));
        IStageChannel command = await journal._stage.AcceptInvitationAsync(
            new ConnectionInvitation(director, ChannelPurpose.Command, pass.CommandAddress));

        await journal._stage.ConnectToDirector(director, replication, command);

        say(Texts.T("colaborador.log.me-pegue-al-corrillo"));
        return journal;
    }

    // ── la puerta del corrillo ───────────────────────────────────────────────

    /// <summary>
    /// Acuña un pase para quien esta entrando y se queda esperandola.
    ///
    /// El pase se acuña EN LA PUERTA y no antes: la direccion de una invitacion lleva la IP
    /// del telefono adentro, asi que un pase guardado en un papelito repartido de vispera
    /// apunta a donde el organizador ya no esta. Acuñarlo en el momento lo ata a la direccion
    /// que este telefono tiene AHORA.
    ///
    /// Y la invitacion es de un solo uso: el listener la saca de la lista al primer toque. Por
    /// eso cada quien se lleva la suya.
    /// </summary>
    public async Task<HuddleStage.OpenDoor> OpenTheDoorAsync(CancellationToken life)
    {

        ConnectionInvitation warmUp = await _stage.CreateInvitationAsync(ChannelPurpose.Coordination);
        _ = _stage.WaitForConnectionAsync(warmUp, life);
        await Task.Delay(600, life);

        ConnectionInvitation forCoordination = await _stage.CreateInvitationAsync(ChannelPurpose.Coordination);
        ConnectionInvitation forReplication = await _stage.CreateInvitationAsync(ChannelPurpose.Replication);
        ConnectionInvitation forCommands = await _stage.CreateInvitationAsync(ChannelPurpose.Command);

        Task<IStageChannel> waitCoordination = _stage.WaitForConnectionAsync(forCoordination, life);
        Task<IStageChannel> waitReplication = _stage.WaitForConnectionAsync(forReplication, life);
        Task<IStageChannel> waitCommands = _stage.WaitForConnectionAsync(forCommands, life);

        return new HuddleStage.OpenDoor
        {
            Pass = new HuddlePass
            {
                CommunityLeaderTicket = _wire.LocalTicket,
                CommunityLeaderEndpointId = _wire.LocalEndpointId,
                InvitedBy = forCoordination.InviterId.ToString(),
                CoordinationAddress = forCoordination.Address,
                ReplicationAddress = forReplication.Address,
                CommandAddress = forCommands.Address,
            },
            Coordination = waitCoordination,
            Replication = waitReplication,
            Commands = waitCommands,
        };
    }

    /// <summary>La deja entrar cuando toca, y le manda el diario entero: el sustrato NO pone
    /// al dia a un Cast nuevo por su cuenta.</summary>
    public async Task WelcomeAsync(HuddleStage.OpenDoor door)
    {
        IStageChannel coordination = await door.Coordination;
        await _stage.JoinCoordination(coordination.RemotePerformerId, coordination);

        IStageChannel replication = await door.Replication;
        IStageChannel command = await door.Commands;

        await _stage.AcceptCastConnection(replication.RemotePerformerId, replication, command);
        await _stage.SendCatchUpAsync(replication.RemotePerformerId, 0);

        _say(Texts.T("organizador.log.entro-al-corrillo"));
    }


    /// <summary>
    /// Espera a que el diario deje de crecer y devuelve en que asiento quedo.
    ///
    /// Un Cast recien pegado NO sabe en que asiento va el Director, asi que no se puede
    /// esperar un numero concreto: se espera a que el catch-up termine de aplicarse, que se
    /// nota porque el contador se queda quieto. Preguntarlo antes da 0 y hace creer que la
    /// replicacion no anda, cuando lo unico que pasa es que todavia esta llegando.
    /// </summary>
    public async Task<long> SettleAsync(TimeSpan quiet, TimeSpan atMost, CancellationToken life)
    {
        long deadline = Environment.TickCount64 + (long)atMost.TotalMilliseconds;
        long seen = CurrentEntryId;
        long lastMoved = Environment.TickCount64;

        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(200, life);

            long now = CurrentEntryId;

            if (now != seen)
            {
                seen = now;
                lastMoved = Environment.TickCount64;
                continue;
            }

            if (Environment.TickCount64 - lastMoved >= quiet.TotalMilliseconds) break;
        }

        return seen;
    }

    // ── el puerto ────────────────────────────────────────────────────────────

    /// <summary>Quien es este telefono en la red. UNO por aparato y no uno por bingo: si
    /// fuera por bingo, el mismo telefono seria N performers distintos en el corrillo.</summary>
    private static PerformerId WhoAmI()
    {
        string path = Path.Combine(FileSystem.AppDataDirectory, "quien-soy.txt");

        if (File.Exists(path))
            return new PerformerId(Guid.ParseExact(File.ReadAllText(path).Trim(), "N"));

        PerformerId fresh = PerformerId.New();
        File.WriteAllText(path, fresh.ToString());
        return fresh;
    }

    /// <summary>
    /// Correr un Task desde un metodo sincrono sin colgar la app.
    ///
    /// StageV2 hace 'await Task.Run(...)' sin ConfigureAwait(false), asi que bloquearlo
    /// desde el hilo de UI de MAUI postea la continuacion al hilo que esta bloqueado: es
    /// deadlock duro. El Task.Run de afuera saca la espera de ese hilo.
    /// </summary>
    private static T Block<T>(Func<Task<T>> work) =>
        Task.Run(work).GetAwaiter().GetResult();

    private static void Block(Func<Task> work) =>
        Task.Run(work).GetAwaiter().GetResult();

    /// <summary>Un comando a medio armar. Junta el guion y sus parametros, y recien al
    /// ejecutarse se lo entrega al Stage -- que lo asienta si es Director y lo reenvia si
    /// es Cast.</summary>
    public sealed class Invocation
    {
        private readonly FestivalJournal _journal;
        private readonly string _script;
        private readonly string _check;
        private readonly Parameters _values = new();

        internal Invocation(FestivalJournal journal, string script, string check)
        {
            _journal = journal;
            _script = script;
            _check = check;
        }

        public Invocation WithParameters(Action<Parameters> configure)
        {
            configure(_values);
            return this;
        }

        public string PerformCommand() => Block(() =>
            _journal._stage.PerformCmd(_script, _values, DateTime.Now, "0.0.0.0", "Anonymous"));

        public string PerformQuery() => _journal._stage.PerformQry(_script, _values);

        public string PerformCheckThenCommand() => Block(() =>
            _journal._stage.PerformCheckThenCommand(
                _check, _script, _values, DateTime.Now, "0.0.0.0", "Anonymous"));
    }
}
