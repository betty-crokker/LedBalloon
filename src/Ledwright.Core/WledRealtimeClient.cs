using System.Buffers.Binary;
using System.Net.Sockets;
using Ledwright.Core.Models;

namespace Ledwright.Core;

/// <summary>WLED's realtime UDP protocol selector, sent as the first byte of every packet.</summary>
public enum WledRealtimeProtocol : byte
{
    /// <summary>Index + RGB pairs. Efficient when only a few pixels change.</summary>
    Warls = 1,

    /// <summary>RGB triples starting at pixel 0. Capped at 490 pixels by the UDP payload size.</summary>
    Drgb = 2,

    /// <summary>RGBW quads starting at pixel 0.</summary>
    Drgbw = 3,

    /// <summary>RGB triples with a 16-bit start index, so strips longer than 490 pixels can be split across packets.</summary>
    Dnrgb = 4,
}

/// <summary>
/// Per-pixel streaming over UDP (default port 21324).
/// <para>
/// This bypasses WLED's effect engine entirely — you are driving the strip frame by frame. Every
/// packet carries a timeout in seconds; when packets stop arriving, the device reverts to its normal
/// effect on its own. That timeout is the safety net: if your app crashes mid-animation the lights
/// come back rather than freezing.
/// </para>
/// </summary>
public sealed class WledRealtimeClient : IDisposable
{
    /// <summary>WLED's default realtime port. Confirm against <see cref="WledInfo.UdpPort"/> if you want to be exact.</summary>
    public const int DefaultPort = 21324;

    /// <summary>Practical UDP payload ceiling before fragmentation on a normal 1500-byte MTU.</summary>
    private const int MaxPayload = 1472;

    private const int DrgbHeader = 2;
    private const int DnrgbHeader = 4;

    /// <summary>Pixels that fit in one DRGB packet.</summary>
    public const int MaxDrgbPixels = (MaxPayload - DrgbHeader) / 3;

    /// <summary>Pixels that fit in one DNRGB packet.</summary>
    public const int MaxDnrgbPixels = (MaxPayload - DnrgbHeader) / 3;

    private readonly UdpClient _udp = new();
    private readonly string _host;
    private readonly int _port;

    public WledRealtimeClient(string host, int port = DefaultPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        // Strip any scheme so callers can pass the same string they gave WledClient.
        _host = WledClient.NormalizeHost(host).Host;
        _port = port;
        _udp.Connect(_host, _port);
    }

    public string Host => _host;

    public int Port => _port;

    /// <summary>
    /// Streams a full frame, choosing DRGB or DNRGB and splitting across packets as needed.
    /// </summary>
    /// <param name="pixels">One colour per LED, starting at the first LED.</param>
    /// <param name="holdSeconds">
    /// How long the device keeps showing this frame if nothing else arrives, 1-254 seconds.
    /// 255 means hold until the device is reset, which is rarely what you want.
    /// </param>
    public async ValueTask SendFrameAsync(
        IReadOnlyList<RgbColor> pixels,
        byte holdSeconds = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Count == 0)
        {
            return;
        }

        if (pixels.Count <= MaxDrgbPixels)
        {
            await SendDrgbAsync(pixels, holdSeconds, cancellationToken).ConfigureAwait(false);
            return;
        }

        for (int offset = 0; offset < pixels.Count; offset += MaxDnrgbPixels)
        {
            int count = Math.Min(MaxDnrgbPixels, pixels.Count - offset);
            await SendDnrgbAsync(pixels, offset, count, holdSeconds, cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask SendDrgbAsync(
        IReadOnlyList<RgbColor> pixels,
        byte holdSeconds,
        CancellationToken cancellationToken)
    {
        var packet = new byte[DrgbHeader + (pixels.Count * 3)];
        packet[0] = (byte)WledRealtimeProtocol.Drgb;
        packet[1] = holdSeconds;

        int at = DrgbHeader;
        for (int i = 0; i < pixels.Count; i++)
        {
            RgbColor c = pixels[i];
            packet[at++] = c.R;
            packet[at++] = c.G;
            packet[at++] = c.B;
        }

        return SendAsync(packet, cancellationToken);
    }

    private ValueTask SendDnrgbAsync(
        IReadOnlyList<RgbColor> pixels,
        int start,
        int count,
        byte holdSeconds,
        CancellationToken cancellationToken)
    {
        var packet = new byte[DnrgbHeader + (count * 3)];
        packet[0] = (byte)WledRealtimeProtocol.Dnrgb;
        packet[1] = holdSeconds;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)start);

        int at = DnrgbHeader;
        for (int i = 0; i < count; i++)
        {
            RgbColor c = pixels[start + i];
            packet[at++] = c.R;
            packet[at++] = c.G;
            packet[at++] = c.B;
        }

        return SendAsync(packet, cancellationToken);
    }

    /// <summary>
    /// Sends sparse pixel updates as index/colour pairs. Cheaper than a full frame when only a few
    /// LEDs changed, but the single-byte index caps it at the first 256 LEDs.
    /// </summary>
    public ValueTask SendSparseAsync(
        IReadOnlyList<(byte Index, RgbColor Color)> updates,
        byte holdSeconds = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        var packet = new byte[DrgbHeader + (updates.Count * 4)];
        packet[0] = (byte)WledRealtimeProtocol.Warls;
        packet[1] = holdSeconds;

        int at = DrgbHeader;
        foreach ((byte index, RgbColor color) in updates)
        {
            packet[at++] = index;
            packet[at++] = color.R;
            packet[at++] = color.G;
            packet[at++] = color.B;
        }

        return SendAsync(packet, cancellationToken);
    }

    private async ValueTask SendAsync(byte[] packet, CancellationToken cancellationToken)
    {
        try
        {
            await _udp.SendAsync(packet, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new WledException($"Could not stream to {_host}:{_port}.", ex);
        }
    }

    public void Dispose() => _udp.Dispose();
}
