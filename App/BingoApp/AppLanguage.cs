using System.Globalization;

namespace BingoApp;

/// <summary>
/// El dueno del idioma en este telefono. Se resuelve UNA vez al arrancar: lo que la
/// persona eligio con el selector de banderas si alguna vez eligio, y si no, lo que
/// dice el aparato. Todo lo demas (Texts, las paginas por el puente, las pantallas
/// nativas) lo lee de aca; la pagina no resuelve el idioma por su cuenta con
/// navigator.language, porque dos verdades sobre lo mismo es justo lo que se quiere
/// evitar: la pagina en ingles y los toasts del C# en espanol.
/// </summary>
public static class AppLanguage
{
    private const string Key = "bingo.language";

    /// <summary>Alguien cambio el idioma con el selector. Las pantallas nativas que
    /// estan abiertas se repintan; las paginas reciben el catalogo nuevo por el puente.</summary>
    public static event Action Changed;

    /// <summary>Se llama una vez, en App(). Antes de esto Texts habla espanol.</summary>
    public static void Start()
    {
        string guardado = Preferences.Default.Get(Key, string.Empty);

        Texts.Language = guardado.Length > 0
            ? guardado
            : Texts.Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    public static string Current => Texts.Language;

    public static bool IsEnglish => Texts.Language == Texts.English;

    /// <summary>Lo que la persona toco en el selector. Se guarda para la proxima vez
    /// y se avisa, aunque haya elegido el mismo que ya tenia: asi la primera eleccion
    /// explicita queda escrita y el aparato deja de mandar.</summary>
    public static void Choose(string tag)
    {
        string elegido = Texts.Normalize(tag);
        bool cambia = elegido != Texts.Language;

        Texts.Language = elegido;
        Preferences.Default.Set(Key, elegido);

        if (cambia) Changed?.Invoke();
    }
}
