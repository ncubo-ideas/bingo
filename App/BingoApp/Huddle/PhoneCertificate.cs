using System.Security.Cryptography.X509Certificates;
using Choreography.Transport.Https;

namespace BingoApp.Huddle;

public static class PhoneCertificate
{
    private const string FileName = "mi-cert-tls.pfx";

    public static X509Certificate2 MineOrNew(string displayName, string myAddress,
        Action<string> say = null)
    {
        string path = Path.Combine(FileSystem.AppDataDirectory, FileName);

        if (File.Exists(path))
        {
            try
            {
                X509Certificate2 saved = X509CertificateLoader.LoadPkcs12(
                    File.ReadAllBytes(path), null,
                    X509KeyStorageFlags.Exportable);

                say?.Invoke(Texts.T("corrillo.certificado.de-siempre", ("huella", Fingerprint(saved)[..16])));
                return saved;
            }
            catch (Exception failure)
            {
                say?.Invoke(Texts.T("corrillo.certificado.ilegible", ("motivo", failure.Message)));
            }
        }

        X509Certificate2 fresh = SelfSignedCert.Generate(displayName, "localhost", myAddress);

        File.WriteAllBytes(path, fresh.Export(X509ContentType.Pkcs12));
        say?.Invoke(Texts.T("corrillo.certificado.nuevo", ("huella", Fingerprint(fresh)[..16])));

        return fresh;
    }

    public static string Fingerprint(X509Certificate2 cert) => SelfSignedCert.Fingerprint(cert);
}
