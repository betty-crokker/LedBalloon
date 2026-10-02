using System.Buffers.Binary;
using System.Text;
using LedBalloon.Core;
using LedBalloon.Core.Effects;
using Xunit;

namespace LedBalloon.Core.Tests;

/// <summary>
/// Inventing a signal for the sound-reactive effects, and laying it out the way the firmware reads
/// it.
/// <para>
/// Neither controller here has a microphone, so two dozen of the effects they offer had nothing to
/// react to - and no way to be checked, because a port written from reading the firmware could not
/// be measured against hardware that could not be made to produce a reference picture. Sending the
/// audio makes the hardware the reference again.
/// </para>
/// </summary>
public class MusicNobodyIsPlayingTests
{
    [Fact]
    public void Silence_is_a_picture_rather_than_an_absence()
    {
        AudioFrame quiet = AudioFrame.Silence;

        // A sound effect fed silence is not an effect failing to draw. It is an effect drawing what
        // silence looks like, which is a real answer to "what would this do on my house".
        Assert.Equal(0, quiet.Volume);
        Assert.Equal(0, quiet.MajorPeakHz);
        Assert.False(quiet.Beat);
        Assert.All(Enumerable.Range(0, AudioFrame.BinCount), i => Assert.Equal(0, quiet.Bin(i)));
    }

    [Fact]
    public void The_beat_is_loudest_as_it_lands_and_mostly_gone_by_the_next()
    {
        AudioFrame onIt = SyntheticAudio.At(0);
        AudioFrame after = SyntheticAudio.At(0.25);
        AudioFrame justBefore = SyntheticAudio.At(0.49);

        Assert.True(onIt.Volume > after.Volume);
        Assert.True(after.Volume > justBefore.Volume);

        // And never all the way down: a room with music in it has a floor, so an effect reading the
        // volume is alive between beats rather than blinking.
        Assert.True(justBefore.Volume > 40);
    }

    [Fact]
    public void It_comes_round_again_at_the_tempo_it_was_asked_for()
    {
        // 120 to the minute is half a second, so these two are the same instant of the same bar.
        Assert.Equal(SyntheticAudio.At(0.1).Volume, SyntheticAudio.At(2.1).Volume, 6);

        // And at half the tempo the bar is twice as long: a second apart is the same instant there,
        // where at 120 it would be the next beat but one.
        Assert.Equal(SyntheticAudio.At(0.1, 60).Volume, SyntheticAudio.At(1.1, 60).Volume, 6);
        Assert.NotEqual(SyntheticAudio.At(0.1, 60).Volume, SyntheticAudio.At(0.6, 60).Volume, 6);
    }

    [Fact]
    public void The_bass_walks_so_that_the_frequency_effects_have_somewhere_to_go()
    {
        // Freqmap turns the loudest frequency into a position and Freqwave turns it into a hue. One
        // note would hold both of them still and make them look broken in a new way.
        double[] bars = [.. Enumerable.Range(0, 4).Select(b => SyntheticAudio.At((b * 0.5) + 0.3).MajorPeakHz)];

        Assert.Equal(4, bars.Distinct().Count());
        Assert.All(bars, hz => Assert.InRange(hz, 100, 260));
    }

    [Fact]
    public void The_bands_are_bass_heavy_on_the_kick_and_bright_on_the_off_beat()
    {
        AudioFrame kick = SyntheticAudio.At(0);
        AudioFrame hat = SyntheticAudio.At(0.26);

        // The kick is in the low bands.
        Assert.True(kick.Bin(0) > kick.Bin(12));

        // The hat is in the high ones, and it is the only thing up there.
        Assert.True(hat.Bin(15) > hat.Bin(0));
    }

    [Fact]
    public void The_same_instant_gives_the_same_frame_every_time()
    {
        // Which is what lets the strip in the panel and the run on the house be driven from one
        // signal and agree. A preview of an impression of the music would be a different picture.
        AudioFrame once = SyntheticAudio.At(1.234);
        AudioFrame twice = SyntheticAudio.At(1.234);

        Assert.Equal(once.Volume, twice.Volume);
        Assert.Equal(once.MajorPeakHz, twice.MajorPeakHz);
        Assert.Equal([.. once.Bins], [.. twice.Bins]);
    }

    [Fact]
    public void The_packet_is_laid_out_where_the_firmware_looks()
    {
        // Every offset here was confirmed against 192.168.0.131 on 0.15.3: it answered "UDP sound
        // sync - receiving" and named the format "v2", which it only does once a packet parses.
        var frame = new AudioFrame(
            Volume: 200,
            Bins: [.. Enumerable.Range(0, AudioFrame.BinCount).Select(i => (byte)(i * 16))],
            MajorPeakHz: 440,
            Magnitude: 1500,
            Beat: true);

        byte[] packet = new byte[WledAudioSync.PacketBytes];
        WledAudioSync.Write(packet, frame, counter: 7);

        Assert.Equal("00002", Encoding.ASCII.GetString(packet[..5]));
        Assert.Equal(0, packet[5]);
        Assert.Equal(200f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12)));
        Assert.Equal(1, packet[16]);
        Assert.Equal(7, packet[17]);
        Assert.Equal(16 * 3, packet[18 + 3]);
        Assert.Equal(1500f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(36)));
        Assert.Equal(440f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(40)));
    }

    [Fact]
    public void A_volume_outside_what_a_byte_could_mean_is_held_rather_than_wrapped()
    {
        byte[] packet = new byte[WledAudioSync.PacketBytes];

        WledAudioSync.Write(packet, AudioFrame.Silence with { Volume = 4000 }, 0);

        Assert.Equal(255f, BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(12)));
    }

    [Fact]
    public void A_packet_of_the_wrong_size_is_refused_here_rather_than_dropped_there()
    {
        // The firmware checks the length and ignores anything else without a word, which is a
        // silence that would be very hard to read from this end.
        Assert.Throws<ArgumentException>(() =>
            WledAudioSync.Write(new byte[20], AudioFrame.Silence, 0));
    }
}
