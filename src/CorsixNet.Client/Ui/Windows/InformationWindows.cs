// The information, confirmation and tip of the day dialogs, ported from CorsixTH CorsixTH/Lua/dialogs/information.lua,
// dialogs/confirm_dialog.lua and dialogs/resizables/tip_of_the_day.lua; the tips are CorsixTH's own
// (languages/english.lua, totd_window.tips), the ones about this game's play.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// <c>UIInformation</c>: paragraphs of text to read and close, such as a room's description the first time one is
/// built.
/// </summary>
internal sealed class InformationWindow(IReadOnlyList<string> paragraphs, string? title = null) : UiWindow
{
    public override string Title => title ?? UiText.Get("corsixnet.information.caption", "Information");

    /// <summary>Centred, as CorsixTH's <c>UIInformation</c>.</summary>
    public override Vector2? DefaultPosition => new(0.5f, 0.5f);

    /// <summary>Each information window stands alone; opening another does not replace this one.</summary>
    public override bool IsSameAs(UiWindow other) => ReferenceEquals(other, this);

    private readonly InformationScreen _screen = new();

    /// <summary>The original's look (<see cref="InformationScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host)
    {
        if (!InformationScreen.IsAvailable(host))
        {
            return false;
        }

        _screen.Measure(host, paragraphs);
        return true;
    }

    public override Point FullscreenSize => _screen.Size;

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse, paragraphs);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        if (_screen.Clicked(local, mouse, previous))
        {
            host.PlaySound("no4.wav");
            host.Close(this);
        }
    }

    /// <summary>global_confirm: Enter closes it.</summary>
    public override bool TakesKey(string action) => action == "global_confirm";

    public override void FullscreenKey(IWindowHost host, string action) => host.Close(this);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var first = true;
        foreach (var paragraph in paragraphs)
        {
            if (!first)
            {
                yield return WindowRow.Gap;
            }

            first = false;
            foreach (var line in BottomPanel.Wrap(paragraph, Width))
            {
                yield return line;
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(WindowCell.Button(UiText.Get("install.ok", "OK"), () => host.Close(this)).WithTooltip(UiText.Get("tooltip.information.close", "Close the information dialog")));
    }

    /// <summary>Characters on a line: CorsixTH's information box is 300 pixels of text wide.</summary>
    private const int Width = 60;
}

/// <summary>
/// <c>UIConfirmDialog</c>: a question with OK and Cancel; OK runs the action, both close the window.
/// </summary>
internal sealed class ConfirmWindow(string question, Action ok) : UiWindow
{
    public override string Title => UiText.Get("corsixnet.confirm.caption", "Confirm");

    private readonly ConfirmScreen _screen = new();

    /// <summary>The original's look (<see cref="ConfirmScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host)
    {
        if (!ConfirmScreen.IsAvailable(host))
        {
            return false;
        }

        _screen.Measure(host, question);
        return true;
    }

    public override Point FullscreenSize => _screen.Size;

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse, question);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        switch (_screen.Clicked(local, mouse, previous))
        {
            case "ok":
                host.PlaySound("YesX.wav");
                host.Close(this);
                ok();
                break;
            case "cancel":
                host.PlaySound("No4.wav");
                host.Close(this);
                break;
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) => _screen.Tooltip(host, local);

    /// <summary>global_confirm: Enter is OK.</summary>
    public override bool TakesKey(string action) => action == "global_confirm";

    public override void FullscreenKey(IWindowHost host, string action)
    {
        host.PlaySound("YesX.wav");
        host.Close(this);
        ok();
    }

    /// <summary>Centred, as CorsixTH's <c>UIConfirmDialog</c>.</summary>
    public override Vector2? DefaultPosition => new(0.5f, 0.5f);

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        foreach (var line in BottomPanel.Wrap(question, 60))
        {
            yield return line;
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("install.ok", "OK"), () =>
            {
                host.Close(this);
                ok();
            }).WithTooltip(OriginalTooltips.Get(host, 87, "Confirm")),
            WindowCell.Button(UiText.Get("graphics_window.cancel", "Cancel"), () => host.Close(this)).WithTooltip(OriginalTooltips.Get(host, 86, "Cancel")));
    }
}

/// <summary><c>UITipOfTheDay</c>: a random tip to start with, and the buttons to go through them all.</summary>
internal sealed class TipOfTheDayWindow(int first) : UiWindow
{
    /// <summary>At the bottom right, as CorsixTH's <c>UITipOfTheDay</c>.</summary>
    public override Vector2? DefaultPosition => new(-20, -20);

    /// <summary>CorsixTH's tips about playing (the ones about its website, translations and bug reports are left out).</summary>
    public static readonly IReadOnlyList<string> Tips =
    [
        "Every hospital needs a reception desk and a GP's office to get going. After that, it depends on what kind of patients are visiting your hospital. A pharmacy is always a good choice, though.",
        "Machines such as the Inflator need maintenance. Employ a handyman or two to repair your machines, or you'll risk your staff and patients getting hurt.",
        "After a while, your staff will get tired. Be sure to build a staff room, so they can relax.",
        "Place enough radiators to keep your staff and patients warm, or they will become unhappy. Use the town map to locate any spots in your hospital that need more heating.",
        "A doctor's skill level greatly influences the quality and speed of their diagnoses. Place a skilled doctor in your GP's office, and you won't need as many additional diagnosis rooms.",
        "Juniors and doctors can improve their skills by learning from a consultant in the training room. If the consultant has a special qualification (surgeon, psychiatrist, or researcher), they will also pass on this knowledge to their pupils.",
        "You can adjust some settings such as the sounds, scrolling and autosaving in the options window, found in the menu (Esc).",
        "Each level has certain requirements to fulfill before you can move on to the next one. Check the status window to see your progression towards the level goals.",
        "Click on the door of a room to see its queue. You can do useful fine tuning here, such as reordering the queue or sending a patient to another room.",
        "Unhappy staff will ask for salary rises frequently. Make sure your staff is working in a comfortable environment to keep that from happening.",
        "Patients will get thirsty while waiting in your hospital, even more so if you turn up the heating! Place vending machines in strategic positions for some extra income.",
        "You can abort the diagnosis progress for a patient prematurely and guess the cure, if you already encountered the disease. Beware that this may increase the risk of a wrong cure, resulting in death for the patient.",
        "Emergencies can be a good source for some extra cash, provided that you have enough capacities to handle the emergency patients in time.",
    ];

