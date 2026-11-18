// CorsixTH's own dialogs laid out as it lays them out: panels at fixed places (window.lua's addBevelPanel with
// makeButton and makeToggleButton), and the settings dialogs' layout of a title, a column or more of settings with their
// buttons and a big back button (CorsixTH/Lua/dialogs/resizable.lua: buildDialog, createOptionsElement,
// _getOptionYPos, _startNewColumn).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Rendering;

namespace OpenWards.Client.Ui;

/// <summary>
/// A bevelled panel of a CorsixTH dialog: where it is, its label and colour, whether it is lowered (a caption, or a
/// toggle button that is on), and for a button what a click does (+1 for the left button, -1 for the right) and its
/// tooltip. A caption's highlight and shadow can be the window's colour (<c>col.caption, col.bg, col.bg</c>).
/// </summary>
internal sealed record DialogPanel(Rectangle Area, string? Label, Color Colour, bool Lowered = false, Action<int>? Click = null, string? Tooltip = null, bool Enabled = true, Color? Highlight = null, Color? Shadow = null)
{
    /// <summary>CorsixTH's caption colour (<c>Colours.Caption</c>), drawn lowered with the window's colour round it.</summary>
    public static readonly Color CaptionColour = new(134, 126, 178);

    /// <summary>A text box's colour (<c>Colours.Textbox</c>).</summary>
    public static readonly Color TextboxColour = Color.Black;

    /// <summary>The title panel of a dialog: lowered, in <c>Colours.Title</c>.</summary>
    public static DialogPanel Title(Rectangle area, string label) => new(area, label, ResizableLook.Title, Lowered: true);

    /// <summary>A setting's name: lowered, in <c>Colours.Caption</c> with the window's colour as highlight and shadow.</summary>
    public static DialogPanel Caption(Rectangle area, string label, string? tooltip = null) =>
        new(area, label, CaptionColour, Lowered: true, Tooltip: tooltip, Highlight: ResizableLook.PanelDefault, Shadow: ResizableLook.PanelDefault);

    /// <summary>A button (<c>makeButton</c>, or <c>makeToggleButton</c> drawn lowered while on).</summary>
    public static DialogPanel Button(Rectangle area, string label, Action<int> click, string? tooltip = null, bool on = false, bool enabled = true) =>
        new(area, label, enabled ? ResizableLook.PanelDefault : ResizableLook.Disabled, Lowered: on, Click: enabled ? click : null, Tooltip: tooltip, Enabled: enabled);
}

/// <summary>
/// Draws the panels of a CorsixTH dialog over its window and handles the mouse on them: a button presses while held
/// and acts when let go over it, with <c>selectx.wav</c> (<c>default_button_sound</c>).
/// </summary>
internal sealed class ResizablePanels
{
    private int? _pressed;
    private bool _pressedRight;

    /// <summary>Whether CorsixTH's look can be drawn: its sprites and the game's white font are there.</summary>
    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { Resizable: not null } graphics && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    /// <summary>The game's white font (<c>Font01V</c>), which CorsixTH's panels use for their labels.</summary>
    public static TextRenderer Font(IWindowHost host) => host.Graphics!.Font("FONT01V", OriginalGraphics.MainPalette)!;

    /// <summary><c>UIResizable:draw</c> and the panels: the window in its colour and frame, then each panel.</summary>
    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, Point size, IReadOnlyList<DialogPanel> panels, MouseState mouse, Color? colour = null)
    {
        var look = host.Graphics!.Resizable!;
        var font = Font(host);
        look.DrawWindow(batch, new Rectangle(origin, size), colour ?? ResizableLook.PanelDefault);
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed;
        for (var i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            var area = new Rectangle(origin + panel.Area.Location, panel.Area.Size);
            var pressed = held && _pressed == i && panel.Area.Contains(local);
            look.DrawBevel(batch, area, panel.Colour, panel.Lowered != pressed, panel.Label, font, highlight: panel.Highlight, shadow: panel.Shadow);
        }
    }

    /// <summary>A button presses on the mouse going down over it and acts on its going up still over it.</summary>
    public void Mouse(IWindowHost host, IReadOnlyList<DialogPanel> panels, Point local, MouseState mouse, MouseState previous)
    {
        var leftDown = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        var rightDown = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        if (leftDown || rightDown)
        {
            _pressed = ButtonAt(panels, local);
            _pressedRight = rightDown;
            return;
        }

        var up = _pressedRight
            ? mouse.RightButton == ButtonState.Released && previous.RightButton == ButtonState.Pressed
            : mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed;
        if (!up)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is { } index && index == ButtonAt(panels, local) && panels[index].Click is { } click)
        {
            host.PlaySound(WindowLayer.DefaultButtonSound);
            click(_pressedRight ? -1 : 1);
        }
    }

    /// <summary>The tooltip of the panel under the mouse.</summary>
    public static string? Tooltip(IReadOnlyList<DialogPanel> panels, Point local) =>
        panels.LastOrDefault(panel => panel.Tooltip is not null && panel.Area.Contains(local))?.Tooltip;

    /// <summary>The middle of the <paramref name="nth"/> button reading <paramref name="text"/> (a trailing * matches the start), for scripts.</summary>
    public static Point? FindButton(IReadOnlyList<DialogPanel> panels, string text, int nth)
    {
        var prefix = text.EndsWith('*');
        var wanted = prefix ? text[..^1] : text;
        var matches = panels.Where(panel => panel.Click is not null && panel.Label is { } label
            && (prefix ? label.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) : label.Equals(wanted, StringComparison.OrdinalIgnoreCase))).ToList();
        return matches.Count >= nth ? matches[nth - 1].Area.Center : null;
    }

    private static int? ButtonAt(IReadOnlyList<DialogPanel> panels, Point local)
    {
        for (var i = panels.Count - 1; i >= 0; i--)
        {
            if (panels[i].Click is not null && panels[i].Area.Contains(local))
            {
                return i;
            }
        }

        return null;
    }
}

