using Puppeteer;

namespace BingoApp.CommunityLeader;

/// <summary>
/// Le presta voz al diario. Sin esto el sustrato se queja contra su logger por defecto
/// y en el telefono nadie lo oye: la rehidratacion es PERMISIVA, o sea que un asiento
/// que revienta al releerse se saltea y el bingo sigue como si nunca hubiera pasado.
/// Con esto, eso mismo aparece en 'lo que va pasando'.
/// </summary>
internal sealed class JournalVoice : IPuppeteerLogger
{
    private readonly Action<string> _say;

    public JournalVoice(Action<string> say) => _say = say ?? (_ => { });

    public void Error(string message, Exception exception)
    {
        _say(Texts.T("general.log.diario-se-queja", ("mensaje", message)));

        // La excepcion DE ARRIBA tambien se dice. Antes solo se recorrian las internas, asi
        // que una que no tuviera ninguna --la mayoria de las del dominio, que se lanzan
        // solas-- se quejaba sin decir de que: quedaba el titulo y el script, nunca el
        // motivo. Eso costo una tarde con una correccion que no se aplicaba.
        if (exception is not null)
        {
            _say("    " + Texts.T("general.log.porque", ("error", exception.GetType().Name + ": " + exception.Message)));
        }

        Exception root = exception;

        while (root?.InnerException is not null)
        {
            root = root.InnerException;
            _say("    " + Texts.T("general.log.causa", ("error", root.GetType().Name + ": " + root.Message)));
        }
    }

    public void Debug(string message) => _say("    " + Texts.T("general.log.diario", ("mensaje", message)));
}
