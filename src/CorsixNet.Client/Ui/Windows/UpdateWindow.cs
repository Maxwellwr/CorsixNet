// The window telling of a newer version, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/update.lua (UIUpdate: 320
// by 320, the caption, this version in red and the new one in green, what changed, Download and Ignore).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary><c>UIUpdate</c>: a newer version is out; Download opens its page, Ignore closes the window.</summary>
/// <param name="update">The version found.</param>
/// <param name="open">Opens a web page (<c>buttonDownload</c>).</param>
internal sealed class UpdateWindow(UpdateInfo update, Action<string> open) : UiWindow
{
    public static readonly Point Size = new(320, 320);

    private static readonly Color OldVersion = new(255, 0, 0);
    private static readonly Color NewVersion = new(0, 255, 0);

    private readonly ResizablePanels _panels = new();

    public UpdateInfo Update => update;

    public override string Title => UiText.Get("update_window.caption", "Update Available!");

    public override bool UsesResizableLook => true;

    public override bool IsSameAs(UiWindow other) => other is UpdateWindow;

    public override Vector2? DefaultPosition => new(0.5f, 0.25f);

    public override Vector2? FullscreenPosition => new(0.5f, 0.25f);

    public override bool IsFullscreen(IWindowHost host) => ResizablePanels.IsAvailable(host);

    public override Point FullscreenSize => Size;

    private List<DialogPanel> Panels(IWindowHost host) =>
    [
        DialogPanel.Title(new Rectangle(80, 10, 160, 20), Title),
        DialogPanel.Caption(new Rectangle(20, 50, 140, 20), UiText.Get("update_window.current_version", "Current Version:")),
        DialogPanel.Caption(new Rectangle(20, 70, 140, 20), UiText.Get("update_window.new_version", "New Version:")),
        new DialogPanel(new Rectangle(160, 50, 140, 20), update.CurrentVersion, OldVersion, Highlight: ResizableLook.PanelDefault, Shadow: ResizableLook.PanelDefault),
        new DialogPanel(new Rectangle(160, 70, 140, 20), update.NewVersion, NewVersion, Highlight: ResizableLook.PanelDefault, Shadow: ResizableLook.PanelDefault),
        DialogPanel.Button(new Rectangle(20, 225, 280, 40), UiText.Get("update_window.download", "Go to download page"), _ => Download(host),
            UiText.Get("tooltip.update_window.download", "Go to the download page for the very latest version")),
        DialogPanel.Button(new Rectangle(20, 270, 280, 40), UiText.Get("update_window.ignore", "Skip and go to main menu"), _ => host.Close(this),
            UiText.Get("tooltip.update_window.ignore", "Ignore this update for now. You will be notified again when you next open the game")),
    ];

    /// <summary><c>buttonDownload</c>: the release's page opens, and the window closes.</summary>
    private void Download(IWindowHost host)
    {
        open(update.DownloadUrl);
        host.Close(this);
    }

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        _panels.Draw(batch, host, origin, Size, Panels(host), mouse);

        // UIUpdate:draw: what changed, wrapped, from (20, 100); as much as fits above the buttons.
        var font = ResizablePanels.Font(host);
        var top = origin.Y + 100;
        foreach (var line in update.Description.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')
            .SelectMany(paragraph => Maps.MapViewScreen.WrapToWidth(paragraph, Size.X - 40, text => font.Measure(text).Width)))
        {
            if (top + font.LineHeight > origin.Y + 220)
            {
                break;
            }

            font.Draw(batch, line, new Vector2(origin.X + 20, top));
            top += font.LineHeight;
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) =>
        _panels.Mouse(host, Panels(host), local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResizablePanels.Tooltip(Panels(host), local);

    public override Point? FindFullscreenButton(IWindowHost host, string text, int nth) => ResizablePanels.FindButton(Panels(host), text, nth);

    /// <summary>Without CorsixTH's sprites: the two versions, what changed, Download and Ignore.</summary>
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        yield return WindowRow.Of(UiText.Get("update_window.current_version", "Current Version:"), update.CurrentVersion);
        yield return WindowRow.Of(UiText.Get("update_window.new_version", "New Version:"), update.NewVersion);
        foreach (var line in update.Description.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Take(12))
        {
            yield return line;
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("update_window.download", "Go to download page"), () => Download(host)),
            WindowCell.Button(UiText.Get("update_window.ignore", "Skip and go to main menu"), () => host.Close(this)));
    }
}
