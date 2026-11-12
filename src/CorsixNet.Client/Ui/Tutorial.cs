// The tutorial of the first level, ported from CorsixTH CorsixTH/Lua/game_ui.lua (tutorial_phases, GameUI:tutorialStep,
// startTutorial) and the tutorialStep calls in dialogs/bottom_panel.lua, build_room.lua, edit_room.lua,
// furnish_corridor.lua, hire_staff.lua, information.lua, place_objects.lua and place_staff.lua. The texts are the
// adviser's (original_strings.lua, adviser.tutorial: S[11]).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Ui;

/// <summary>Where the build room tool stands, as the tutorial follows it.</summary>
internal enum TutorialBuildStage
{
    None,
    Blueprint,
    Door,
    Windows,
    Objects,
}

/// <summary>How the blueprint being dragged out looks: no area yet, too small, over bad tiles, both, or fine.</summary>
internal enum TutorialBlueprint
{
    NoArea,
    TooSmall,
    Invalid,
    TooSmallAndInvalid,
    Fine,
}

/// <summary>What the interface shows this frame, as far as the tutorial cares.</summary>
internal sealed record TutorialView
{
    public bool CorridorWindowOpen { get; init; }

    /// <summary>Whether the reception desk on the cursor could go down where it is; null when none is on the cursor.</summary>
    public bool? ReceptionDeskValid { get; init; }

    public int ReceptionDesks { get; init; }

    public bool HireWindowOpen { get; init; }

    public StaffClass? HireCategory { get; init; }

    /// <summary>The class of the candidate on the cursor to place, if any.</summary>
    public StaffClass? HiringClass { get; init; }

    public bool HiringValid { get; init; }

    public int Receptionists { get; init; }

    public int Doctors { get; init; }

    public bool RoomListOpen { get; init; }

    public TutorialBuildStage BuildStage { get; init; }

    public string? BuildRoomType { get; init; }

    public TutorialBlueprint Blueprint { get; init; }

    /// <summary>Whether the door, window or object under the cursor could go there; null when none is shown.</summary>
    public bool? PlacementValid { get; init; }

    public int MissingObjects { get; init; }

    public int ActiveGpOffices { get; init; }

    public bool InformationWindowOpen { get; init; }
}

/// <summary>
/// CorsixTH's tutorial: five chapters (the reception desk, a receptionist, the GP's office, a doctor, the end), each in
/// phases; each phase has the adviser's text, kept up until the next, and the button to press blinks. CorsixTH's
/// dialogs step it from their callbacks; here it follows what the interface shows each frame (<see cref="Update"/>).
/// </summary>
/// <remarks>
/// This game's windows skip some of CorsixTH's phases: the corridor window's Buy puts the desk on the cursor at once
/// (no order and accept phases, 1.2 to 1.4), the hire window lists every candidate (no next and previous, 2.3 and 2.4)
/// and the room list has no categories (no 3.2).
/// </remarks>
internal sealed class Tutorial
{
    private readonly Adviser _adviser;
    private readonly Func<OriginalTexts?> _texts;
    private readonly Action _finished;
    private TutorialView _last = new();

    /// <param name="adviser">Who says the texts.</param>
    /// <param name="texts">The game's strings, when loaded.</param>
    /// <param name="finished">Chapter 5's begin_callback: the end of the tutorial's information and the opening watch.</param>
    public Tutorial(Adviser adviser, Func<OriginalTexts?> texts, Action finished)
    {
        _adviser = adviser;
        _texts = texts;
        _finished = finished;
    }

    /// <summary>The tutorial of the game shown, for the windows' blinking buttons; null without one.</summary>
    public static Tutorial? Current { get; set; }

    /// <summary>Whether <paramref name="key"/> is lit this moment: it blinks about twice a second while the tutorial asks for it.</summary>
    public static bool Lit(string key) => Current?.Blinks(key) == true && Environment.TickCount64 / 300 % 2 == 0;

    public int Chapter { get; private set; }

    public int Phase { get; private set; }

    public bool IsActive => Chapter != 0;

    /// <summary><c>GameUI:startTutorial</c>: from the first chapter.</summary>
    public void Start(TutorialView view)
    {
        _last = view;
        Chapter = 1;
        Phase = 0;
        Step(1, [0], 1);
    }

    /// <summary>Says the current phase's text again, kept up, after the adviser was cleared for the new game.</summary>
    public void SayAgain()
    {
        if (IsActive && Text(Chapter, Phase) is { } text)
        {
            _adviser.SayAndStay(text, Environment.TickCount64);
        }
    }

    /// <summary>Whether the button or line <paramref name="key"/> blinks now (<c>startButtonBlinking</c>).</summary>
    /// <param name="key">
    /// <c>corridor</c>, <c>hire</c> and <c>build</c> on the bottom panel, <c>buy:reception_desk</c>, <c>staff:Receptionist</c>,
    /// <c>staff:Doctor</c>, <c>hire-button</c>, <c>room:gp</c>, <c>confirm</c> (the room edit panel) and <c>information</c>.
    /// </param>
    public bool Blinks(string key) => (Chapter, Phase, key) switch
    {
        (1, 1, "corridor") => true,
        (1, 2, "buy:reception_desk") => true,
        (2 or 4, 1, "hire") => true,
        (2, 2, "staff:Receptionist") => true,
        (2, 5, "hire-button") => true,
        (3, 1, "build") => true,
        (3, 3, "room:gp") => true,
        (3, 15, "confirm") => true,
        (3, 16, "information") => true,
        (4, 2, "staff:Doctor") => true,
        (4, 3, "hire-button") => true,
        _ => false,
    };

