using System.Text;
using System.Text.Json;
using BingoApp.Contract;
using BingoApp.Collaborator;
using BingoApp.Rehearsal;

namespace BingoApp;

/// <summary>
/// Quien ayuda en la noche. La pantalla es una pagina (collaborator.html) y este
/// code-behind es el puente: le manda un retrato de lo que tiene entre manos y recibe los
/// gestos.
///
/// UNA pantalla para DOS oficios. Cual de los dos no se elige aca ni se recuerda del
/// telefono: viene en el codigo que dio el organizador, porque el que reparte los oficios
/// es el. Lo unico que este puente hace con eso es contarselo a la pagina, que se arma de
/// una sola de las dos caras.
/// </summary>
public partial class CollaboratorScreen : ContentPage
{
    private const int MyPort = 8090;

    private readonly StringBuilder _log = new();

    private LiveCollaborator _collaborator;
    private string _currentCode;
    private bool _inside;

    /// <summary>Si esta a la vista el codigo del comprador. Mientras lo este, el atras no
    /// mueve nada: ese codigo todavia no se lo dio a nadie, y la capa se cierra por un
    /// solo lugar -- "listo, siguiente persona" --, que es ademas el unico que limpia el
    /// nombre del comprador.</summary>
    private bool _showingCode;
    private string _lastSaid = string.Empty;
    private bool _lastWasBad;

    /// <summary>El bingo a la que se esta entrando. Hace falta aparte de
    /// _collaborator.Festival porque la cabecera se pinta ANTES de presentarse, y hasta que
    /// PresentMyselfAsync no vuelve, el colaborador todavia recuerda el bingo anterior.</summary>
    private OpenFestival _festival;

    private readonly string _arrivedCode;

    private string _codeForThePage;

    public CollaboratorScreen() => InitializeComponent();

    public CollaboratorScreen(string code) : this() => _arrivedCode = code;

    /// <summary>Se vuelve por aca cada vez que se cierra la camara, no solo al abrir la
    /// pantalla. Por eso repintar es obligatorio: si no, vuelve con lo ultimo que se
    /// dibujo antes de irse.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        _collaborator = LiveCollaborator.Instance;
        _collaborator.TalkTo(Say);
        _collaborator.Changed += OnChanged;

        QrInbox.Arrived += OnCodeArrived;

        PushState();

        if (_arrivedCode is not null && !_inside)
        {
            OnCodeArrived(_arrivedCode);
            return;
        }