    /// <summary>The tips' names in the language files, in the order of <see cref="Tips"/>.</summary>
    private static readonly string[] TipNames =
    [
        "totd_window.tips.1", "totd_window.tips.2", "totd_window.tips.3", "totd_window.tips.4", "totd_window.tips.5", "totd_window.tips.6",
        "corsixnet.tips.options", "totd_window.tips.12", "totd_window.tips.15", "totd_window.tips.16", "totd_window.tips.17", "totd_window.tips.18",
        "totd_window.tips.19",
    ];

    private int _tip = ((first % Tips.Count) + Tips.Count) % Tips.Count;

    public override string Title => UiText.Get("corsixnet.tips.caption", "Tip of the day");

    /// <summary>The tip shown, 0-based.</summary>
    public int Tip => _tip;

    /// <summary>The window's colour, picked at random once a game (<c>col_bg</c>: each of red, green and blue from 20 to 200).</summary>
    private static readonly Color Colour = new(Random.Shared.Next(20, 201), Random.Shared.Next(20, 201), Random.Shared.Next(20, 201));

    /// <summary>The window in CorsixTH's look: 380 by 110 (UITipOfTheDay on a screen wide enough).</summary>
    public static readonly Point LookSize = new(380, 110);

    /// <summary>The previous and next buttons side by side at the bottom (<c>add_nav_buttons</c>).</summary>
    private static readonly Rectangle PreviousButton = new(10, LookSize.Y - 30, (LookSize.X / 2) - 20, 20);

    private static readonly Rectangle NextButton = new((LookSize.X / 2) + 10, LookSize.Y - 30, (LookSize.X / 2) - 20, 20);

    private string? _pressed;

    /// <summary>CorsixTH's look (<see cref="ResizableLook"/>) where its sprites and the game's font are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => host.Graphics is { Resizable: not null } graphics && graphics.Font("FONT01V", OriginalGraphics.MainPalette) is not null;

    public override Point FullscreenSize => LookSize;

    public override Vector2? FullscreenPosition => new(-20, -20);

    /// <summary><c>UITipOfTheDay:draw</c>: the window, the tip in white wrapped 10 pixels in, and the two buttons.</summary>
    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var look = graphics.Resizable!;
        var font = graphics.Font("FONT01V", OriginalGraphics.MainPalette)!;
        look.DrawWindow(batch, new Rectangle(origin, LookSize), Colour);
        var y = origin.Y + 10;
        foreach (var line in Maps.MapViewScreen.WrapToWidth(UiText.Get(TipNames[_tip], Tips[_tip]), LookSize.X - 20, text => font.Measure(text).Width))
        {
            font.Draw(batch, line, new Vector2(origin.X + 10, y));
            y += font.LineHeight;
        }

        var local = new Point(mouse.X, mouse.Y) - origin;
        var down = mouse.LeftButton == ButtonState.Pressed;
        look.DrawBevel(batch, new Rectangle(origin + PreviousButton.Location, PreviousButton.Size), Colour, down && _pressed == "previous" && PreviousButton.Contains(local), UiText.Get("totd_window.previous", "Previous tip"), font);
        look.DrawBevel(batch, new Rectangle(origin + NextButton.Location, NextButton.Size), Colour, down && _pressed == "next" && NextButton.Contains(local), UiText.Get("totd_window.next", "Next tip"), font);
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        string? At(Point at) => PreviousButton.Contains(at) ? "previous" : NextButton.Contains(at) ? "next" : null;
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = At(local);
        }
        else if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var pressed = _pressed;
            _pressed = null;
            if (pressed is not null && pressed == At(local))
            {
                // buttonPrev and buttonNext, going round.
                _tip = pressed == "previous" ? (_tip + Tips.Count - 1) % Tips.Count : (_tip + 1) % Tips.Count;
            }
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) =>
        PreviousButton.Contains(local) ? UiText.Get("tooltip.totd_window.previous", "Display the previous tip")
        : NextButton.Contains(local) ? UiText.Get("tooltip.totd_window.next", "Display the next tip")
        : null;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        foreach (var line in BottomPanel.Wrap(UiText.Get(TipNames[_tip], Tips[_tip]), 60))
        {
            yield return line;
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button(UiText.Get("totd_window.previous", "Previous tip"), () => _tip = (_tip + Tips.Count - 1) % Tips.Count).WithTooltip(UiText.Get("tooltip.totd_window.previous", "Display the previous tip")),
            WindowCell.Button(UiText.Get("totd_window.next", "Next tip"), () => _tip = (_tip + 1) % Tips.Count).WithTooltip(UiText.Get("tooltip.totd_window.next", "Display the next tip")));
    }
}
