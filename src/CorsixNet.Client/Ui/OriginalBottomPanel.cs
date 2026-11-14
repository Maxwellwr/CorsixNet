// The bottom panel as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/bottom_panel.lua (drawPanels,
// draw, drawReputationMeter, drawDynamicInfo, showAdditionalButtons) and dialogs/message.lua (the fax icons).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui;

/// <summary>What the bottom panel shows, gathered by the screen each frame.</summary>
/// <param name="Balance">The hospital's balance.</param>
/// <param name="Reputation">The hospital's reputation, from 0 to 1000.</param>
/// <param name="Date">The day and month (<c>_S.date_format.daymonth</c>).</param>
/// <param name="Info">The dynamic info of what is under the cursor, or null.</param>
/// <param name="Pause">The pause text while the game is paused, or null.</param>
/// <param name="Faxes">The kinds of the faxes waiting (<see cref="OriginalBottomPanel.FaxIcon"/>), at most five shown.</param>
/// <param name="IsOn">Whether a button's dialog is open, so the button shows pressed (toggle buttons).</param>
/// <param name="IsLit">Whether the tutorial's flashing button is lit now.</param>
internal sealed record BottomPanelState(
    long Balance,
    int Reputation,
    string Date,
    string? Info,
    string? Pause,
    IReadOnlyList<string> Faxes,
    Func<string, bool> IsOn,
    Func<string, bool> IsLit);

/// <summary>
/// The original's bottom panel, 640 by 48 pixels at the bottom middle of the screen: the bank button, the balance and
/// reputation meter, the date, the fax door, the build, corridor, edit and hire buttons, and the dynamic info bar that
/// gives way to the buttons of the fullscreen dialogs while the cursor is over the panel. Waiting faxes stand on its
/// left as icons. Clicks give the buttons' keys to the screen.
/// </summary>
internal sealed class OriginalBottomPanel : IDisposable
{
    public const int Width = 640;
    public const int Height = 48;

    /// <summary>A fax icon's size, and how far above the panel they stand (UIMessage's 30 by 28, stop_y -24).</summary>
    private const int FaxWidth = 30;
    private const int FaxHeight = 28;
    private const int FaxRise = 24;
    private const int MaxFaxes = 5;

    private static readonly Color TooltipColor = new(40, 40, 24, 235);

    /// <summary>The buttons: the panel's own, then those shown over the dynamic info bar while hovering.</summary>
    private static readonly PanelButton[] Buttons =
    [
        new("bank", "Bank", 1, 0, new Rectangle(6, 6, 35, 36), 2, Hover: false, "Bank Manager", null, "ingame_panel_bankManager"),
        new("rooms", "Build", 4, 206, new Rectangle(6, 6, 35, 36), 5, Hover: false, "Build rooms", "build", "ingame_panel_buildRoom"),
        new("corridor", "Corridor", 6, 248, new Rectangle(1, 6, 35, 36), 7, Hover: false, "Furnish corridor", "corridor", "ingame_panel_furnishCorridor"),
        new("edit", "Edit room", 8, 285, new Rectangle(1, 6, 35, 36), 9, Hover: false, "Edit rooms/items", null, "ingame_panel_editRoom"),
        new("hire", "Hire", 10, 322, new Rectangle(1, 6, 35, 36), 11, Hover: false, "Hire staff", "hire", "ingame_panel_hireStaff"),
        new("staff", "Staff", 15, 364, new Rectangle(6, 6, 35, 36), 16, Hover: true, "Staff management", null, "ingame_panel_staffManage"),
        new("town", "Town", 17, 407, new Rectangle(1, 6, 35, 36), 18, Hover: true, "Town map", null, "ingame_panel_townMap"),
        new("casebook", "Casebook", 19, 445, new Rectangle(1, 6, 35, 36), 20, Hover: true, "Drug casebook", null, "ingame_panel_casebook"),
        new("research", "Research", 21, 483, new Rectangle(1, 6, 35, 36), 22, Hover: true, "Research", null, "ingame_panel_research"),
        new("status", "Status", 23, 521, new Rectangle(1, 6, 35, 36), 24, Hover: true, "Status", null, "ingame_panel_status"),
        new("charts", "Charts", 25, 559, new Rectangle(1, 6, 35, 36), 26, Hover: true, "Charts", null, "ingame_panel_charts"),
        new("policy", "Policy", 27, 597, new Rectangle(1, 6, 35, 36), 28, Hover: true, "Policy", null, "ingame_panel_policy"),
    ];

