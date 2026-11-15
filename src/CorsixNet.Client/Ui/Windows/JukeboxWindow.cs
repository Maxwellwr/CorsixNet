// The jukebox, ported from CorsixTH CorsixTH/Lua/dialogs/jukebox.lua (UIJukebox: its panels of Req13V, the play,
// rewind, fast forward, stop and loop buttons, the track list with its switches, the titles, the tooltips).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Audio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The jukebox (<c>UIJukebox</c>): the title playing, play or pause, the previous and next track, stop (which turns the
/// music off for later too), loop, and every track with a switch to leave it out. Opened from the options menu or with
/// J (<c>ingame_jukebox</c>), when there is music.
/// </summary>
/// <param name="tracks">How many tracks the playlist has, for the window's height.</param>
internal sealed class JukeboxWindow(int tracks) : UiWindow
{
    private readonly JukeboxScreen _screen = new();

    public override string Title => UiText.Get("menu_options.jukebox", "Jukebox").Replace("(%1%)", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary><c>setDefaultPosition(26, 26)</c>.</summary>
    public override Vector2? DefaultPosition => new(26, 26);

    public override bool IsFullscreen(IWindowHost host) => JukeboxScreen.IsAvailable(host);

    public override Point FullscreenSize => JukeboxScreen.SizeFor(tracks);

    public override Vector2? FullscreenPosition => new(26, 26);

    public override bool IsDone(IWindowHost host) => host.Music is not { HasMusic: true };

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => JukeboxScreen.Tooltip(host, local);

    public override bool IsSameAs(UiWindow other) => other is JukeboxWindow;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (host.Music is not { } music)
        {
            yield break;
        }

        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        var playing = music.Current is not null && !music.IsPaused;
        yield return WindowCell.Line(music.Current is { } current ? music.Playlist[current].Track.Title : string.Empty, highlight: true);
        yield return WindowRow.Of(
            WindowCell.Button(playing ? "||" : ">", () => JukeboxScreen.TogglePlayPause(host), highlight: playing).WithTooltip(Tip(205, "Play")),
            WindowCell.Button("<<", music.PlayPrevious).WithTooltip(Tip(206, "Rewind")),
            WindowCell.Button(">>", music.PlayNext).WithTooltip(Tip(207, "Fast forward")),
            WindowCell.Button("[]", () => JukeboxScreen.Stop(host)).WithTooltip(Tip(208, "Stop")),
            WindowCell.Button("@", music.ToggleLoop, highlight: music.Current is { } looped && music.Playlist[looped].Loop).WithTooltip(Tip(209, "Loop")));
        for (var i = 0; i < music.Playlist.Count; i++)
        {
            var index = i;
            var entry = music.Playlist[i];
            yield return WindowRow.Of(
                new WindowCell(entry.Track.Title, Highlight: music.Current == i),
                WindowCell.Button(entry.Enabled ? UiText.Get("options_window.option_on", "On") : UiText.Get("options_window.option_off", "Off"), () => JukeboxScreen.ToggleTrack(host, index), highlight: entry.Enabled));
        }
    }
}

/// <summary>
/// The jukebox in Req13V's sprites, 259 pixels wide: the head with the title and the close button, the row of buttons,
/// a row of 30 pixels for each track with its switch, and the foot.
/// </summary>
internal sealed class JukeboxScreen
{
    public const int Width = 259;

