using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BingoApp.Huddle;

public static class NetworkAddress
{
    public static string Mine(Action<string> say = null)
    {
        List<string> skipped = new();

        foreach (NetworkInterface card in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (card.OperationalStatus != OperationalStatus.Up) continue;
            if (card.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (UnicastIPAddressInformation bound in card.GetIPProperties().UnicastAddresses)
            {
                if (bound.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                string which = bound.Address.ToString();

                if (IPAddress.IsLoopback(bound.Address)) { skipped.Add(which + " (loopback)"); continue; }

                if (which.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    skipped.Add(which + " (" + Texts.T("corrillo.red.sin-dhcp") + ")");
                    continue;
                }

                say?.Invoke(Texts.T("corrillo.red.me-ven-en", ("direccion", which), ("tarjeta", card.Name)));
                return which;
            }
        }

        if (skipped.Count > 0) say?.Invoke(Texts.T("corrillo.red.descarte", ("cuales", string.Join(", ", skipped))));

        throw new InvalidOperationException(
            Texts.T("corrillo.red.ninguna"));
    }
}
