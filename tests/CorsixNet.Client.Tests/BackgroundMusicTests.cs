using CorsixNet.Client.Audio;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Formats;

namespace CorsixNet.Client.Tests;

/// <summary>The background music (CorsixTH's audio.lua playlist and midi_player.cpp).</summary>
public class BackgroundMusicTests
{
    [Fact]
    public void ThePlayerSendsTheSongWithItsVolumeAndReportsItsEnd()
    {
        var output = new RecordingOutput();
        using var player = new MidiPlayer(output);
        player.SetVolume(0.5);

        // A program change, the channel volume at 100, a note one tick long, and two ticks later the end.
        player.PlayXmi(Song([0xC0, 0x05, 0xB0, 0x07, 0x64, 0x90, 0x3C, 0x64, 0x01, 0x02, 0xFF, 0x2F, 0x00]));
        Assert.True(SpinWait.SpinUntil(player.TakeMusicOver, TimeSpan.FromSeconds(5)));

        // play_xmi stops what played first (all notes and sound off, controllers reset), then General MIDI goes on.
        var all = output.Messages;
        var start = all.FindIndex(m => m.SequenceEqual(new byte[] { 0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7 }));
        Assert.Equal(48, start);
        var sent = all[start..];
        // init_playback: each channel at half of 127.
        Assert.Equal(16, sent.Count(m => m is [0xB0 or 0xB1 or 0xB2 or 0xB3 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8 or 0xB9 or 0xBA or 0xBB or 0xBC or 0xBD or 0xBE or 0xBF, 0x07, 63]));
        Assert.Contains(sent, m => m.SequenceEqual(new byte[] { 0xC0, 0x05 }));
        Assert.Contains(sent, m => m.SequenceEqual(new byte[] { 0xB0, 0x07, 50 }));
        Assert.Contains(sent, m => m.SequenceEqual(new byte[] { 0x90, 0x3C, 0x64 }));
        Assert.Contains(sent, m => m.SequenceEqual(new byte[] { 0x90, 0x3C, 0x00 }));
    }

    [Fact]
    public void ThePlaylistSkipsDisabledTracksAndPlaysTheNextWhenOneEnds()
    {
        var output = new RecordingOutput();
        var song = Song([0x90, 0x3C, 0x64, 0x01, 0xFF, 0x2F, 0x00]);
        var read = new List<string>();
        using var music = new BackgroundMusic(
            [new MusicTrack("a", "A"), new MusicTrack("b", "B"), new MusicTrack("c", "C")],
            new MidiPlayer(output),
            path =>
            {
                lock (read)
                {
                    read.Add(path);
                }

                return song;
            },
            new Random(1));
        Assert.True(music.HasMusic);

        music.Playlist[1].Enabled = false;
        music.Play(0);
        Assert.Equal(0, music.Current);

        // The track ends: the next enabled one, skipping the disabled second.
        Assert.True(SpinWait.SpinUntil(() =>
        {
            music.Update();
            return music.Current == 2;
        }, TimeSpan.FromSeconds(5)));
        music.PlayPrevious();
        Assert.Equal(0, music.Current);

        // Turned off it stops, and play_music keeps it from starting again.
        Assert.False(music.TogglePlayMusic());
        Assert.Null(music.Current);
        music.Play(1);
        Assert.Null(music.Current);
        Assert.True(music.TogglePlayMusic());
        Assert.NotNull(music.Current);
        Assert.NotEqual(1, music.Current);
    }

    [Fact]
    public void TheJukeboxLoopsATrackAndKeepsTheDisabledOnes()
    {
        var song = Song([0x90, 0x3C, 0x64, 0x01, 0x7F, 0x7F, 0xFF, 0x2F, 0x00]);
        using var music = new BackgroundMusic(
            [new MusicTrack(Path.Combine("x", "A.XMI"), "A"), new MusicTrack(Path.Combine("x", "B.XMI"), "B"), new MusicTrack(Path.Combine("x", "C.XMI"), "C")],
            new MidiPlayer(new RecordingOutput()),
            _ => song,
            new Random(1));
        music.ReadDisabledTracks([string.Join(Path.DirectorySeparatorChar, "Sound", "Midi", "C.XMI")]);
        Assert.Equal([true, true, false], music.Playlist.Select(entry => entry.Enabled));

        music.Play(0);
        music.ToggleLoop();
        Assert.True(music.Playlist[0].Loop);
        Assert.Equal([true, false, false], music.Playlist.Select(entry => entry.Enabled));
        music.ToggleLoop();
        Assert.Equal([true, true, false], music.Playlist.Select(entry => entry.Enabled));

        // Turning off the track playing plays another.
        music.ToggleTrack(0);
        Assert.Equal(1, music.Current);
        Assert.Equal([string.Join(Path.DirectorySeparatorChar, "Sound", "Midi", "A.XMI"), string.Join(Path.DirectorySeparatorChar, "Sound", "Midi", "C.XMI")], music.DisabledTracks());
    }

    [Fact]
    public void ALongTitleLosesFiveLettersAtATime()
    {
        Assert.Equal("Short", JukeboxScreen.Shorten("Short", text => text.Length * 10));
        Assert.Equal("ABCDEFGHIJKLMNO...", JukeboxScreen.Shorten("ABCDEFGHIJKLMNOPQRSTUVWX", text => text.Length * 10));
    }

    [Fact]
    public void WithoutAMidiOutputThereIsNoMusic()
    {
        using var music = new BackgroundMusic([new MusicTrack("a", "A")], null, _ => [], new Random(1));
        Assert.False(music.HasMusic);
        music.PlayRandom();
        Assert.Null(music.Current);
    }

    /// <summary>An XMI file with the given events.</summary>
    private static byte[] Song(byte[] events) =>
    [
        .. "FORM\0\0\0\x0EXDIRINFO\0\0\0\x02\x01\0CAT \0\0\0\0XMIDFORM\0\0\0\0XMIDTIMB\0\0\0\0EVNT"u8,
        0, 0, 0, (byte)events.Length,
        .. events,
    ];

    private sealed class RecordingOutput : IMidiOutput
    {
        private readonly List<byte[]> _messages = [];

        public List<byte[]> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public void Send(ReadOnlySpan<byte> message)
        {
            lock (_messages)
            {
                _messages.Add(message.ToArray());
            }
        }

        public void Dispose()
        {
        }
    }
}
