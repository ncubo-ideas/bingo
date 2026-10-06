using BingoApp.CommunityLeader;
using BingoApp.Contract;

namespace BingoApp.Rehearsal;

/// <summary>
/// El dueño del ciclo de vida del ensayo. Levanta un organizador invisible EN ESTE
/// telefono, crea el "Bingo de ensayo" y devuelve un codigo real que apunta a loopback:
/// de ahi en adelante todo es el flujo de siempre -- la puerta, la venta, el canto --
/// contra un organizador que da la casualidad de que vive en el mismo proceso.
///
/// La regla que marca un ensayo es la URL: un bingo cuyo organizador atiende en
/// 127.0.0.1 no puede ser de verdad, porque un organizador real publica su IP de wlan.
/// Con eso alcanza para la franja, la pastilla y el borrado -- sin llave nueva en el
/// QR y sin migrar los codigos guardados.
/// </summary>
public static class RehearsalConductor
{
    private const string LoopbackHost = "127.0.0.1";

    /// <summary>Con cuantos cartones se practica. Pocos a proposito: alcanzan para
    /// vender varias veces y para que un carton complete rapido cuando se canta.</summary>
    private const int Cards = 30;

    /// <summary>El vigia del ensayo del cantor: el bingo de que ensayo cuida, sus
    /// sembrados, y el aviso hacia la pantalla. Uno solo, porque hay un ensayo a la
    /// vez -- Start lo arma y Finish lo desarma.</summary>
    private static Action _watching;

    private static string _watchedId;

    private static IReadOnlyList<(string Buyer, long Card)> _seeded;

    private static Action<string> _celebrate;

    private static int _settling;

    public static bool IsRehearsal(OpenFestival festival) =>
        festival is not null && IsRehearsalUrl(festival.CommunityLeaderUrl);