    /// <summary>Follows the interface: each chapter moves on when what it asks for is done, and back when it is left.</summary>
    public void Update(TutorialView view)
    {
        if (!IsActive)
        {
            _last = view;
            return;
        }

        if (view.ReceptionDeskValid is not null || view.HiringClass is not null || view.BuildStage != TutorialBuildStage.None)
        {
            _grace = 0;
        }

        switch (Chapter)
        {
            case 1:
                ReceptionDesk(view);
                break;
            case 2:
                HireStaff(view, StaffClass.Receptionist, view.Receptionists > _last.Receptionists);
                break;
            case 3:
                GpOffice(view);
                break;
            case 4:
                HireStaff(view, StaffClass.Doctor, view.Doctors > _last.Doctors);
                break;
        }

        _last = view;
    }

    private void ReceptionDesk(TutorialView view)
    {
        switch (Phase)
        {
            case 1 when view.CorridorWindowOpen:
                // UIBottomPanel:dialogFurnishCorridor.
                Step(1, [1], 2);
                break;
            case 2 when view.ReceptionDeskValid is not null:
                // UIFurnishCorridor:confirm: the desk is on the cursor.
                Step(1, [2], 4);
                break;
            case 2 when !view.CorridorWindowOpen:
                Step(1, [2], 1);
                break;
            case 4 or 5 when view.ReceptionDesks > _last.ReceptionDesks:
                // UIPlaceObjects:placeObject with the reception desk.
                Step(1, [4, 5], 0, next: true);
                break;
            case 4 or 5 when view.ReceptionDeskValid is null:
                // Off the cursor: placed (the desk comes with the next tick) or given up.
                if (GivenUp())
                {
                    Step(1, [4, 5], 1);
                }

                break;
            case 4 or 5:
                Step(1, view.ReceptionDeskValid == true ? [5] : [4], view.ReceptionDeskValid == true ? 4 : 5);
                break;
        }
    }

    /// <summary>Chapters 2 and 4: open the hire window, pick the class, hire one, place them.</summary>
    private void HireStaff(TutorialView view, StaffClass staffClass, bool hired)
    {
        var chapter = Chapter;
        var choose = chapter == 2 ? 5 : 3;
        var place = choose + 1;
        if (Phase == 1 && view.HireWindowOpen)
        {
            Step(chapter, [1], 2);
            return;
        }

        if (Phase >= place && hired)
        {
            // UIPlaceStaff:onMouseUp.
            Step(chapter, [place, place + 1], 0, next: true);
            return;
        }

        if (Phase >= place)
        {
            if (view.HiringClass is null)
            {
                if (GivenUp())
                {
                    Step(chapter, [place, place + 1], 1);
                }
            }
            else
            {
                Step(chapter, view.HiringValid ? [place + 1] : [place], view.HiringValid ? place : place + 1);
            }

            return;
        }

        if (Phase >= 2 && view.HiringClass == staffClass)
        {
            // UIHireStaff:hire.
            Step(chapter, [choose], place);
            return;
        }

        if (Phase >= 2 && !view.HireWindowOpen)
        {
            Step(chapter, [2, choose], 1);
            return;
        }

        if (Phase == 2 && view.HireCategory == staffClass)
        {
            Step(chapter, [2], choose);
        }
        else if (Phase == choose && view.HireCategory != staffClass)
        {
            Step(chapter, [choose], 2);
        }
    }

    private void GpOffice(TutorialView view)
    {
        switch (Phase)
        {
            case 1 when view.RoomListOpen:
                Step(3, [1], 3);
                return;
            case 3 when view.BuildStage == TutorialBuildStage.Blueprint && view.BuildRoomType == "gp":
                Step(3, [3], 4);
                return;
            case 3 when !view.RoomListOpen:
                Step(3, [3], 1);
                return;
            case 16 when !view.InformationWindowOpen:
                Step(3, [16], 0, next: true);
                return;
        }

        if (Phase is < 4 or > 15)
        {
            return;
        }

        if (view.ActiveGpOffices > _last.ActiveGpOffices)
        {
            // UIEditRoom:confirm: the help box comes up with the first one.
            if (view.InformationWindowOpen)
            {
                Step(3, [Phase], 16);
            }
            else
            {
                Step(3, [Phase], 0, next: true);
            }

            return;
        }

        if (view.BuildStage == TutorialBuildStage.None || view.BuildRoomType != "gp")
        {
            if (view.BuildStage != TutorialBuildStage.None || GivenUp())
            {
                Step(3, [Phase], 1);
            }

            return;
        }

        var wanted = view.BuildStage switch
        {
            TutorialBuildStage.Blueprint => view.Blueprint switch
            {
                TutorialBlueprint.Invalid => 5,
                TutorialBlueprint.TooSmall => 6,
                TutorialBlueprint.TooSmallAndInvalid => 7,
                TutorialBlueprint.Fine => 8,
                _ => 4,
            },
            TutorialBuildStage.Door => view.PlacementValid == false ? 10 : 9,
            TutorialBuildStage.Windows => view.PlacementValid == false ? 12 : 11,
            _ => view.MissingObjects == 0 ? 15 : view.PlacementValid == false ? 14 : 13,
        };
        if (wanted != Phase)
        {
            Step(3, [Phase], wanted);
        }
    }

