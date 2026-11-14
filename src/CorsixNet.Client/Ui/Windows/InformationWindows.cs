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
internal sealed class InformationWindow(IReadOnlyList<string> paragraphs, string title = "Information") : UiWindow
{
    public override string Title => title;

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
        yield return WindowRow.Of(WindowCell.Button("OK", () => host.Close(this)).WithTooltip("Close the information dialog"));
    }

    /// <summary>Characters on a line: CorsixTH's information box is 300 pixels of text wide.</summary>
    private const int Width = 60;
}

/// <summary>
/// <c>UIConfirmDialog</c>: a question with OK and Cancel; OK runs the action, both close the window.
/// </summary>
internal sealed class ConfirmWindow(string question, Action ok) : UiWindow
{
    public override string Title => "Confirm";

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
            WindowCell.Button("OK", () =>
            {
                host.Close(this);
                ok();
            }).WithTooltip(OriginalTooltips.Get(host, 87, "Confirm")),
            WindowCell.Button("Cancel", () => host.Close(this)).WithTooltip(OriginalTooltips.Get(host, 86, "Cancel")));
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

    private int _tip = ((first % Tips.Count) + Tips.Count) % Tips.Count;

    public override string Title => "Tip of the day";

    /// <summary>The tip shown, 0-based.</summary>
    public int Tip => _tip;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        foreach (var line in BottomPanel.Wrap(Tips[_tip], 60))
        {
            yield return line;
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(
            WindowCell.Button("Previous tip", () => _tip = (_tip + Tips.Count - 1) % Tips.Count).WithTooltip("Display the previous tip"),
            WindowCell.Button("Next tip", () => _tip = (_tip + 1) % Tips.Count).WithTooltip("Display the next tip"));
    }
}
