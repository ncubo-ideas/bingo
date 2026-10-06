using System.Text;
using System.Text.Json;
using BingoEngine;
using BingoApp.CommunityLeader;
using BingoApp.Contract;

namespace BingoApp;

/// <summary>
/// El detalle de un bingo: el hub del organizador. La pantalla es una pagina
/// (funcion.html) y este code-behind es el puente. Cada gesto termina en un metodo de
/// LiveCommunityLeader que ya existia; lo unico nuevo es el retrato en JSON.
/// </summary>
public partial class FestivalScreen : ContentPage
{
    /// <summary>Las mismas figuras curadas que ofrece el wizard, por la misma razon:
    /// el catalogo del motor trae ademas las piezas internas.</summary>
    private readonly StringBuilder _log = new();
    private readonly string _festivalId;

    /// <summary>El PNG del ultimo QR de cada oficio. El retrato se manda en CADA cambio
    /// -- cada bolita, cada venta -- y encodear dos QR cada vez es caro y ademas inutil:
    /// el codigo solo cambia cuando alguien usa el anterior.</summary>
    private readonly Dictionary<string, (string Link, string Png)> _codes = new(StringComparer.Ordinal);

    private LiveCommunityLeader _communityLeader;

    public FestivalScreen(string festivalId)
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