    public static bool IsRehearsalUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri where)
            && (string.Equals(where.Host, LoopbackHost, StringComparison.Ordinal)
                || string.Equals(where.Host, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Arma el ensayo entero y devuelve el codigo con el que se entra, como si el
    /// papelito hubiera llegado de afuera. Una ronda con una figura y premio en
    /// efectivo: lo minimo que hace que la noche se sienta completa.
    /// </summary>
    public static string Start(string duty, string whoPractices, Action<string> say,
        Action<string> celebrate = null)
    {
        LiveCommunityLeader leader = LiveCommunityLeader.Instance;

        leader.TalkTo(line => say(Texts.T("organizador.ensayo.etiqueta") + " " + line));

        if (!leader.Started) leader.Start(CancellationToken.None);

        List<RoundPlan> rounds = new()
        {
            new RoundPlan
            {
                Promises = new List<PromisePlan>
                {
                    new PromisePlan { Figure = "AnyLine", IsCash = true, Prize = "50000" },
                },
            },
        };

        OpenFestival born = leader.CreateFestival(Texts.T("organizador.ensayo.nombre"), rounds, Cards, 2000, DateTime.Now);

        // Que el bingo se frene solo cuando un carton complete: el que ensaya a cantar
        // vive el ciclo entero -- cantar, el freno, revisar -- sin telefonos de
        // jugadores. Al que ensaya a vender no le toca: sin cartones jugando, no frena.
        leader.AnnounceWinsBy(born.FestivalId, "Automatic");

        // El publico inventado del cantor: Rita y Beto ya tienen carton y juegan solos.
        // Cuando una bolita complete la figura de uno, la mesa se frena -- y eso ES el
        // ensayo: cantar, el freno, y seguir. El vendedor no lo necesita: su publico
        // son los nombres que teclea al vender.
        if (Duties.IsCaller(duty))
        {
            IReadOnlyList<(string, long)> seeded =
                leader.SeedTheAudience(born.FestivalId, new[] { "Rita", "Beto" });

            WatchTheTable(leader, born.FestivalId, seeded, say, celebrate);
        }

        OpenFestival paper = leader.EntrustTo(born.FestivalId, duty, whoPractices, Cards);

        // El codigo apunta a ESTE telefono. Se re-serializa sin firma, que es un codigo
        // valido de siempre: la firma original cubria la URL de wlan, no esta.
        paper.CommunityLeaderUrl = new UriBuilder(leader.MyUrl) { Host = LoopbackHost }
            .Uri.GetLeftPart(UriPartial.Authority);

        return paper.ToLink();
    }

    /// <summary>
    /// El ensayo sobrevive a cerrar la app: volver es tocarlo en la lista. Pero el
    /// organizador invisible NO sobrevive -- vive en el proceso -- asi que antes de
    /// volver a entrar hay que levantarlo de nuevo: arrancar el village (que reabre
    /// el diario del ensayo desde su carpeta) y, si el ensayo es de cantar, rearmar
    /// el vigia con los sembrados que el diario recuerda. Si la carpeta ya no existe,
    /// el ensayo murio de verdad y se avisa para que la fila se olvide.
    /// </summary>
    public static void EnsureAlive(OpenFestival festival, Action<string> say,
        Action<string> celebrate = null)
    {
        if (!IsRehearsal(festival)) return;

        LiveCommunityLeader leader = LiveCommunityLeader.Instance;

        leader.TalkTo(line => say(Texts.T("organizador.ensayo.etiqueta") + " " + line));

        if (!leader.Started) leader.Start(CancellationToken.None);

        if (!leader.Festivals.Contains(festival.FestivalId))
            throw new InvalidOperationException(
                Texts.T("organizador.no.ensayo-ya-borrado"));

        if (!Duties.IsCaller(festival.Duty)) return;
        if (string.Equals(_watchedId, festival.FestivalId, StringComparison.Ordinal)) return;

        // Los sembrados vuelven del diario, no de la memoria: SoldTo se rehidrata al
        // abrir la carpeta, y cada comprador del ensayo tiene un solo carton.
        List<(string Buyer, long Card)> seeded = new();

        foreach ((string buyer, IReadOnlyList<long> cards) in leader.BuyersOf(festival.FestivalId))
            if (cards.Count > 0) seeded.Add((buyer, cards[0]));

        if (seeded.Count > 0)
            WatchTheTable(leader, festival.FestivalId, seeded, say, celebrate);
    }

    /// <summary>
    /// El vigia de la mesa del cantor. En la vida real, cuando la mesa se frena el
    /// ORGANIZADOR revisa el carton y entrega el premio; en el ensayo el organizador
    /// es invisible, asi que ese papel lo hace el conductor: detecta el freno, le da
    /// el premio a quien completo -- la mesa es la autoridad y rechaza al que no --
    /// y se lo cuenta al que ensaya. Sin esto el freno seria una carcel: nadie
    /// podria validar y el canto no seguiria nunca.
    /// </summary>
    private static void WatchTheTable(LiveCommunityLeader leader, string festivalId,
        IReadOnlyList<(string Buyer, long Card)> seeded, Action<string> say,
        Action<string> celebrate)
    {
        StopWatching(leader);

        _watchedId = festivalId;
        _seeded = seeded;
        _celebrate = celebrate ?? (_ => { });
        _watching = () => Task.Run(() => SettleIfFrozen(leader, say));

        leader.Changed += _watching;

        // Y una mirada apenas armado: si el ensayo revive con la mesa YA frenada (la
        // app se cerro justo despues de un bingo), no va a llegar ningun aviso que lo
        // despierte -- los cantos contra una mesa frenada rebotan sin avisar.
        _watching();
    }

    private static void StopWatching(LiveCommunityLeader leader)
    {
        if (_watching is not null) leader.Changed -= _watching;

        _watching = null;
        _watchedId = null;
        _seeded = null;
        _celebrate = null;
    }

    private static void SettleIfFrozen(LiveCommunityLeader leader, Action<string> say)
    {
        // Un arreglo a la vez: cada canto dispara un aviso y no hace falta que cada
        // aviso revise la mesa de nuevo mientras uno ya la esta revisando.
        if (Interlocked.Exchange(ref _settling, 1) == 1) return;

        try
        {
            string id = _watchedId;
            IReadOnlyList<(string Buyer, long Card)> seeded = _seeded;

            if (id is null || seeded is null) return;
            if (!leader.TableAwaitsValidation(id)) return;

            foreach ((string buyer, long card) in seeded)
            {
                try
                {
                    // La mesa es la autoridad: si este carton no completo, revienta
                    // y se prueba el siguiente. El que pasa, gano de verdad.
                    leader.GiveThePrize(id, card, "AnyLine");

                                        _celebrate?.Invoke(Texts.T("organizador.ensayo.celebracion", ("nombre", buyer), ("figura", OnOffer.Called("AnyLine")), ("carton", card.ToString())));

                    return;
                }
                catch (Exception)
                {
                    // No era su carton; el siguiente sembrado puede ser el que gano.
                }
            }

            say(Texts.T("organizador.log.ensayo-raro"));
        }
        catch (Exception failure)
        {
            say(Texts.T("organizador.log.vigia-tropezo", ("motivo", Texts.Explain(failure))));
        }
        finally
        {
            _settling = 0;
        }
    }

    /// <summary>Borra el ensayo entero de este telefono. El desmonte vive en el
    /// organizador -- el diario se apaga ANTES de tocar el disco.</summary>
    public static void Finish(string festivalId)
    {
        LiveCommunityLeader leader = LiveCommunityLeader.Instance;

        if (string.Equals(_watchedId, festivalId, StringComparison.Ordinal))
            StopWatching(leader);

        leader.StrikeTheFestival(festivalId);
    }
}
