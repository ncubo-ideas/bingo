using BingoApp.Contract;

namespace BingoApp;

public partial class CoverScreen : ContentPage
{
    public CoverScreen()
    {
        InitializeComponent();
        ShowWhatIAm();
        ShowLanguage();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AppLanguage.Changed += OnLanguageChanged;
        ShowWhatIAm();
        ShowLanguage();

        if (QrInbox.Collect() is not string link) return;

        if (Huddle.HuddlePass.IsPassLink(link))
        {
            Dispatcher.Dispatch(async () => await Navigation.PushAsync(new HuddleDemoScreen(link)));
            return;
        }

        // El codigo dice a que vino quien lo escaneo. Antes se adivinaba por lo
        // que el telefono recordaba, y un enlace nuevo te volvia jugador siempre.
        if (IsForCollaborator(link))
        {
            RememberedRole.Current = WhoIAm.Collaborator;
            Dispatcher.Dispatch(async () => await Navigation.PushAsync(new CollaboratorScreen(link)));
            return;
        }

        RememberedRole.Current = WhoIAm.Player;
        Dispatcher.Dispatch(async () => await Navigation.PushAsync(new PlayerScreen(link)));
    }

    private static bool IsForCollaborator(string link)
    {
        try
        {
            return Duties.IsCollaborator(OpenFestival.FromText(link).Duty);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>La fila de volver, arriba de todo. El papel recordado ya estaba guardado
    /// pero solo se imprimia como etiqueta: informacion en el lugar donde cabia una
    /// accion. Quien vuelve --y el que vuelve es el caso real de esta pantalla, porque el
    /// que llega por QR no la ve-- entra de un toque.</summary>
    private void ShowWhatIAm()
    {
        WhoIAm which = RememberedRole.Current;
        bool alreadyChose = which != WhoIAm.Unchosen;

        ComeBackPanel.IsVisible = alreadyChose;
        OrElseLabel.IsVisible = alreadyChose;
        ForgetButton.IsVisible = alreadyChose;

        if (!alreadyChose) return;

        ComeBackName.Text = ComeBackNameFor(which);
        ComeBackSays.Text = Texts.T("portada.ultima-vez", ("papel", RememberedRole.Describe(which)));
    }

    /// <summary>Lo que se vuelve es LO SUYO, no un papel: al jugador le esperan cartones y
    /// al organizador un bingo. Nombrarlo asi es la diferencia entre reconocer el lugar y
    /// leer una etiqueta.</summary>
    private static string ComeBackNameFor(WhoIAm which) =>
        which == WhoIAm.Unchosen ? Texts.T("general.volver") : Texts.T("portada.volver." + which);

    /// <summary>Entrar por donde se entro la ultima vez. Es el mismo camino que las filas
    /// de abajo, sin volver a elegir.</summary>
    private async void OnComeBack(object sender, EventArgs e)
    {
        switch (RememberedRole.Current)
        {
            case WhoIAm.CommunityLeader:
                await Navigation.PushAsync(new FestivalsScreen());
                break;

            case WhoIAm.Collaborator:
                await Navigation.PushAsync(new CollaboratorScreen());
                break;

            case WhoIAm.Player:
                await Navigation.PushAsync(new PlayerScreen());
                break;
        }
    }

    private async void OnChooseCommunityLeader(object sender, EventArgs e)
    {
        RememberedRole.Current = WhoIAm.CommunityLeader;
        await Navigation.PushAsync(new FestivalsScreen());
    }

    private async void OnChooseCollaborator(object sender, EventArgs e)
    {
        RememberedRole.Current = WhoIAm.Collaborator;
        await Navigation.PushAsync(new CollaboratorScreen());
    }

    private async void OnChoosePlayer(object sender, EventArgs e)
    {
        RememberedRole.Current = WhoIAm.Player;
        await Navigation.PushAsync(new PlayerScreen());
    }

    private void OnForget(object sender, EventArgs e)
    {
        RememberedRole.Forget();
        ShowWhatIAm();
    }

    private async void OnTryHuddle(object sender, EventArgs e) =>
        await Navigation.PushAsync(new HuddleDemoScreen());

    // ── el idioma ────────────────────────────────────────────────────────────

    protected override void OnDisappearing()
    {
        AppLanguage.Changed -= OnLanguageChanged;
        base.OnDisappearing();
    }

    private void OnSpanish(object sender, EventArgs e) => AppLanguage.Choose(Texts.Spanish);

    private void OnEnglish(object sender, EventArgs e) => AppLanguage.Choose(Texts.English);

    /// <summary>Los textos del XAML se resolvieron al inflar la pagina; cuando el idioma
    /// cambia estando esta pantalla abierta, lo que depende del idioma se vuelve a poner
    /// desde aca.</summary>
    private void OnLanguageChanged()
    {
        ShowWhatIAm();
        ShowLanguage();
    }

    /// <summary>El idioma vigente se ve apagado: no hay que tocarlo para saber cual es.</summary>
    private void ShowLanguage()
    {
        LanguageLabel.Text = Texts.T("general.idioma");
        SpanishButton.Opacity = AppLanguage.IsEnglish ? 0.55 : 1;
        EnglishButton.Opacity = AppLanguage.IsEnglish ? 1 : 0.55;
    }
}
