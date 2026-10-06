using System.Text.Json;
using Choreography.Transport.Brokered;
using BingoApp.Transport;
using BingoApp.Contract;

namespace BingoApp.Collaborator;

/// <summary>
/// El colaborador en vivo: vende o canta en los bingos a las que se unio con el
/// QR del organizador. Maneja SU lista de bingos; cada venta y cada canto viajan
/// marcados con el id del bingo que corresponde.
/// </summary>
public sealed class LiveCollaborator : IDisposable
{
    private const string SavedKey = "vendedor.funciones";

    private static LiveCollaborator _instance;

    /// <summary>El colaborador es uno solo por telefono: las pantallas comparten esta instancia.</summary>
    public static LiveCollaborator Instance => _instance ??= new LiveCollaborator();

    private readonly List<long> _inCustody = new();
    private readonly List<(string Who, string Mark, long Card)> _sold = new();
    private readonly List<int> _balls = new();
    private readonly object _guard = new();

    private Action<string> _say = _ => { };
    private PhoneToPhone _phone;
    private CancellationTokenSource _life;
    private OpenFestival _festival;
    private string _leaderTicket;
    private string _myName;

    /// <summary>El puerto en el que escucho. Se guarda para poder volver a presentarme sola
    /// cuando se cae el corrillo, sin que nadie toque la pantalla.</summary>
    private int _myPort;

    private string _myDuty = Duties.Sells;
    private IDisposable _listener;
    private IDisposable _roundListener;
    private IDisposable _drawListener;
    private CommunityLeader.FestivalJournal _replica;

    /// <summary>En que asiento va mi copia del diario, o 0 si todavia no me pegue.</summary>
    public long ReplicaEntry => _replica?.CurrentEntryId ?? 0;

    /// <summary>Entre por mi copia porque el organizador no contesto. Puedo LEER todo lo
    /// mio, pero no puedo asentar nada: sin Director no hay a quien reenviarle el comando.</summary>
    public bool WorkingAlone { get; private set; }

    /// <summary>
    /// No hay organizador a quien contarle.
    ///
    /// Dos formas de quedarse sin el, y las dos terminan igual. La conocida es entrar sola
    /// porque no contesto. La otra la descubrimos en el emulador y no se veia venir: cuando
    /// el organizador se cae, el sustrato ASCIENDE a la colaboradora. Su vigia llama a una
    /// eleccion, ella es la unica que se ve a si misma, el quorum es de uno, y se corona.
    ///
    /// Ascendida no falla nada: sus comandos dejan de reenviarse y se asientan en SU copia,
    /// tan campantes. Y esa copia se aparta cuando el organizador vuelve. Lo medimos: una
    /// venta cobrada se escribio en su diario, el organizador volvio, y la venta desaparecio
    /// sin un solo error -- la persona pago y no tenia carton. La libreta no la salvo porque
    /// nunca se entero de que no habia a quien contarle.
    ///
    /// De ahi la regla, que ademas es la del bingo: quien vende NUNCA es quien organiza. Si
    /// se encuentra mandando, es que se quedo sola.
    /// </summary>
    public bool ThereIsNobodyInCharge =>
        WorkingAlone || _replica is null || _replica.IAmTheOneInCharge;

    /// <summary>Si puede cobrar sin el organizador. Hacen falta las dos cosas: la copia del
    /// diario --que es la que dice que esos cartones son suyos-- y la libreta donde anotarlo.
    /// Sin alguna de las dos, decirle que cobre seria mentirle.</summary>
    public bool CanSellAlone => _replica is not null && _notebook is not null;

    /// <summary>Lo que cobre y el organizador todavia no sabe.</summary>
    private SalesNotebook _notebook;

    /// <summary>Cuantas ventas llevo anotadas sin poder contarle. Cero es lo normal; que
    /// suba quiere decir que el organizador esta caido y ella siguio cobrando.</summary>
    public int SalesWaitingToBeTold => _notebook?.Count ?? 0;

    /// <summary>Las que el organizador rechazo cuando por fin se las conto. Cada una es
    /// plata que ella tiene y un carton que no existe: no se van solas de aca.</summary>
    public IReadOnlyList<(string When, string Buyer, IReadOnlyList<long> Cards, string Why)>
        SalesThatBounced =>
        (_notebook?.Trouble ?? new List<SalesNotebook.Sale>())
            .Select(s => (s.When, s.Buyer, (IReadOnlyList<long>)s.Cards, s.Bounced))
            .ToList();

    public void TalkTo(Action<string> say) => _say = say ?? (_ => { });

    /// <summary>A que vino: se lo dijo el QR del organizador, no lo eligio el.</summary>
    public string MyDuty => _myDuty;

    public bool Sells => Duties.IsSeller(_myDuty);

    public bool Calls => Duties.IsCaller(_myDuty);

    public event Action Changed;

    public OpenFestival Festival => _festival;

    /// <summary>Mi numero de colaboradora en este bingo, el que le digo a una colega
    /// para que me pase cartones. Cero mientras el organizador no me lo haya dicho.</summary>
    public int MyNumber { get; private set; }

    public string MyName => _myName;

    public IReadOnlyList<long> InCustody
    {
        get { lock (_guard) return _inCustody.ToArray(); }
    }

    public IReadOnlyList<(string Who, string Mark, long Card)> Sold
    {
        get { lock (_guard) return _sold.ToArray(); }
    }

    /// <summary>La ronda como me la conto el organizador (al alistarme y en cada
    /// aviso). Es para la pantalla: quien canta por fin sabe que figuras se juegan
    /// y que da cada una.</summary>
    public int RoundPosition { get; private set; }

    public string RoundPlaying { get; private set; } = string.Empty;

    /// <summary>Lo que se juega, figura por figura. La cabecera solo necesitaba la linea
    /// de texto, pero la pantalla de cantar dibuja cada figura con su premio, y de una
    /// linea ya armada no se vuelve a las partes.</summary>
    public IReadOnlyList<(string Figure, string Prize, bool IsCash)> RoundGames
    { get; private set; } = Array.Empty<(string, string, bool)>();

    /// <summary>"AnyRow $10 · FullCard una canasta" — el premio vive en cada figura.</summary>
    public string RoundPrizeLine { get; private set; } = string.Empty;

    public bool BetweenRounds { get; private set; }

    public bool FestivalOver { get; private set; }

    /// <summary>Las bolitas que ya salieron en la ronda vigente: las que el organizador me
    /// conto al alistarme, las que canto el despues, y las mias. Es la tombola entera --
    /// con solo las mias, quien canta repetiria todo lo que canto el.</summary>
    public IReadOnlyList<int> BallsAlreadyCalled
    {
        get { lock (_guard) return _balls.ToArray(); }
    }

    public async Task<bool> PresentMyselfAsync(OpenFestival festival, string myName,
        int myPort, CancellationToken life)
    {
        bool switchedFestival = _festival is not null
            && !string.Equals(_festival.FestivalId, festival.FestivalId, StringComparison.Ordinal);

        if (switchedFestival)
            lock (_guard) { _sold.Clear(); _inCustody.Clear(); _balls.Clear(); }

        _festival = festival;
        _myName = myName;
        _myPort = myPort;
        _myDuty = Duties.IsCollaborator(festival.Duty) ? Duties.Clean(festival.Duty) : Duties.Sells;
        _life ??= CancellationTokenSource.CreateLinkedTokenSource(life);

        string myAddress = Huddle.NetworkAddress.Mine(_say);

        _say(Texts.T("colaborador.log.soy-entro-a", ("nombre", myName), ("oficio", Duties.Describe(_myDuty)), ("bingo", festival.Name)));

        if (_phone is null)
        {
            string inbox = Path.Combine(FileSystem.AppDataDirectory,
                "vendedor-" + myName.ToLowerInvariant());

            _phone = new PhoneToPhone(myName.ToLowerInvariant(), myPort, inbox,
                line => _say("    " + line));
            _phone.Listen();
        }

        // Una libreta por bingo: mezclar dos bingos en un archivo es invitar a que la venta
        // de uno se drene contra el otro. Se abre ANTES de presentarse, porque si el
        // organizador ya se cayo desde la ultima vez, lo primero que hay que saber es que
        // ella trae ventas cobradas encima.
        if (_notebook is null || switchedFestival)
        {
            _notebook = new SalesNotebook(
                Path.Combine(FileSystem.AppDataDirectory,
                    "vendedor-" + myName.ToLowerInvariant()
                    + "-libreta-" + WireNames.Safe(festival.FestivalId) + ".json"),
                _say);
        }

        JoinRequest request = new()
        {
            FestivalId = festival.FestivalId,
            Name = myName,
            ListensAt = $"http://{myAddress}:{myPort}",
            Duty = _myDuty,
            Mark = festival.Mark,
        };

        PhoneToPhone.Reply reply = await _phone.EnlistAndListenAsync(
            festival.CommunityLeaderUrl, request.ToText(), _life.Token);

        if (!reply.Ok)
        {
            // El organizador no contesta, pero si ya tengo su diario replicado de una
            // entrada anterior, no hace falta que conteste: abro el mio y sigo trabajando.
            // Es lo unico que la replica vino a dar.
            if (!IsFinal(reply.Because) && await OpenMyReplicaAloneAsync(festival))
            {
                WorkingAlone = true;

                // Una vez, no en cada reintento. Este es el otro renglon que se repetia:
                // mientras el reenganche insiste, se pasa por aca cada vuelta.
                if (!string.Equals(_noAlreadySaid, "solo", StringComparison.Ordinal))
                {
                    _noAlreadySaid = "solo";
                    _say(Texts.T("colaborador.log.sigo-con-mi-copia"));
                }

                Changed?.Invoke();

                // Y se lo espera, que hasta ahora no se hacia: entrar sola era quedarse sola
                // hasta que alguien tocara la pantalla, porque el vigia del sustrato --el que
                // avisa que se cayo-- solo existe cuando hubo corrillo, y aca no lo hubo.
                // Sin esto la libreta no se drena nunca: ella cobra, el vuelve, y nadie va.
                KnockAgainBecauseTheLeaderIsGone();

                return true;
            }

            SayTheNoOnce(reply.Because, reply.Why);

            // Si el no es definitivo, el bingo se borra de mi lista: si no, la tarjeta
            // queda ahi para siempre fallando cada vez que alguien la toca.
            if (IsFinal(reply.Because))
            {
                // Salvo que traiga plata encima. La sacaron del bingo y quedan ventas que
                // cobro y nadie asento: borrar la tarjeta seria hacerlas desaparecer de la
                // unica pantalla donde se pueden ver, y con ellas el nombre de a quien hay
                // que devolverle. Se pasan al renglon de los problemas y el bingo se queda.
                int owed = _notebook?.Count ?? 0;

                if (owed > 0)
                {
                    foreach (SalesNotebook.Sale hers in _notebook.Waiting)
                        _notebook.Bounce(hers.Id, Texts.T("colaborador.log.te-sacaron-antes"));

                            _say(Texts.T("colaborador.log.ojo-te-sacaron", ("n", owed.ToString())));

                    return false;
                }

                Forget(festival.FestivalId);
                _say(Texts.T("colaborador.log.saco-de-mi-lista"));
            }

            return false;
        }

        WorkingAlone = false;

        // Entro: el proximo "no" que venga es nuevo y hay que decirlo.
        _noAlreadySaid = null;

        ReadWhatIWasHanded(reply.Body);

        await JoinTheHuddleAsync(reply.Body);

        // El canal de vuelta: el organizador me rutea un topico propio al alistarme y por
        // ahi me manda o me quita cartones en caliente. Escucha cualquier colaborador,
        // tambien quien canta: es el unico camino que el organizador tiene para avisarle
        // algo, y un aviso de cartones que no le toca no le hace nada.
        _listener?.Dispose();

        // Por el papelito, igual que del otro lado: dos colaboradoras que se llaman igual
        // compartirian buzon, y los cartones de una le llegarian a la otra.
        _listener = _phone.Subscribe(
            CustodyNotice.TopicFor(festival.FestivalId, WireNames.Handle(festival.Mark, myName)),
            OnMoreCards);

        _roundListener?.Dispose();

        _roundListener = _phone.Subscribe(
            RoundNotice.TopicFor(festival.FestivalId), OnRound);

        // Las bolitas del organizador. Quien canta y el cantan sobre la MISMA tombola,
        // asi que tiene que ver las de el o los dos van a estar tocando numeros que el
        // otro ya saco. Se suscribe cualquier colaborador: a quien vende no le hace
        // nada, y quien vende hoy puede ser quien canta en el bingo siguiente.
        _drawListener?.Dispose();

        _drawListener = _phone.Subscribe(
            DrawNotice.TopicFor(festival.FestivalId), OnDraw);

        Changed?.Invoke();

        return true;
    }

