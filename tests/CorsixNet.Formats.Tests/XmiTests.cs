using System.Text;

namespace CorsixNet.Formats.Tests;

public class XmiTests
{
    [Fact]
    public void ConvertsEventsToASingleTrackMidiFile()
    {
        byte[] events =
        [
            0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, // tempo 500,000
            0x3C, // 60 ticks
            0xC0, 0x05, // program change
            0x90, 0x3C, 0x64, 0x78, // note 60 for 120 ticks
            0x7F, 0x7F, 0x02, // 256 ticks
            0x91, 0x40, 0x50, 0x10, // note 64 for 16 ticks, ending after the end of the track
            0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, // a second tempo, dropped
            0xFF, 0x2F, 0x00,
        ];

        var midi = Xmi.ToMidi(Build(events));

        byte[] track =
        [
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,
            0xAE, 0x70, 0xC0, 0x05, // 6,000: 60 ticks times 100
            0x00, 0x90, 0x3C, 0x64,
            0xDD, 0x60, 0x3C, 0x00, // 18,000: the note's end, with running status
            0xEA, 0x20, 0x91, 0x40, 0x50,
            0x00, 0xFF, 0x2F, 0x00, // the end of the track comes before the second note's end, which is lost
        ];
        byte[] expected =
        [
            .. "MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1,
            0x17, 0x70, // division: 500,000 × 0.012 = 6,000 ticks a quarter note
            .. "MTrk"u8, 0, 0, 0, (byte)track.Length,
            .. track,
        ];
        Assert.Equal(expected, midi);
    }

    [Fact]
    public void WithoutATempoTheDefaultIsUsedAndSysExIsKept()
    {
        var midi = Xmi.ToMidi(Build([0xF0, 0x02, 0x7E, 0xF7, 0xFF, 0x2F, 0x00]));

        Assert.NotNull(midi);
        Assert.Equal([0x17, 0x70], midi[12..14]);
        Assert.Equal([0x00, 0xF0, 0x02, 0x7E, 0xF7, 0x00, 0xFF, 0x2F, 0x00], midi[22..]);
    }

    [Fact]
    public void NoEventsMeanNoMidi()
    {
        Assert.Null(Xmi.ToMidi(Build([])));
    }

    [Fact]
    public void RejectsDataWithoutEventsOrEndingEarly()
    {
        Assert.Throws<InvalidDataException>(() => Xmi.ToMidi("FORM\0\0\0\0XDIR"u8));
        Assert.Throws<InvalidDataException>(() => Xmi.ToMidi(Build([0x10])));
        Assert.Throws<InvalidDataException>(() => Xmi.ToMidi(Build([0x90, 0x3C])));
        Assert.Throws<InvalidDataException>(() => Xmi.ToMidi(Build([0xFF, 0x01, 0x05, 0x41])));
    }

    [Fact]
    public void ConvertsTheRealMusic()
    {
        var install = RealGameData.Require();
        var tracks = install.MusicTracks();

        Assert.NotEmpty(tracks);
        Assert.All(tracks, track =>
        {
            var midi = Xmi.ToMidi(File.ReadAllBytes(track.Path));
            Assert.NotNull(midi);
            Assert.Equal("MThd"u8.ToArray(), midi[..4]);
            Assert.Equal([0xFF, 0x2F, 0x00], midi[^3..]);
        });
    }

    /// <summary>An XMI file as the original's: the FORM, CAT and XMID chunks, an empty TIMB chunk, then EVNT.</summary>
    private static byte[] Build(byte[] events)
    {
        var data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes("FORM\0\0\0\x0EXDIRINFO\0\0\0\x02\x01\0CAT \0\0\0\0XMIDFORM\0\0\0\0XMIDTIMB\0\0\0\0EVNT"));
        data.AddRange([0, 0, 0, (byte)events.Length]);
        data.AddRange(events);
        return [.. data];
    }
}
