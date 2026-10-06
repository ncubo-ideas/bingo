using System.Text;
using System.Text.Json;
using BingoApp.CommunityLeader;
using BingoApp.Contract;

namespace BingoApp;

/// <summary>
/// El inicio del organizador. La pantalla es una pagina (home.html) y este code-behind
/// es el puente: le manda un retrato de la cartelera cada vez que algo cambia, y recibe
/// los gestos. Nada de esto sabe de bingo -- todo sale de Listing(), que le pregunta a
/// cada bingo por su propio diario.
/// </summary>
public partial class FestivalsScreen : ContentPage
{
    private readonly StringBuilder _log = new();

    private LiveCommunityLeader _communityLeader;

    public FestivalsScreen() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _communityLeader = LiveCommunityLeader.Instance;
        _communityLeader.TalkTo(Say);
        _communityLeader.Changed += OnChanged;

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
        try
        {
            if (!_communityLeader.Started) _communityLeader.Start(CancellationToken.None);

            PushState();
        }
        catch (Exception failure)
        {
            Failed(failure);
        }
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

            case "new":
                OnScreen(async () => await Navigation.PushAsync(new NewFestivalScreen()));
                break;

            case "open":
                if (parts.Length > 1)
                {
                    string id = parts[1];
                    OnScreen(async () => await Navigation.PushAsync(new FestivalScreen(id)));
                }
                break;

            case "ensayo":
                MakeRehearsal();
                break;

            case "ensayofin":
                if (parts.Length > 1) EndRehearsal(parts[1]);
                break;

            case "papel":
                // Volver a la portada: organizar no es lo unico que hace esta persona.
                // Hasta ahora la unica salida era el boton atras del sistema, repetido.
                OnScreen(async () => await Navigation.PopToRootAsync());
                break;

