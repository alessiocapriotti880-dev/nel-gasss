using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PcOptimizer.Benchmark.Model;

namespace PcOptimizer.Benchmark.Network;

/// <summary>Scheda di rete usata per uscire su Internet.</summary>
public sealed record PrimaryAdapter(string Name, string Type, IPAddress? Gateway);

/// <summary>Ping ICMP a cadenza fissa verso il router e verso un host Internet, per latenza, jitter e perdita.</summary>
public static class PingProbe
{
    private static readonly string[] VirtualHints = ["virtual", "hyper-v", "vpn", "tap-", "wintun", "wireguard", "vmware", "virtualbox", "loopback"];

    public static PrimaryAdapter? DetectPrimaryAdapter()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (Nic: n, Gateway: Gateway(n)))
            .Where(x => x.Gateway is not null)
            .ToList();
        var best = candidates
            .OrderBy(x => VirtualHints.Any(h => x.Nic.Description.Contains(h, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
            .ThenBy(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
            .FirstOrDefault();
        return best.Nic is null ? null : new PrimaryAdapter(best.Nic.Description, TypeName(best.Nic.NetworkInterfaceType), best.Gateway);
    }

    public static async Task<PingTargetResult> RunAsync(
        string target, string kind, int count, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        using var ping = new Ping();
        var buffer = new byte[32];
        var results = new List<double?>(count);
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(target, 1000, buffer);
                // RoundtripTime è in ms interi: con Stopwatch si avrebbe il ritardo del thread, non della rete.
                results.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch (PingException)
            {
                results.Add(null);
            }

            var next = interval * (i + 1) - clock.Elapsed;
            if (next > TimeSpan.Zero)
            {
                await Task.Delay(next, cancellationToken);
            }
        }

        return PingStatistics.Compute(target, kind, results);
    }

    private static IPAddress? Gateway(NetworkInterface nic)
    {
        try
        {
            return nic.GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static string TypeName(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Rete mobile",
        _ => type.ToString(),
    };
}
