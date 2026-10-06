using System.Globalization;
using System.Text;
using BingoApp.Huddle;

namespace BingoApp;

public partial class HuddleDemoScreen : ContentPage
{
    private readonly StringBuilder _log = new();
    private readonly CancellationTokenSource _life = new();

    private HuddleDemo.DemoCommunityLeader _communityLeader;
    private HuddleDemo.DemoCollaborator _collaborator;
    private long _nextCard = 787;

    private readonly string _arrivedPass;

    public HuddleDemoScreen() => InitializeComponent();

    public HuddleDemoScreen(string link) : this() => _arrivedPass = link;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (string.IsNullOrWhiteSpace(_arrivedPass) || _collaborator is not null) return;

        ChoosePanel.IsVisible = false;
        PastedPass.Text = _arrivedPass;
        Say(Texts.T("corrillo.pase-por-enlace"));
        OnAttach(this, EventArgs.Empty);
    }

    private void OnBeCommunityLeader(object sender, EventArgs e)
    {
        ChoosePanel.IsVisible = false;
        StatusLabel.Text = Texts.T("corrillo.abriendo");

        string festivalId = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);

        Task.Run(async () =>
        {
            try
            {
                _communityLeader = new HuddleDemo.DemoCommunityLeader(Say);
                HuddlePass pass = await _communityLeader.OpenAsync(festivalId, _life.Token);

                OnScreen(() =>
                {
                    PassToCopy.Text = pass.ToText();
                    PassPanel.IsVisible = true;
                    StatusLabel.Text = Texts.T("corrillo.pase-listo");
                });
            }
            catch (Exception failure)
            {
                Failed(failure);
            }
        });
    }

    private void OnBeCollaborator(object sender, EventArgs e)
    {
        ChoosePanel.IsVisible = false;
        AttachPanel.IsVisible = true;
        StatusLabel.Text = Texts.T("corrillo.pega-el-pase-corto");
    }

    private async void OnCopyPass(object sender, TappedEventArgs e)
    {
        string pass = PassToCopy.Text;

        if (string.IsNullOrWhiteSpace(pass)) return;

        try
        {
            await Clipboard.SetTextAsync(pass);
        }
        catch (Exception failure)
        {
            Say(Texts.T("corrillo.no-pude-copiar-pase", ("motivo", Texts.Explain(failure))));
            return;
        }

        Say(Texts.T("corrillo.pase-copiado"));

        CopiedBadge.IsVisible = true;
        await Task.Delay(1600);
        CopiedBadge.IsVisible = false;
    }

    private void OnWait(object sender, EventArgs e)
    {
        StatusLabel.Text = Texts.T("corrillo.esperando-al-vendedor");

        Task.Run(async () =>
        {
            try
            {
                await _communityLeader.WelcomeAsync(_life.Token);

                OnScreen(() =>
                {
                    TestPanel.IsVisible = true;
                    StatusLabel.Text = Texts.T("corrillo.armado", ("entrada", _communityLeader.Entry.ToString()));
                });
            }
            catch (Exception failure)
            {
                Failed(failure);
            }
        });
    }

    private void OnAttach(object sender, EventArgs e)
    {
        string text = PastedPass.Text;
        StatusLabel.Text = Texts.T("corrillo.pegandome");

        Task.Run(async () =>
        {
            try
            {
                HuddlePass pass = HuddlePass.FromText(text);

                _collaborator = new HuddleDemo.DemoCollaborator(Say);
                await _collaborator.EnterAsync(pass, _life.Token);

                bool reached = await _collaborator.CatchUpAsync(1, _life.Token);

                if (reached) Say("    " + await _collaborator.ProgressAsync(_life.Token));

                OnScreen(() =>
                {
                    AttachPanel.IsVisible = false;
                    TestPanel.IsVisible = true;
                    StatusLabel.Text = reached
                        ? Texts.T("corrillo.replicando", ("entrada", _collaborator.Entry.ToString()))
                        : Texts.T("corrillo.pegue-sin-diario");
                });
            }
            catch (Exception failure)
            {
                Failed(failure);
            }
        });
    }

    private void OnSell(object sender, EventArgs e)
    {
        long card = _nextCard;
        _nextCard = card == 787 ? 789 : 791;

        Task.Run(async () =>
        {
            try
            {
                if (_communityLeader is not null)
                    await _communityLeader.SellAsync(card, "Rita", _life.Token);
                else
                    await _collaborator.SellAsync(card, "Beto", _life.Token);

                OnScreen(() => StatusLabel.Text = Texts.T("corrillo.vendi", ("carton", card.ToString())));
            }
            catch (Exception failure)
            {
                Failed(failure);
            }
        });
    }

    private void OnLook(object sender, EventArgs e)
    {
        Task.Run(async () =>
        {
            try
            {
                if (_communityLeader is not null)
                {
                    Say(Texts.T("corrillo.estado.organizador", ("entrada", _communityLeader.Entry.ToString()), ("avance", _communityLeader.Progress())));
                }
                else
                {
                    Say(Texts.T("corrillo.estado.vendedor", ("entrada", _collaborator.Entry.ToString()), ("avance", await _collaborator.ProgressAsync(_life.Token))));
                }
            }
            catch (Exception failure)
            {
                Failed(failure);
            }
        });
    }

    private void Failed(Exception failure)
    {
        Say(Texts.T("general.log.se-cayo", ("error", failure.GetType().Name + ": " + Texts.Explain(failure))));

        Exception root = failure;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
            Say("  " + Texts.T("general.log.causa", ("error", root.GetType().Name + ": " + root.Message)));
        }

        OnScreen(() => StatusLabel.Text = Texts.T("corrillo.se-cayo"));
    }

    private void Say(string line)
    {
        Console.WriteLine("[BINGO] " + line);

        string all;

        lock (_log)
        {
            _log.AppendLine(line);
            if (_log.Length > 30000) _log.Remove(0, _log.Length - 20000);
            all = _log.ToString();
        }

        OnScreen(() => LogBox.Text = all);
    }

    private static void OnScreen(Action what)
    {
        if (MainThread.IsMainThread) what();
        else MainThread.BeginInvokeOnMainThread(what);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
    }
}
