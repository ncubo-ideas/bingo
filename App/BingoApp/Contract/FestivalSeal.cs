using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BingoApp.Contract;

/// <summary>
/// El sello de un codigo de funcion: la firma del organizador PEGADA al link, no adentro
/// del JSON. Un link firmado es el de siempre mas dos colas -- &amp;s=firma &amp;h=huella --,
/// asi que todo lo que hoy lee links sin firma sigue leyendo, y firmar es OTRA opcion,
/// no un reemplazo.
///
/// Se firma el texto EXACTO del payload (lo que va entre "d=" y el primer "&amp;"), tal
/// como viaja. Firmar el JSON re-serializado seria firmar una reconstruccion: cualquier
/// diferencia de orden o de llaves entre versiones rompe la firma sin que nadie haya
/// tocado nada.
///
/// La familia criptografica es la del certificado que el telefono ya tiene
/// (mi-cert-tls.pfx): RSA-2048 con SHA-256 y padding Pkcs1, y la huella con el mismo
/// molde que TrustPeerHttpsFingerprint -- SHA-256 del DER en hex minuscula -- para que
/// una sola identidad sirva para el transporte y para los codigos.
/// </summary>
public static class FestivalSeal
{
    public static string SignedLink(OpenFestival open, X509Certificate2 cert)
    {
        string link = open.ToLink();
        return link + SignedTail(link, cert);
    }

    /// <summary>
    /// La cola firmada de un link YA armado. Existe aparte de SignedLink porque el sobre
    /// tiene dos productores: ToLink y la re-serializacion a mano de CodeFor (el codigo
    /// del comprador). Los dos firman lo mismo -- el payload tal como viaja -- por aca.
    /// </summary>
    public static string SignedTail(string link, X509Certificate2 cert)
    {
        string payload = link[OpenFestival.Scheme.Length..];

        using RSA key = cert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException(Texts.T("contrato.certificado-sin-llave"));

        byte[] signature = key.SignData(Encoding.UTF8.GetBytes(payload),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return "&s=" + AsBase64Url(signature) + "&h=" + Huddle.PhoneCertificate.Fingerprint(cert);
    }

    /// <summary>Si esta firma sobre este payload es de quien porta este certificado.
    /// Cualquier cosa ilegible cuenta como NO: una firma que no se puede leer no se
    /// distingue de una falsificada.</summary>
    public static bool Verify(string signedPayload, string signature, X509Certificate2 signer)
    {
        if (string.IsNullOrWhiteSpace(signedPayload) || string.IsNullOrWhiteSpace(signature))
            return false;

        try
        {
            using RSA key = signer.GetRSAPublicKey();
            if (key is null) return false;

            return key.VerifyData(Encoding.UTF8.GetBytes(signedPayload),
                FromBase64Url(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string AsBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string text)
    {
        string raw = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(raw.PadRight(raw.Length + (4 - raw.Length % 4) % 4, '='));
    }
}