    private void OnRound(BrokerRecord notice)
    {
        string what = RoundNotice.What(notice.Value);

        if (string.Equals(what, RoundNotice.Starting, StringComparison.Ordinal))
        {
            RoundPosition = RoundNotice.Position(notice.Value);
            RoundPlaying = RoundNotice.Playing(notice.Value) ?? string.Empty;
            RoundGames = RoundNotice.Promises(notice.Value);
            RoundPrizeLine = RoundNotice.PrizeLine(notice.Value);
            BetweenRounds = false;
            FestivalOver = false;

            // Ronda nueva, tombola nueva: las 75 vuelven a estar disponibles.
            lock (_guard) _balls.Clear();

            _say(Texts.T("organizador.log.arranca-la-ronda", ("n", RoundPosition.ToString()), ("premios", RoundPrizeLine)));
        }
        else if (string.Equals(what, RoundNotice.Settled, StringComparison.Ordinal))
        {
            BetweenRounds = true;
            _say(Texts.T("colaborador.log.ronda-cerrada-esperando"));
        }
        else if (string.Equals(what, RoundNotice.Over, StringComparison.Ordinal))
        {
            FestivalOver = true;
            _say(Texts.T("colaborador.termino"));
        }

        Changed?.Invoke();
    }

    /// <summary>Una bolita del organizador. Puede ser una que salio, o una correccion --
    /// y la correccion CAMBIA una por otra, no agrega: si la mala quedara encendida, la
    /// tombola no dejaria volver a tocarla nunca.</summary>
    private void OnDraw(BrokerRecord notice)
    {
        int number = DrawNotice.Number(notice.Value);
        int instead = DrawNotice.Instead(notice.Value);

        if (number is < 1 or > 75) return;
        if (!Register(number, instead)) return;

                _say(instead > 0
            ? Texts.T("colaborador.log.organizador-corrigio", ("malo", instead.ToString()), ("bueno", number.ToString()))
            : Texts.T("jugador.log.salio-el", ("numero", number.ToString())));

        Changed?.Invoke();
    }

    /// <summary>
    /// Deja la bolita asentada en mi lista, y dice si cambio algo.
    ///
    /// La correccion entra EN EL LUGAR de la mala y no al final, porque se puede corregir
    /// cualquiera de la ronda y no solo la ultima: sacando y agregando, corregir la
    /// tercera de doce la dejaba de ultima, y la pantalla mostraba como "ultima bolita"
    /// una que salio hace rato. El diario del organizador hace lo mismo -- reemplaza en su
    /// posicion --, asi que asi las dos listas dicen lo mismo.
    /// </summary>
    private bool Register(int number, int instead)
    {
        lock (_guard)
        {
            int at = instead > 0 ? _balls.IndexOf(instead) : -1;

            if (at >= 0)
            {
                if (_balls.Contains(number)) return false;

                _balls[at] = number;
                return true;
            }

            if (_balls.Contains(number)) return false;

            _balls.Add(number);
            return true;
        }
    }

    /// <summary>Un no del que no se vuelve con el mismo codigo. Un timeout o un 500 no
    /// cuentan: de esos si conviene reintentar mas tarde.</summary>
    private static bool IsFinal(string because) =>
        because is Refusal.SentHome or Refusal.NoMark or Refusal.MarkTaken
            or Refusal.OtherDuty or Refusal.NotYourMark or Refusal.Closed;

    /// <summary>El ultimo "no" que ya se dijo. Se guarda el CODIGO, no el texto: el mismo
    /// motivo con otro numero adentro sigue siendo el mismo motivo.</summary>
    private string _noAlreadySaid;

    /// <summary>
    /// El "no" del organizador, dicho UNA vez.
    ///
    /// El reenganche insiste cada tanto, y cada vuelta pasaba por aca: con un bingo que el
    /// organizador ya no tiene abierto, ella veia "el organizador no me dejo entrar" cada
    /// treinta segundos, toda la noche. Ese aviso, repetido, no informa nada -- asusta. Y
    /// asusta a la persona que esta atendiendo la fila.
    ///
    /// Ademas el texto ahora depende del motivo. "No me dejo entrar" suena a que la
    /// rechazaron a ella; cuando lo que pasa es que ese bingo ya no esta abierto, eso es lo
    /// que hay que decirle, con lo que tiene que hacer al respecto.
    /// </summary>
    private void SayTheNoOnce(string because, string why)
    {
        string code = because ?? Refusal.Plain;

        if (string.Equals(_noAlreadySaid, code, StringComparison.Ordinal)) return;

        _noAlreadySaid = code;

                // El "no" se dice en el idioma de ESTE telefono, desde el codigo que cruzo el cable.
        _say(Refusal.Dicho(code, why));
    }

    private void OnMoreCards(BrokerRecord notice)
    {
        IReadOnlyList<long> arrived = CustodyNotice.Read(notice.Value);
        IReadOnlyList<long> taken = CustodyNotice.ReadTaken(notice.Value);

        if (arrived.Count == 0 && taken.Count == 0)
        {
            _say(Texts.T("colaborador.log.custodia-no-entendi"));
            return;
        }

        List<long> fresh = new();
        List<long> gone = new();

        lock (_guard)
        {
            foreach (long card in arrived)
            {
                if (_inCustody.Contains(card)) continue;

                _inCustody.Add(card);
                fresh.Add(card);
            }

            foreach (long card in taken)
            {
                if (!_inCustody.Remove(card)) continue;

                gone.Add(card);
            }
        }

        if (fresh.Count == 0 && gone.Count == 0)
        {
            _say(Texts.T("colaborador.log.custodia-repetida"));
            return;
        }

        if (fresh.Count > 0)
            _say(Texts.T("colaborador.log.me-entrego-mas", ("cuales", string.Join(", ", fresh))));

        if (gone.Count > 0)
            _say(Texts.T("colaborador.log.salieron-de-mi-taco", ("cuales", string.Join(", ", gone))));

        Changed?.Invoke();
    }


    /// <summary>
    /// Se pega al corrillo si el organizador mando pase.
    ///
    /// Por ahora la replica es un ESPEJO MUDO: se mantiene al dia pero nadie la lee todavia
    /// -- todo lo que la pantalla muestra sigue viniendo por HTTP, igual que siempre. Asi el
    /// dia que falle la replicacion no se cae la velada, se cae un numero de diagnostico.
    /// </summary>
    private async Task JoinTheHuddleAsync(string body)
    {
        // Antes esto salia con 'if (_replica is not null) return;' y tapaba dos casos que en
        // una velada pasan seguido: la que vuelve a entrar a un bingo que ya tenia --su copia
        // ya estaba abierta, asi que no se pegaba nunca-- y la que se re-presenta porque se le
        // cayo el organizador.
        //
        // Y OJO con como se decide: no se le puede preguntar al sustrato si sigo pegada. Su
        // Connectivity se calcula al entrar y no se vuelve a mirar nunca, asi que contesta que
        // si para siempre y este return se comeria el reenganche entero. Lo que se mira son
        // dos marcas nuestras: si perdi al organizador, y si estoy trabajando sola.
        if (_replica is not null && !_lostTheLeader && !WorkingAlone) return;

        bool comingBack = _replica is not null;

        if (comingBack)
        {
            _say(Texts.T("colaborador.log.copia-sin-corrillo"));

            try { _replica.Dispose(); } catch { }

            _replica = null;
            WorkingAlone = false;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body ?? "{}");

            if (!doc.RootElement.TryGetProperty("pase", out JsonElement handed)
                || handed.ValueKind != JsonValueKind.String) return;

            Huddle.HuddlePass pass = Huddle.HuddlePass.FromText(handed.GetString());
            if (!string.IsNullOrWhiteSpace(pass.CommunityLeaderTicket)) _leaderTicket = pass.CommunityLeaderTicket;

            string dir = Path.Combine(FileSystem.AppDataDirectory, "replica-" + _festival.FestivalId);

            if (comingBack) dir = StartAFreshCopy(dir);

            ForgetIWasEverInCharge(dir);

            _replica = await CommunityLeader.FestivalJournal.JoinAsCastAsync(
                "funcion-" + _festival.FestivalId, dir, pass, _say, _life.Token,
                typeof(Village.Elder).Assembly, typeof(BingoEngine.BingoGame).Assembly);

            _replica.WhenTheLeaderIsGone(KnockAgainBecauseTheLeaderIsGone);

            // Ya estoy adentro otra vez: el bucle que lo estaba esperando se puede ir.
            _lostTheLeader = false;

            long entry = await _replica.SettleAsync(
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), _life.Token);

            if (entry > 0 && ReadFromMyReplica())
            {
                _say(Texts.T("colaborador.log.replica-en-entrada", ("entrada", entry.ToString())));
                Changed?.Invoke();
            }
            else
            {
                _say(Texts.T("colaborador.log.pegue-sin-diario"));
            }

            // Lo primero que se hace al volver al corrillo es contarle lo que se cobro
            // mientras no estaba. Va en su propio hilo: son tantos viajes al Director como
            // ventas haya, y esto corre en el camino que le devuelve la pantalla.
            if (_notebook is not null && _notebook.Count > 0)
                _ = Task.Run(ReadTheNotebookToTheLeader);

