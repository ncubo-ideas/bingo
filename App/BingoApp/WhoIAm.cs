namespace BingoApp;

/// <summary>
/// Que pantalla abre este telefono. Es distinto de Duties, que es lo que dice
/// el QR: esto solo recuerda por donde entro la ultima vez.
/// </summary>
public enum WhoIAm
{
    Unchosen = 0,
    CommunityLeader,
    Collaborator,
    Player,
}

public static class RememberedRole
{
    private const string Key = "funcion.papel";

    public static WhoIAm Current
    {
        get => Enum.TryParse(Preferences.Default.Get(Key, string.Empty), out WhoIAm which)
            ? which
            : WhoIAm.Unchosen;

        set => Preferences.Default.Set(Key, value.ToString());
    }

    public static void Forget() => Preferences.Default.Remove(Key);

    /// <summary>Como se llama el papel, en el idioma de la persona. La clave es el
    /// nombre del enum, que es lo que se guarda en Preferences y no se traduce.</summary>
    public static string Describe(WhoIAm which) => Texts.T("papel." + which);
}
