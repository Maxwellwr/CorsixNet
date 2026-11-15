// The bottom panel as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/bottom_panel.lua (drawPanels,
// draw, drawReputationMeter, drawDynamicInfo, showAdditionalButtons, machineMenuButtonExists) and dialogs/message.lua (the
// fax icons). The machine menu button's sprites are CorsixTH's own (CorsixTH/Bitmap/aux_ui.dat, sprites 22 and 23).
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
/// <param name="FaxKeys">What tells each fax apart (<see cref="Ui.Windows.FaxWindow.Pending"/>), so its icon keeps its place; by default its kind and place.</param>
internal sealed record BottomPanelState(
    long Balance,
    int Reputation,
    string Date,
    DynamicInfoLines? Info,
    string? Pause,
    IReadOnlyList<string> Faxes,
    Func<string, bool> IsOn,
    Func<string, bool> IsLit,
    IReadOnlyList<string>? FaxKeys = null);

/// <summary>
/// The original's bottom panel, 640 by 48 pixels at the bottom middle of the screen: the bank button, the balance and
/// reputation meter, the date, the fax door, the build, corridor, edit and hire buttons, and the dynamic info bar that
/// gives way to the buttons of the fullscreen dialogs while the cursor is over the panel. Waiting faxes stand on its
/// left as icons. Clicks give the buttons' keys to the screen. With CorsixTH's machine menu button
/// (<c>machine_menu_button</c>, on a screen wider than 676 pixels) it is 676 wide, the button after the staff
/// management one and the other hover buttons 38 pixels further right.
/// </summary>
internal sealed class OriginalBottomPanel : IDisposable
{
    public const int Width = 640;

    /// <summary>The panel's width with the machine menu button (<c>UIBottomPanel:drawPanels</c>).</summary>
    public const int WideWidth = 676;