    private readonly SpriteAtlas _sprites;
    private readonly TextRenderer _moneyFont;
    private readonly TextRenderer _dateFont;
    private readonly TextRenderer _whiteFont;
    private readonly TextRenderer _pauseFont;
    private string? _pressed;

    public OriginalBottomPanel(GraphicsDevice device, ThemeHospitalInstall install, Palette palette)
    {
        _sprites = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "PANEL02V", complex: true), palette);
        _moneyFont = Font(device, install, "FONT05V", palette);
        _dateFont = Font(device, install, "FONT16V", palette);
        _whiteFont = Font(device, install, "FONT01V", palette);
        _pauseFont = Font(device, install, "FONT124V", palette);
    }

    /// <summary>Where the panel is: centred at the bottom of the screen (<c>setDefaultPosition(0.5, -0.1)</c>).</summary>
    public static Rectangle Bounds(Viewport viewport) => new((viewport.Width - Width) / 2, viewport.Height - Height, Width, Height);

    /// <summary>
    /// The fax icon of a fax's kind (UIMessage's types): emergency 43, epidemic 45, strike 47, personality 49,
    /// information 51, disease 53, report 55.
    /// </summary>
    public static int FaxIcon(string kind) => kind switch
    {
        "emergency" => 43,
        "epidemy" => 45,
        "strike" => 47,
        "personality" => 49,
        "disease" => 53,
        "report" => 55,
        _ => 51,
    };

    /// <summary>The balance's digits and where each goes from the panel's left (<c>UIBottomPanel:draw</c>).</summary>
    public static IEnumerable<(char Digit, int X)> BalanceDigits(long balance)
    {
        var text = balance.ToString(CultureInfo.InvariantCulture);
        var i = 7 - text.Length;
        foreach (var digit in text)
        {
            yield return (digit, 44 + (i * 13));
            i++;
        }
    }

    /// <summary>Where the reputation meter's marker goes from the panel's left (<c>drawReputationMeter</c>).</summary>
    public static int ReputationX(int reputation) => 55 + (int)Math.Floor(65.0 / 1000 * Math.Clamp(reputation, 0, 1000));

    /// <summary>
    /// Draws the bitmap fonts' text with the face from now on, at least so many pixels to the em, or the bitmap fonts
    /// again (null).
    /// </summary>
    public void UseTrueType(TrueTypeFace? face, int minimumSize = 8)
    {
        _dateFont.UseTrueType(face, minimumSize);
        _whiteFont.UseTrueType(face, minimumSize);
        _pauseFont.UseTrueType(face, minimumSize);
    }

    /// <summary>
    /// Follows the mouse: a button pressed and let go over it gives its key to <paramref name="act"/>, as does a fax
    /// icon ("fax"); a right click on the bank opens the statement ("bankstats").
    /// </summary>
    /// <returns>Whether the mouse is over the panel or a fax icon, so the map does not get it.</returns>
    public bool Update(Viewport viewport, MouseState mouse, MouseState previous, int faxes, Action<string> act)
    {
        var at = new Point(mouse.X, mouse.Y);
        var bounds = Bounds(viewport);
        var over = bounds.Contains(at) || FaxAt(viewport, at, faxes) is not null;
        var hovering = bounds.Contains(at);
        var button = ButtonAt(bounds, at, hovering);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = button?.Key ?? (FaxAt(viewport, at, faxes) is not null ? "fax" : null);
        }
        else if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var released = button?.Key ?? (FaxAt(viewport, at, faxes) is not null ? "fax" : null);
            if (_pressed is not null && _pressed == released)
            {
                act(_pressed);
            }

            _pressed = null;
        }

        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released && button?.Key == "bank")
        {
            act("bankstats");
        }

        return over;
    }

    /// <summary>
    /// The tooltip of the button under the mouse, if any, with its key in brackets (CorsixTH's tooltip.toolbar texts and
    /// <c>hotkey_value_label</c>).
    /// </summary>
    public static string? TooltipAt(Viewport viewport, Point mouse, Func<string, string?> hotkeyText)
    {
        var bounds = Bounds(viewport);
        return ButtonAt(bounds, mouse, bounds.Contains(mouse)) is { } button
            ? hotkeyText(button.Hotkey) is { } key ? $"{button.Tooltip} ({key})" : button.Tooltip
            : null;
    }

    /// <summary>The middle of the button labelled <paramref name="label"/>, for scripts (see <c>InputScript</c>).</summary>
    public static Point? FindButton(Viewport viewport, string label)
    {
        var bounds = Bounds(viewport);
        return Buttons.FirstOrDefault(button => string.Equals(button.Label, label, StringComparison.OrdinalIgnoreCase)) is { } found
            ? new Point(bounds.X + found.X + found.Hit.Center.X, bounds.Y + found.Hit.Center.Y)
            : null;
    }

    public void Draw(SpriteBatch batch, Viewport viewport, BottomPanelState state, Point mouse, MouseState buttons)
    {
        var bounds = Bounds(viewport);
        int x = bounds.X, y = bounds.Y;
        var hovering = bounds.Contains(mouse);

        // drawPanels: the background pieces, the buttons and the dynamic info bar.
        _sprites.Draw(batch, 3, x + 40, y, Color.White);
        _sprites.Draw(batch, 12, x + 364, y, Color.White);
        for (var dx = 377; dx <= 630; dx += 10)
        {
            _sprites.Draw(batch, 13, x + dx, y, Color.White);
        }

        _sprites.Draw(batch, 14, x + 627, y, Color.White);
        var under = ButtonAt(bounds, mouse, hovering);
        foreach (var button in Buttons)
        {
            if (button.Hover && !hovering)
            {
                continue;
            }

            var down = (_pressed == button.Key && under?.Key == button.Key && buttons.LeftButton == ButtonState.Pressed)
                || state.IsOn(button.Key)
                || (button.Lit is { } lit && state.IsLit(lit));
            // A pressed button's panel shows its other sprite (Window:makeButtonOnPanel's sprite_index_active).
            _sprites.Draw(batch, down ? button.Pressed : button.Sprite, x + button.X, y, Color.White);
        }

        foreach (var (digit, dx) in BalanceDigits(state.Balance))
        {
            _moneyFont.Draw(batch, digit.ToString(), new Vector2(x + dx, y + 9));
        }

        var dateWidth = _dateFont.Measure(state.Date).Width;
        _dateFont.Draw(batch, state.Date, new Vector2(x + 140 + ((60 - dateWidth) / 2), y + 20), tint: _dateFont.IsTrueTypeFor(state.Date) ? new Color(175, 50, 15) : null);

        if (!hovering)
        {
            DrawDynamicInfo(batch, x + 364, y, state);
        }

        // The fax door, shut while no fax waits.
        if (state.Faxes.Count == 0)
        {
            _sprites.Draw(batch, 40, x + 177, y + 1, Color.White);
            for (var dx = 0; dx <= 22; dx++)
            {
                _sprites.Draw(batch, 41, x + 179 + dx, y + 1, Color.White);
            }

            _sprites.Draw(batch, 42, x + 201, y + 1, Color.White);
        }

        _sprites.Draw(batch, 36, x + ReputationX(state.Reputation), y + 35, Color.White);

        // The fax icons on the left, standing above the panel.
        for (var i = 0; i < Math.Min(MaxFaxes, state.Faxes.Count); i++)
        {
            var icon = FaxIcon(state.Faxes[i]);
            var area = FaxArea(viewport, i);
            var down = state.IsOn("fax") || (_pressed == "fax" && area.Contains(mouse) && buttons.LeftButton == ButtonState.Pressed);
            _sprites.Draw(batch, down ? icon + 1 : icon, area.X, area.Y, Color.White);
        }
    }

    /// <summary>Draws a tooltip above the cursor, kept on the screen (CorsixTH's <c>UI:drawTooltip</c>).</summary>
    public static void DrawTooltip(SpriteBatch batch, TextRenderer text, Texture2D pixel, Viewport viewport, Point mouse, string tooltip)
    {
        var width = text.Measure(tooltip).Width + 8;
        var height = text.LineHeight + 6;
        var x = Math.Clamp(mouse.X - (width / 2), 0, Math.Max(0, viewport.Width - width));
        var y = Math.Max(0, mouse.Y - height - 4);
        batch.Draw(pixel, new Rectangle(x, y, width, height), TooltipColor);
        text.Draw(batch, tooltip, new Vector2(x + 4, y + 3));
    }

    public void Dispose()
    {
        _sprites.Dispose();
        _moneyFont.Dispose();
        _dateFont.Dispose();
        _whiteFont.Dispose();
        _pauseFont.Dispose();
    }

    /// <summary>
    /// drawDynamicInfo: while paused the pause text in the middle, unless something is under the cursor; else the info's
    /// lines, ten pixels apart.
    /// </summary>
    private void DrawDynamicInfo(SpriteBatch batch, int x, int y, BottomPanelState state)
    {
        if (state.Pause is { } pause && state.Info is null)
        {
            var width = _pauseFont.Measure(pause).Width;
            _pauseFont.Draw(batch, pause, new Vector2(x + 10 + ((255 - width) / 2), y + 14), tint: _pauseFont.IsTrueTypeFor(pause) ? new Color(35, 138, 173) : null);
            return;
        }

        if (state.Info is not { } info)
        {
            return;
        }

        var lines = Maps.MapViewScreen.WrapToWidth(info, 240, text => _whiteFont.Measure(text).Width);
        for (var i = 0; i < Math.Min(3, lines.Count); i++)
        {
            _whiteFont.Draw(batch, lines[i], new Vector2(x + 20, y + (10 * (i + 1))));
        }
    }

    private static PanelButton? ButtonAt(Rectangle bounds, Point at, bool hovering)
    {
        foreach (var button in Buttons)
        {
            if (button.Hover && !hovering)
            {
                continue;
            }

            var hit = new Rectangle(bounds.X + button.X + button.Hit.X, bounds.Y + button.Hit.Y, button.Hit.Width, button.Hit.Height);
            if (hit.Contains(at))
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>The <paramref name="index"/>th fax icon's place: from the panel's left, 30 pixels each, above the panel.</summary>
    private static Rectangle FaxArea(Viewport viewport, int index)
    {
        var bounds = Bounds(viewport);
        return new Rectangle(bounds.X + 1 + (index * FaxWidth), bounds.Y - FaxRise, FaxWidth, FaxHeight);
    }

    private static int? FaxAt(Viewport viewport, Point at, int faxes)
    {
        for (var i = 0; i < Math.Min(MaxFaxes, faxes); i++)
        {
            if (FaxArea(viewport, i).Contains(at))
            {
                return i;
            }
        }

        return null;
    }

    private static TextRenderer Font(GraphicsDevice device, ThemeHospitalInstall install, string name, Palette palette) =>
        new(device, new BitmapFont(install.LoadSpriteSheet("QDATA", name, complex: false)), palette);

    /// <summary>
    /// A button: its key, label (for scripts), panel sprite and where it goes, the area that takes clicks within it and
    /// the pressed sprite drawn there, whether it is one of the hover buttons, its tooltip, the tutorial's name for it and
    /// the hotkey action that opens it too.
    /// </summary>
    private sealed record PanelButton(string Key, string Label, int Sprite, int X, Rectangle Hit, int Pressed, bool Hover, string Tooltip, string? Lit, string Hotkey);
}
