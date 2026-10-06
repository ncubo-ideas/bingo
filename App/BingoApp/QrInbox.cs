namespace BingoApp;

/// <summary>
/// El buzon del deep link. La Activity deja aca el codigo que llego de afuera y
/// la pantalla que este al frente lo recoge. Es de toda la app, no de un papel:
/// por el entran tanto el codigo del colaborador como el del jugador.
/// </summary>
public static class QrInbox
{
    private static readonly object Guard = new();

    private static string _pending;

    public static event Action<string> Arrived;

    public static void Announce(string link)
    {
        lock (Guard) _pending = link;

        Action<string> listener = Arrived;

        if (listener is null) return;

        if (Collect() is string code) listener(code);
    }

    public static string Collect()
    {
        lock (Guard)
        {
            string code = _pending;
            _pending = null;
            return code;
        }
    }
}
