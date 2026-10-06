using ZXing.Net.Maui;

namespace BingoApp;

public partial class ScanScreen : ContentPage
{
    private readonly TaskCompletionSource<string> _result = new();
    private readonly string[] _accepts;

    private int _reads;
    private bool _closed;

    public ScanScreen() : this(null)
    {
    }

    /// <summary>
    /// Que se espera leer y como decirlo. El prefijo era fijo ('bingo://') y eso hacia que
    /// la camara no cerrara NUNCA con ningun otro codigo: el de una colega y el carnet de
    /// un comprador se rechazaban con el cartel "ese QR no es del bingo", o sea que la
    /// maquina le echaba la culpa a la persona que tenia enfrente.
    /// </summary>
    public ScanScreen(string instruction, params string[] accepts)
    {
        InitializeComponent();

        _accepts = accepts is null or { Length: 0 } ? new[] { "bingo://" }.Concat(Contract.OpenFestival.WebSchemes).ToArray() : accepts;

        if (!string.IsNullOrWhiteSpace(instruction)) Instruction.Text = instruction;

        Camera.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            TryHarder = true,
            Multiple = false,
        };
    }

    public Task<string> Scanned => _result.Task;

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        Camera.BarcodesDetected += OnDetected;

        PermissionStatus permission = await Permissions.CheckStatusAsync<Permissions.Camera>();

        if (permission != PermissionStatus.Granted)
            permission = await Permissions.RequestAsync<Permissions.Camera>();

        if (permission != PermissionStatus.Granted) NoCamera.IsVisible = true;
    }

    /// <summary>Salir con el gesto de atras es lo natural cuando el QR no engancha, y no
    /// pasa por Close: sin esto, quien esta esperando el resultado se queda esperando para
    /// siempre y la venta no avanza ni falla.</summary>
    protected override void OnDisappearing()
    {
        Camera.BarcodesDetected -= OnDetected;
        Camera.IsDetecting = false;

        _closed = true;
        _result.TrySetResult(null);

        base.OnDisappearing();
    }

    private void OnTorch(object sender, EventArgs e)
    {
        Camera.IsTorchOn = !Camera.IsTorchOn;
        TorchButton.Text = Texts.T(Camera.IsTorchOn ? "escanear.apagar-la-luz" : "escanear.prender-la-luz");
    }

    private void OnDetected(object sender, BarcodeDetectionEventArgs e)
    {
        BarcodeResult first = e.Results?.FirstOrDefault();

        if (first is null) return;

        _reads++;

        MainThread.BeginInvokeOnMainThread(() => Diagnostics.Text = Texts.T("escanear.lecturas", ("n", _reads.ToString())));

        string read = first.Value;

        if (string.IsNullOrWhiteSpace(read) || !IsWhatWeCameFor(read))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Warning.Text = Texts.T("escanear.qr-equivocado");
                Warning.IsVisible = true;
            });

            return;
        }

        Close(read);
    }

    private bool IsWhatWeCameFor(string read)
    {
        string trimmed = read.TrimStart();

        foreach (string prefix in _accepts)
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private void OnBack(object sender, EventArgs e) => Close(null);

    private void Close(string read)
    {
        if (_closed) return;

        _closed = true;
        Camera.IsDetecting = false;
        _result.TrySetResult(read);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await Navigation.PopModalAsync(); } catch (Exception) { }
        });
    }
}
