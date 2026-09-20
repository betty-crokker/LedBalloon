using System.Net;

namespace Ledwright.Core.Discovery;

/// <summary>A WLED device found on the local network.</summary>
/// <param name="Name">The device's friendly name, e.g. "Desk Strip".</param>
/// <param name="HostName">The mDNS hostname, e.g. "wled-a1b2c3.local".</param>
/// <param name="Address">The resolved address. Prefer this over the hostname when connecting.</param>
/// <param name="Port">The HTTP port, normally 80.</param>
public sealed record WledDiscoveryResult(string Name, string HostName, IPAddress Address, int Port)
{
    /// <summary>What to hand to <see cref="WledClient"/>.</summary>
    public string ConnectHost => Port == 80 ? Address.ToString() : $"{Address}:{Port}";

    public override string ToString() => $"{Name} ({ConnectHost})";
}

/// <summary>Finds WLED devices on the local network.</summary>
public interface IWledDiscovery
{
    /// <summary>
    /// Browses for devices, yielding each as it answers. Callers normally run this for a few seconds;
    /// mDNS is best-effort, so a device that misses one scan may well appear in the next.
    /// </summary>
    IAsyncEnumerable<WledDiscoveryResult> DiscoverAsync(
        TimeSpan scanTime,
        CancellationToken cancellationToken = default);
}