    private static readonly Rectangle CloseButton = new(Width - 42, 19, 24, 24);
    private static readonly Rectangle PlayButton = new(19, 51, 50, 24);
    private static readonly Rectangle RewindButton = new(87, 51, 24, 24);
    private static readonly Rectangle ForwardButton = new(115, 51, 24, 24);
    private static readonly Rectangle StopButton = new(157, 51, 24, 24);
    private static readonly Rectangle LoopButton = new(185, 51, 24, 24);
    private static readonly Rectangle CurrentTitle = new(17, 17, 212, 46);

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ13V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null && graphics.Font("FONT02V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The window's size for so many tracks: 74 + 30 a track + 18 high.</summary>
    public static Point SizeFor(int tracks) => new(Width, 74 + (30 * tracks) + 18);

    /// <summary>A track's switch: in the last panel of its row (at y = 47 + 30 × its number from 1).</summary>
    private static Rectangle TrackButton(int index) => new(Width - 61 + 19, 47 + ((index + 1) * 30) + 4, 24, 24);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        if (host.Music is not { } music || host.Graphics is not { } graphics)
        {
            return;
        }

        var sheet = graphics.Sheet("REQ13V", OriginalGraphics.MainPalette)!;
        var white = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var blue = graphics.Font("FONT02V", OriginalGraphics.MainPalette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var down = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        bool Held(string key, Rectangle area) => down && _pressed == key && area.Contains(local);
        void Row(int left, int middle, int right, int y)
        {
            Panel(left, 0, y);
            for (var x = 30; x <= Width - 61; x += 24)
            {
                Panel(middle, x, y);
            }

            Panel(right, Width - 61, y);
        }

        // The head and its close button.
        Row(389, 390, 391, 0);
        Panel(Held("close", CloseButton) ? 410 : 409, Width - 42, 19);

        // The buttons: play shows pressed while a track plays and is not paused (updatePlayButton).
        var playing = music.Current is not null && !music.IsPaused;
        Panel(playing != Held("play", PlayButton) ? 393 : 392, 0, 49);
        Panel(Held("rewind", RewindButton) ? 395 : 394, 87, 49);
        Panel(Held("forward", ForwardButton) ? 397 : 396, 115, 49);
        Panel(Held("stop", StopButton) ? 399 : 398, 157, 49);
        Panel(Held("loop", LoopButton) ? 401 : 400, 185, 49);

        // The tracks, a disabled one's switch pressed; the title playing in blue, here and in the head.
        for (var i = 0; i < music.Playlist.Count; i++)
        {
            var y = 47 + ((i + 1) * 30);
            Row(402, 403, 404, y);
            var entry = music.Playlist[i];
            var key = "track" + i;
            Panel(!entry.Enabled != Held(key, TrackButton(i)) ? 405 : 404, Width - 61, y);
            var font = music.Current == i ? blue : white;
            var title = Shorten(entry.Track.Title, text => font.Measure(text).Width);
            font.Draw(batch, title, new Vector2(origin.X + 24, origin.Y + y + 11));
            if (music.Current == i)
            {
                font.Draw(batch, title, new Vector2(origin.X + 24, origin.Y + 27));
            }
        }

        Row(406, 407, 408, 74 + (30 * music.Playlist.Count));
    }

    /// <summary>UIJukebox:draw: a title wider than 185 pixels loses five letters at a time and gets "...".</summary>
    public static string Shorten(string title, Func<string, int> width)
    {
        var text = title;
        while (width(text) > 185 && text.Length > 0)
        {
            text = text[..Math.Max(0, text.Length - 5)] + "...";
            if (text == "...")
            {
                break;
            }
        }

        return text;
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        if (host.Music is not { } music)
        {
            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = KeyAt(music, local);
            return;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != KeyAt(music, local))
        {
            return;
        }

        // Every button but close plays selectx.wav (setSound); close has the window's close sound.
        host.PlaySound(pressed == "close" ? "no4.wav" : WindowLayer.DefaultButtonSound);
        switch (pressed)
        {
            case "close":
                host.Close(window);
                break;
            case "play":
                TogglePlayPause(host);
                break;
            case "rewind":
                music.PlayPrevious();
                break;
            case "forward":
                music.PlayNext();
                break;
            case "stop":
                Stop(host);
                break;
            case "loop":
                music.ToggleLoop();
                host.SavePlaylist();
                break;
            default:
                ToggleTrack(host, int.Parse(pressed["track".Length..], System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }

    /// <summary>
    /// <c>togglePlayPause</c>: with nothing playing, music is wanted again (kept in the settings) and a random track
    /// starts; otherwise the track pauses or goes on, leaving the setting alone.
    /// </summary>
    public static void TogglePlayPause(IWindowHost host)
    {
        if (host.Music is not { } music)
        {
            return;
        }

        if (music.Current is null)
        {
            host.SetPlayMusic(true);
            music.PlayRandom();
        }
        else
        {
            music.TogglePause();
        }
    }

    /// <summary><c>stopBackgroundTrack</c>: no music now or later (play_music off, kept in the settings).</summary>
    public static void Stop(IWindowHost host)
    {
        host.SetPlayMusic(false);
        host.Music?.Stop();
    }

    /// <summary><c>toggleTrack</c>, the playlist's preferences saved.</summary>
    public static void ToggleTrack(IWindowHost host, int index)
    {
        host.Music?.ToggleTrack(index);
        host.SavePlaylist();
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return local switch
        {
            _ when CloseButton.Contains(local) => Tip(204, "Close"),
            _ when PlayButton.Contains(local) => Tip(205, "Play"),
            _ when RewindButton.Contains(local) => Tip(206, "Rewind"),
            _ when ForwardButton.Contains(local) => Tip(207, "Fast forward"),
            _ when StopButton.Contains(local) => Tip(208, "Stop"),
            _ when LoopButton.Contains(local) => Tip(209, "Loop"),
            _ when CurrentTitle.Contains(local) => Tip(203, "Current title"),
            _ => null,
        };
    }

    private static string? KeyAt(BackgroundMusic music, Point local)
    {
        if (CloseButton.Contains(local))
        {
            return "close";
        }

        if (PlayButton.Contains(local))
        {
            return "play";
        }

        if (RewindButton.Contains(local))
        {
            return "rewind";
        }

        if (ForwardButton.Contains(local))
        {
            return "forward";
        }

        if (StopButton.Contains(local))
        {
            return "stop";
        }

        if (LoopButton.Contains(local))
        {
            return "loop";
        }

        for (var i = 0; i < music.Playlist.Count; i++)
        {
            if (TrackButton(i).Contains(local))
            {
                return "track" + i;
            }
        }

        return null;
    }
}
