// The information and confirmation dialogs as the original draws them, ported from CorsixTH
// CorsixTH/Lua/dialogs/information.lua (UIInformation: the PulldV frame, the white box, the text, the close button) and
// dialogs/confirm_dialog.lua (UIConfirmDialog: Req04V's frame growing with the text, cancel and OK).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Maps;
using CorsixNet.Client.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// UIInformation's look: a white box in PulldV's border, 300 pixels of black text (Font00V) with 15 round it and the
/// close button in the bottom right corner.
/// </summary>
internal sealed class InformationScreen
{
    private const int TextWidth = 300;
    private const int Margin = 15;
    private const int Bottom = 18 + 15;

    private string? _pressed;

    public Point Size { get; private set; } = new(TextWidth + (2 * Margin), Margin + Bottom);

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("PULLDV", OriginalGraphics.MainPalette, "DATA") is not null
        && graphics.Font("FONT00V", OriginalGraphics.MainPalette) is not null;

    /// <summary>onChangeLanguage: the box is as tall as the wrapped text needs.</summary>
    public void Measure(IWindowHost host, IReadOnlyList<string> paragraphs)
    {
        var font = host.Graphics!.Font("FONT00V", OriginalGraphics.MainPalette)!;
        var lines = paragraphs.Sum(paragraph => MapViewScreen.WrapToWidth(paragraph, TextWidth, t => font.Measure(t).Width).Count);
        Size = new Point(TextWidth + (2 * Margin), Margin + (lines * font.LineHeight) + Bottom);
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, IReadOnlyList<string> paragraphs)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("PULLDV", OriginalGraphics.MainPalette, "DATA")!;
        var font = graphics.Font("FONT00V", OriginalGraphics.MainPalette)!;
        var (width, height) = (Size.X, Size.Y);
        batch.Draw(graphics.Pixel, new Rectangle(origin.X + 4, origin.Y + 4, width - 8, height - 8), Color.White);
        var y = origin.Y + Margin;
        foreach (var paragraph in paragraphs)
        {
            foreach (var line in MapViewScreen.WrapToWidth(paragraph, TextWidth, t => font.Measure(t).Width))
            {
                font.Draw(batch, line, new Vector2(origin.X + Margin, y));
                y += font.LineHeight;
            }
        }

        void Panel(int sprite, int x, int py) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + py, Color.White);
        for (var x = 4; x <= width - 4; x += 4)
        {
            Panel(12, x, 0);
            Panel(16, x, height - 4);
        }

        for (var py = 4; py <= height - 4; py += 4)
        {
            Panel(18, 0, py);
            Panel(14, width - 4, py);
        }

        Panel(11, 0, 0);
        Panel(17, 0, height - 4);
        Panel(13, width - 4, 0);
        Panel(15, width - 4, height - 4);
        var close = CloseArea();
        var local = new Point(mouse.X, mouse.Y) - origin;
        Panel(19, close.X, close.Y);
        if (close.Contains(local))
        {
            Panel(20, close.X, close.Y);
        }
    }

    /// <summary>A click on the close button closes it (and Enter, as the window's key).</summary>
    public bool Clicked(Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = CloseArea().Contains(local) ? "close" : null;
            return false;
        }

        var clicked = mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed && _pressed == "close" && CloseArea().Contains(local);
        if (mouse.LeftButton == ButtonState.Released)
        {
            _pressed = null;
        }

        return clicked;
    }

    private Rectangle CloseArea() => new(Size.X - 28, Size.Y - 28, 18, 18);
}

/// <summary>
/// UIConfirmDialog's look: Req04V's frame, 183 wide and taller by 11 for each line of the question (Font01V, 153
/// pixels wide), with cancel and OK below.
/// </summary>
internal sealed class ConfirmScreen
{
    private const int TextWidth = 153;

    private string? _pressed;
    private int _middles = 1;

    public Point Size => new(183, 22 + (_middles * 11) + 63);

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ04V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    /// <summary>How many middle pieces the question needs (ceil(text height / 11)).</summary>
    public void Measure(IWindowHost host, string question)
    {
        var font = host.Graphics!.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var lines = MapViewScreen.WrapToWidth(question, TextWidth, t => font.Measure(t).Width).Count;
        _middles = Math.Max(1, (int)Math.Ceiling(lines * font.LineHeight / 11.0));
    }

    private int LastY => 22 + (_middles * 11);

    private Rectangle CancelArea => new(8, LastY + 20, 82, 34);

    private Rectangle OkArea => new(90, LastY + 20, 82, 34);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, string question)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ04V", OriginalGraphics.MainPalette)!;
        var font = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);

        Panel(357, 0, 0);
        for (var i = 0; i < _middles; i++)
        {
            Panel(358, 0, 22 + (i * 11));
        }

        Panel(359, 0, LastY);
        Panel(held && _pressed == "cancel" && CancelArea.Contains(local) ? 361 : 360, 0, LastY + 10);
        Panel(held && _pressed == "ok" && OkArea.Contains(local) ? 363 : 362, 90, LastY + 10);
        var y = origin.Y + 17;
        foreach (var line in MapViewScreen.WrapToWidth(question, TextWidth, t => font.Measure(t).Width))
        {
            font.Draw(batch, line, new Vector2(origin.X + 17, y));
            y += font.LineHeight;
        }
    }

    /// <summary>The button let go over, "ok" or "cancel", if any.</summary>
    public string? Clicked(Point local, MouseState mouse, MouseState previous)
    {
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = OkArea.Contains(local) ? "ok" : CancelArea.Contains(local) ? "cancel" : null;
            return null;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return null;
        }

        var pressed = _pressed;
        _pressed = null;
        return pressed == "ok" && OkArea.Contains(local) ? "ok" : pressed == "cancel" && CancelArea.Contains(local) ? "cancel" : null;
    }

    public string? Tooltip(IWindowHost host, Point local) =>
        OkArea.Contains(local) ? OriginalTooltips.Get(host, 87, "Confirm") : CancelArea.Contains(local) ? OriginalTooltips.Get(host, 86, "Cancel") : null;
}