            case "wantlog":
                PushLog();
                break;
        }
    }

    // ── el ensayo del organizador ────────────────────────────────────────────

    /// <summary>
    /// Un bingo de ensayo: se arma y se juega igual que uno real, pero nada cuenta.
    ///
    /// Aca el ensayo NO se reconoce por loopback como el del colaborador -- este bingo es
    /// suyo y nadie entra por codigo --, asi que el telefono se anota cuales de sus bingos
    /// son de practica. Vive en Preferences y no en el diario: que un bingo sea ensayo es
    /// cosa de este telefono, no del pueblo.
    /// </summary>
    private void MakeRehearsal() => OnScreen(() =>
    {
        try
        {
            if (!_communityLeader.Started) _communityLeader.Start(CancellationToken.None);

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

            OpenFestival born = _communityLeader.CreateFestival(
                Texts.T("organizador.ensayo.nombre"), rounds, 30, 2000, DateTime.Now);

            RememberRehearsal(born.FestivalId);

            Say(Texts.T("organizador.log.ensayo-listo"));
            Refresh();
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.ensayo-no-armado", ("motivo", Texts.Explain(failure))));
        }
    });

    /// <summary>Borra el ensayo entero: su diario se apaga y su carpeta se va.</summary>
    private void EndRehearsal(string festivalId) => OnScreen(() =>
    {
        try
        {
            _communityLeader.StrikeTheFestival(festivalId);
            ForgetRehearsal(festivalId);

            Say(Texts.T("organizador.log.ensayo-borrado"));
            Refresh();
        }
        catch (Exception failure)
        {
            Say(Texts.T("organizador.log.ensayo-no-borrado", ("motivo", Texts.Explain(failure))));
        }
    });

    private const string RehearsalsKey = "organizador.ensayos";

    public static IReadOnlyList<string> Rehearsals()
    {
        string raw = Preferences.Default.Get(RehearsalsKey, string.Empty);

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    private static void RememberRehearsal(string id)
    {
        List<string> mine = Rehearsals().ToList();

        if (!mine.Contains(id)) mine.Add(id);

        Preferences.Default.Set(RehearsalsKey, string.Join(",", mine));
    }

    private static void ForgetRehearsal(string id) =>
        Preferences.Default.Set(RehearsalsKey,
            string.Join(",", Rehearsals().Where(one =>
                !string.Equals(one, id, StringComparison.Ordinal))));

    // ── lo que el telefono le cuenta a la pagina ─────────────────────────────

    /// <summary>Como se dice cada figura. Va antes del estado: la cartelera arma la linea
    /// "se juega ..." con estos nombres, y sin ellos la escribe con el del motor.</summary>
    private void PushFigures() => PushRaw(OnOffer.AsMessage());

    /// <summary>El catalogo del idioma de la persona. Va ANTES que las figuras y el
    /// estado: la pagina no adivina el idioma, se lo dice el telefono.</summary>
    private void PushTextos() => PushRaw(Texts.AsMessage());

    /// <summary>El retrato de la cartelera. Los premios se cuentan sumando los juegos de
    /// cada ronda: es lo que el organizador quiere ver de un vistazo, cuanto tiene
    /// prometido esta noche.</summary>
    private void PushState()
    {
        IReadOnlyList<FestivalStatus> rows = _communityLeader.Listing();

        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("kind", "state");
            json.WriteString("who", string.Empty);

            json.WriteStartArray("festivals");

            foreach (FestivalStatus row in rows)
            {
                int prizes = 0;

                try
                {
                    foreach (RoundStatus round in _communityLeader.RoundsOf(row.Id))
                        prizes += round.Promises.Count;
                }
                catch (Exception)
                {
                }

                json.WriteStartObject();
                json.WriteString("id", row.Id);
                json.WriteString("name", row.Name);
                json.WriteString("playing", row.Figures);
                json.WriteString("createdAt", row.CreatedAt);
                json.WriteString("state", row.State);
                json.WriteNumber("draws", row.Draws);
                json.WriteNumber("sold", row.SoldCards);
                json.WriteNumber("cards", row.Cards);
                json.WriteNumber("price", row.CardPrice);
                json.WriteString("playsOn", row.PlaysOn ?? string.Empty);
                json.WriteString("doorsAt", row.DoorsOpenAt ?? string.Empty);
                json.WriteNumber("people", row.People);
                json.WriteNumber("rounds", row.Rounds);
                json.WriteNumber("currentRound", row.CurrentRound);
                json.WriteNumber("prizes", prizes);
                json.WriteBoolean("over",
                    string.Equals(row.State, "Over", StringComparison.Ordinal));

                // Si este bingo es de practica. Lo sabe el telefono, no el pueblo.
                json.WriteBoolean("practice", Rehearsals().Contains(row.Id));

                // Quien vendio cuanto, por bingo. Ventas lo desglosa persona por persona,
                // y de la cartelera sola no sale: hay que ir a los papelitos de cada uno.
                json.WriteStartArray("sellers");
                foreach ((string name, int sold) in SellersOf(row.Id))
                {
                    json.WriteStartObject();
                    json.WriteString("name", name);
                    json.WriteNumber("sold", sold);
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                json.WriteEndObject();
            }

            json.WriteEndArray();

            // Para la pestana "Mas": la version que corre este telefono, y si ya hay un
            // ensayo armado (para ofrecer borrarlo en vez de armar otro).
            json.WriteString("version", AppInfo.Current.VersionString);
            json.WriteString("build", AppInfo.Current.BuildString);

            json.WriteEndObject();
        }

        PushRaw(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Quienes venden en ese bingo y cuanto lleva cada uno. Sale de los
    /// papelitos y no de la memoria: despues de un reinicio la lista se rearma del
    /// diario, y las ventas de la noche no se pueden perder por eso.</summary>
    private List<(string Name, int Sold)> SellersOf(string festivalId)
    {
        List<(string, int)> mine = new();

        try
        {
            IReadOnlyList<Attendee> people = _communityLeader.AttendeesOf(festivalId);

            foreach (HandedOut errand in _communityLeader.ErrandsOf(festivalId))
            {
                if (errand.SentHome) continue;
                if (Duties.IsCaller(errand.Duty)) continue;

                string name = errand.Whose;
                if (string.IsNullOrWhiteSpace(name)) continue;

                Attendee who = people.FirstOrDefault(p =>
                    string.Equals(p.Mark, errand.Mark, StringComparison.Ordinal));

                mine.Add((name, who?.Sold.Count ?? 0));
            }
        }
        catch (Exception)
        {
        }

        return mine;
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
