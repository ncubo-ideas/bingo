using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace BingoApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// El esquema es del NOMBRE DE LA APP, no del concepto: fue 'velada://' hasta que la app
// paso a llamarse BingoApp. Si se vuelve a renombrar hay que cambiarlo aca Y en los
// prefijos de OpenFestival y HuddlePass, o los enlaces que la app genera dejan de entrar
// por esta puerta.
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "bingo")]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "https",
    DataHost = Transport.PhoneToPhone.WebPlayerSite,
    AutoVerify = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "https",
    DataHost = Transport.PhoneToPhone.WebPlayerMirror,
    AutoVerify = true)]
public class MainActivity : MauiAppCompatActivity
{
    private double BarraDeArriba()
    {
        Android.Views.View decor = Window?.DecorView;
        AndroidX.Core.View.WindowInsetsCompat insets = decor is null ? null : AndroidX.Core.View.ViewCompat.GetRootWindowInsets(decor);
        if (insets is null) return 0;

        int top = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.StatusBars()
            | AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout()).Top;

        return top / decor.Resources.DisplayMetrics.Density;
    }

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SinBandaDeArriba();
        Texts.TopBar = BarraDeArriba;
        HandOff(Intent);
    }

    /// <summary>
    /// La barra de estado, transparente. Venia del morado que trae la plantilla de MAUI
    /// (colorPrimaryDark), y como la pagina ya dibuja de borde a borde -- por eso el CSS
    /// compensa con max(env(safe-area-inset-top), 30px) -- lo unico que hacia ese color
    /// era una banda ajena arriba de todas las pantallas: morada sobre la cabecera blanca
    /// del colaborador, morada sobre el ambar de la puerta. Transparente deja ver lo que
    /// pinta cada pantalla, que es lo que el diseño ya resolvia.
    /// Los iconos del sistema van CLAROS: lo que hay arriba de todo ya no es papel blanco
    /// sino la cabecera de color de cada pantalla -- el verde de quien vende, el morado de
    /// quien canta, el degradado de la puerta. Estaban en oscuro de cuando la cabecera del
    /// colaborador era blanca, y sobre el verde la hora se leia a duras penas.
    /// </summary>
    private void SinBandaDeArriba()
    {
        if (Window is null) return;

        Window.SetStatusBarColor(Android.Graphics.Color.Transparent);

        if (Build.VERSION.SdkInt < BuildVersionCodes.R) return;

        AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(Window, false);

        AndroidX.Core.View.WindowInsetsControllerCompat mando =
            AndroidX.Core.View.WindowCompat.GetInsetsController(Window, Window.DecorView);

        mando.AppearanceLightStatusBars = false;
    }

    protected override void OnNewIntent(Intent intent)
    {
        base.OnNewIntent(intent);
        HandOff(intent);
    }

    private static void HandOff(Intent intent)
    {
        string link = intent?.DataString;

        if (string.IsNullOrWhiteSpace(link)) return;

        if (link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !Contract.OpenFestival.IsFestivalLink(link)) return;

        QrInbox.Announce(link);
    }
}

