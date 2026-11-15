namespace CorsixNet.Formats.Tests;

public class StandardMidiFileTests
{
    [Fact]
    public void MergesTheTracksAndAppliesTheTempo()
    {
        // Division 96 ticks a quarter note. Track 1: tempo 1,000,000 µs a quarter note at tick 96. Track 2: a note at
        // 0, with running status its end at 96 (0.5 s at the default tempo) and a program change at 192 (1.5 s).
        byte[] track1 = [0x60, 0xFF, 0x51, 0x03, 0x0F, 0x42, 0x40, 0x00, 0xFF, 0x2F, 0x00];
        byte[] track2 = [0x00, 0x90, 0x3C, 0x64, 0x60, 0x3C, 0x00, 0x60, 0xC0, 0x05, 0x00, 0xF0, 0x02, 0x7E, 0xF7, 0x00, 0xFF, 0x2F, 0x00];
        byte[] file =
        [
            .. "MThd"u8, 0, 0, 0, 6, 0, 1, 0, 2, 0, 96,
            .. "MTrk"u8, 0, 0, 0, (byte)track1.Length, .. track1,
            .. "MTrk"u8, 0, 0, 0, (byte)track2.Length, .. track2,
        ];

        var messages = StandardMidiFile.Messages(file);

        Assert.Equal(
            [(0L, "903C64"), (500_000L, "903C00"), (1_500_000L, "C005"), (1_500_000L, "F07EF7")],
            messages.Select(m => (m.Microseconds, Convert.ToHexString(m.Message))));
    }

    [Fact]
    public void RejectsWhatIsNotAMidiFile()
    {
        Assert.Throws<InvalidDataException>(() => StandardMidiFile.Messages("RIFF0000"u8));
        Assert.Throws<InvalidDataException>(() => StandardMidiFile.Messages([.. "MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1, 0xE7, 0x28]));
    }

    [Fact]
    public void TheOwnMusicFolderGivesOneTrackANameRecordedFirstTitledByItsList()
    {
        var folder = Directory.CreateTempSubdirectory("corsixnet-music").FullName;
        try
        {
            foreach (var name in new[] { "song.mid", "song.ogg", "tune.MID", "other.txt", "extra.mp3" })
            {
                File.WriteAllBytes(Path.Combine(folder, name), []);
            }

            File.WriteAllText(Path.Combine(folder, "names.txt"), "TUNE.MID\nA Tune\nsong.ogg\nThe Song\n");
            var tracks = ThemeHospitalInstall.OwnMusicTracks(folder, ["OGG", "MP3"], ["MID"]);

            Assert.Equal(["A Tune", "The Song", "extra"], tracks.Select(track => track.Title));
            Assert.Equal("song.ogg", Path.GetFileName(tracks[1].Path));
            Assert.Empty(ThemeHospitalInstall.OwnMusicTracks(Path.Combine(folder, "missing"), ["OGG"], []));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