        PushFigures();
        Refresh();
    }

    protected override void OnDisappearing()
    {
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
        string[] parts = (e.Message ?? string.Empty).Split('|', 2);

        switch (parts[0])
        {
            case "ready":
                PushTextos();
                PushFigures();
                Refresh();
                break;

            case "idioma":
                if (parts.Length > 1) AppLanguage.Choose(parts[1]);
                PushTextos();
                PushFigures();
                Refresh();
                PushLog();
                break;

            case "leave":
                OnScreen(async () => await Navigation.PopAsync());
                break;

            case "call":
                OnScreen(async () => await Navigation.PushAsync(new CallerScreen(_festivalId)));
                break;

            case "more":
                PutMore();
                break;

            case "sendhome":
                if (parts.Length > 1) SendHome(parts[1]);
                break;

            case "handmore":
                if (parts.Length > 1) HandMore(parts[1]);
                break;

            case "takeback":
                if (parts.Length > 1) TakeBack(parts[1]);
                break;

            case "movecards":
                if (parts.Length > 1) MoveCards(parts[1]);
                break;

            case "addround":
                if (parts.Length > 1) AddRound(parts[1]);
                break;

            case "copy":
                if (parts.Length > 1) Copy(parts[1]);
                break;

            case "entrust":
                if (parts.Length > 1) Entrust(parts[1]);
                break;

            case "share":
                if (parts.Length > 1) Share(parts[1]);
                break;

            case "poster":
                PushPoster();
                break;

            case "price":
                if (parts.Length > 1) Price(parts[1]);
                break;

            case "announce":
                if (parts.Length > 1) Announce(parts[1]);
                break;

            case "doorsat":
                if (parts.Length > 1) DoorsAt(parts[1]);
                break;

            case "wantlog":
                PushLog();
                break;
        }
    }

    private void PutMore() => Task.Run(() =>
    {
        try
        {
            IReadOnlyList<long> fresh = _communityLeader.PutMoreUpForSale(_festivalId, 10);

            PushToast(Texts.T("organizador.log.cartones-mas-en-venta", ("n", fresh.Count.ToString())));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-poner-mas", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void HandMore(string what) => Task.Run(async () =>
    {
        (string mark, int howMany) = MarkAndHowMany(what);

        try
        {
            await _communityLeader.HandMoreAsync(_festivalId, mark, howMany);

            PushToast(Texts.T("organizador.log.le-entregaste", ("n", howMany.ToString())));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-entregar-mas", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void TakeBack(string what) => Task.Run(async () =>
    {
        (string mark, int howMany) = MarkAndHowMany(what);

        try
        {
            await _communityLeader.TakeBackAsync(_festivalId, mark, howMany);

            PushToast(Texts.T("organizador.log.le-recogiste", ("n", howMany.ToString())));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-recoger", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void MoveCards(string what) => Task.Run(async () =>
    {
        string[] parts = what.Split('|');

        if (parts.Length < 3 || !int.TryParse(parts[2], out int howMany))
        {
            Say(Texts.T("organizador.log.no-entendi-movimiento"));
            return;
        }

        try
        {
            await _communityLeader.MoveCardsAsync(_festivalId, parts[0], parts[1], howMany);

            PushToast(Texts.T("organizador.log.pasaste", ("n", howMany.ToString())));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-mover", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    /// <summary>Una marca y un cuantos. El cuantos por omision es 3 porque los codigos de
    /// antes de que se pudiera elegir mandaban la marca sola.</summary>
    private static (string Mark, int HowMany) MarkAndHowMany(string what)
    {
        string[] parts = (what ?? string.Empty).Split('|');

        return (parts[0],
            parts.Length > 1 && int.TryParse(parts[1], out int said) && said > 0 ? said : 3);
    }

    private void SendHome(string mark) => Task.Run(async () =>
    {
        try
        {
            await _communityLeader.SendHomeAsync(_festivalId, mark);

            PushToast(Texts.T("organizador.log.ya-no-puede"));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-mandar-a-casa", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    /// <summary>Anotar a una persona y sacarle su codigo, que se le ensena de una: el
    /// organizador la anoto para pasarselo, no para verla en una lista.</summary>
    private void Entrust(string what) => Task.Run(() =>
    {
        string[] parts = what.Split('|');

        if (parts.Length < 2)
        {
            Say(Texts.T("organizador.log.no-entendi-a-quien"));
            return;
        }

        string duty = parts[0];
        string name = parts[1];
        int cards = parts.Length > 2 && int.TryParse(parts[2], out int said) ? said : 0;

        try
        {
            OpenFestival open = _communityLeader.EntrustTo(_festivalId, duty, name, cards);

            PushState();
            PushCode(open);
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-anotar-a", ("nombre", name), ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    /// <summary>Volver a ensenar el codigo de alguien que ya esta anotado.</summary>
    private void Share(string mark) => Task.Run(() =>
    {
        try
        {
            PushCode(_communityLeader.QrForMark(_festivalId, mark));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-sacar-codigo", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void Price(string said) => Task.Run(() =>
    {
        if (!long.TryParse(said, out long price) || price <= 0)
        {
            Say(Texts.T("organizador.log.no-entendi-precio"));
            return;
        }

        try
        {
            _communityLeader.PriceTheCards(_festivalId, price);
            PushToast(Texts.T("organizador.log.carton-vale", ("precio", price.ToString())));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-poner-precio", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    /// <summary>La hora llega escrita "HH:mm", como la manda la pagina y como la guarda
    /// el pueblo: partirla aca es el unico lugar donde hace falta.</summary>
    private void DoorsAt(string said) => Task.Run(() =>
    {
        string[] parts = (said ?? string.Empty).Split(':');

        if (parts.Length != 2
            || !int.TryParse(parts[0], out int hour)
            || !int.TryParse(parts[1], out int minute))
        {
            Say(Texts.T("organizador.log.no-entendi-hora"));
            return;
        }

        try
        {
            _communityLeader.OpenTheDoorsAt(_festivalId, hour, minute);
            PushToast(Texts.T("organizador.log.abre-a-las", ("hora", said)));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-poner-hora", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void Announce(string how) => Task.Run(() =>
    {
        try
        {
            _communityLeader.AnnounceWinsBy(_festivalId, how);
            PushToast(Texts.T(string.Equals(how, "Automatic", StringComparison.Ordinal)
                ? "organizador.log.anuncio-automatico"
                : "organizador.log.anuncio-gente"));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-elegir-anuncio", ("motivo", Texts.Explain(failure))));
            PushToast(Texts.Explain(failure));
        }
    });

    private void AddRound(string plan)
    {
        List<PromisePlan> promises = new();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(plan);

            foreach (JsonElement game in doc.RootElement.GetProperty("games").EnumerateArray())
            {
                promises.Add(new PromisePlan
                {
                    Figure = game.GetProperty("figure").GetString(),
                    IsCash = game.GetProperty("cash").GetBoolean(),
                    Prize = game.GetProperty("prize").GetString(),
                });
            }
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-entendi-ronda", ("motivo", Texts.Explain(failure))));
            return;
        }

        Task.Run(() =>
        {
            try
            {
                _communityLeader.AnnounceAnotherRound(_festivalId,
                    new RoundPlan { Promises = promises });

                PushToast(Texts.T("organizador.log.ronda-anunciada"));
            }
            catch (Exception failure)
            {
                Say(Texts.T("organizador.log.no-pude-anunciar-ronda", ("motivo", Texts.Explain(failure))));
                PushToast(Texts.Explain(failure));
            }
        });
    }

    private void Copy(string link) => OnScreen(async () =>
    {
        try
        {
            await Clipboard.SetTextAsync(link);
            PushToast(Texts.T("organizador.log.codigo-copiado"));
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-pude-copiar", ("motivo", Texts.Explain(failure))));
        }
    });

    // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

    /// <summary>Cuales figuras existen y como se dicen. El dibujito lo sabe la pagina;
    /// el nombre viene de aca porque el telefono tambien arma frases con el.</summary>
    private void PushFigures() => PushRaw(OnOffer.AsMessage());

    /// <summary>El catalogo del idioma de la persona. Va ANTES que las figuras y el
    /// estado: la pagina no adivina el idioma, se lo dice el telefono.</summary>
    private void PushTextos() => PushRaw(Texts.AsMessage());

    private void PushState()
    {
        IReadOnlyList<RoundStatus> rounds = _communityLeader.RoundsOf(_festivalId);
        IReadOnlyList<HandedOut> team = _communityLeader.ErrandsOf(_festivalId);
        IReadOnlyList<Attendee> people = _communityLeader.AttendeesOf(_festivalId);
        (int forSale, int left) = _communityLeader.HowManyForSale(_festivalId);

        // QrFor y no MarkedQrFor: el segundo CORTA un papelito si no hay ninguno en la
        // mesa, y llamarlo desde el retrato le cortaba uno a cada latido de la pantalla.
        // Ahora los papelitos se cortan cuando el organizador anota a alguien, y nada mas.
        OpenFestival festival = _communityLeader.QrFor(_festivalId);

        List<Attendee> players = people.Where(p => !p.IsCollaborator).ToList();

        // "Quienes juegan" son DOS mitades: los que entraron con su telefono (alistados)
        // y los que compraron y todavia no entran, que el diario conoce por la venta.
        // Antes solo viajaba la primera, y la pantalla prometia que la venta los metia.
        List<(string Name, IReadOnlyList<long> Cards, bool Entered)> playing = new();

        foreach (Attendee who in players)
            playing.Add((who.Name, who.Cards, true));

        foreach ((string buyer, IReadOnlyList<long> cards) in _communityLeader.BuyersOf(_festivalId))
        {
            if (playing.Any(p => string.Equals(p.Name, buyer, StringComparison.OrdinalIgnoreCase)))
                continue;

            playing.Add((buyer, cards, false));
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "state");
            json.WriteString("name", festival.Name);
            json.WriteString("where", Texts.T("organizador.estas-en", ("url", _communityLeader.MyUrl)));
            json.WriteNumber("sold", _communityLeader.SoldCount(_festivalId));
            json.WriteNumber("players", playing.Count);
            json.WriteNumber("draws", _communityLeader.Drawn(_festivalId).Count);
            json.WriteNumber("forSale", forSale);
            json.WriteNumber("left", left);

            // La configuracion y la agenda, del diario: el detalle las pinta y ofrece
            // cambiar lo que se pueda. El precio en cero significa "sin precio"; el modo
            // vacio significa el de siempre, que es que lo canta la gente.
            (long cardPrice, string winsAnnounced, string playsOn, string doorsOpenAt) =
                _communityLeader.SettingsOf(_festivalId);

            json.WriteNumber("price", cardPrice);
            json.WriteString("announce", winsAnnounced);
            json.WriteString("playsOn", playsOn);
            json.WriteString("doorsAt", doorsOpenAt);

            // Lo que queda en el libro, por numero: es de donde sale lo que el organizador
            // le aparta a cada colaborador, y el tramo es como lo piensa.
            json.WriteStartArray("free");
            foreach (long card in _communityLeader.StillInTheBook(_festivalId))
                json.WriteNumberValue(card);
            json.WriteEndArray();
            json.WriteNumber("rounds", rounds.Count);
            json.WriteNumber("prizes", rounds.Sum(r => r.Promises.Count));
            json.WriteNumber("currentRound",
                rounds.FirstOrDefault(r => r.IsPlaying)?.Position ?? 0);
            json.WriteBoolean("over", _communityLeader.IsOver(_festivalId));

            json.WriteStartArray("roundList");
            foreach (RoundStatus round in rounds)
            {
                json.WriteStartObject();
                json.WriteNumber("position", round.Position);
                json.WriteString("standing", round.Standing);

                json.WriteStartArray("games");
                foreach (PromiseStatus promise in round.Promises)
                {
                    json.WriteStartObject();
                    json.WriteString("figure", promise.Playing);
                    json.WriteString("prize", promise.Prize);
                    json.WriteBoolean("cash", promise.IsCash);

                    json.WriteStartArray("winners");
                    foreach (long card in promise.Winners) json.WriteNumberValue(card);
                    json.WriteEndArray();

                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("team");
            foreach (HandedOut one in team)
            {
                // Lo que tiene en la mano sale de la memoria del organizador, que es
                // donde vive la custodia en vivo; si el se reinicio, la lista se rearma
                // del diario y el papelito igual se puede administrar.
                Attendee mine = people.FirstOrDefault(p =>
                    string.Equals(p.Mark, one.Mark, StringComparison.Ordinal));

                json.WriteStartObject();
                json.WriteString("mark", one.Mark);
                json.WriteString("duty", one.Duty);
                json.WriteString("standing", one.Standing);
                json.WriteString("takenBy", one.TakenBy);
                json.WriteString("meantFor", one.MeantFor);
                json.WriteBoolean("elseTook", one.SomebodyElse);
                json.WriteNumber("cardsMeant", one.CardsMeant);
                json.WriteNumber("cards", mine?.Cards.Count ?? 0);
                json.WriteNumber("sold", mine?.Sold.Count ?? 0);

                // Los numeros que tiene en la mano, no solo cuantos: la pantalla los dice
                // por tramo ("tiene 7-20"), que es como los reparte y como los pregunta.
                json.WriteStartArray("hand");
                foreach (long card in mine?.Cards ?? new List<long>())
                    json.WriteNumberValue(card);
                json.WriteEndArray();

                // 'aca' es ALCANZABLE, no 'figura en la lista': despues de un reinicio la
                // lista se rearma del diario, pero la direccion del telefono no esta ahi.
                // Entregarle cartones a quien no escucha los manda a un limbo del que no
                // vuelven, asi que el boton solo aparece cuando de verdad se le puede hablar.
                json.WriteBoolean("here", !string.IsNullOrWhiteSpace(mine?.ListensAt));
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("playerList");
            foreach ((string name, IReadOnlyList<long> cards, bool entered) in playing)
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteBoolean("entered", entered);

                json.WriteStartArray("cards");
                foreach (long card in cards) json.WriteNumberValue(card);
                json.WriteEndArray();

                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>
    /// El codigo de UNA persona, cuando el organizador lo pide. El QR viaja como PNG en
    /// base64 porque la pagina no tiene con que dibujarlo, y el enlace entero va aparte
    /// para poder copiarlo y mandarlo por mensaje.
    ///
    /// Se manda cuando se pide y no adentro del retrato: el retrato late seguido, y meter
    /// el PNG de cada colaborador en cada latido es mandar la pantalla entera de vuelta
    /// cien veces sin que nadie la este mirando.
    /// </summary>
    private void PushCode(OpenFestival open)
    {
        // Firmado si el organizador tiene su llave; sin firma si no. Los dos son codigos
        // validos: la firma es OTRA opcion del link, no un requisito del formato.
        string link = _communityLeader?.Certificate is null
            ? open.ToLink()
            : FestivalSeal.SignedLink(open, _communityLeader.Certificate);

        string mark = open.Mark ?? string.Empty;

        if (!_codes.TryGetValue(mark, out (string Link, string Png) had)
            || !string.Equals(had.Link, link, StringComparison.Ordinal))
        {
            had = (link, Convert.ToBase64String(QrImage.Png(link, 8)));
            _codes[mark] = had;
        }

        bool calls = Duties.IsCaller(open.Duty);
        string who = string.IsNullOrWhiteSpace(open.MeantFor) ? Texts.T("organizador.codigo.esta-persona") : open.MeantFor;

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "code");
            json.WriteString("forWhom", who);
            json.WriteString("says", Texts.T(calls
                ? "organizador.codigo.que-lo-escanee-canta"
                : "organizador.codigo.que-lo-escanee-vende"));
            json.WriteString("text", link);
            json.WriteString("png", had.Png);
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private void PushPoster()
    {
        string site = "https://" + Transport.PhoneToPhone.WebPlayerSite + "/";

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "code");
            json.WriteString("title", Texts.T("organizador.cartel.titulo"));
            json.WriteString("says", Texts.T("organizador.cartel.que-dice"));
            json.WriteString("text", site);
            json.WriteString("png", Convert.ToBase64String(QrImage.Png(site, 10)));
            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
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