    /// <summary>
    /// Frames to wait before taking something gone from the cursor (or a room gone from the tool) as given up rather
    /// than placed: what was placed only turns up in the game with its next tick.
    /// </summary>
    private const int GraceFrames = 90;

    private int _grace;

    private bool GivenUp() => ++_grace > GraceFrames;

    /// <summary><c>GameUI:tutorialStep</c>: from one of <paramref name="from"/> to <paramref name="to"/>, or the next chapter.</summary>
    private void Step(int chapter, int[] from, int to, bool next = false)
    {
        if (Chapter != chapter || !from.Contains(Phase))
        {
            return;
        }

        _grace = 0;
        if (next)
        {
            Chapter++;
            Phase = 1;
        }
        else
        {
            Phase = to;
        }

        if (Chapter == 5)
        {
            // The end of the tutorial (chapter 5's begin_callback); then the tutorial is over.
            _adviser.EndStayUp();
            Chapter = 0;
            Phase = 0;
            _finished();
            return;
        }

        if (Text(Chapter, Phase) is { } text)
        {
            _adviser.SayAndStay(text, Environment.TickCount64);
        }
        else
        {
            _adviser.EndStayUp();
        }
    }

    /// <summary>The adviser's text for a phase (adviser.tutorial), with English stand-ins.</summary>
    private string? Text(int chapter, int phase)
    {
        (int Index, string Fallback)? entry = (chapter, phase) switch
        {
            (1, 1) => (30, "First, your hospital needs a Reception Desk. Select one from the Corridor Equipment menu."),
            (1, 2) => (41, "Click Buy on the Reception Desk's line to get one."),
            (1, 4) => (42, "Click the right mouse-button to rotate the Desk and place it down in your hospital with the left mouse button."),
            (1, 5) => (47, "The Reception Desk is greyed out because it's in an invalid position. Try moving or rotating it."),
            (2, 1) => (31, "You will also need a Receptionist to deal with your Patients."),
            (2, 2) => (34, "Click on Receptionists to see those currently available for work."),
            (2, 5) => (35, "Decide which Receptionist has a good ability and fair wage and then click Hire to employ her."),
            (2, 6) => (37, "Move the Receptionist and place her anywhere. Cleverly, she'll make her way to the desk."),
            (2, 7) => (61, "You can't place your Receptionist there."),
            (3, 1) => (33, "To start diagnosing your patient's diseases you must build a GP's Office."),
            (3, 3) => (45, "Click on the GP's Office to select it."),
            (3, 4) => (48, "Click and hold the left mouse button while you drag out your room to size."),
            (3, 5) => (52, "This blueprint is invalid - the red areas indicate where you have overlapped another room or the hospital walls."),
            (3, 6) => (53, "This room blueprint is red because it's too small. Drag it out further to make it bigger."),
            (3, 7) => (62, "The Blueprint is too small and in an invalid place."),
            (3, 8) => (57, "The blueprint is now big enough. You can still move it or change its size if you want."),
            (3, 9) => (49, "Move the mouse around the blueprint walls to put the door where you want it."),
            (3, 10) => (54, "You've tried to place the door in an invalid position. Try somewhere else on the blueprint walls."),
            (3, 11) => (50, "Place windows in the same way as the door. Your staff are happier when they've got something to look out of."),
            (3, 12) => (55, "This window is in an invalid position. Try placing it somewhere else on the walls."),
            (3, 13) => (51, "Right-click to rotate room items, and left-click to place them."),
            (3, 14) => (56, "This item is in an invalid position. Please either place it somewhere else, or rotate it to make it fit."),
            (3, 15) => (59, "Click Confirm to open your room for business, or Esc to go back a step."),
            (3, 16) => (64, "The Help box tells you all about that nice GP's Office you've just built."),
            (4, 1) => (32, "You need a Doctor to diagnose and treat the sickly folk."),
            (4, 2) => (36, "Click on Doctors to have a look at the Doctors available to hire."),
            (4, 3) => (46, "Have a good look at the abilities of each Doctor before choosing one."),
            (4, 4) => (40, "Place the Doctor anywhere in the hospital. He'll head off to the GP's Office once there's someone to diagnose."),
            (4, 5) => (60, "You can't put the Doctor there."),
            _ => null,
        };
        return entry is { } found ? _texts()?.String(11, found.Index) ?? found.Fallback : null;
    }
}