            WatchMyReplica();
        }
        catch (Exception failure)
        {
            _say(Texts.T("colaborador.log.no-me-pude-pegar", ("motivo", Texts.Explain(failure))));
        }
    }


    /// <summary>
    /// Lee de MI replica lo que hasta ahora le preguntaba al organizador.
    ///
    /// Son los MISMOS guiones que el corre sobre su diario -- es el mismo diario, byte por
    /// byte. La diferencia es que esto corre local: sirve con el telefono del organizador
    /// apagado, que es lo unico que esto vino a dar.
    ///
    /// Si la replica todavia no esta, devuelve false y quien llama se queda con lo que traia
    /// por HTTP. Asi el dia que falle la replicacion se degrada, no se cae.
    /// </summary>
    /// <summary>Donde vive mi copia del diario de este bingo.</summary>
    private static string ReplicaDir(string festivalId) =>
        Path.Combine(FileSystem.AppDataDirectory, "replica-" + festivalId);

    /// <summary>Si ya tengo copia de este bingo, aunque el organizador este apagado.</summary>
    public static bool HasReplica(string festivalId) =>
        !string.IsNullOrWhiteSpace(festivalId) && Directory.Exists(ReplicaDir(festivalId));

    /// <summary>
    /// Abre mi copia del diario SIN corrillo: el organizador no esta, asi que no hay a quien
    /// pegarse. Sirve para leer lo mio -- mis cartones, mis ventas -- que es lo que tengo que
    /// poder mirar aunque a el se le haya muerto el telefono.
    ///
    /// Lo que NO se puede es escribir: sin Director no hay a quien reenviarle un comando. La
    /// venta sigue necesitandolo, y eso es correcto: el es quien decide.
    /// </summary>
    private async Task<bool> OpenMyReplicaAloneAsync(OpenFestival festival)
    {
        if (!HasReplica(festival.FestivalId)) return false;

        try
        {
            _replica ??= await CommunityLeader.FestivalJournal.OpenMineAsync(
                "funcion-" + festival.FestivalId, ReplicaDir(festival.FestivalId), _say, _life.Token,
                typeof(Village.Elder).Assembly, typeof(BingoEngine.BingoGame).Assembly);

            return ReadFromMyReplica();
        }
        catch (Exception failure)
        {
            _say(Texts.T("colaborador.log.no-pude-abrir-copia", ("motivo", Texts.Explain(failure))));
            return false;
        }
    }

    /// <summary>
    /// Devolverle cartones al organizador POR EL CORRILLO, sin pasar por su puerta.
    ///
    /// La guarda va en el Check y no adentro del comando, y esa es toda la gracia: el check
    /// corre EN EL DIRECTOR, contra SU estado, antes de tomar el candado de escritura. Si
    /// dice que no, no se asienta nada y el diario no se entera. Escrita adentro del comando
    /// seria letra muerta -- el comando corre igual --, y escrita en C# de este lado correria
    /// contra mi propia copia, que es pedirle a quien manda que se autorice sola.
    ///
    /// Y como el cuerpo del check NUNCA se journalea, no se vuelve a ejecutar al rehidratar:
    /// una guarda no puede dejar un diario que despues no abre.
    ///
    /// Devuelve null si salio bien, o el motivo si el Director dijo que no.
    /// </summary>
    private string GiveBackThroughTheHuddle(IReadOnlyList<long> cards)
    {
        string answer;

        try
        {
            answer = _replica
                .Using(
                    "Check(festival.ErrandLetsThemDo(@marca, @quien, @oficio)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.papelito-no-vigente") + "';"
                    + "Check(festival.MerchantCanGiveBack(@marca, @cartones)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.cartones-no-en-mano") + "';",
                    "festival.MerchantMarked(@marca).GiveBack(festival, @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", _festival.Mark);
                    p.UserParameter<string>("quien", _myName);
                    p.UserParameter<string>("oficio", Duties.AsChore(_myDuty));
                    p.UserParameter<List<long>>("cartones", new List<long>(cards));
                })
                .PerformCheckThenCommand();
        }
        catch (Exception reventado)
        {
            _say(Texts.T("colaborador.log.devolucion-revento", ("error", reventado.GetType().Name + ": " + reventado.Message)));
            return reventado.Message;
        }

        _say(Texts.T("colaborador.log.devolucion-contesto", ("respuesta", answer ?? "<null>")));

        // El contrato del sustrato: texto de vuelta es RECHAZO, vacio es que entro.
        return string.IsNullOrWhiteSpace(answer) ? null : WhyTheySaidNo(answer);
    }

    /// <summary>
    /// El motivo del "no", en castellano y no en JSON.
    ///
    /// Un Check rechazado vuelve envuelto -- {"EWI":[{"Error":"..."}]} -- y eso es lo que el
    /// sustrato le da a quien llama. Mostrarselo asi a una persona que esta atendiendo una
    /// fila no es mostrarle nada. Si no se puede desenvolver, se devuelve tal cual: mejor
    /// crudo que en blanco.
    /// </summary>
    private static string WhyTheySaidNo(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

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

    /// <summary>
    /// La marca del papelito de la colaboradora numero <paramref name="number"/>, leida de mi
    /// replica.
    ///
    /// El numero que la gente se dice en voz alta ("pasaselos a la 2") es el PUESTO del
    /// papelito en el diario, asi que la marca se saca de ahi. Se resuelve de este lado
    /// porque es una lectura, y si mi copia esta atrasada y me da la marca equivocada no
    /// pasa nada: la marca viaja como parametro y el Check la valida en el Director, que es
    /// quien sabe la verdad. Devuelve null si todavia no la conozco.
    /// </summary>
    private string MarkOfCollaborator(int number)
    {
        if (_replica is null || number <= 0) return null;

        try
        {
            string mark = Pick(
                _replica.Using("print festival.ErrandAt(@posicion).Mark m;")
                    .WithParameters(p => p.UserParameter<int>("posicion", number))
                    .PerformQuery(), "m");

            return string.IsNullOrWhiteSpace(mark) ? null : mark;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// El traspaso por el corrillo. Mismo reparto que la devolucion: el papelito lo valida
    /// ErrandLetsThemDo y la tenencia MerchantCanPassTo, las dos EN EL DIRECTOR y antes del
    /// candado de escritura, asi un "no" no deja asiento.
    ///
    /// Devuelve null si salio bien, o el motivo si el Director dijo que no.
    /// </summary>
    private string PassThroughTheHuddle(string toMark, IReadOnlyList<long> cards)
    {
        string answer;

        try
        {
            answer = _replica
                .Using(
                    "Check(festival.ErrandLetsThemDo(@marca, @quien, @oficio)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.papelito-no-vigente") + "';"
                    + "Check(festival.MerchantCanPassTo(@marca, @marcaDeElla, @cartones)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.no-se-pueden-pasar") + "';",
                    "festival.MerchantMarked(@marca).PassTo("
                    + "festival, festival.MerchantMarked(@marcaDeElla), @cartones);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", _festival.Mark);
                    p.UserParameter<string>("marcaDeElla", toMark);
                    p.UserParameter<string>("quien", _myName);
                    p.UserParameter<string>("oficio", Duties.AsChore(_myDuty));
                    p.UserParameter<List<long>>("cartones", new List<long>(cards));
                })
                .PerformCheckThenCommand();
        }
        catch (Exception reventado)
        {
            _say(Texts.T("colaborador.log.traspaso-revento", ("error", reventado.GetType().Name + ": " + reventado.Message)));
            return reventado.Message;
        }

        return string.IsNullOrWhiteSpace(answer) ? null : WhyTheySaidNo(answer);
    }

    /// <summary>
    /// El planteo de anulacion por el corrillo. Ella no deshace nada: deja el pedido escrito
    /// y el organizador lo resuelve despues.
    ///
    /// Van tres guardas, y la de la ronda no es de adorno: a media ronda los cartones no se
    /// mueven, y esa es la misma condicion que el organizador ya exige cuando el pedido le
    /// entra por la puerta. 'mesa' es el juego y 'festival' es el pueblo; los dos viven en
    /// el mismo actor, asi que un solo Check puede mirar a los dos.
    ///
    /// Devuelve null si salio bien, o el motivo si el Director dijo que no.
    /// </summary>
    private string PleadThroughTheHuddle(long card)
    {
        string answer;

        try
        {
            answer = _replica
                .Using(
                    "Check(festival.ErrandLetsThemDo(@marca, @quien, @oficio)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.papelito-no-vigente") + "';"
                    + "Check(mesa.DrawnCount == 0) "
                    + "Error '" + Texts.ForDsl("organizador.no.ronda-empezada") + "';"
                    + "Check(festival.CanPleadToUndo(@numero)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.carton-no-vendido-o-pedido") + "';",
                    "seller = festival.MerchantMarked(@marca); card = festival.ParticipationNumbered(@numero); seller.Plead(festival, card);")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", _festival.Mark);
                    p.UserParameter<string>("quien", _myName);
                    p.UserParameter<string>("oficio", Duties.AsChore(_myDuty));
                    p.UserParameter<int>("numero", (int)card);
                })
                .PerformCheckThenCommand();
        }
        catch (Exception reventado)
        {
            _say(Texts.T("colaborador.log.pedido-revento", ("error", reventado.GetType().Name + ": " + reventado.Message)));
            return reventado.Message;
        }

        return string.IsNullOrWhiteSpace(answer) ? null : WhyTheySaidNo(answer);
    }

    /// <summary>
    /// La venta por el corrillo. Cuarto verbo por este camino, mismo reparto que los otros
    /// tres: el papelito lo valida ErrandLetsThemDo y la tenencia MerchantCanSellThem, las
    /// dos EN EL DIRECTOR y antes del candado de escritura.
    ///
    /// Aca la guarda pesa mas que en los otros: si el Director dice que no, ella YA COBRO.
    /// Que el "no" llegue sin asiento es lo unico que permite decirselo a la cara en vez de
    /// descubrirlo despues cuadrando la plata.
    ///
    /// El comando es el mismo que arma el organizador cuando la venta le entra por la
    /// puerta -- comprador, venta, y los cartones a la mesa --, porque es el mismo hecho:
    /// lo unico que cambia es por donde entro.
    ///
    /// Devuelve null si salio bien, o el motivo si el Director dijo que no.
    /// </summary>
    private (Answer How, string Why) SellThroughTheHuddle(
        string buyer, string contact, IReadOnlyList<long> cards, string buyerMark)
    {
        // La misma guarda otra vez, pegada al comando. Aca no sobra: entre que quien llama
        // pregunto y este comando sale pueden pasar los quince segundos que el sustrato
        // espera antes de coronarla, y escribir despues de eso es escribir en el aire.
        if (ThereIsNobodyInCharge) return (Answer.NotThere, null);

        string answer;

        try
        {
            answer = _replica
                .Using(
                    "Check(festival.ErrandLetsThemDo(@marca, @quien, @oficio)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.papelito-no-vigente") + "';"
                    + "Check(festival.MerchantCanSellThem(@marca, @cartones)) "
                    + "Error '" + Texts.ForDsl("colaborador.no.cartones-no-en-mano") + "';",
                    "{"
                    + " comprador = Villager(@nombre, @contacto, @marcaComprador);"
                    + " festival.MerchantMarked(@marca).SellThemTo(festival, @cartones, comprador);"
                    + " foreach (numero in @cartones) { mesa.PutCardInPlay(numero); }"
                    + " }")
                .WithParameters(p =>
                {
                    p.UserParameter<string>("marca", _festival.Mark);
                    p.UserParameter<string>("quien", _myName);
                    p.UserParameter<string>("oficio", Duties.AsChore(_myDuty));
                    p.UserParameter<string>("nombre", buyer);
                    p.UserParameter<string>("contacto",
                        string.IsNullOrWhiteSpace(contact) ? Texts.T("organizador.contacto.en-el-bingo") : contact.Trim());
                    p.UserParameter<string>("marcaComprador", buyerMark);
                    p.UserParameter<List<long>>("cartones", new List<long>(cards));
                })
                .PerformCheckThenCommand();
        }
        catch (Exception reventado)
        {
            return HowThatWent(reventado, Texts.T("colaborador.que.la-venta"));
        }

        return string.IsNullOrWhiteSpace(answer)
            ? (Answer.Entered, null)
            : (Answer.SaidNo, WhyTheySaidNo(answer));
    }

    /// <summary>
    /// Las tres cosas que le pueden pasar a un comando reenviado, y por que hay que
    /// distinguirlas.
    ///
    /// La diferencia que importa es entre "el organizador dijo que no" y "el organizador no
    /// esta". Las dos se veian igual --un texto de error-- y por eso la venta con el padre
    /// caido terminaba en "NO cobres", que es tratar una caida como un rechazo. Con la
    /// primera hay que devolverle la plata a la persona; con la segunda hay que cobrarle y
    /// anotarlo.
    /// </summary>
    private enum Answer
    {
        /// <summary>Entro al diario.</summary>
        Entered,

        /// <summary>El Director la miro y la rechazo. NO se asento nada.</summary>
        SaidNo,

        /// <summary>No hubo a quien preguntarle, o no contesto.</summary>
        NotThere,
    }

    /// <summary>
    /// Leer una excepcion del sustrato para saber cual de las tres cosas paso.
    ///
    /// La clasificacion es la de Puppeteer (los 4 modos de falla de ForwardToDirector) y se
    /// discrimina por el texto del mensaje, porque el sustrato no tiene
    /// excepciones tipadas para esto. Es fragil a proposito y esta en un solo lugar.
    ///
    /// El timeout --que se le mando y no contesto-- cae en NotThere aunque en rigor sea
    /// dudoso: el comando PUDO haberse aplicado. Podemos permitirnoslo porque la venta se
    /// vuelve a ofrecer con su Check, y una venta que ya entro deja los cartones fuera de su
    /// mano, asi que el reintento REBOTA en vez de duplicarse. El Check es nuestra de-dup.
    /// </summary>
    private (Answer How, string Why) HowThatWent(Exception reventado, string what)
    {
        bool notThere = reventado switch
        {
            OperationCanceledException => true,
            ObjectDisposedException => true,
            System.Net.Sockets.SocketException => true,
            IOException => true,
            InvalidOperationException bad => ItIsTheChannelTalking(bad.Message),
            _ => false,
        };

                _say(Texts.T("colaborador.log.como-le-fue", ("que", what), ("suerte", Texts.T(notThere ? "colaborador.log.no-encontro-al-organizador" : "colaborador.log.no-la-aceptaron")), ("error", reventado.GetType().Name + ": " + reventado.Message)));

        return (notThere ? Answer.NotThere : Answer.SaidNo, reventado.Message);
    }

    /// <summary>Los tres comienzos con los que el sustrato avisa que no hay canal. Si Puppeteer
    /// les cambia el texto, esto deja de reconocerlos y una caida vuelve a leerse como
    /// rechazo -- que es exactamente el error que vinimos a sacar.</summary>
    private static bool ItIsTheChannelTalking(string message) =>
        !string.IsNullOrEmpty(message)
        && (message.StartsWith("Stage is ", StringComparison.Ordinal)
            || message.StartsWith("No Director", StringComparison.Ordinal)
            || message.StartsWith("Command channel", StringComparison.Ordinal));

    /// <summary>
    /// El codigo del comprador, armado por ella.
    ///
    /// Todo lo que lleva adentro ya lo tiene: el nombre del bingo, su id, la direccion y las
    /// figuras vienen en el QR que ella escaneo, el carnet lo acuña ella, y los cartones los
    /// acaba de vender. Lo unico que no puede poner es el sello del organizador, y el sello
    /// nunca fue lo que protegia: el payload termina en el primer '&', asi que recortarle la
    /// cola a un codigo firmado da uno sin firmar que el jugador acepta sin chistar. Quien
    /// decide de verdad es el diario en la puerta, que coteja carton por carton contra a
    /// quien figuran vendidos.
    /// </summary>
    private string CodeForTheBuyer(string buyer, string buyerMark, IReadOnlyList<long> justSold)
    {
        List<long> hers = new(CardsUnderTheMark(buyerMark));

        // Lo del diario NO alcanza cuando el organizador esta caido: sus compras anteriores
        // de esta misma noche tampoco entraron todavia. Si el codigo solo llevara lo ultimo,
        // en la puerta le reconocerian un carton y le negarian los otros tres.
        foreach (long card in _notebook?.CardsUnder(buyerMark) ?? new List<long>())
            if (!hers.Contains(card)) hers.Add(card);

        foreach (long card in justSold)
            if (!hers.Contains(card)) hers.Add(card);

        hers.Sort();

        var withOwner = new
        {
            v = _festival.Version,
            n = _festival.Name,
            r = _festival.FestivalId,
            e = _festival.CommunityLeaderUrl,
            i = _leaderTicket ?? _festival.IrohTicket,
            f = _festival.Figures,
            k = Duties.Plays,
            yo = buyer,
            m = buyerMark,
            c = hers.ToArray(),
        };

        return OpenFestival.Scheme + Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(withOwner)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// El canto por el corrillo. Quinto y ultimo verbo por este camino.
    ///
    /// La ronda viaja con la bolita porque el sustrato identifica un tell por su contenido, y
    /// "salio el 7" dice lo mismo en la ronda 1 que en la ronda 2: sin la ronda, el segundo 7
    /// se toma por repeticion del primero y no se reparte.
    ///
    /// Del lado del organizador esa ronda va como parametro Eval --el actor la resuelve al
    /// cargar los argumentos-- y ahi esta bien. Reenviada NO SE PUEDE: un Eval viaja sin
    /// valor, y un valor nulo en un slot de tipo valor es un renglon que el diario escribe y
    /// el lector no puede releer. Lo medimos: en vivo anda y al reabrir el actor el asiento
    /// DESAPARECE, con una sola linea en el registro. Declararlo nullable no lo arregla,
    /// apaga la alarma.
    ///
    /// Asi que la ronda se lee de MI copia y viaja como valor. Lo que el Eval daba --que la
    /// verdad la diga el actor-- lo da el Check: si mi copia estaba atrasada y mando una
    /// ronda vieja, el Director la rechaza SIN DEJAR ASIENTO. Es mejor que antes, no un
    /// parche: aquel calculaba la verdad, este rechaza la mentira y no deja rastro.
    ///
    /// Y el cuerpo queda con el MISMO texto que el canto del organizador, que es lo que
    /// mantiene un solo 'define action' y la reaccion de la bolita con su checkpoint intacto.
    /// Si esto se separara, las bolitas dejarian de repartirse sin un solo error.
    ///
    /// Devuelve null si salio bien, o el motivo si el Director dijo que no.
    /// </summary>
    private string SingThroughTheHuddle(int number, int instead)
    {
        if (ThereIsNobodyInCharge)
            return Texts.T("colaborador.no.bolita-no-se-guarda");

        int round = TheRoundMyCopySays();

        if (round <= 0) return Texts.T("colaborador.no.no-se-que-ronda");

        string answer;

        try
        {
            answer = instead > 0
                ? _replica
                    .Using(
                        "Check(festival.ErrandLetsThemDo(@marca, @quien, @oficio)) "
                        + "Error '" + Texts.ForDsl("colaborador.no.papelito-no-vigente") + "';"
                        + "Check(mesa.RoundNumber == @ronda) "
                        + "Error '" + Texts.ForDsl("colaborador.no.ronda-cambio-corregias") + "';"
                        + "Check(mesa.CanCorrectDraw(@malo, @bueno)) "
                        + "Error '" + Texts.ForDsl("colaborador.no.correccion-ya-no") + "';",
                        "mesa.CorrectDraw(@malo, @bueno, @ronda);")
                    .WithParameters(p =>
                    {
                        p.UserParameter<string>("marca", _festival.Mark);
                        p.UserParameter<string>("quien", _myName);
                        p.UserParameter<string>("oficio", Duties.AsChore(_myDuty));
                        p.UserParameter<int>("malo", instead);
                        p.UserParameter<int>("bueno", number);
                        p.UserParameter<int>("ronda", round);
                    })
                    .PerformCheckThenCommand()
                : _replica
                    .Using(
                        "Check(festival.ErrandLetsThemDo(@marca, @quien, @oficio)) "
                        + "Error '" + Texts.ForDsl("colaborador.no.papelito-no-vigente") + "';"
                        + "Check(!mesa.IsAwaitingValidation) "
                        + "Error '" + Texts.ForDsl("colaborador.no.mesa-frenada") + "';"
                        + "Check(mesa.IsRunning) "
                        + "Error '" + Texts.ForDsl("colaborador.no.sin-ronda-sonando") + "';"
                        + "Check(mesa.RoundNumber == @ronda) "
                        + "Error '" + Texts.ForDsl("colaborador.no.ronda-cambio-cantabas") + "';"
                        + "Check(mesa.CanRegisterDraw(@numero)) "
                        + "Error '" + Texts.ForDsl("colaborador.no.bolita-ya-salio") + "';",
                        "mesa.RegisterDraw(@numero, @ronda);")
                    .WithParameters(p =>
                    {
                        p.UserParameter<string>("marca", _festival.Mark);
                        p.UserParameter<string>("quien", _myName);
                        p.UserParameter<string>("oficio", Duties.AsChore(_myDuty));
                        p.UserParameter<int>("numero", number);
                        p.UserParameter<int>("ronda", round);
                    })
                    .PerformCheckThenCommand();
        }
        catch (Exception reventado)
        {
            (Answer how, string why) = HowThatWent(reventado, Texts.T("colaborador.que.el-canto"));

            return how == Answer.NotThere
                                ? Texts.T("colaborador.no.bolita-no-se-guarda")
                : why;
        }

        return string.IsNullOrWhiteSpace(answer) ? null : WhyTheySaidNo(answer);
    }

    /// <summary>La ronda que va, segun mi copia. Cero si todavia no la se -- y ahi es mejor
    /// no cantar que cantar contra una ronda adivinada.</summary>
    private int TheRoundMyCopySays()
    {
        if (_replica is null) return 0;

        try
        {
            return AsInt(_replica.PerformQry("print mesa.RoundNumber n;"), "n");
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Anotarla en la libreta. El carnet que se guarda es el que la persona se
    /// llevo escrito: acuñar otro al drenar le daria un numero distinto del de su papel.</summary>
    private void WriteItInTheNotebook(SaleNotice sale, string buyerMark)
    {
        if (_notebook is null)
        {
            _say(Texts.T("colaborador.log.sin-libreta"));
                        throw new InvalidOperationException(
                Texts.T("colaborador.no.no-puedo-anotar"));
        }

        DateTime now = DateTime.Now;

        _notebook.Write(new SalesNotebook.Sale
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Buyer = sale.Buyer,
            Contact = sale.Contact ?? string.Empty,
            Cards = new List<long>(sale.Cards),
            BuyerMark = buyerMark,
            When = now.ToString("HH:mm"),
        });

                _say(Texts.T("colaborador.log.cobra-igual-anotada", ("hora", now.ToString("HH:mm")), ("n", _notebook.Count.ToString())));
    }

    /// <summary>
    /// Leerle la libreta al organizador, ahora que volvio.
    ///
    /// Una por una y en el orden en que se cobraron, cada una por el mismo camino de
    /// siempre. No es un camino nuevo: es la misma venta por corrillo, disparada mas tarde.
    ///
    /// Si una rebota hay que mirar DOS VECES antes de asustar a nadie, porque el rechazo mas
    /// probable no es que este mal: es que ya haya entrado. Eso pasa cuando el comando llego
    /// pero el acuse se perdio --el sustrato no de-duplica por comando--, y el sintoma es
    /// exactamente el mismo que el de una venta invalida. La diferencia se ve en el diario:
    /// si esos cartones figuran vendidos A ESE CARNET, la venta esta hecha y solo hay que
    /// tacharla.
    ///
    /// Y si se cayo de nuevo a mitad de la lectura, se para y se sigue en el proximo
    /// reenganche: lo que queda anotado sigue anotado.
    /// </summary>
    private void ReadTheNotebookToTheLeader()
    {
        if (_notebook is null || _notebook.Count == 0) return;
        if (ThereIsNobodyInCharge) return;

        IReadOnlyList<SalesNotebook.Sale> pending = _notebook.Waiting;

        _say(Texts.T("colaborador.log.organizador-volvio", ("n", pending.Count.ToString())));

        int told = 0;

        foreach (SalesNotebook.Sale sale in pending)
        {
            (Answer how, string why) =
                SellThroughTheHuddle(sale.Buyer, sale.Contact, sale.Cards, sale.BuyerMark);

            if (how == Answer.NotThere)
            {
                _say(Texts.T("colaborador.log.se-volvio-a-caer", ("n", _notebook.Count.ToString())));
                break;
            }

            if (how == Answer.SaidNo && !ItWasAlreadyIn(sale))
            {
                // El "no" que llega tarde. Ella ya cobro, asi que un rechazo callado aca es
                // plata perdida sin que nadie se entere. No se tacha: se pasa al renglon de
                // los problemas y se queda ahi hasta que una persona lo resuelva con otra.
                                _say(Texts.T("colaborador.log.venta-no-entro", ("hora", sale.When), ("comprador", sale.Buyer), ("cuales", string.Join(", ", sale.Cards)), ("motivo", why)));
                _notebook.Bounce(sale.Id, why);
                continue;
            }

            _notebook.CrossOut(sale.Id);
            told++;
        }

        if (told > 0) _say(Texts.T("colaborador.log.le-conte", ("n", told.ToString())));

        ReadFromMyReplica();
        Changed?.Invoke();
    }

    /// <summary>Si esos cartones ya figuran vendidos a ESE carnet, la venta esta hecha:
    /// el comando llego y lo que se perdio fue el acuse. Tacharla en silencio es lo
    /// correcto -- gritarle a la persona por una venta que si entro es peor que callarse.</summary>
    private bool ItWasAlreadyIn(SalesNotebook.Sale sale)
    {
        List<long> theirs = CardsUnderTheMark(sale.BuyerMark);

        foreach (long card in sale.Cards)
            if (!theirs.Contains(card)) return false;

        _say(Texts.T("colaborador.log.venta-ya-asentada", ("hora", sale.When), ("comprador", sale.Buyer)));

        return true;
    }

    /// <summary>Lo que ese carnet ya tiene comprado, segun mi copia. Si mi copia todavia no
    /// alcanzo la venta que acabo de hacer no importa: esa la agrega quien llama, que la
    /// tiene en la mano.</summary>
    private List<long> CardsUnderTheMark(string buyerMark)
    {
        if (_replica is null || string.IsNullOrWhiteSpace(buyerMark)) return new List<long>();

        try
        {
            return NumbersIn(_replica
                .Using("foreach (n in festival.NumbersBoughtBy(@marcaComprador)) { print n 'c'; }")
                .WithParameters(p => p.UserParameter<string>("marcaComprador", buyerMark))
                .PerformQuery());
        }
        catch (Exception)
        {
            return new List<long>();
        }
    }

    /// <summary>Cada cuanto miro si mi copia del diario crecio.</summary>
    private static readonly TimeSpan ReplicaBeat = TimeSpan.FromSeconds(2);

    private Thread _replicaWatch;

    /// <summary>
    /// Releer lo mio cuando mi copia del diario crece.
    ///
    /// Lo que hago yo ya me deja la pantalla al dia, y lo que me manda el organizador me
    /// llega por mi topico. Lo que no tenia aviso es lo que ESCRIBE OTRO en el corrillo: si
    /// una colega me pasa cartones con un comando reenviado, eso entra al diario, se replica
    /// a mi copia, y mi pantalla no se entera -- yo sigo mostrando los que tenia. Le paso lo
    /// mismo que le pasaba al organizador, una capa mas afuera.
    ///
    /// Es una lectura de mi propia copia, sin red: el que decide sigue siendo el Director.
    /// </summary>
    private void WatchMyReplica()
    {
        if (_replicaWatch is not null) return;

        _replicaWatch = new Thread(() =>
        {
            long seen = -1;

            while (_life is not null && !_life.IsCancellationRequested)
            {
                try
                {
                    long now = _replica?.CurrentEntryId ?? -1;

                    // La primera vuelta solo toma la marca: al pegarme ya lei todo lo mio.
                    if (seen >= 0 && now != seen && ReadFromMyReplica()) Changed?.Invoke();

                    seen = now;
                }
                catch
                {
                    // La copia se esta cerrando o todavia no esta. Se vuelve a mirar.
                }

                try { Thread.Sleep(ReplicaBeat); } catch { return; }
            }
        })
        {
            IsBackground = true,
            Name = "mi-replica",
        };

        _replicaWatch.Start();
    }

    /// <summary>
    /// Aparta la copia vieja y devuelve donde va la nueva.
    ///
    /// Volver a pegarse SOBRE la copia vieja no alcanza, y por dos motivos que no se ven.
    ///
    /// Uno: cuando se cae el organizador, el sustrato le arranca una eleccion a este Cast, y
    /// con un solo peer el quorum es UN voto -- se corona solo y deja escrito un termino mas
    /// alto en su term.bin. El organizador vuelve con el termino viejo, porque
    /// PromoteToDirector(force:true) a proposito no lo sube, asi que su anuncio le llega como
    /// "viejo, lo ignoro" y NUNCA vuelve a ser Director de esa copia. No hay como bajar un
    /// termino desde afuera; lo unico que queda es una copia nueva.
    ///
    /// Dos: el organizador manda el diario entero desde cero al aceptar un Cast, asi que una
    /// copia que ya tiene asientos los recibe repetidos y los descarta por colision. Vacia,
    /// ese "desde cero" es exactamente lo que corresponde.
    ///
    /// No se borra: se renombra. Si el reenganche sale mal quiero poder mirar que habia.
    /// </summary>
    /// <summary>
    /// Borrarle el mandato antes de pegarse.
    ///
    /// El sustrato le guarda en disco --_stage/term.bin-- el "mandato" que gano cuando lo
    /// ascendieron. Ese numero SOBREVIVE al cierre de la app. Y el organizador siempre se
    /// anuncia con mandato CERO, porque se corona a la fuerza y a proposito no lo sube.
    ///
    /// Resultado, y esto es lo que se veia en el emulador: bastaba que al organizador se le
    /// reiniciara la app quince segundos UNA vez -- lo suficiente para que a ella la
    /// ascendieran -- para que su telefono quedara con mandato 1 para siempre. Desde ahi, cada
    /// vez que ella vuelve a entrar a ese bingo, el sustrato descarta el anuncio del
    /// organizador por viejo ("Stale DirectorAnnounce ... announceTerm=0 &lt; myTerm=1") y ella
    /// se queda SIN CORRILLO, en silencio: el diario no le llega, la pantalla le muestra cero
    /// cartones, y borrar la carpeta de la replica era lo unico que lo arreglaba.
    ///
    /// Aca no hay nada que preservar: quien vende no manda nunca, asi que un mandato suyo no
    /// significa nada y lo unico que hace es tapar al organizador. Se borra ANTES de abrir el
    /// diario, que es el unico momento en que se puede tocar ese archivo.
    /// </summary>
    private void ForgetIWasEverInCharge(string dir)
    {
        try
        {
            string mandate = Path.Combine(dir, "_stage", "term.bin");

            if (!File.Exists(mandate)) return;

            File.Delete(mandate);
            _say(Texts.T("colaborador.log.borre-mandato"));
        }
        catch (Exception failure)
        {
            _say(Texts.T("colaborador.log.no-pude-borrar-mandato", ("motivo", Texts.Explain(failure))));
        }
    }

    private string StartAFreshCopy(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                string apartada = dir + ".vieja";

                if (Directory.Exists(apartada)) Directory.Delete(apartada, recursive: true);

                Directory.Move(dir, apartada);
                _say(Texts.T("colaborador.log.aparte-copia-vieja"));
            }
        }
        catch (Exception failure)
        {
            // Si no se puede apartar, se sigue con la que hay: pegarse con una copia sucia es
            // peor que no pegarse, pero no pegarse tampoco sirve. Que lo diga y que se vea.
            _say(Texts.T("colaborador.log.no-pude-apartar-copia", ("motivo", Texts.Explain(failure))));
        }

        return dir;
    }

    private volatile bool _knocking;

    /// <summary>
    /// Se me cayo el organizador y todavia no volvi a entrar.
    ///
    /// Hace falta una marca propia porque NADA de lo que ya teniamos cambia cuando el Director
    /// se muere: sigo con mi replica en la mano y sigo sin estar "trabajando sola", asi que
    /// mirando esas dos cosas parece que estuviera todo bien. La primera version del bucle se
    /// colgaba de ahi y salia sin golpear una sola vez.
    ///
    /// La prende el aviso del sustrato y la apaga volver a pegarse, y nadie mas la toca.
    /// </summary>
    private volatile bool _lostTheLeader;

    /// <summary>
    /// Volver a presentarme cuando se cayo el organizador.
    ///
    /// Al organizador se le puede reiniciar la app en plena velada -- bateria, Android que la
    /// cierra, lo que sea -- y cuando vuelve NO re-invita a nadie: vuelve sin peers, y mi copia
    /// se queda congelada sin que se note, porque todo lo demas sigue andando por la puerta.
    ///
    /// El sustrato no lo rearma solo y es a proposito: con PortableHttps el reenganche ES el
    /// apareamiento normal, y ese lo maneja el anfitrion. La direccion que sobrevive es la de
    /// EL --viene en el QR y ya es el ancla de todo el sistema--, asi que la que golpea soy yo.
    /// Presentarme de nuevo me devuelve un pase nuevo, con invitaciones frescas atadas a la IP
    /// que el tenga ahora.
    ///
    /// Esto lo llama el vigia del sustrato, sincronico y antes de que arranque la eleccion, asi
    /// que aca no se hace NADA caro: se levanta la mano y se sigue por otro hilo.
    /// </summary>
    private void KnockAgainBecauseTheLeaderIsGone()
    {
        if (_knocking) return;
        if (_festival is null || _myPort <= 0) return;

        _lostTheLeader = true;
        _knocking = true;

        // Que la pantalla se entere AHORA. El retrato que ella esta mirando se armo cuando el
        // organizador todavia estaba, y perderlo no mueve el diario -- que es lo unico que
        // dispara una repintada. Sin esto, ella confirmaba una venta creyendo que le llegaba
        // y el aviso de "cobra igual, queda anotada" no aparecia nunca: el dato estaba bien
        // del lado del codigo y la pantalla mostraba una foto vieja.
        Changed?.Invoke();

        _ = Task.Run(async () =>
        {
            try
            {
                _say(Texts.T("colaborador.log.se-cayo-el-organizador"));

                await KeepKnockingUntilImBackAsync();
            }
            catch (Exception failure)
            {
                _say(Texts.T("colaborador.log.deje-de-esperar", ("motivo", Texts.Explain(failure))));
            }
            finally
            {
                _knocking = false;
            }
        });
    }

    /// <summary>Cuanto se espera entre golpe y golpe. Arranca corto porque la mayoria de los
    /// reinicios duran poco, y se estira para no gastar bateria toda la noche si el telefono
    /// del organizador no vuelve nunca.</summary>
    private static readonly int[] WaitsInSeconds = { 2, 4, 8, 15, 30 };

    /// <summary>
    /// Insistir hasta volver a estar adentro.
    ///
    /// Un solo intento no alcanza y lo medimos: cuando el organizador reinicia, ella se entera
    /// a los 15 segundos, golpea, y el todavia esta arrancando -- una app MAUI tarda mas que
    /// eso en levantar. Ese unico golpe fallaba, ella caia a trabajar con su copia sola, y ahi
    /// se quedaba PARA SIEMPRE aunque el volviera un minuto despues: el aviso del sustrato
    /// suena una sola vez.
    ///
    /// Primero se saluda, que es barato, y recien cuando contesta se hace el alistarse
    /// completo. Al reves seria mandar el tramite entero contra una puerta cerrada.
    /// </summary>
    /// <summary>Cuantas veces se le pide entrar a un organizador que SI contesta y SI dice
    /// que no. Que no contesta se le espera toda la noche -- el telefono se esta cargando y
    /// vuelve --, pero un "no" que ya llego cinco veces no va a cambiar en la sexta, y seguir
    /// pidiendo solo gasta bateria y le llena la pantalla de avisos.</summary>
    private const int TimesToTakeANo = 5;

    private async Task KeepKnockingUntilImBackAsync()
    {
        int attempt = 0;
        int nos = 0;

        while (_lostTheLeader && _life is not null && !_life.IsCancellationRequested)
        {
            int waitFor = WaitsInSeconds[Math.Min(attempt, WaitsInSeconds.Length - 1)];
            attempt++;

            try { await Task.Delay(TimeSpan.FromSeconds(waitFor), _life.Token); }
            catch (OperationCanceledException) { return; }

            if (_festival is null) return;

            // No contesta: se sigue esperando, sin contar y sin decir nada. Este es el caso
            // normal y el que NO se puede abandonar.
            if (!await _phone.CanReachAsync(_festival.CommunityLeaderUrl, _life.Token)) continue;

            try
            {
                await PresentMyselfAsync(_festival, _myName, _myPort, _life.Token);

                if (!_lostTheLeader)
                {
                    _say(Texts.T("colaborador.log.volvi-al-corrillo"));
                    return;
                }
            }
            catch (Exception failure)
            {
                _say(Texts.T("colaborador.log.no-pude-presentarme", ("motivo", Texts.Explain(failure))));
            }

            // Contesto pero no me dejo entrar, o me dejo entrar sin corrillo. Se insiste unas
            // cuantas veces, porque puede estar todavia terminando de abrir sus bingos -- y
            // despues se para. Antes esto no tenia techo: con un bingo que el ya no tiene
            // abierto, ella recibia el mismo aviso cada treinta segundos hasta que cerrara la
            // app.
            if (++nos < TimesToTakeANo) continue;

            // Salvo que le deba ventas. Ahi el techo no aplica: la bateria vale menos que
            // plata cobrada sin asentar, y ya no molesta a nadie porque el motivo se dice una
            // sola vez. Se sigue golpeando en silencio hasta que el bingo vuelva a estar.
            if (_notebook is not null && _notebook.Count > 0) continue;

                        _say(Texts.T("colaborador.log.dejo-de-insistir"));
            return;
        }
    }

    private bool ReadFromMyReplica()
    {
        if (_replica is null || string.IsNullOrWhiteSpace(_festival?.Mark)) return false;

        // Las bolitas van PRIMERO y aparte, porque son de todos. Lo que sigue --custodia y
        // ventas-- solo tiene sentido para quien vende: al papelito de quien canta no se le
        // puede pedir su comerciante, y pedirselo no devuelve vacio, TIRA. Estaban en el
        // mismo try, asi que a quien canta le fallaba la lectura entera y no leia NUNCA nada
        // de su propia copia.
        ReadTheBallsFromMyReplica();

        if (!Sells) return true;

        try
        {
            string mark = _festival.Mark;

            List<long> custody = NumbersIn(_replica
                .Using("foreach (n in festival.NumbersHeldBy(@marca)) { print n 'c'; }")
                .WithParameters(p => p.UserParameter<string>("marca", mark))
                .PerformQuery());

            List<long> soldCards = NumbersIn(_replica
                .Using("foreach (n in festival.NumbersSoldUnder(@marca)) { print n 'c'; }")
                .WithParameters(p => p.UserParameter<string>("marca", mark))
                .PerformQuery());

            List<(string, string, long)> sold = new();

            foreach (long card in soldCards)
            {
                string read = _replica
                    .Using("{ print festival.ParticipationNumbered(@carton).Buyer.Name c; "
                        + "print festival.ParticipationNumbered(@carton).Buyer.Mark m; }")
                    .WithParameters(p => p.UserParameter<long>("carton", card))
                    .PerformQuery();

                sold.Add((Pick(read, "c"), Pick(read, "m") ?? string.Empty, card));
            }

            _replicaComplained = false;

            // Lo que esta en la libreta se descuenta de la mano y se muestra vendido, aunque
            // el diario todavia la muestre con esos cartones. Sin esto la pantalla se los
            // devuelve al taco en el siguiente latido y ella los vende otra vez: dos personas
            // pagando por el mismo carton, que es justo lo que la custodia venia a impedir.
            foreach (SalesNotebook.Sale waiting in
                     _notebook?.Waiting ?? (IReadOnlyList<SalesNotebook.Sale>)Array.Empty<SalesNotebook.Sale>())
            {
                foreach (long card in waiting.Cards)
                {
                    custody.Remove(card);

                    if (!sold.Any(s => s.Item3 == card))
                        sold.Add((waiting.Buyer, waiting.BuyerMark, card));
                }
            }

            lock (_guard)
            {
                _inCustody.Clear();
                _inCustody.AddRange(custody);
                _sold.Clear();
                _sold.AddRange(sold);
            }

            return true;
        }
        catch (Exception failure)
        {
            // Se avisa UNA vez y no en cada latido. Cuando me re-pego, mi copia arranca vacia
            // y el organizador me la va llenando: durante esos segundos leer falla, y es
            // esperado -- no es una falla, es que todavia no llego. Repetirlo cada dos
            // segundos convierte una espera normal en tres quejas que asustan.
            if (!_replicaComplained)
            {
                _replicaComplained = true;
                _say(Texts.T("colaborador.log.no-puedo-leer-copia", ("motivo", Texts.Explain(failure))));
            }

            return false;
        }
    }

    /// <summary>
    /// Las bolitas que van saliendo, leidas de MI copia del diario.
    ///
    /// La mesa es la autoridad: ella tiene el orden y tiene las correcciones hechas en su
    /// lugar. Antes esto solo llegaba empujado -- el organizador mandaba un aviso por cada
    /// bolita --, y ese empujon lo hace EL al pie de su propio canto. Desde que el canto
    /// puede entrarle reenviado por el corrillo, el aviso no sale, y las tombolas de las
    /// otras colaboradoras se quedaban clavadas sin que nadie viera un error.
    ///
    /// Leerlo de la copia lo arregla y de paso saca un camino de empuje en vez de agregar
    /// uno: el vigia de la replica ya despierta cuando el diario avanza, y avanza en cada
    /// bolita.
    ///
    /// Lo que ella canto y su copia todavia no alcanzo se conserva al final. Su propia
    /// bolita se enciende en el momento y no puede desaparecerle mientras la replicacion
    /// llega, que es justo el segundo en que esta mirando la pantalla.
    /// </summary>
    private void ReadTheBallsFromMyReplica()
    {
        try
        {
            List<long> called = NumbersIn(_replica.PerformQry(
                "foreach (n in mesa.DrawnNumbers) { print n 'c'; }"));

            lock (_guard)
            {
                List<int> mine = new(_balls);

                _balls.Clear();

                foreach (long ball in called) _balls.Add((int)ball);

                foreach (int ball in mine)
                    if (!_balls.Contains(ball)) _balls.Add(ball);
            }
        }
        catch (Exception)
        {
            // Todavia no hay mesa, o la copia recien nacio. No es una falla: es que no llego.
            // Se queda lo que haya, que es lo que le empujaron.
        }
    }

    /// <summary>Si ya me queje de no poder leer mi copia. Se apaga sola en cuanto una lectura
    /// sale bien, asi que un problema de verdad --uno que no se cura-- se dice una vez y una
    /// recuperacion vuelve a habilitar el aviso para la proxima.</summary>
    private bool _replicaComplained;

    private static List<long> NumbersIn(string json)
    {
        List<long> numbers = new();
        if (string.IsNullOrWhiteSpace(json)) return numbers;

        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(json, @"""c""\s*:\s*(-?\d+)"))
        {
            if (long.TryParse(m.Groups[1].Value, out long n)) numbers.Add(n);
        }

        return numbers;
    }

    private static string Pick(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
            json, @"""" + key + @"""\s*:\s*""([^""]*)""");

        return m.Success ? m.Groups[1].Value : null;
    }

    private void ReadWhatIWasHanded(string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body ?? "{}");

            // El organizador confirma a que me dejo entrar; su palabra vale mas que la mia.
            if (doc.RootElement.TryGetProperty("rol", out JsonElement duty)
                && duty.ValueKind == JsonValueKind.String
                && Duties.IsCollaborator(duty.GetString()))
            {
                _myDuty = Duties.Clean(duty.GetString());
            }

            if (doc.RootElement.TryGetProperty("numero", out JsonElement mine)
                && mine.ValueKind == JsonValueKind.Number)
            {
                MyNumber = mine.GetInt32();
            }

            // La ronda vigente, con la palabra del organizador. Un organizador viejo
            // no manda nada de esto y la pantalla se queda sin rotulo de ronda.
            if (doc.RootElement.TryGetProperty("ronda", out JsonElement round)
                && round.ValueKind == JsonValueKind.Number)
            {
                RoundPosition = round.GetInt32();
            }

            if (doc.RootElement.TryGetProperty("sejuega", out JsonElement playing)
                && playing.ValueKind == JsonValueKind.String)
            {
                RoundPlaying = playing.GetString() ?? string.Empty;
            }

            // El organizador escribe los premios del alistamiento con las MISMAS claves
            // que el aviso de ronda, asi que se leen con el mismo lector. La guarda
            // sigue haciendo falta: un organizador viejo no manda 'premios' y leer eso
            // como vacio le borraria a la pantalla lo que ya sabia.
            if (doc.RootElement.TryGetProperty("premios", out JsonElement prizes)
                && prizes.ValueKind == JsonValueKind.Array)
            {
                RoundGames = RoundNotice.Promises(body);
                RoundPrizeLine = RoundNotice.PrizeLine(body);
            }

            if (doc.RootElement.TryGetProperty("bolitas", out JsonElement balls)
                && balls.ValueKind == JsonValueKind.Array)
            {
                lock (_guard)
                {
                    _balls.Clear();

                    foreach (JsonElement one in balls.EnumerateArray())
                    {
                        if (one.ValueKind == JsonValueKind.Number && one.TryGetInt32(out int ball))
                            _balls.Add(ball);
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("vendidos", out JsonElement sold)
                && sold.ValueKind == JsonValueKind.Array)
            {
                lock (_guard)
                {
                    _sold.Clear();

                    foreach (JsonElement one in sold.EnumerateArray())
                    {
                        if (!one.TryGetProperty("quien", out JsonElement who)) continue;
                        if (!one.TryGetProperty("carton", out JsonElement card)) continue;

                        string mark = one.TryGetProperty("marca", out JsonElement marked)
                            ? marked.GetString() ?? string.Empty
                            : string.Empty;

                        _sold.Add((who.GetString(), mark, card.GetInt64()));
                    }
                }
            }

            if (!doc.RootElement.TryGetProperty("cartones", out JsonElement cards)) return;

            lock (_guard)
            {
                _inCustody.Clear();

                foreach (JsonElement one in cards.EnumerateArray())
                    _inCustody.Add(one.GetInt64());
            }

                        _say(InCustody.Count == 0
                ? Texts.T(Calls ? "colaborador.log.no-toco-cartones" : "colaborador.log.no-me-entrego")
                : Texts.T("colaborador.log.me-entrego-en-custodia", ("cuales", string.Join(", ", InCustody))));
        }
        catch (Exception failure)
        {
            _say(Texts.T("colaborador.log.no-entendi-entrega", ("motivo", Texts.Explain(failure))));
        }
    }

    /// <summary>
    /// De quien es este numero, ANTES de cobrar. Devuelve null si el organizador no
    /// contesto: no saber no es lo mismo que ser nuevo, y quien vende tiene que poder
    /// distinguir las dos cosas.
    /// </summary>
    public async Task<WhoIsAnswer> WhoIsAsync(string buyerMark, CancellationToken life)
    {
        if (_festival is null || string.IsNullOrWhiteSpace(buyerMark)) return null;

        WhoIsQuestion asking = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            Collaborator = _myName,
            Buyer = buyerMark,
        };

        // Primero mi copia: es la misma pregunta contra el mismo diario, pero corre en este
        // telefono. Y esta pregunta se hace ANTES de cobrar, o sea en el peor momento para
        // depender de que el organizador conteste.
        WhoIsAnswer fromMine = WhoIsFromMyReplica(buyerMark);
        if (fromMine is not null) return fromMine;

        try
        {
            PhoneToPhone.Reply reply =
                await _phone.AskWhoIsAsync(_festival.CommunityLeaderUrl, asking.ToText(), life);

            return reply.Ok ? WhoIsAnswer.FromText(reply.Body) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// De quien es este numero, leido de MI copia del diario.
    ///
    /// La marca va por PARAMETRO y no pegada al texto: lo que ella teclea llega crudo si no
    /// son ocho alfanumericos, y un apostrofe romperia la consulta. El organizador aprendio
    /// eso por las malas y la regla se hereda igual de este lado.
    /// </summary>
    private WhoIsAnswer WhoIsFromMyReplica(string buyerMark)
    {
        if (_replica is null) return null;

        string looking = BuyerMark.Normalize(buyerMark);
        if (looking.Length == 0) return null;

        try
        {
            int howMany = AsInt(_replica.Using("print festival.CountBoughtBy(@marca) n;")
                .WithParameters(p => p.UserParameter<string>("marca", looking))
                .PerformQuery(), "n");

            string name = string.Empty;

            if (howMany > 0)
            {
                long first = AsLong(_replica.Using("print festival.BoughtByAt(@marca, @puesto) n;")
                    .WithParameters(p =>
                    {
                        p.UserParameter<string>("marca", looking);
                        p.UserParameter<int>("puesto", 1);
                    })
                    .PerformQuery(), "n");

                name = Pick(_replica.Using("print festival.ParticipationNumbered(@carton).Buyer.Name q;")
                    .WithParameters(p => p.UserParameter<long>("carton", first))
                    .PerformQuery(), "q")
                    ?? string.Empty;
            }

            // Y lo de la libreta, que el diario no puede saber. Con el organizador caido es
            // lo UNICO que hay: quien compro hace diez minutos no figura en ningun lado, y
            // sin esto la app le diria "comprador nuevo" a alguien que acaba de comprarle.
            // Esta pregunta es la defensa contra el teclazo, y se hace antes de cobrar.
            IReadOnlyList<long> alsoOwed = _notebook?.CardsUnder(looking) ?? new List<long>();

            if (alsoOwed.Count > 0)
            {
                howMany += alsoOwed.Count;

                if (name.Length == 0) name = _notebook.WhoHasThisMark(looking) ?? string.Empty;
            }

            return new WhoIsAnswer
            {
                Mark = looking,
                Known = howMany > 0,
                Who = name,
                HowMany = howMany,
            };
        }
        catch (Exception failure)
        {
            _say(Texts.T("colaborador.log.no-pude-buscar-numero", ("motivo", Texts.Explain(failure))));
            return null;
        }
    }

    private static int AsInt(string json, string key) =>
        int.TryParse(Pick(json, key) ?? NumberIn(json, key), out int n) ? n : 0;

    private static long AsLong(string json, string key) =>
        long.TryParse(Pick(json, key) ?? NumberIn(json, key), out long n) ? n : 0;

    private static string NumberIn(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
            json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");

        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Vender. Si buyerMark viene con algo, es el numero que el comprador ENSENO:
    /// eso es lo que hace que el carton nuevo caiga en SU telefono y no en el de otra
    /// persona que se llame igual.</summary>
    public async Task<string> SellAsync(string buyer, string contact,
        IReadOnlyList<long> cards, string buyerMark, CancellationToken life)
    {
        if (!Sells)
            throw new InvalidOperationException(Texts.T("colaborador.no.entraste-a-cantar"));
        if (string.IsNullOrWhiteSpace(buyer))
            throw new InvalidOperationException(Texts.T("colaborador.no.falta-nombre-comprador"));
        if (cards.Count == 0)
            throw new InvalidOperationException(Texts.T("colaborador.no.ningun-carton"));

        SaleNotice sale = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            Seller = _myName,
            Buyer = buyer.Trim(),
            Contact = contact,
            Cards = cards.ToList(),
            BuyerMark = string.IsNullOrWhiteSpace(buyerMark) ? null : buyerMark.Trim(),
        };

        string mintedMark;
        string code;

        // Por el corrillo si tengo copia; por la puerta si todavia no. Igual que la
        // devolucion, el traspaso y la anulacion, salvo en una cosa: aca el "no esta" NO es
        // un no. Trabajando sola ni se intenta -- ya sabemos que no hay Director, y golpear
        // una puerta cerrada solo le hace esperar el timeout con la fila enfrente.
        if (_replica is not null)
        {
            // El carnet lo acuña quien atiende. Mint() es azar puro, sin llave y sin
            // autoridad: no hay ninguna razon para ir a pedirselo a nadie.
            mintedMark = BuyerMark.Normalize(sale.BuyerMark);

            if (mintedMark.Length == 0) mintedMark = BuyerMark.Mint();

            (Answer how, string why) = ThereIsNobodyInCharge
                ? (Answer.NotThere, null)
                : SellThroughTheHuddle(sale.Buyer, contact, cards, mintedMark);

            if (how == Answer.SaidNo)
            {
                _say(Texts.T("colaborador.log.no-acepto-la-venta", ("motivo", why)));
                _say(Texts.T("colaborador.log.no-cobres"));
                return null;
            }

            // El organizador no esta. Los cartones SON de ella --el se los entrego y el
            // diario lo dice--, asi que no hay dos vendedoras que puedan pisarse: la venta
            // se cobra y queda anotada para leersela cuando aparezca. Esto es lo unico que
            // separa "se le reinicio el telefono al padre" de "se para la fila en la puerta".
            if (how == Answer.NotThere) WriteItInTheNotebook(sale, mintedMark);

            code = CodeForTheBuyer(sale.Buyer, mintedMark, cards);
        }
        else
        {
            PhoneToPhone.Reply reply =
                await _phone.ReportSaleAsync(_festival.CommunityLeaderUrl, sale.ToText(), life);

            if (!reply.Ok)
            {
                _say(Texts.T("colaborador.log.no-acepto-la-venta", ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
                _say(Texts.T("colaborador.log.no-cobres"));
                return null;
            }

            // La marca del comprador vuelve adentro de su codigo. Sin quedarmela, la proxima
            // vez que esta misma persona me compre yo no tendria como decir cual es.
            mintedMark = MarkInside(reply.Body);
            code = reply.Body;
        }

        lock (_guard)
        {
            foreach (long card in cards)
            {
                _inCustody.Remove(card);
                _sold.Add((sale.Buyer, mintedMark, card));
            }
        }

        _say(Texts.T("colaborador.log.vendi-a", ("cuales", string.Join(", ", cards)), ("comprador", sale.Buyer)));
        Changed?.Invoke();

        return code;
    }

    /// <summary>La marca que el organizador acuno para el comprador, que viaja adentro del
    /// codigo que le acaba de devolver.</summary>
    private static string MarkInside(string code)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(OpenFestival.RawJson(code));

            return doc.RootElement.TryGetProperty("m", out JsonElement mark)
                ? mark.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Le pasa cartones de su taco a otro colaborador que se quedo sin. Solo
    /// puede darlos quien los tiene: el organizador no reasigna por detras, nada mas
    /// asienta lo que se dieron de mano.</summary>
    /// <summary>Plantearle al organizador que una venta se deshaga. Ella no la deshace:
    /// el que le da efecto es el. Puede ser una venta que hizo otra colaboradora.</summary>
    public async Task<bool> PleadToUndoAsync(long card, CancellationToken life)
    {
        if (!Sells)
        {
            _say(Texts.T("colaborador.log.cantar-no-anulaciones"));
            return false;
        }

        if (card <= 0) throw new InvalidOperationException(Texts.T("colaborador.no.ningun-carton"));

        PleaRequest asked = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            From = _myName,
            Card = card,
        };

        // Por el corrillo si estoy pegada; por la puerta si todavia no. El camino viejo se
        // queda mientras haya bingos donde no me pegue.
        if (!ThereIsNobodyInCharge)
        {
            string no = PleadThroughTheHuddle(card);

            if (no is not null)
            {
                _say(Texts.T("colaborador.log.no-recibio-pedido", ("motivo", no)));
                return false;
            }
        }
        else
        {
            PhoneToPhone.Reply reply =
                await _phone.AskToUndoAsync(_festival.CommunityLeaderUrl, asked.ToText(), life);

            if (!reply.Ok)
            {
                _say(Texts.T("colaborador.log.no-recibio-pedido", ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
                return false;
            }
        }

        _say(Texts.T("colaborador.log.pedi-anular", ("carton", card.ToString())));
        Changed?.Invoke();

        return true;
    }

    /// <summary>Soltar lo que no vendio. Sigue adentro del bingo: si despues el padre
    /// le entrega mas, vuelve a vender.</summary>
    public async Task<bool> GiveBackAsync(IReadOnlyList<long> cards, CancellationToken life)
    {
        if (!Sells)
        {
            _say(Texts.T("colaborador.log.cantar-no-devolver"));
            return false;
        }

        if (cards is null || cards.Count == 0)
            throw new InvalidOperationException(Texts.T("colaborador.no.ningun-carton"));

        lock (_guard)
        {
            foreach (long card in cards)
            {
                if (!_inCustody.Contains(card))
                    throw new InvalidOperationException(Texts.T("colaborador.no.no-en-la-mano", ("carton", card.ToString())));
            }
        }

        GiveBackRequest asked = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            From = _myName,
            Cards = cards.ToList(),
        };

        // Por el corrillo si estoy pegada; por la puerta si todavia no. El camino viejo se
        // queda mientras haya bingos donde no me pegue -- borrarlo antes de eso seria dejar
        // sin devolver a quien entro a uno que no replica.
        if (!ThereIsNobodyInCharge)
        {
            string no = GiveBackThroughTheHuddle(cards);

            if (no is not null)
            {
                _say(Texts.T("colaborador.log.no-acepto-devolucion", ("motivo", no)));
                _say(Texts.T("colaborador.log.sigues-con-cartones"));
                return false;
            }
        }
        else
        {
            PhoneToPhone.Reply reply =
                await _phone.AskToGiveBackAsync(_festival.CommunityLeaderUrl, asked.ToText(), life);

            if (!reply.Ok)
            {
                _say(Texts.T("colaborador.log.no-acepto-devolucion", ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
                _say(Texts.T("colaborador.log.sigues-con-cartones"));
                return false;
            }
        }

        lock (_guard)
        {
            foreach (long card in cards) _inCustody.Remove(card);
        }

        _say(Texts.T("colaborador.log.le-devolvi", ("cuales", string.Join(", ", cards))));
        Changed?.Invoke();

        return true;
    }

    public async Task<bool> PassToAsync(int toNumber, IReadOnlyList<long> cards, CancellationToken life)
    {
        if (!Sells)
        {
            _say(Texts.T("colaborador.log.cantar-no-pasar"));
            return false;
        }

        if (toNumber <= 0)
            throw new InvalidOperationException(Texts.T("colaborador.no.falta-numero-colaboradora"));
        if (toNumber == MyNumber)
            throw new InvalidOperationException(Texts.T("colaborador.no.tu-propio-numero"));
        if (cards is null || cards.Count == 0)
            throw new InvalidOperationException(Texts.T("colaborador.no.ningun-carton"));

        lock (_guard)
        {
            foreach (long card in cards)
            {
                if (!_inCustody.Contains(card))
                    throw new InvalidOperationException(Texts.T("colaborador.no.no-en-la-mano", ("carton", card.ToString())));
            }
        }

        HandoverRequest asked = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            From = _myName,
            ToNumber = toNumber,
            Cards = cards.ToList(),
        };

        // Por el corrillo si estoy pegada y se quien es ella; por la puerta si no. El camino
        // viejo se queda mientras haya bingos donde no me pegue -- borrarlo antes seria dejar
        // sin pasar cartones a quien entro a uno que no replica.
        string toMark = ThereIsNobodyInCharge ? null : MarkOfCollaborator(toNumber);

        if (toMark is not null)
        {
            string no = PassThroughTheHuddle(toMark, cards);

            if (no is not null)
            {
                _say(Texts.T("colaborador.log.no-acepto-traspaso", ("motivo", no)));
                _say(Texts.T("colaborador.log.no-le-entregues"));
                return false;
            }
        }
        else
        {
            PhoneToPhone.Reply reply =
                await _phone.AskForHandoverAsync(_festival.CommunityLeaderUrl, asked.ToText(), life);

            if (!reply.Ok)
            {
                _say(Texts.T("colaborador.log.no-acepto-traspaso", ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
                _say(Texts.T("colaborador.log.no-le-entregues"));
                return false;
            }
        }

        lock (_guard)
        {
            foreach (long card in cards) _inCustody.Remove(card);
        }

        _say(Texts.T("colaborador.log.le-pase", ("cuales", string.Join(", ", cards)), ("numero", toNumber.ToString())));
        Changed?.Invoke();

        return true;
    }

    /// <summary>Un jugador devuelve un carton y pide otro. El colaborador elige los dos
    /// numeros y se lo PROPONE al organizador, que decide contra su diario. El papel
    /// nuevo se entrega recien cuando el organizador contesta que si.</summary>
    public async Task<string> ExchangeAsync(long cardLeaving, long cardComingIn, CancellationToken life)
    {
        if (!Sells)
        {
            _say(Texts.T("colaborador.log.cantar-no-cambiar"));
            return null;
        }

        lock (_guard)
        {
            if (!_inCustody.Contains(cardComingIn))
                            throw new InvalidOperationException(
                Texts.T("colaborador.no.no-en-la-mano", ("carton", cardComingIn.ToString())));
        }

        ExchangeRequest asked = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            Collaborator = _myName,
            CardLeaving = cardLeaving,
            CardComingIn = cardComingIn,
        };

        PhoneToPhone.Reply reply =
            await _phone.AskForExchangeAsync(_festival.CommunityLeaderUrl, asked.ToText(), life);

        if (!reply.Ok)
        {
            _say(Texts.T("colaborador.log.no-acepto-cambio", ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
            _say(Texts.T("colaborador.log.no-entregues-nuevo"));
            return null;
        }

        lock (_guard)
        {
            _inCustody.Remove(cardComingIn);
            if (!_inCustody.Contains(cardLeaving)) _inCustody.Add(cardLeaving);

            string buyer = null;
            string mark = string.Empty;

            for (int i = _sold.Count - 1; i >= 0; i--)
            {
                if (_sold[i].Card != cardLeaving) continue;

                buyer = _sold[i].Who;
                mark = _sold[i].Mark;
                _sold.RemoveAt(i);
            }

            if (buyer is not null) _sold.Add((buyer, mark, cardComingIn));
        }

        _say(Texts.T("colaborador.log.cambie", ("sale", cardLeaving.ToString()), ("entra", cardComingIn.ToString())));
        Changed?.Invoke();

        return reply.Body;
    }

    /// <summary>Digitar una bolita: viaja al organizador, el la valida, la journalea en la
    /// mesa de ESTE bingo y la reparte a los jugadores de este bingo.</summary>
    public Task<bool> CallAsync(int number, CancellationToken life) =>
        CallAsync(number, 0, life);

    /// <summary>Me equivoque de tecla: la que cante no era esa. Va por el mismo camino que
    /// el canto -- el organizador lo asienta en su diario y de ahi sale para todos.</summary>
    public Task<bool> CorrectAsync(int wrong, int right, CancellationToken life) =>
        CallAsync(right, wrong, life);

    private async Task<bool> CallAsync(int number, int instead, CancellationToken life)
    {
        if (!Calls)
        {
            _say(Texts.T("colaborador.log.entraste-a-vender"));
            return false;
        }

        CallNotice call = new()
        {
            FestivalId = _festival.FestivalId,
            Mark = _festival.Mark,
            Who = _myName,
            Number = number,
            Instead = instead,
        };

                string what = instead > 0
            ? Texts.T("colaborador.que.la-correccion", ("malo", instead.ToString()), ("bueno", number.ToString()))
            : Texts.T("colaborador.que.el-canto-del", ("numero", number.ToString()));

        // Por el corrillo si tengo copia; por la puerta si todavia no. Igual que los otros
        // cuatro verbos. Aca NO hay libreta y no puede haberla: una bolita guardada para
        // despues entraria fuera de orden, y el orden de la ronda es lo unico que hace que un
        // carton se pueda revisar. Con el organizador caido, el canto se para -- y esta bien
        // que se pare, porque sin el tampoco hay quien reparta las bolitas.
        if (!ThereIsNobodyInCharge)
        {
            string no = SingThroughTheHuddle(number, instead);

            if (no is not null)
            {
                _say(Texts.T("colaborador.log.no-acepto-que", ("que", what), ("motivo", no)));
                return false;
            }
        }
        else
        {
            PhoneToPhone.Reply reply =
                await _phone.ReportCallAsync(_festival.CommunityLeaderUrl, call.ToText(), life);

            if (!reply.Ok)
            {
                _say(Texts.T("colaborador.log.no-acepto-que", ("que", what), ("motivo", Refusal.Dicho(reply.Because, reply.Why))));
                return false;
            }
        }

        // Mi propia bolita se enciende aca y no esperando el aviso de vuelta: el aviso
        // llega igual y no hace nada (ya esta), pero si se pierde en el camino la
        // tombola no se queda mostrando disponible una que yo acabo de cantar.
        Register(number, instead);

                _say(instead > 0
            ? Texts.T("organizador.log.corregi", ("malo", instead.ToString()), ("bueno", number.ToString()))
            : Texts.T("colaborador.log.cante-y-repartio", ("numero", number.ToString())));

        Changed?.Invoke();

        return true;
    }

    // ── la lista de bingos del colaborador ──────────────────────────────────

    public static void Remember(string code, string myName)
    {
        List<JoinedFestival> joined = Joined().ToList();

        string id = IdOf(code);
        joined.RemoveAll(j => string.Equals(IdOf(j.Code), id, StringComparison.Ordinal));
        joined.Add(new JoinedFestival { Code = code?.Trim(), MyName = myName });

        Preferences.Default.Set(SavedKey, JsonSerializer.Serialize(joined));
    }

    /// <summary>Sacar un bingo de mi lista. Hace falta cuando el organizador me dice que
    /// ya no me deja entrar: si no, la tarjeta se queda ahi fallando en cada toque.</summary>
    public static void Forget(string festivalId)
    {
        List<JoinedFestival> joined = Joined().ToList();

        if (joined.RemoveAll(j =>
                string.Equals(IdOf(j.Code), festivalId, StringComparison.Ordinal)) == 0) return;

        Preferences.Default.Set(SavedKey, JsonSerializer.Serialize(joined));
    }

    public static IReadOnlyList<JoinedFestival> Joined()
    {
        try
        {
            string raw = Preferences.Default.Get(SavedKey, string.Empty);
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<JoinedFestival>();

            return JsonSerializer.Deserialize<List<JoinedFestival>>(raw) ?? new List<JoinedFestival>();
        }
        catch (Exception)
        {
            return Array.Empty<JoinedFestival>();
        }
    }

    private static string IdOf(string code)
    {
        try
        {
            return OpenFestival.FromText(code).FestivalId;
        }
        catch (Exception)
        {
            return code ?? string.Empty;
        }
    }

    public void Dispose()
    {
        try { _life?.Cancel(); } catch { }
        try { _listener?.Dispose(); } catch { }
        try { _roundListener?.Dispose(); } catch { }
        try { _drawListener?.Dispose(); } catch { }
        try { _phone?.Dispose(); } catch { }
        try { _life?.Dispose(); } catch { }
    }
}
