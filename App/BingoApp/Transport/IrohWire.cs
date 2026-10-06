using System;
using System.IO;
using Choreography.Transport.Iroh;
using Puppeteer;
using Choreography.StageManager;

namespace BingoApp.Transport;

// El transporte iroh de un Stage: una llave por diario, guardada junto a el, para que la identidad
// del diario (su EndpointId) sobreviva reinicios. Sin relay por defecto: el bingo vive en la LAN de
// la parroquia y las invitaciones llevan las direcciones directas. BINGO_IROH_RELAY=1 prende los
// relays publicos de iroh, que exigen internet, para probar entre redes distintas.
public static class IrohWire
{
    public const string KeyFile = "iroh.key";

    public static IrohTransport ForStage(PerformerId stageId, string dir, IPuppeteerLogger logger, Action<string> say)
    {
        Directory.CreateDirectory(dir);
        string key = KeyFor(dir, say);
        bool relay = RelayWanted();
        IrohTransportOptions options = new IrohTransportOptions
        {
            SecretKeyBase32 = key,
            UseRelay = relay,
            UseDnsDiscovery = false,
            // Con relay, el bind espera hasta tener un relay de casa: si no, el primer ticket sale sin relay.
            OnlineTimeout = TimeSpan.FromSeconds(relay ? 45 : 15),
        };
        say?.Invoke("iroh: soy " + Short(IrohEndpointIdentity.EndpointIdOf(key))
            + (options.UseRelay ? ", con relay publico" : ", sin relay (solo LAN)"));
        return new IrohTransport(stageId, options, logger);
    }

    public static string KeyFor(string dir, Action<string> say)
    {
        string path = Path.Combine(dir, KeyFile);
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        string fresh = IrohEndpointIdentity.GenerateSecretKeyBase32();
        File.WriteAllText(path, fresh);
        say?.Invoke("iroh: llave nueva para este diario");
        return fresh;
    }

    public static bool RelayWanted()
    {
        string flag = Environment.GetEnvironmentVariable("BINGO_IROH_RELAY");
        return flag == "1" || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static string Short(string endpointId) =>
        string.IsNullOrEmpty(endpointId) || endpointId.Length <= 12 ? endpointId : endpointId[..12] + "...";
}
