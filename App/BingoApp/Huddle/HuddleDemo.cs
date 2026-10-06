using Choreography.StageManager;
using Puppeteer;

namespace BingoApp.Huddle;

public static class HuddleDemo
{
    private const string SeedScript = @"
    upgrade('{0}') {{
        elder    = Elder(@communityLeader);
        dia      = CalendarDay(@anio, @mes, @diaMes);
        hoy      = CalendarDay(@hoyAnio, @hoyMes, @hoyDia);
        festival = elder.CreateFestival(@comoSeLlama, dia, hoy);
        mesa     = BingoGame();
        elder.PutUpForSale(festival, {{787, 789, 791}});
    }}";

    public sealed class DemoCommunityLeader : IAsyncDisposable
    {
        private readonly Action<string> _say;

        private StageV2 _journal;
        private HuddleStage.OpenDoor _door;

        public DemoCommunityLeader(Action<string> say) => _say = say;

        public HuddlePass Pass => _door?.Pass;

        public long Entry => _journal?.CurrentEntryId ?? 0;

        public async Task<HuddlePass> OpenAsync(string festivalId, CancellationToken life)
        {
            _say(Texts.T("corrillo.cabecera.organizador"));

            string myAddress = NetworkAddress.Mine(_say);
            _journal = HuddleStage.MountJournal("organizador", _say);

            await _journal.StartAsync(life);
            _say(Texts.T("corrillo.diario-arrancado"));

            _journal.PromoteToDirector(force: true);
            _say(Texts.T("corrillo.soy-director"));

            Parameters data = new Parameters();
            data.UserParameter<string>("communityLeader", "Bartolo");
            data.UserParameter<string>("comoSeLlama", "Bingo de agosto");
            data.UserParameter<int>("anio", 2026);
            data.UserParameter<int>("mes", 8);
            data.UserParameter<int>("diaMes", 15);
            data.UserParameter<int>("hoyAnio", 2026);
            data.UserParameter<int>("hoyMes", 7);
            data.UserParameter<int>("hoyDia", 31);

            await _journal.PerformCmd(
                string.Format(SeedScript, "la-funcion-" + festivalId),
                data, DateTime.Now, "0.0.0.0", "Anonymous", life);

            _say(Texts.T("corrillo.sembrado", ("id", festivalId), ("entrada", _journal.CurrentEntryId.ToString())));
            _say("    " + _journal.PerformQry(
                "{ print elder.Name organizador; print festival.SoldCount vendidos; }", new Parameters()));

            _door = await HuddleStage.OpenTheDoorAsync(_journal, myAddress, life, _say);

            try
            {
                File.WriteAllText(
                    Path.Combine(FileSystem.AppDataDirectory, "pase.txt"),
                    _door.Pass.ToLink());

                _say(Texts.T("corrillo.pase-guardado"));
            }
            catch (Exception failure)
            {
                _say(Texts.T("corrillo.pase-no-guardado", ("motivo", Texts.Explain(failure))));
            }

            return _door.Pass;
        }

        public async Task WelcomeAsync(CancellationToken life)
        {
            await HuddleStage.WelcomeCollaboratorAsync(_journal, _door, _say);
        }

        public async Task SellAsync(long card, string who, CancellationToken life)
        {
            Parameters data = new Parameters();
            data.UserParameter<string>("nombre", who);
            data.UserParameter<string>("contacto", "la casa de la esquina");
            data.UserParameter<long>("numero", card);

            await _journal.PerformCmd(
                "comprador = Villager(@nombre, @contacto); "
                + "card = festival.ParticipationNumbered(@numero); elder.SellTo(festival, card, comprador); "
                + "mesa.PutCardInPlay(@numero);",
                data, DateTime.Now, "0.0.0.0", "Anonymous", life);

            _say(Texts.T("corrillo.vendi-entrada", ("carton", card.ToString()), ("nombre", who), ("entrada", _journal.CurrentEntryId.ToString())));
        }

        public string Progress() => _journal.PerformQry(
            "{ print festival.SoldCount vendidos; print mesa.CardsInPlayCount enJuego; }",
            new Parameters());

        public async ValueTask DisposeAsync()
        {
            if (_journal is not null) await _journal.DisposeAsync();
        }
    }

    public sealed class DemoCollaborator : IAsyncDisposable
    {
        private readonly Action<string> _say;

        private StageV2 _journal;

        public DemoCollaborator(Action<string> say) => _say = say;

        public long Entry => _journal?.CurrentEntryId ?? 0;

        public async Task EnterAsync(HuddlePass pass, CancellationToken life)
        {
            _say(Texts.T("corrillo.cabecera.vendedor"));

            _journal = HuddleStage.MountJournal("vendedor", _say);

            await _journal.StartAsync(life);
            _say(Texts.T("corrillo.diario-arrancado-vacio"));

            await HuddleStage.AttachToCommunityLeaderAsync(_journal, pass, life, _say);
        }

        public async Task<bool> CatchUpAsync(long entry, CancellationToken life)
        {
            _say(Texts.T("corrillo.espero-entrada", ("entrada", entry.ToString())));

            bool reached = await HuddleStage.WaitToReach(
                _journal, entry, TimeSpan.FromSeconds(60), life);

            _say(reached
                ? Texts.T("corrillo.llegue", ("entrada", _journal.CurrentEntryId.ToString()))
                : Texts.T("corrillo.no-llegue", ("entrada", _journal.CurrentEntryId.ToString()), ("meta", entry.ToString())));

            return reached;
        }

        public async Task<string> ProgressAsync(CancellationToken life)
        {
            for (int attempt = 1; attempt <= 40; attempt++)
            {
                try
                {
                    return _journal.PerformQry(
                        "{ print festival.SoldCount vendidos; print mesa.CardsInPlayCount enJuego; }",
                        new Parameters());
                }
                catch (Exception failure) when (failure.Message.Contains("on type 'Object'", StringComparison.Ordinal))
                {
                    if (attempt == 1) _say(Texts.T("corrillo.diario-aplicandose"));
                    await Task.Delay(250, life);
                }
            }

            return Texts.T("corrillo.diario-no-termino");
        }

        public async Task SellAsync(long card, string who, CancellationToken life)
        {
            Parameters data = new Parameters();
            data.UserParameter<string>("nombre", who);
            data.UserParameter<string>("contacto", "el 8888-1111");
            data.UserParameter<long>("numero", card);

            await _journal.PerformCmd(
                "comprador = Villager(@nombre, @contacto); "
                + "card = festival.ParticipationNumbered(@numero); elder.SellTo(festival, card, comprador); "
                + "mesa.PutCardInPlay(@numero);",
                data, DateTime.Now, "0.0.0.0", "Anonymous", life);

            _say(Texts.T("corrillo.vendi-reenviado", ("carton", card.ToString()), ("nombre", who), ("entrada", _journal.CurrentEntryId.ToString())));
        }

        public async ValueTask DisposeAsync()
        {
            if (_journal is not null) await _journal.DisposeAsync();
        }
    }
}
