using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using LedBalloon.Core.Effects;

namespace LedBalloon.Core;

/// <summary>
/// Sends a controller what it is hearing, over the port its own sound sync uses.
/// </summary>
/// <remarks>
/// WLED's AudioReactive usermod can take its audio from the network instead of from a microphone,
/// so that one box with a microphone can drive a houseful of boxes without. Nothing says the sender
/// has to be a WLED box.
/// <para>
/// That is the whole trick here. Neither controller has a microphone, so its two dozen
/// sound-reactive effects had nothing to react to and no way to be checked - a port written from
/// reading the firmware could not be measured against hardware that could not be made to produce a
/// reference picture. Sending the audio makes the hardware the reference again: known signal in,
/// rendered pixels out over the live-preview socket, and the port has something to be wrong against.
/// </para>
/// <para>
/// It is also worth having on its own. A house with no microphone can run its sound-reactive
/// effects properly, to invented music, which is a thing it simply could not do before.
/// </para>
/// <para>
/// The controller has to be told to listen: its AudioReactive sync mode is off by default and has
/// to be set to receive. Measured against 0.15.3 - it reports "UDP sound sync - receiving" and
/// names the format "v2" once packets arrive.
/// </para>
/// </remarks>
public sealed class WledAudioSync : IDisposable
{
    /// <summary>The port WLED's audio sync uses unless somebody has moved it.</summary>
    public const int DefaultPort = 11988;

    /// <summary>
    /// The size of the packet the firmware expects, and the version it is.
    /// </summary>
    /// <remarks>
    /// Checked by length as well as by header on the receiving side, so a packet of the wrong size
    /// is dropped without a word. 44 bytes, laid out below.
    /// </remarks>
    public const int PacketBytes = 44;

    /// <summary>The v2 header, five characters and a terminator.</summary>
    private const string Header = "00002";

    private readonly UdpClient _udp = new();
    private byte _counter;

    public WledAudioSync(string host, int port = DefaultPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        // Unicast to the one controller rather than broadcast to the network. Measured: the usermod
        // picks it up either way, and a houseful of lights is not the place for a packet every 20
        // milliseconds going to everything on the subnet.
        Host = WledClient.NormalizeHost(host).Host;
        Port = port;

        _udp.Connect(Host, port);
    }

    public string Host { get; }

    public int Port { get; }

    /// <summary>
    /// Lays one audio frame out the way the firmware reads it.
    /// </summary>
    /// <remarks>
    /// The v2 layout, by offset:
    /// <list type="table">
    /// <item><term>0</term><description>header, 6 bytes, "00002" and a terminator</description></item>
    /// <item><term>6</term><description>sound pressure, 2 bytes, unread by the 1D effects</description></item>
    /// <item><term>8</term><description>the raw volume, a float</description></item>
    /// <item><term>12</term><description>the smoothed volume, a float - what the effects read</description></item>
    /// <item><term>16</term><description>the beat flag</description></item>
    /// <item><term>17</term><description>a rolling frame counter, so dropped packets can be seen</description></item>
    /// <item><term>18</term><description>the sixteen bands, one byte each</description></item>
    /// <item><term>34</term><description>zero crossings, 2 bytes</description></item>
    /// <item><term>36</term><description>the peak's magnitude, a float</description></item>
    /// <item><term>40</term><description>the loudest frequency in hertz, a float</description></item>
    /// </list>
    /// </remarks>
    public static void Write(Span<byte> packet, AudioFrame frame, byte counter)
    {
        if (packet.Length < PacketBytes)
        {
            throw new ArgumentException($"An audio sync packet is {PacketBytes} bytes.", nameof(packet));
        }

        packet[..PacketBytes].Clear();
        Encoding.ASCII.GetBytes(Header, packet);

        float volume = (float)Math.Clamp(frame.Volume, 0, 255);

        BinaryPrimitives.WriteSingleLittleEndian(packet[8..], volume);
        BinaryPrimitives.WriteSingleLittleEndian(packet[12..], volume);

        packet[16] = frame.Beat ? (byte)1 : (byte)0;
        packet[17] = counter;

        for (int i = 0; i < AudioFrame.BinCount; i++)
        {
            packet[18 + i] = frame.Bin(i);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(packet[34..], 200);
        BinaryPrimitives.WriteSingleLittleEndian(packet[36..], (float)frame.Magnitude);
        BinaryPrimitives.WriteSingleLittleEndian(packet[40..], (float)frame.MajorPeakHz);
    }

    /// <summary>Sends one frame.</summary>
    public async Task SendAsync(AudioFrame frame, CancellationToken cancellationToken = default)
    {
        byte[] packet = new byte[PacketBytes];
        Write(packet, frame, _counter++);

        await _udp.SendAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Plays the invented music at the controller until told to stop.
    /// </summary>
    /// <remarks>
    /// Every 20 milliseconds, which is a little faster than the controller draws. The usermod holds
    /// the last packet it got, so sending slower than the frame rate would show as the effect
    /// stepping rather than moving; sending much faster buys nothing.
    /// </remarks>
    public async Task PlayAsync(
        double beatsPerMinute = SyntheticAudio.DefaultBeatsPerMinute,
        CancellationToken cancellationToken = default)
    {
        var since = System.Diagnostics.Stopwatch.StartNew();
        using var clock = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        try
        {
            while (await clock.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendAsync(SyntheticAudio.At(since.Elapsed, beatsPerMinute), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Asked to stop. The controller goes quiet on its own a second or so later.
        }
    }

    public void Dispose()
    {
        _udp.Dispose();
        GC.SuppressFinalize(this);
    }
}