        if (QrInbox.Collect() is string pending) OnCodeArrived(pending);
    }

    protected override void OnDisappearing()
    {
        QrInbox.Arrived -= OnCodeArrived;
        if (_collaborator is not null) _collaborator.Changed -= OnChanged;
        base.OnDisappearing();
    }

    private void OnChanged() => PushState();

    /// <summary>El boton de atras del telefono tiene que hacer lo mismo que la flecha:
    /// adentro de un bingo vuelve a la PUERTA, no saca de la pantalla. Quien ayuda
    /// puede estar metida en varios a la vez -- vendiendo en uno y cantando en otro -- y
    /// la lista es como se cambia de uno al otro. Solo estando ya en la puerta el atras
    /// sale de aca.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (_showingCode)
        {
            Say(Texts.T("colaborador.log.primero-el-codigo"));
            return true;
        }

        if (!_inside) return base.OnBackButtonPressed();

        _inside = false;
        Say(Texts.T("colaborador.log.volviste-a-tus-bingos"));
        PushState();

        return true;
    }

    // ── lo que la pagina le pide al telefono ─────────────────────────────────

    private void OnWebMessage(object sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        string[] parts = (e.Message ?? string.Empty).Split('|');
        string verb = parts[0];
        string One(int i) => parts.Length > i ? parts[i] : string.Empty;

        switch (verb)
        {
            case "ready":
                PushTextos();
                PushFigures();
                PushState();
                PushLog();
                if (_codeForThePage is not null && !_inside) OnCodeArrived(_codeForThePage);
                break;

            case "idioma":
                if (parts.Length > 1) AppLanguage.Choose(parts[1]);
                PushTextos();
                PushFigures();
                PushState();
                PushLog();
                break;

            case "wantlog":
                PushLog();
                break;

            case "scan":
                Scan(One(1));
                break;

            case "enter":
                Enter(One(2), One(1));
                break;

            case "again":
                Again(One(1));
                break;

            case "another":
                _inside = false;
                Say(Texts.T("colaborador.log.volviste-a-tus-bingos"));
                PushState();
                break;

            case "whois":
                WhoIs(One(1));
                break;

            case "scanbuyer":
                ScanBuyer();
                break;

            case "scancolleague":
                ScanColleague();
                break;

            case "sell":
                Sell(One(1), One(2), One(3), Cards(One(4)));
                break;

            case "handover":
                HandOver(One(1), Cards(One(2)));
                break;

            case "giveback":
                GiveBack(Cards(One(1)));
                break;

            case "exchange":
                Exchange(One(1), One(2));
                break;

            case "plead":
                Plead(One(1));
                break;

            case "call":
                Call(One(1));
                break;

            case "correct":
                Correct(One(1), One(2));
                break;

            case "copy":
                Copy(One(1));
                break;

            case "codeclosed":
                _showingCode = false;
                break;

            case "ensayo":
                StartRehearsal(One(1));
                break;

            case "ensayoFin":
                FinishRehearsal();
                break;
        }
    }

    // ── el ensayo ────────────────────────────────────────────────────────────

    /// <summary>Arma el bingo de ensayo y entra con su codigo, como si el papelito
    /// hubiera llegado de afuera. Por eso el ensayo no tiene pantallas propias: desde
    /// aca todo es el flujo de siempre.</summary>
    private void StartRehearsal(string duty) => Task.Run(() =>
    {
        try
        {
            // El festejo del vigia (Rita gano, premio entregado) entra como toast:
            // es el momento del ensayo del cantor y no puede vivir solo en el registro.
            string code = RehearsalConductor.Start(duty, RehearsalName(), Say,
                line => { Say(line); PushToast(line); });

            OnScreen(() => Enter(code, null));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.ensayo-no-armado", ("motivo", Texts.Explain(failure))));
        }
    });

    /// <summary>Con que nombre se practica: el que ya uso en algun bingo de verdad, o
    /// 'Vos' si esta es su primera noche -- que es justo a quien el ensayo apunta.</summary>
    private static string RehearsalName()
    {
        foreach (JoinedFestival one in LiveCollaborator.Joined())
            if (!string.IsNullOrWhiteSpace(one.MyName)) return one.MyName.Trim();

        return Texts.T("colaborador.ensayo.vos");
    }

    /// <summary>La pagina ya confirmo. Primero se sale de la vista y de la lista --
    /// que la pantalla quede limpia no puede esperar al disco -- y el desmonte va
    /// atras: el conductor cierra el diario ANTES de borrar su carpeta.</summary>
    private void FinishRehearsal()
    {
        OpenFestival rehearsal = _festival;

        if (rehearsal is null || !RehearsalConductor.IsRehearsal(rehearsal))
        {
            Say(Texts.T("colaborador.log.no-es-ensayo"));
            return;
        }

        _inside = false;
        _festival = null;

        LiveCollaborator.Forget(rehearsal.FestivalId);
        PushState();

        Task.Run(() =>
        {
            try
            {
                RehearsalConductor.Finish(rehearsal.FestivalId);
                Say(Texts.T("colaborador.log.ensayo-borrado"));
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.ensayo-no-borrado", ("motivo", Texts.Explain(failure))));
            }

            OnScreen(PushState);
        });
    }

    /// <summary>Tipo declarado interfaz y valor List: la regla de las dos reglas de los
    /// parametros de coleccion, que esta escrita entera arriba de HandTo.</summary>
    private static List<long> Cards(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(one => long.TryParse(one.Trim(), out long card) ? card : 0)
            .Where(card => card > 0)
            .ToList();

    // ── entrar ───────────────────────────────────────────────────────────────

    /// <summary>El codigo entro solo, por el enlace o por la camara. NO alista a nadie sin
    /// nombre: sin esto, quien escanea desde la camara del sistema entraria con un nombre
    /// que nunca escribio.
    ///
    /// Pero si el organizador corto el codigo A NOMBRE de alguien, el nombre viene adentro
    /// y la puerta lo rellena: preguntarle como se llama a quien recibio SU codigo es
    /// preguntarle algo que el organizador ya contesto. Queda editable, que para eso es un
    /// campo -- el papelito de Sonia lo puede acabar usando su hermana.</summary>
    private void OnCodeArrived(string code) => OnScreen(() =>
    {
        _codeForThePage = code;
        PushScanned("code", code);

        string meantFor = MeantForIn(code);

        if (meantFor.Length > 0)
        {
            PushScanned("name", meantFor);
            Say(Texts.T("colaborador.log.codigo-cortado-para", ("nombre", meantFor)));
            return;
        }

        Say(Texts.T("colaborador.log.escribe-tu-nombre"));
    });

    private static string MeantForIn(string code)
    {
        try
        {
            return OpenFestival.FromText(code).MeantFor ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private async void Scan(string myName)
    {
        ScanScreen camera = new();
        await Navigation.PushModalAsync(camera);

        string read = await camera.Scanned;

        if (read is not null) Enter(read, myName);
    }

    /// <summary>Volver a una conocida. Va por posicion en la lista y no por el codigo:
    /// el codigo es largo y no tiene por que pasear por la pagina para volver igual.</summary>
    private void Again(string which)
    {
        IReadOnlyList<JoinedFestival> joined = LiveCollaborator.Joined();

        if (!int.TryParse(which, out int at) || at < 0 || at >= joined.Count)
        {
            Say(Texts.T("colaborador.log.bingo-no-en-lista"));
            return;
        }

        Enter(joined[at].Code, joined[at].MyName);
    }

    private void Enter(string code, string myName)
    {
        OpenFestival festival;

        try
        {
            festival = OpenFestival.FromText(code);
        }
        catch (Exception failure)
        {
            Say(Texts.T("jugador.log.codigo-no-sirve", ("motivo", Texts.Explain(failure))));
            return;
        }

        // El nombre puede venir EN EL CODIGO: el organizador ya lo escribio al cortarle
        // el papelito. Exigirlo antes era pedirle lo que estaba a punto de recibir --
        // y encima no se podia ni abrir la camara sin escribirlo.
        //
        // Solo hace falta escribirlo cuando el papelito salio sin nombre, que es el
        // codigo que el organizador deja "para quien llegue".
        string name = string.IsNullOrWhiteSpace(myName)
            ? (festival.MeantFor ?? string.Empty).Trim()
            : myName.Trim();

        if (name.Length == 0)
        {
            Say(Texts.T("colaborador.log.codigo-anonimo"));
            PushNeedName();
            return;
        }

        // Cambiar de bingo desde la lista: limpio lo de la anterior y me vuelvo a
        // presentar, sin reiniciar la app.
        bool switched = _inside && _collaborator.Festival is not null
            && !string.Equals(_collaborator.Festival.FestivalId, festival.FestivalId,
                StringComparison.Ordinal);

        if (switched)
        {
            Say(Texts.T("colaborador.log.me-paso-de-bingo", ("de", _collaborator.Festival.FestivalId), ("a", festival.FestivalId)));
        }
        else if (_inside)
        {
            // Volver a presentarse es el camino de vuelta cuando al organizador se le
            // reinicio la app: el recupera del diario lo que yo tengo en la mano.
            Say(Texts.T("colaborador.log.me-vuelvo-a-presentar"));
        }

        _currentCode = code?.Trim();
        _inside = true;
        _festival = festival;
        _codeForThePage = null;

        PushState();

        Task.Run(async () =>
        {
            try
            {
                // Un ensayo se toca en la lista y VUELVE, pero su organizador invisible
                // vive en el proceso: si la app se reinicio hay que levantarlo antes de
                // presentarse, o el loopback no contesta. Y si su carpeta ya no existe,
                // el ensayo murio: se saca de la lista en vez de dejarla fallando.
                if (RehearsalConductor.IsRehearsal(festival))
                {
                    try
                    {
                        RehearsalConductor.EnsureAlive(festival, Say,
                            line => { Say(line); PushToast(line); });
                    }
                    catch (Exception gone)
                    {
                        LiveCollaborator.Forget(festival.FestivalId);
                        _inside = false;
                        Say(gone.Message);
                        PushState();
                        return;
                    }
                }

                // La firma se coteja la PRIMERA vez. Si ya tengo el diario de este bingo
                // replicado en este telefono, esa copia solo pudo nacer de una entrada que
                // ya paso la verificacion: pedirla de nuevo seria exigirle al organizador
                // que este prendido para dejarme mirar lo mio. Es el mismo criterio que el
                // jugador ya usa con los codigos que tiene guardados.
                if (!Collaborator.LiveCollaborator.HasReplica(festival.FestivalId)
                    && !await SealChecksOutAsync(festival))
                {
                    _inside = false;
                    PushState();
                    return;
                }

                bool entered = await _collaborator.PresentMyselfAsync(
                    festival, name, MyPort, CancellationToken.None);

                if (!entered)
                {
                    _inside = false;
                    PushState();
                    return;
                }

                LiveCollaborator.Remember(_currentCode, name);
                PushState();
            }
            catch (Exception failure)
            {
                _inside = false;
                Say(Texts.T("jugador.log.no-pude-entrar", ("error", failure.GetType().Name + ": " + Texts.Explain(failure))));
                PushState();
            }
        });
    }

    /// <summary>
    /// La firma del codigo, cuando trae. Un codigo SIN firma entra como siempre -- los
    /// guardados de antes no traen y son validos para siempre. Uno CON firma se coteja
    /// contra el certificado que el propio organizador sirve en /certificado/: si el que
    /// atiende en esa direccion no es quien firmo el codigo, aca no se entra.
    ///
    /// Sin pin y sin memoria a proposito: se verifica en el momento, contra el telefono
    /// que esta atendiendo AHORA. Lo que prueba es que el sobre llego sin tocar y que lo
    /// acuno el mismo telefono al que me estoy por presentar.
    /// </summary>
    private async Task<bool> SealChecksOutAsync(OpenFestival festival)
    {
        if (string.IsNullOrWhiteSpace(festival.Signature)) return true;

        using System.Security.Cryptography.X509Certificates.X509Certificate2 cert =
            await Transport.PhoneToPhone.FetchPeerCertAsync(festival.CommunityLeaderUrl);

        if (cert is null)
        {
            Say(Texts.T("colaborador.log.firma.sin-certificado"));
            return false;
        }

        if (!string.IsNullOrWhiteSpace(festival.SignerFingerprint)
            && !string.Equals(Huddle.PhoneCertificate.Fingerprint(cert),
                festival.SignerFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Say(Texts.T("colaborador.log.firma.certificado-cambio"));
            return false;
        }

        if (!FestivalSeal.Verify(festival.SignedPayload, festival.Signature, cert))
        {
            Say(Texts.T("colaborador.log.firma.no-es-del-organizador"));
            return false;
        }

        Say(Texts.T("jugador.log.firma.verificada"));
        return true;
    }

    // ── vender ───────────────────────────────────────────────────────────────

    private void WhoIs(string mark) => Task.Run(async () =>
    {
        try
        {
            WhoIsAnswer answer = await _collaborator.WhoIsAsync(mark, CancellationToken.None);

            PushKnown(answer is null || !answer.Known ? null : answer.Who);
        }
        catch (Exception)
        {
            // Preguntar quien es es una comodidad, no un paso: si no se puede, ella
            // escribe el nombre y la venta sigue igual.
            PushKnown(null);
        }
    });

    private async void ScanBuyer()
    {
        ScanScreen camera = new(Texts.T("colaborador.scan.apunta-al-comprador"), BuyerBadge.Scheme);
        await Navigation.PushModalAsync(camera);

        string read = await camera.Scanned;

        if (read is null) return;

        (string mark, string name) = BuyerBadge.Read(read);

        if (mark.Length == 0)
        {
            Say(Texts.T("colaborador.log.no-es-numero-de-comprador"));
            return;
        }

        PushScanned("buyer", mark);
        if (name.Length > 0) PushKnown(name);
    }

    private void Sell(string mark, string buyer, string contact, List<long> cards)
    {
        if (cards.Count == 0)
        {
            Say(Texts.T("colaborador.log.toca-los-cartones-dar"));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                string code = await _collaborator.SellAsync(buyer, contact, cards, mark,
                    CancellationToken.None);

                if (string.IsNullOrWhiteSpace(code)) return;

                PushCode(buyer, code);
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.no-pude-vender", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    private void Exchange(string leaving, string comingIn)
    {
        if (!long.TryParse(leaving, out long outCard) || !long.TryParse(comingIn, out long inCard))
        {
            Say(Texts.T("colaborador.log.no-entendi-cual-por-cual"));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                string code = await _collaborator.ExchangeAsync(outCard, inCard,
                    CancellationToken.None);

                if (string.IsNullOrWhiteSpace(code)) return;

                // El codigo nuevo hay que darselo: el viejo ya no vale para ese carton.
                PushCode(WhoBought(inCard), code);
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.no-pude-cambiar", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    private string WhoBought(long card)
    {
        foreach ((string who, _, long one) in _collaborator.Sold)
            if (one == card) return who;

        return Texts.T("colaborador.quien-compro");
    }

    private void Plead(string card)
    {
        if (!long.TryParse(card, out long which)) return;

        Task.Run(async () =>
        {
            try
            {
                await _collaborator.PleadToUndoAsync(which, CancellationToken.None);
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.no-pude-pedir-anulacion", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    // ── lo demas ─────────────────────────────────────────────────────────────

    /// <summary>Leer el numero de una colega. El codigo trae el bingo y el numero, nunca
    /// su marca: la marca autoriza y no tiene por que salir de su telefono.</summary>
    private async void ScanColleague()
    {
        ScanScreen camera = new(Texts.T("colaborador.scan.apunta-a-la-colega"), ColleagueCode.Scheme);
        await Navigation.PushModalAsync(camera);

        string read = await camera.Scanned;

        if (read is null) return;

        int number = ColleagueCode.NumberIn(read, _collaborator.Festival?.FestivalId);

        if (number == 0)
        {
            Say(Texts.T("colaborador.log.no-es-numero-de-colega"));
            return;
        }

        PushScanned("colleague", number.ToString());
        Say(Texts.T("colaborador.log.le-vas-a-pasar", ("numero", number.ToString())));
    }

    private void HandOver(string toWhom, List<long> cards)
    {
        if (!int.TryParse(toWhom, out int to) || to <= 0)
        {
            Say(Texts.T("colaborador.pasar.ingresa-el-numero"));
            return;
        }

        if (cards.Count == 0)
        {
            Say(Texts.T("colaborador.log.toca-los-cartones-pasar"));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await _collaborator.PassToAsync(to, cards, CancellationToken.None);
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.no-pude-pasar", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    private void GiveBack(List<long> cards)
    {
        if (cards.Count == 0)
        {
            Say(Texts.T("colaborador.log.toca-los-cartones-devolver"));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await _collaborator.GiveBackAsync(cards, CancellationToken.None);
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.no-pude-devolver", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    // ── cantar ───────────────────────────────────────────────────────────────

    private void Call(string what)
    {
        if (!int.TryParse(what, out int number) || number is < 1 or > 75)
        {
            Say(Texts.T("colaborador.log.bolita-no-existe"));
            return;
        }

        if (AlreadyOut().Contains(number))
        {
            Say(Texts.T("cantando.ya-salio", ("numero", number.ToString())));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await _collaborator.CallAsync(number, CancellationToken.None);
            }
            catch (Exception failure)
            {
                Say(Texts.T("jugador.log.no-pude-cantar", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    /// <summary>Me equivoque de tecla. La bolita mala se cambia por la buena en el diario
    /// del organizador, que es el unico que las asienta; de ahi vuelve el aviso y la
    /// tombola de todos queda igual.</summary>
    private void Correct(string what, string forWhat)
    {
        if (!int.TryParse(what, out int wrong) || !int.TryParse(forWhat, out int right)
            || right is < 1 or > 75)
        {
            Say(Texts.T("colaborador.log.no-entendi-correccion"));
            return;
        }

        if (AlreadyOut().Contains(right))
        {
            Say(Texts.T("cantando.ya-salio", ("numero", right.ToString())));
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                if (await _collaborator.CorrectAsync(wrong, right, CancellationToken.None))
                    PushToast(Texts.T("organizador.log.listo-era-el", ("numero", right.ToString())));
            }
            catch (Exception failure)
            {
                Say(Texts.T("colaborador.log.no-pude-corregir", ("motivo", Texts.Explain(failure))));
            }
        });
    }

    /// <summary>Todo lo que ya salio en la ronda. Lo lleva el colaborador y no la
    /// pantalla: ahi entran tambien las que canto el organizador, que llegan por aviso.
    /// </summary>
    private List<int> AlreadyOut() => new(_collaborator.BallsAlreadyCalled);

    private void Copy(string text) => OnScreen(async () =>
    {
        try
        {
            await Clipboard.SetTextAsync(text);
            PushToast(Texts.T("organizador.log.codigo-copiado"));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-copiar", ("motivo", Texts.Explain(failure))));
        }
    });

    // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

    private void PushState()
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "state");
            json.WriteBoolean("inside", _inside);

            // El oficio sale del codigo mientras se entra, y del colaborador una vez
            // adentro: entre las dos cosas hay un momento en que solo existe el codigo.
            string duty = _collaborator?.MyDuty is { Length: > 0 } mine
                ? mine
                : _festival?.Duty ?? Duties.Sells;

            json.WriteString("duty", Duties.IsCaller(duty) ? Duties.Calls : Duties.Sells);
            json.WriteString("who", _collaborator?.MyName ?? string.Empty);
            json.WriteString("festivalName", _festival?.Name ?? string.Empty);
            json.WriteString("festivalId", _festival?.FestivalId ?? string.Empty);
            json.WriteNumber("myNumber", _collaborator?.MyNumber ?? 0);

            // El ensayo se reconoce por la URL, no por una llave: un organizador que
            // atiende en loopback solo puede ser el invisible de este telefono.
            json.WriteBoolean("practice", RehearsalConductor.IsRehearsal(_festival));

            json.WriteString("roundLine", RoundLine());
            json.WriteBoolean("between", _collaborator?.BetweenRounds ?? false);
            // 'solo' es AHORA MISMO, no como entro. Antes viajaba WorkingAlone, que solo es
            // cierto si el organizador ya estaba caido cuando ella abrio la app. El caso
            // comun es el otro -- entra bien y el se cae despues -- y ahi la pantalla no
            // decia nada: ella confirmaba una venta que se iba a la libreta creyendo que le
            // habia llegado.
            json.WriteBoolean("solo", _collaborator?.ThereIsNobodyInCharge ?? false);
            json.WriteBoolean("canSellAlone", _collaborator?.CanSellAlone ?? false);
            json.WriteNumber("waiting", _collaborator?.SalesWaitingToBeTold ?? 0);

            // Las que rebotaron: plata que ella tiene y un carton que no existe. Van con
            // todo lo que hace falta para ir a buscar a la persona -- la hora, el nombre,
            // los cartones y el motivo -- porque "hubo un problema" no sirve para nada.
            json.WriteStartArray("bounced");
            foreach ((string when, string buyer, IReadOnlyList<long> cards, string why) in
                     _collaborator?.SalesThatBounced
                     ?? Array.Empty<(string, string, IReadOnlyList<long>, string)>())
            {
                json.WriteStartObject();
                json.WriteString("when", when);
                json.WriteString("buyer", buyer);
                json.WriteString("why", why);
                json.WriteStartArray("cards");
                foreach (long card in cards) json.WriteNumberValue(card);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteBoolean("over", _collaborator?.FestivalOver ?? false);

            json.WriteStartArray("custody");
            foreach (long card in _collaborator?.InCustody ?? Array.Empty<long>())
                json.WriteNumberValue(card);
            json.WriteEndArray();

            json.WriteStartArray("sold");
            foreach ((string who, string mark, long card) in
                     _collaborator?.Sold ?? Array.Empty<(string, string, long)>())
            {
                json.WriteStartObject();
                json.WriteString("who", who);
                json.WriteString("mark", mark);
                json.WriteNumber("card", card);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            // Las figuras de la ronda, cada una con su premio: es la MISMA superficie de
            // cantar que ve el organizador. Los ganadores no van -- ella todavia no se
            // entera de un bingo, y la pantalla prefiere no decir nada a decir que no
            // hay ganador cuando lo que pasa es que ella no sabe.
            json.WriteStartArray("games");
            foreach ((string figure, string prize, bool isCash) in
                     _collaborator?.RoundGames ?? Array.Empty<(string, string, bool)>())
            {
                json.WriteStartObject();
                json.WriteString("figure", figure);
                json.WriteString("prize", prize);
                json.WriteBoolean("cash", isCash);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            List<int> out_ = _collaborator is null ? new List<int>() : AlreadyOut();

            json.WriteStartArray("drawn");
            foreach (int n in out_) json.WriteNumberValue(n);
            json.WriteEndArray();

            // La ultima es la ultima de TODAS, no la ultima suya: los dos cantan sobre la
            // misma tombola y la bolita grande tiene que decir lo mismo en los dos
            // telefonos.
            json.WriteNumber("last", out_.Count > 0 ? out_[^1] : 0);

            // Cual esta abierta sale del COLABORADOR, no de la vista: volver a la puerta
            // no la saca del bingo, solo la trae al conmutador. Si dependiera de la
            // vista, al volver perderia de vista donde esta parada, que es justo lo que
            // vino a mirar.
            json.WriteStartArray("mine");
            WriteMine(json, _collaborator?.Festival?.FestivalId);
            json.WriteEndArray();

            json.WriteString("myQr", MyNumberQr());

            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private string RoundLine()
    {
        if (_collaborator is null || _collaborator.RoundPosition <= 0) return string.Empty;

        return Texts.T("colaborador.cabecera.ronda", ("n", _collaborator.RoundPosition.ToString()))
            + (_collaborator.RoundPrizeLine.Length > 0
                ? "  ·  " + _collaborator.RoundPrizeLine
                : string.Empty);
    }

    /// <summary>Las que ya conoce, para volver sin escanear otra vez. Una que no se pueda
    /// leer se salta: la lista tiene que abrir aunque una entrada vieja este rota.</summary>
    private static void WriteMine(Utf8JsonWriter json, string openNow)
    {
        IReadOnlyList<JoinedFestival> joined = LiveCollaborator.Joined();

        for (int at = 0; at < joined.Count; at++)
        {
            string name;
            string duty;
            string id;
            bool practice;

            try
            {
                OpenFestival one = OpenFestival.FromText(joined[at].Code);
                name = one.Name;
                duty = Duties.IsCaller(one.Duty) ? Duties.Calls : Duties.Sells;
                id = one.FestivalId;
                practice = RehearsalConductor.IsRehearsal(one);
            }
            catch (Exception)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteString("id", at.ToString());
            json.WriteString("name", name);
            json.WriteString("duty", duty);
            json.WriteBoolean("practice", practice);
            json.WriteBoolean("now",
                openNow is not null && string.Equals(id, openNow, StringComparison.Ordinal));
            json.WriteEndObject();
        }
    }

    private string MyNumberQr()
    {
        int mine = _collaborator?.MyNumber ?? 0;

        if (mine <= 0) return string.Empty;

        try
        {
            string code = ColleagueCode.For(_collaborator.Festival?.FestivalId, mine);

            return Convert.ToBase64String(QrImage.Png(code, 8));
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private void PushCode(string forWhom, string code)
    {
        _showingCode = true;

        code = Contract.OpenFestival.ForEveryone(code);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "code");
            json.WriteString("forWhom", string.IsNullOrWhiteSpace(forWhom) ? Texts.T("colaborador.esa-persona") : forWhom);
            json.WriteString("text", code);

            try
            {
                json.WriteString("png", Convert.ToBase64String(QrImage.Png(code, 8)));
            }
            catch (Exception)
            {
                json.WriteString("png", string.Empty);
            }

            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private void PushKnown(string name)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "known");
            json.WriteString("name", name ?? string.Empty);
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Le dice a la pagina que este codigo no trae nombre y que sin uno no se
    /// entra. Antes la pagina lo exigia siempre, incluso cuando el codigo lo traia; ahora
    /// lo pide solo cuando de verdad falta.</summary>
    private void PushNeedName() => PushRaw("{\"kind\":\"needname\"}");

    private void PushScanned(string what, string text)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "scanned");
            json.WriteString("what", what);
            json.WriteString("text", text ?? string.Empty);
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private void PushToast(string text)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "toast");
            json.WriteString("text", text);
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>El registro es el UNICO canal de error de esta pantalla: no hay un solo
    /// aviso de sistema. Por eso viaja tambien la ultima linea sola, para que el rotulo
    /// del plegable la arrastre, y si fue un "no" se pinte de rojo.</summary>
    private void PushLog()
    {
        string all;
        lock (_log) all = _log.ToString();

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "log");
            json.WriteString("text", all);
            json.WriteString("last", _lastSaid);
            json.WriteBoolean("bad", _lastWasBad);
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Como se dice cada figura. Quien canta ve las de la ronda con su dibujito,
    /// y el nombre no puede salir en el idioma del motor.</summary>
    private void PushFigures() => PushRaw(OnOffer.AsMessage());

    /// <summary>El catalogo del idioma de la persona. Va ANTES que las figuras y el
    /// estado: la pagina no adivina el idioma, se lo dice el telefono.</summary>
    private void PushTextos() => PushRaw(Texts.AsMessage());

    private void PushRaw(string json) => OnScreen(() => Web.SendRawMessage(json));

    private static readonly string[] SoundsBad =
    {
                // En los dos idiomas: la frase ya llega dicha en el de la persona.
        "no pude", "no me", "no entendi", "no entendí", "no logre", "no logré", "me falta", "no existe",
        "no sirve", "SE CAYO", "SE CAYÓ", "NO PUDE", "ya salio", "ya salió", "me dijeron que no",
        "I could not", "I did not", "I do not", "COULD NOT", "IT CRASHED", "does not exist",
        "no good to me", "already came out", "is missing", "did NOT", "Do NOT", "NOT accept",
    };

    /// <summary>Cuanto se calla un aviso malo que ya se dio igualito. Repetirlo no informa:
    /// asusta, y le asusta a quien esta atendiendo la fila.</summary>
    private static readonly TimeSpan SameWarningStaysQuiet = TimeSpan.FromMinutes(5);

    /// <summary>Los avisos malos que ya se gritaron, con la hora. Lo que se calla es el
    /// GRITO, no el aviso: al registro entran todos, siempre, porque ahi es donde se va a
    /// buscar despues.</summary>
    private readonly Dictionary<string, DateTime> _alreadyWarned = new(StringComparer.Ordinal);

    private void Say(string line)
    {
        Console.WriteLine("[BINGO] " + line);

        lock (_log)
        {
            _log.AppendLine(line);
            if (_log.Length > 30000) _log.Remove(0, _log.Length - 20000);
        }

        _lastSaid = line;

        bool bad = SoundsBad.Any(one => line.Contains(one, StringComparison.OrdinalIgnoreCase));

        // Un reintento que insiste dice lo mismo cada vuelta. La colaboradora veia el mismo
        // rojo cada treinta segundos toda la noche por un bingo que el organizador ya no
        // tenia abierto -- y un aviso que aparece solo, repetido y sin nada que hacer al
        // respecto, se lee como que algo se rompio.
        _lastWasBad = bad && NotSaidLately(line);

        PushLog();
    }

    private bool NotSaidLately(string line)
    {
        DateTime now = DateTime.UtcNow;

        lock (_alreadyWarned)
        {
            if (_alreadyWarned.TryGetValue(line, out DateTime when)
                && now - when < SameWarningStaysQuiet)
            {
                return false;
            }

            // Se limpia lo viejo al pasar, para no ir juntando renglones toda la noche.
            foreach (string old in _alreadyWarned
                         .Where(each => now - each.Value >= SameWarningStaysQuiet)
                         .Select(each => each.Key)
                         .ToList())
            {
                _alreadyWarned.Remove(old);
            }

            _alreadyWarned[line] = now;

            return true;
        }
    }

    private static void OnScreen(Action what)
    {
        if (MainThread.IsMainThread) what();
        else MainThread.BeginInvokeOnMainThread(what);
    }
}