/// <summary>A setting of a CorsixTH settings dialog: its name, the value on its button, what a click does, whether it is on.</summary>
internal sealed record DialogSetting(string Label, string Value, Action<int> Change, string? Tooltip = null, bool On = false, bool NewColumn = false);

/// <summary>A big button under a settings dialog's settings (<c>nav_buttons</c>, or the back button).</summary>
internal sealed record DialogNavButton(string Label, Action Click, string? Tooltip = null);

/// <summary>
/// <c>UIResizable:buildDialog</c>: the title centred at the top, then a row for each setting, its name on the left and
/// its button to the right of it, columns side by side where a setting starts one, and under them the big buttons
/// (one across for the back button; two a row for the options' buttons, the last across when odd).
/// </summary>
internal static class SettingsDialogLayout
{
    public const int Margin = 15;
    public const int RowHeight = 20;
    public const int ElementSpacing = 10;
    public const int LabelButtonGap = 20;
    public const int TitleWidth = 170;
    public const int BigButtonHeight = 40;
    public const int BigButtonPadding = 5;
    public const int ButtonWidth = 135;

    /// <summary>The dialog's size and panels.</summary>
    /// <param name="labelWidth">The settings' names' width: 135, or 165 for the customise dialog.</param>
    /// <param name="navigation">The big buttons; a single one is the back button across the dialog.</param>
    /// <param name="extraHeight">The height besides the settings' rows (<c>extra_height</c>); by default room for the title and the back button.</param>
    public static (Point Size, List<DialogPanel> Panels) Build(string title, IReadOnlyList<DialogSetting> settings, IReadOnlyList<DialogNavButton> navigation, int labelWidth = ButtonWidth, int? extraHeight = null)
    {
        var columns = 1 + settings.Count(setting => setting.NewColumn);
        var columnWidth = labelWidth + LabelButtonGap + ButtonWidth;
        var width = (Margin * 2) + (columnWidth * columns);
        var panels = new List<DialogPanel>();
        var index = 1;
        int NextY() => Margin + ((ElementSpacing + RowHeight) * (index++ - 1));

        panels.Add(DialogPanel.Title(new Rectangle((width - TitleWidth) / 2, NextY(), TitleWidth, RowHeight), title));
        var column = 1;
        foreach (var setting in settings)
        {
            if (setting.NewColumn)
            {
                index = 2;
                column++;
            }

            var y = NextY();
            var offset = columnWidth * (column - 1);
            panels.Add(DialogPanel.Caption(new Rectangle(Margin + offset, y, labelWidth, RowHeight), setting.Label, setting.Tooltip));
            panels.Add(DialogPanel.Button(new Rectangle(labelWidth + LabelButtonGap + offset, y, ButtonWidth, RowHeight), setting.Value, setting.Change, setting.Tooltip, setting.On));
        }

        // The big buttons go under the longest column (the title is row 1).
        index = 2 + LongestColumn(settings);
        var navY = NextY() + BigButtonPadding;
        if (navigation.Count == 1)
        {
            panels.Add(DialogPanel.Button(new Rectangle(Margin, navY, width - (Margin * 2), BigButtonHeight), navigation[0].Label, _ => navigation[0].Click(), navigation[0].Tooltip));
        }
        else
        {
            var x = Margin;
            for (var i = 0; i < navigation.Count; i++)
            {
                var buttonWidth = i == navigation.Count - 1 && navigation.Count % 2 == 1 ? (ButtonWidth * 2) + ElementSpacing : ButtonWidth;
                var nav = navigation[i];
                panels.Add(DialogPanel.Button(new Rectangle(x, navY, buttonWidth, BigButtonHeight), nav.Label, _ => nav.Click(), nav.Tooltip));
                x += ButtonWidth + ElementSpacing;
                if (x + ButtonWidth + Margin > width)
                {
                    x = Margin;
                    navY += BigButtonHeight + ElementSpacing;
                }
            }
        }

        var extra = extraHeight ?? (Margin + RowHeight + ElementSpacing + BigButtonPadding + BigButtonHeight + Margin);
        var height = Math.Max(navY + BigButtonHeight + Margin, (settings.Count * (ElementSpacing + RowHeight)) + extra);
        return (new Point(width, height), panels);
    }

    /// <summary>How many settings the longest column has.</summary>
    private static int LongestColumn(IReadOnlyList<DialogSetting> settings)
    {
        var lengths = new List<int> { 0 };
        foreach (var setting in settings)
        {
            if (setting.NewColumn)
            {
                lengths.Add(0);
            }

            lengths[^1]++;
        }

        return lengths.Max();
    }
}
