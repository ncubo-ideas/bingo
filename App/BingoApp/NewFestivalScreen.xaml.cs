using System.Text;
using System.Text.Json;
using BingoEngine;
using BingoApp.CommunityLeader;
using BingoApp.Contract;

namespace BingoApp;

/// <summary>
/// Armar un bingo, un paso a la vez. La pantalla es una pagina (organizer.html) y
/// este code-behind es el puente: le pasa el catalogo de figuras del motor, recibe el
/// plan armado y lo traduce a lo que el village ya sabe crear.
///
/// Nada se asienta hasta el ultimo paso: el diario nace de una sola vez, ya con su
/// agenda, asi que arrepentirse a mitad de camino no deja bingos a medio nacer.
/// </summary>
public partial class NewFestivalScreen : ContentPage
{
    private readonly StringBuilder _log = new();

    private LiveCommunityLeader _communityLeader;
    private bool _creating;

    /// <summary>El bingo recien creado, mientras la pantalla de exito espera que el
    /// organizador diga si entra o se va al inicio.</summary>
    private string _born;

    public NewFestivalScreen() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _communityLeader = LiveCommunityLeader.Instance;
        _communityLeader.TalkTo(Say);
    }

    // ── lo que la pagina le pide al telefono ─────────────────────────────────

    private void OnWebMessage(object sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        string[] parts = (e.Message ?? string.Empty).Split('|', 2);

        switch (parts[0])
        {
            case "ready":
                PushTextos();
                PushFigures();
                break;

            case "idioma":
                if (parts.Length > 1) AppLanguage.Choose(parts[1]);
                PushTextos();
                PushFigures();
                PushLog();
                break;

            case "say":
                if (parts.Length > 1) Say(parts[1]);
                break;

            case "wantlog":
                PushLog();
                break;

            case "leave":
                OnScreen(async () => await Navigation.PopAsync());
                break;

            case "create":
                if (parts.Length > 1) Create(parts[1]);
                break;

            case "open":
                Enter();
                break;
        }
    }

    /// <summary>Cuales figuras existen. Como se dicen y como se dibujan lo sabe la
    /// pagina: al telefono le toca decir cuales el motor reconoce.</summary>
    private void PushFigures() => PushRaw(OnOffer.AsMessage());

    /// <summary>El catalogo del idioma de la persona. Va ANTES que las figuras y el
    /// estado: la pagina no adivina el idioma, se lo dice el telefono.</summary>
    private void PushTextos() => PushRaw(Texts.AsMessage());

    /// <summary>El plan entero llega de una sola vez, ya armado. Aca se traduce a lo
    /// que el village entiende y se crea; si algo no cuadra, la pagina se entera por el
    /// registro y vuelve a habilitar el boton.</summary>
    private void Create(string plan)
    {
        if (_creating) return;

        string name;
        int howManyCards;
        long cardPrice = 0;
        DateTime? playsOn = null;
        string doorsOpenAt = string.Empty;
        List<RoundPlan> rounds = new();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(plan);
            JsonElement root = doc.RootElement;

            name = root.GetProperty("name").GetString();
            howManyCards = root.GetProperty("cards").GetInt32();

            // El dia que se juega y la hora que abre. Los dos opcionales: sin dia el
            // bingo es de hoy, y sin hora simplemente no se dice a que hora.
            if (root.TryGetProperty("date", out JsonElement day)
                && DateTime.TryParse(day.GetString(), out DateTime saidDay))
            {
                playsOn = saidDay;
            }

            if (root.TryGetProperty("time", out JsonElement hour))
                doorsOpenAt = hour.GetString() ?? string.Empty;

            // El precio es opcional en el wizard: un bingo puede nacer sin el y
            // ponerselo despues desde su configuracion.
            if (root.TryGetProperty("price", out JsonElement said)
                && said.TryGetInt64(out long saidPrice) && saidPrice > 0)
            {
                cardPrice = saidPrice;
            }

            foreach (JsonElement round in root.GetProperty("rounds").EnumerateArray())
            {
                List<PromisePlan> promises = new();

                foreach (JsonElement game in round.GetProperty("games").EnumerateArray())
                {
                    promises.Add(new PromisePlan
                    {
                        Figure = game.GetProperty("figure").GetString(),
                        IsCash = game.GetProperty("cash").GetBoolean(),
                        Prize = game.GetProperty("prize").GetString(),
                    });
                }

                rounds.Add(new RoundPlan { Promises = promises });
            }
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.no-entendi-lo-que-arme", ("motivo", Texts.Explain(failure))));
            PushFailed();
            return;
        }

        _creating = true;

        Task.Run(() =>
        {
            try
            {
                if (!_communityLeader.Started) _communityLeader.Start(CancellationToken.None);

                OpenFestival open = _communityLeader.CreateFestival(
                    name, rounds, howManyCards, cardPrice, playsOn, doorsOpenAt);

                // Ya no se navega solo. Despues de crear hay dos caminos distintos --
                // entrar a repartir cartones, o dejarlo listo para despues -- y elegirlo
                // es del organizador: la pagina muestra el exito y avisa cual quiso.
                _born = open.FestivalId;
                PushMade();
            }
            catch (Exception failure)
            {
                Failed(failure);

                _creating = false;
                PushFailed();
            }
        });
    }

    // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

    private void PushFailed() => PushRaw("{\"kind\":\"failed\"}");

    private void PushMade() => PushRaw("{\"kind\":\"made\"}");

    /// <summary>Entrar al bingo que se acaba de crear. Primero se empuja el hub y DESPUES
    /// se saca esta pagina: sacarse primero deja al Shell sin pagina desde donde empujar,
    /// y el push no pasa -- se vuelve a la cartelera como si nada.</summary>
    private void Enter()
    {
        if (string.IsNullOrEmpty(_born)) return;

        OnScreen(async () =>
        {
            await Navigation.PushAsync(new FestivalScreen(_born));
            Navigation.RemovePage(this);
        });
    }

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
