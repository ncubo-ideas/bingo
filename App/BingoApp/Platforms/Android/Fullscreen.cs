using Android.Views;
using AndroidX.Core.View;

namespace BingoApp;

/// <summary>
/// Esconde las barras del sistema mientras el jugador esta en su carton. La feria se pinta
/// hasta el borde del vidrio, como cualquier juego; el reloj y la bateria vuelven a
/// aparecer si uno desliza desde el borde, y quedan puestas de nuevo al salir de la
/// pantalla, porque el organizador y la colaboradora si las quieren: ellos trabajan y
/// necesitan ver la hora.
/// </summary>
public static class Fullscreen
{
    public static void HideBars() => Toggle(true);

    public static void ShowBars() => Toggle(false);

    /// <summary>
    /// Esconder las barras es solo el primer paso de tres, y saltarse cualquiera deja una
    /// franja NEGRA arriba de la feria. Segundo: la ventana sigue empezando debajo de donde
    /// estaban las barras, y eso lo suelta LayoutNoLimits. Tercero, y el que de verdad
    /// costo encontrar: en un telefono con muesca la ventana se queda por debajo del
    /// recorte (49dp en el aparato de prueba) hasta que uno pide permiso para dibujar ahi.
    /// El fondo llega al vidrio, y el contenido igual no se esconde detras de la muesca
    /// porque la pagina respeta env(safe-area-inset-top).
    /// </summary>
    private static void Toggle(bool hide)
    {
        Android.App.Activity activity = Platform.CurrentActivity;
        Android.Views.Window window = activity?.Window;

        if (window is null) return;

        WindowCompat.SetDecorFitsSystemWindows(window, !hide);

        if (hide) window.AddFlags(WindowManagerFlags.LayoutNoLimits);
        else window.ClearFlags(WindowManagerFlags.LayoutNoLimits);

        WindowManagerLayoutParams frame = window.Attributes;

        frame.LayoutInDisplayCutoutMode = hide
            ? LayoutInDisplayCutoutMode.ShortEdges
            : LayoutInDisplayCutoutMode.Default;

        window.Attributes = frame;

        WindowInsetsControllerCompat bars =
            WindowCompat.GetInsetsController(window, window.DecorView);

        if (bars is not null)
        {
            if (hide)
            {
                bars.SystemBarsBehavior =
                    WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                bars.Hide(WindowInsetsCompat.Type.SystemBars());
            }
            else
            {
                bars.Show(WindowInsetsCompat.Type.SystemBars());
            }
        }

        StretchToTheEdges(window, hide);
    }

    /// <summary>
    /// Esconder las barras no alcanza: MAUI igual acomoda su contenido por debajo de donde
    /// estaban, y ese hueco queda NEGRO arriba de la feria. Hay que comerse los margenes
    /// que el sistema pide, y volver a dejarlos pasar al salir.
    /// </summary>
    private static void StretchToTheEdges(Android.Views.Window window, bool hide)
    {
        if (window.DecorView.FindViewById(Android.Resource.Id.Content) is not ViewGroup content)
            return;

        content.SetFitsSystemWindows(!hide);
        ViewCompat.SetOnApplyWindowInsetsListener(content, hide ? new NoMargins() : null);

        for (int i = 0; i < content.ChildCount; i++)
        {
            Android.Views.View child = content.GetChildAt(i);

            if (child is null) continue;

            child.SetFitsSystemWindows(!hide);
            ViewCompat.SetOnApplyWindowInsetsListener(child, hide ? new NoMargins() : null);

            if (hide) child.SetPadding(0, 0, 0, 0);
        }

        ViewCompat.RequestApplyInsets(content);
    }

    private sealed class NoMargins : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(
            Android.Views.View view, WindowInsetsCompat insets)
        {
            view.SetPadding(0, 0, 0, 0);
            return WindowInsetsCompat.Consumed;
        }
    }
}