    /// <summary>How far the hover buttons after the machine menu button move right to make room for it.</summary>
    public const int MachineButtonOffset = 38;

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
        new("machines", "Machines", 0, 407, new Rectangle(2, 6, 36, 36), 0, Hover: true, "Machine menu", null, "ingame_panel_machineMenu"),
        new("town", "Town", 17, 407, new Rectangle(1, 6, 35, 36), 18, Hover: true, "Town map", null, "ingame_panel_townMap"),
        new("casebook", "Casebook", 19, 445, new Rectangle(1, 6, 35, 36), 20, Hover: true, "Drug casebook", null, "ingame_panel_casebook"),
        new("research", "Research", 21, 483, new Rectangle(1, 6, 35, 36), 22, Hover: true, "Research", null, "ingame_panel_research"),
        new("status", "Status", 23, 521, new Rectangle(1, 6, 35, 36), 24, Hover: true, "Status", null, "ingame_panel_status"),
        new("charts", "Charts", 25, 559, new Rectangle(1, 6, 35, 36), 26, Hover: true, "Charts", null, "ingame_panel_charts"),
        new("policy", "Policy", 27, 597, new Rectangle(1, 6, 35, 36), 28, Hover: true, "Policy", null, "ingame_panel_policy"),
    ];

    private readonly SpriteAtlas _sprites;

    /// <summary>CorsixTH's aux_ui sprites, for the machine menu button; null if they could not be read.</summary>
    private readonly SpriteAtlas? _auxSprites;
    private readonly TextRenderer _moneyFont;
    private readonly TextRenderer _dateFont;
    private readonly TextRenderer _whiteFont;
    private readonly TextRenderer _pauseFont;
    private string? _pressed;
    private readonly FaxDrawer _drawer = new();
    private readonly Texture2D? _pixel;

    public OriginalBottomPanel(GraphicsDevice device, ThemeHospitalInstall install, Palette palette)
    {
        _sprites = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "PANEL02V", complex: true), palette);
        _moneyFont = Font(device, install, "FONT05V", palette);
        _dateFont = Font(device, install, "FONT16V", palette);
        _whiteFont = Font(device, install, "FONT01V", palette);
        _pauseFont = Font(device, install, "FONT124V", palette);
        _auxSprites = AuxSprites.Load() is { } aux ? new SpriteAtlas(device, aux, palette) : null;
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);
    }

    /// <summary>Whether the panel has the machine menu button (CorsixTH's <c>machine_menu_button</c>, on by default).</summary>
    public static bool MachineMenuButton { get; set; } = true;

    /// <summary><c>UIBottomPanel:machineMenuButtonExists</c>: the setting is on and the screen is wider than 676 pixels.</summary>
    public static bool HasMachineButton(Viewport viewport) => MachineMenuButton && viewport.Width > WideWidth;

    /// <summary>The panel's width: 676 with the machine menu button, else 640.</summary>
    public static int PanelWidth(Viewport viewport) => HasMachineButton(viewport) ? WideWidth : Width;

    /// <summary>Where the panel is: centred at the bottom of the screen (<c>setDefaultPosition(0.5, -0.1)</c>).</summary>
    public static Rectangle Bounds(Viewport viewport)
    {
        var width = PanelWidth(viewport);
        return new((viewport.Width - width) / 2, viewport.Height - Height, width, Height);
    }

    /// <summary>The buttons the panel has, each with where it goes from the panel's left.</summary>
    private static IEnumerable<(PanelButton Button, int X)> PlacedButtons(Viewport viewport)
    {
        var wide = HasMachineButton(viewport);
        foreach (var button in Buttons)
        {
            if (button.Key == "machines")
            {
                if (wide)
                {
                    yield return (button, button.X);
                }

                continue;
            }

            yield return (button, button.X + (wide && button.Hover && button.Key != "staff" ? MachineButtonOffset : 0));
        }
    }

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
    /// icon ("fax:" and its place); a right click on the bank opens the statement ("bankstats"), on a fax icon dismisses that fax
    /// ("faxdismiss:" and its place, UIMessage:dismissMessage).
    /// </summary>
    /// <returns>Whether the mouse is over the panel or a fax icon, so the map does not get it.</returns>
    public bool Update(Viewport viewport, MouseState mouse, MouseState previous, int faxes, Action<string> act)
    {
        var at = new Point(mouse.X, mouse.Y);
        var bounds = Bounds(viewport);
        var over = bounds.Contains(at) || FaxAt(viewport, at, faxes) is not null;
        var hovering = bounds.Contains(at);
        var button = ButtonAt(viewport, at, hovering);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = button?.Key ?? FaxKey(viewport, at, faxes);
        }
        else if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var released = button?.Key ?? FaxKey(viewport, at, faxes);
            if (_pressed is not null && _pressed == released)
            {
                act(_pressed);
            }

            _pressed = null;
        }

        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released)
        {
            if (button?.Key == "bank")
            {
                act("bankstats");
            }
            else if (FaxAt(viewport, at, faxes) is { } fax)
            {
                act(string.Create(CultureInfo.InvariantCulture, $"faxdismiss:{fax}"));
            }
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
        if (ButtonAt(viewport, mouse, bounds.Contains(mouse)) is not { } button)
        {
            return null;
        }

        var name = button.Key switch
        {
            "bank" => "corsixnet.toolbar.bank_tooltip",
            "corridor" => "tooltip.toolbar.objects",
            "staff" => "tooltip.toolbar.staff_list",
            "town" => "tooltip.toolbar.town_map",
            "machines" => "tooltip.toolbar.machine_menu",
            var other => "tooltip.toolbar." + other,
        };
        var template = UiText.Get(name, button.Tooltip + " (%s)");
        return hotkeyText(button.Hotkey) is { } key
            ? UiText.Fill(template, key)
            : System.Text.RegularExpressions.Regex.Replace(template, @"\s*\(%s\)", string.Empty);
    }

    /// <summary>The middle of the button labelled <paramref name="label"/>, for scripts (see <c>InputScript</c>).</summary>
    public static Point? FindButton(Viewport viewport, string label)
    {
        var bounds = Bounds(viewport);
        return PlacedButtons(viewport).FirstOrDefault(placed => string.Equals(placed.Button.Label, label, StringComparison.OrdinalIgnoreCase)) is { Button: { } found } hit
            ? new Point(bounds.X + hit.X + found.Hit.Center.X, bounds.Y + found.Hit.Center.Y)
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
        var wide = HasMachineButton(viewport);
        for (var dx = 377; dx <= (wide ? 660 : 630); dx += 10)
        {
            _sprites.Draw(batch, 13, x + dx, y, Color.White);
        }

        _sprites.Draw(batch, 14, x + (wide ? 665 : 627), y, Color.White);
        var under = ButtonAt(viewport, mouse, hovering);
        foreach (var (button, bx) in PlacedButtons(viewport))
        {
            if (button.Hover && !hovering)
            {
                continue;
            }

            var down = (_pressed == button.Key && under?.Key == button.Key && buttons.LeftButton == ButtonState.Pressed)
                || state.IsOn(button.Key)
                || (button.Lit is { } lit && state.IsLit(lit));
            if (button.Key == "machines")
            {
                // The machine menu button's custom_draw: panel sprite 0, then aux_ui's idle (22) or pressed (23) sprite.
                _sprites.Draw(batch, button.Sprite, x + bx, y, Color.White);
                _auxSprites?.Draw(batch, down ? 23 : 22, x + bx, y, Color.White);
                continue;
            }

            // A pressed button's panel shows its other sprite (Window:makeButtonOnPanel's sprite_index_active).
            _sprites.Draw(batch, down ? button.Pressed : button.Sprite, x + bx, y, Color.White);
        }

        foreach (var (digit, dx) in BalanceDigits(state.Balance))
        {
            _moneyFont.Draw(batch, digit.ToString(), new Vector2(x + dx, y + 9));
        }

        var dateWidth = _dateFont.Measure(state.Date).Width;
        _dateFont.Draw(batch, state.Date, new Vector2(x + 140 + ((60 - dateWidth) / 2), y + 20), tint: _dateFont.IsTrueTypeFor(state.Date) ? new Color(175, 50, 15) : null);

        if (!hovering)
        {
            DrawDynamicInfo(batch, x + 364, y, PanelWidth(viewport), state);
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

        // The fax icons, sliding up out of the door and then left to their places above the panel (UIMessage:onTick);
        // until one is up it shows only above the panel's edge. The emergency's siren turns.
        var shown = Math.Min(MaxFaxes, state.Faxes.Count);
        _drawer.Sync(Enumerable.Range(0, shown).Select(i => state.FaxKeys is { } keys && i < keys.Count ? keys[i] : state.Faxes[i] + ":" + i).ToList());
        _drawer.Advance(Environment.TickCount64);
        var strip = new Rectangle(0, bounds.Y - FaxRise, viewport.Width, FaxHeight);
        for (var i = 0; i < shown; i++)
        {
            var icon = FaxIcon(state.Faxes[i]);
            var (_, fx, fy) = _drawer.Icons[i];
            var area = new Rectangle(bounds.X + fx, bounds.Y + fy, FaxWidth, FaxHeight);
            var down = (state.Faxes[i] != "strike" && state.IsOn("fax")) || (_pressed == FaxKey(i) && area.Contains(mouse) && buttons.LeftButton == ButtonState.Pressed);
            var sprite = down ? icon + 1 : icon;
            var up = fy == FaxDrawer.StopY;
            if (up)
            {
                _sprites.Draw(batch, sprite, area.X, area.Y, Color.White);
            }
            else
            {
                _sprites.DrawClipped(batch, sprite, area.X, area.Y, strip, Color.White);
            }

            if (icon == 43)
            {
                var siren = 57 + (int)(Environment.TickCount64 / FaxDrawer.TickMilliseconds % 4);
                if (up)
                {
                    _sprites.Draw(batch, siren, area.X + 10, area.Y + 8, Color.White);
                }
                else
                {
                    _sprites.DrawClipped(batch, siren, area.X + 10, area.Y + 8, strip, Color.White);
                }
            }
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
        _pixel?.Dispose();
    }

    /// <summary>
    /// drawDynamicInfo: while paused the pause text in the middle, unless something is under the cursor; else the info's
    /// lines, ten pixels apart.
    /// </summary>
    private void DrawDynamicInfo(SpriteBatch batch, int x, int y, int width, BottomPanelState state)
    {
        if (state.Pause is { } pause && state.Info is null)
        {
            var pauseWidth = _pauseFont.Measure(pause).Width;
            _pauseFont.Draw(batch, pause, new Vector2(x + 10 + ((255 - pauseWidth) / 2), y + 14), tint: _pauseFont.IsTrueTypeFor(pause) ? new Color(35, 138, 173) : null);
            return;
        }

        if (state.Info is not { } info)
        {
            return;
        }

        // drawDynamicInfo: a line every 10 pixels; on the last, the bar 100 wide at the right of the info area (white
        // frame, black, orange for the share done) with white dividers.
        var lines = info.Lines.Take(3).ToList();
        var right = x + (width - 364);
        for (var i = 1; i <= lines.Count; i++)
        {
            foreach (var line in Maps.MapViewScreen.WrapToWidth(lines[i - 1], 240, text => _whiteFont.Measure(text).Width).Take(1))
            {
                _whiteFont.Draw(batch, line, new Vector2(x + 20, y + (10 * i)));
            }

            if (i == lines.Count && info.Progress is { } progress && _pixel is { } pixel)
            {
                var barX = right - 111;
                batch.Draw(pixel, new Rectangle(barX - 1, y + (10 * i), 100, 10), Color.White);
                batch.Draw(pixel, new Rectangle(barX, y + 1 + (10 * i), 98, 8), Color.Black);
                batch.Draw(pixel, new Rectangle(barX, y + 1 + (10 * i), ProgressWidth(progress), 8), new Color(221, 83, 0));
                foreach (var divider in info.Dividers ?? [])
                {
                    batch.Draw(pixel, new Rectangle(barX + (int)Math.Floor(divider * 100), y + (10 * i), 1, 10), Color.White);
                }
            }
        }
    }

    /// <summary>The orange part of the progress bar: 98 pixels for the whole.</summary>
    public static int ProgressWidth(decimal progress) => (int)Math.Floor(98 * Math.Clamp(progress, 0m, 1m));

    private static PanelButton? ButtonAt(Viewport viewport, Point at, bool hovering)
    {
        var bounds = Bounds(viewport);
        foreach (var (button, x) in PlacedButtons(viewport))
        {
            if (button.Hover && !hovering)
            {
                continue;
            }

            var hit = new Rectangle(bounds.X + x + button.Hit.X, bounds.Y + button.Hit.Y, button.Hit.Width, button.Hit.Height);
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

    /// <summary>The key a fax icon gives when clicked: "fax:" and its place.</summary>
    private static string FaxKey(int index) => string.Create(CultureInfo.InvariantCulture, $"fax:{index}");

    private string? FaxKey(Viewport viewport, Point at, int faxes) => FaxAt(viewport, at, faxes) is { } index ? FaxKey(index) : null;

    /// <summary>The fax icon under the mouse, where the icons are now (in their places before they are first drawn).</summary>
    private int? FaxAt(Viewport viewport, Point at, int faxes)
    {
        var bounds = Bounds(viewport);
        for (var i = 0; i < Math.Min(MaxFaxes, faxes); i++)
        {
            var area = i < _drawer.Icons.Count
                ? new Rectangle(bounds.X + _drawer.Icons[i].X, bounds.Y + _drawer.Icons[i].Y, FaxWidth, FaxHeight)
                : FaxArea(viewport, i);
            if (area.Contains(at))
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

/// <summary>CorsixTH's own interface sprites (<c>CorsixTH/Bitmap/aux_ui.tab</c> and <c>.dat</c>), carried in the client.</summary>
internal static class AuxSprites
{
    /// <summary>The sheet, drawn with the game's palette as CorsixTH does (<c>loadSpriteTable("Bitmap", "aux_ui", true)</c>); null if missing.</summary>
    public static SpriteSheet? Load()
    {
        var table = Read("aux_ui.tab");
        var data = Read("aux_ui.dat");
        return table is null || data is null ? null : SpriteSheet.Parse(table, data, complex: true);
    }

    /// <summary>One of CorsixTH's palettes of 8-bit colours (<c>_loadPalette</c> with transparency and 8 bits), or null if missing.</summary>
    public static Palette? ReadPalette(string name) => Read(name) is { } data ? Palette.Parse(data, eightBit: true) : null;

    private static byte[]? Read(string name)
    {
        using var stream = typeof(AuxSprites).Assembly.GetManifestResourceStream("CorsixNet.Client.Assets." + name);
        if (stream is null)
        {
            return null;
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

/// <summary>
/// Where the fax icons are (CorsixTH's UIMessage positions): a new one starts in the fax door (175, 4), rises 8 pixels a
/// tick to 24 above the panel, then moves left 3 a tick to its place, 30 pixels for each icon before it; when one goes,
/// those after it move left to their new places.
/// </summary>
internal sealed class FaxDrawer
{
    /// <summary>One of CorsixTH's UI ticks.</summary>
    public const long TickMilliseconds = 1000 / 18;

    public const int StartX = 175;
    public const int StartY = 4;
    public const int StopY = -24;

    private readonly List<(string Key, int X, int Y)> _icons = [];
    private long _last;

    public IReadOnlyList<(string Key, int X, int Y)> Icons => _icons;

    /// <summary>The place an icon slides to: 1 + 30 for each before it (setXLimit).</summary>
    public static int StopX(int index) => 1 + (index * 30);

    /// <summary>Keeps the icons of the faxes shown, in their order: new ones start in the door, gone ones are dropped.</summary>
    public void Sync(IReadOnlyList<string> keys)
    {
        var old = _icons.ToDictionary(icon => icon.Key, StringComparer.Ordinal);
        _icons.Clear();
        foreach (var key in keys)
        {
            _icons.Add(old.TryGetValue(key, out var icon) ? icon : (key, StartX, StartY));
        }
    }

    /// <summary>Moves the icons on by the ticks that have passed since the last call.</summary>
    public void Advance(long now)
    {
        if (_last == 0)
        {
            _last = now;
        }

        var ticks = Math.Min(100, (now - _last) / TickMilliseconds);
        _last += ticks * TickMilliseconds;
        for (var i = 0; i < ticks; i++)
        {
            Tick();
        }
    }

    /// <summary>UIMessage:onTick for each icon.</summary>
    public void Tick()
    {
        for (var i = 0; i < _icons.Count; i++)
        {
            var (key, x, y) = _icons[i];
            if (y > StopY)
            {
                y = Math.Max(StopY, y - 8);
            }
            else if (x > StopX(i))
            {
                x = Math.Max(StopX(i), x - 3);
            }

            _icons[i] = (key, x, y);
        }
    }
}
