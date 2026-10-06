using QRCoder;

namespace BingoApp;

/// <summary>Dibuja un codigo para mostrarlo o imprimirlo. Lo usa el organizador con sus
/// codigos de colaborador y el colaborador con el que le re-emite al comprador.</summary>
public static class QrImage
{
    private const QRCodeGenerator.ECCLevel Correction = QRCodeGenerator.ECCLevel.M;

    public static byte[] Png(string text, int pixelsPerModule = 12)
    {
        QRCodeGenerator generator = new QRCodeGenerator();
        QRCodeData data = generator.CreateQrCode(text, Correction);

        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    public static ImageSource ForScreen(string text)
    {
        byte[] png = Png(text);

        return ImageSource.FromStream(() => new MemoryStream(png));
    }

    /// <summary>Para la pantalla del jugador, que es una pagina web adentro del telefono.
    /// Va incrustado en el mismo mensaje que el resto del estado: la pagina no puede pedir
    /// nada por red, y aunque pudiera, el codigo no tiene por que salir del aparato.</summary>
    public static string AsDataUri(string text, int pixelsPerModule = 10) =>
        "data:image/png;base64," + Convert.ToBase64String(Png(text, pixelsPerModule));

    public static async Task<string> SaveForPrintingAsync(string text, string name)
    {
        string path = Path.Combine(FileSystem.CacheDirectory, $"qr-{name}.png");

        await File.WriteAllBytesAsync(path, Png(text, 28));

        return path;
    }
}
