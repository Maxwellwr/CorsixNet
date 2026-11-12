using CorsixNet.Client.Ui;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

public sealed class TutorialTests
{
    private readonly Adviser _adviser = new();
    private bool _finished;

    private Tutorial NewTutorial() => new(_adviser, () => null, () => _finished = true);

    [Fact]
    public void The_tutorial_walks_through_the_reception_desk_receptionist_gps_office_and_doctor()
    {
        var tutorial = NewTutorial();
        var view = new TutorialView();
        tutorial.Start(view);
        Assert.Equal((1, 1), (tutorial.Chapter, tutorial.Phase));
        Assert.StartsWith("First, your hospital needs a Reception Desk", _adviser.Current, StringComparison.Ordinal);
        Assert.True(tutorial.Blinks("corridor"));

        // Stays up while nothing happens.
        _adviser.Update(Environment.TickCount64 + Adviser.SpeakMilliseconds * 10);
        Assert.NotNull(_adviser.Current);

        Expect(tutorial, view = view with { CorridorWindowOpen = true }, 1, 2);
        Assert.True(tutorial.Blinks("buy:reception_desk"));
        Expect(tutorial, view = view with { CorridorWindowOpen = false, ReceptionDeskValid = false }, 1, 4);
        Expect(tutorial, view, 1, 5);
        Expect(tutorial, view = view with { ReceptionDeskValid = true }, 1, 4);
        Expect(tutorial, view = view with { ReceptionDeskValid = null, ReceptionDesks = 1 }, 2, 1);

        Expect(tutorial, view = view with { HireWindowOpen = true, HireCategory = StaffClass.Doctor }, 2, 2);
        Assert.True(tutorial.Blinks("staff:Receptionist"));
        Expect(tutorial, view = view with { HireCategory = StaffClass.Receptionist }, 2, 5);
        Expect(tutorial, view = view with { HireWindowOpen = false, HiringClass = StaffClass.Receptionist, HiringValid = false }, 2, 6);
        Expect(tutorial, view, 2, 7);
        Expect(tutorial, view = view with { HiringClass = null, Receptionists = 1 }, 3, 1);

        Expect(tutorial, view = view with { RoomListOpen = true }, 3, 3);
        Assert.True(tutorial.Blinks("room:gp"));
        Expect(tutorial, view = view with { RoomListOpen = false, BuildStage = TutorialBuildStage.Blueprint, BuildRoomType = "gp" }, 3, 4);
        Expect(tutorial, view = view with { Blueprint = TutorialBlueprint.TooSmall }, 3, 6);
        Expect(tutorial, view = view with { Blueprint = TutorialBlueprint.TooSmallAndInvalid }, 3, 7);
        Expect(tutorial, view = view with { Blueprint = TutorialBlueprint.Fine }, 3, 8);
        Expect(tutorial, view = view with { BuildStage = TutorialBuildStage.Door, PlacementValid = false }, 3, 10);
        Expect(tutorial, view = view with { BuildStage = TutorialBuildStage.Windows, PlacementValid = null }, 3, 11);
        Expect(tutorial, view = view with { BuildStage = TutorialBuildStage.Objects, MissingObjects = 3, PlacementValid = false }, 3, 14);
        Expect(tutorial, view = view with { MissingObjects = 0, PlacementValid = null }, 3, 15);
        Assert.True(tutorial.Blinks("confirm"));
        Expect(tutorial, view = view with { BuildStage = TutorialBuildStage.None, BuildRoomType = null, ActiveGpOffices = 1, InformationWindowOpen = true }, 3, 16);
        Expect(tutorial, view = view with { InformationWindowOpen = false }, 4, 1);

        Expect(tutorial, view = view with { HireWindowOpen = true, HireCategory = StaffClass.Receptionist }, 4, 2);
        Expect(tutorial, view = view with { HireCategory = StaffClass.Doctor }, 4, 3);
        Expect(tutorial, view = view with { HireWindowOpen = false, HiringClass = StaffClass.Doctor, HiringValid = true }, 4, 4);
        Assert.False(_finished);
        tutorial.Update(view with { HiringClass = null, Doctors = 1 });

        Assert.True(_finished);
        Assert.False(tutorial.IsActive);
    }

    [Fact]
    public void Leaving_a_step_goes_back_to_the_start_of_its_chapter()
    {
        var tutorial = NewTutorial();
        var view = new TutorialView();
        tutorial.Start(view);
        Expect(tutorial, view = view with { CorridorWindowOpen = true }, 1, 2);
        Expect(tutorial, view = view with { CorridorWindowOpen = false }, 1, 1);

        // Off the cursor without a desk turning up: given up after a moment.
        Expect(tutorial, view = view with { CorridorWindowOpen = true }, 1, 2);
        Expect(tutorial, view = view with { CorridorWindowOpen = false, ReceptionDeskValid = true }, 1, 4);
        Expect(tutorial, view = view with { ReceptionDeskValid = null }, 1, 4);
        for (var frame = 0; frame < 100; frame++)
        {
            tutorial.Update(view);
        }

        Assert.Equal((1, 1), (tutorial.Chapter, tutorial.Phase));

        // A desk placed without the tutorial's steps does not move it on, as in CorsixTH (only from phase 4).
        Expect(tutorial, view with { ReceptionDesks = 1 }, 1, 1);
    }

    private static void Expect(Tutorial tutorial, TutorialView view, int chapter, int phase)
    {
        tutorial.Update(view);
        Assert.Equal((chapter, phase), (tutorial.Chapter, tutorial.Phase));
    }
}
