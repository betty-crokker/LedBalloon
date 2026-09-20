using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Zeroconf;

namespace Ledwright.Core.Discovery;

/// <summary>
/// mDNS discovery of the <c>_wled._tcp</c> service, via a managed Bonjour implementation.
/// <para>
/// Using a managed resolver rather than the OS APIs is what keeps discovery identical on Windows,
/// Linux and macOS — no Bonjour service on Windows, no Avahi dependency on Linux.
/// </para>
/// </summary>
public sealed class ZeroconfWledDiscovery : IWledDiscovery
{
    /// <summary>The DNS-SD service type WLED advertises itself under.</summary>
    public const string ServiceType = "_wled._tcp.local.";

    /// <inheritdoc />
    public async IAsyncEnumerable<WledDiscoveryResult> DiscoverAsync(
        TimeSpan scanTime,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<WledDiscoveryResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // Run the browse in the background and surface hosts through the channel as they answer,
        // so a caller populating a list sees devices appear one by one rather than all at the end.
        Task browse = Task.Run(async () =>
        {
            try
            {
                await ZeroconfResolver.ResolveAsync(
                    ServiceType,
                    scanTime: scanTime,
                    callback: host =>
                    {
                        if (TryMap(host, out WledDiscoveryResult? result))
                        {
                            channel.Writer.TryWrite(result!);
                        }
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        await foreach (WledDiscoveryResult result in channel.Reader
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return result;
        }

        await browse.ConfigureAwait(false);
    }

    /// <summary>Convenience wrapper: scan once and return everything found, de-duplicated by address.</summary>
    public async Task<IReadOnlyList<WledDiscoveryResult>> ScanAsync(
        TimeSpan scanTime,
        CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, WledDiscoveryResult>(StringComparer.OrdinalIgnoreCase);

        await foreach (WledDiscoveryResult result in DiscoverAsync(scanTime, cancellationToken)
            .ConfigureAwait(false))
        {
            found[result.Address.ToString()] = result;
        }

        return [.. found.Values];
    }

    private static bool TryMap(IZeroconfHost host, out WledDiscoveryResult? result)
    {
        result = null;

        string? address = host.IPAddresses?.FirstOrDefault() ?? host.IPAddress;
        if (!IPAddress.TryParse(address, out IPAddress? parsed))
        {
            return false;
        }

        int port = host.Services?.Values.FirstOrDefault()?.Port ?? 80;
        string name = string.IsNullOrWhiteSpace(host.DisplayName) ? parsed.ToString() : host.DisplayName;

        result = new WledDiscoveryResult(name, host.Id ?? name, parsed, port);
        return true;
    }
}
