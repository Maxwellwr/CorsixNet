using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>The adviser's queue and history, what makes him speak, and his place in the bottom panel.</summary>
public class AdviserTests
{
    [Fact]
    public void MessagesAreSaidInTurnForAWhileEach()
    {
        var adviser = new Adviser();

        adviser.Say("one", 0);
        adviser.Say("two", 0);
        adviser.Say("two", 0);
        adviser.Say("one", 0);

        Assert.Equal("one", adviser.Current);
        Assert.Equal(1, adviser.Queued);
        adviser.Update(Adviser.SpeakMilliseconds - 1);
        Assert.Equal("one", adviser.Current);
        adviser.Update(Adviser.SpeakMilliseconds);
        Assert.Equal("two", adviser.Current);
        adviser.Next(Adviser.SpeakMilliseconds);
        Assert.Null(adviser.Current);
        Assert.Equal(["two", "one"], adviser.History);
    }

    [Fact]
    public void QuietSendsTheRestToTheHistoryAndAMessageSaidAgainMovesUp()
    {
        var adviser = new Adviser();
        adviser.Say("one", 0);
        adviser.Say("two", 0);
        adviser.Say("three", 0);

        adviser.DismissAll();

        Assert.Null(adviser.Current);
        Assert.Equal(["three", "two", "one"], adviser.History);
        adviser.Say("one", 0);
        Assert.Equal(["one", "three", "two"], adviser.History);

        for (var i = 0; i < Adviser.HistoryLimit + 5; i++)
        {
            adviser.Say($"m{i}", 0, interrupt: true);
        }

        Assert.Equal(Adviser.HistoryLimit, adviser.History.Count);
        Assert.Equal($"m{Adviser.HistoryLimit + 4}", adviser.History[0]);
    }

    [Fact]
    public void ADisabledAdviserSaysNothing()
    {
        var adviser = new Adviser { IsEnabled = false };

        adviser.Say("one", 0);

        Assert.Null(adviser.Current);
        Assert.Empty(adviser.History);
    }

    [Fact]
    public void AdviceFillsInItsArgumentsAndFallsBackToEnglish()
    {
        Assert.Equal("Another patient cured: that makes 12.", AdviserChecks.AnotherPatientCured.Text(null, 12));
        Assert.Equal("Research has produced a new X-Ray.", AdviserChecks.NewMachineResearched.Text(null, "X-Ray"));
    }

    [Fact]
    public void AnAutopsyComingOutIsAnnounced()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        checks.Observe(host.Simulation, 0, null, 0);

        host.Simulation.Hospitals[0].AutopsiesDiscovered = 1;
        checks.Observe(host.Simulation, 0, null, 0);

        Assert.Equal(AdviserChecks.AutopsyDiscovered.Fallback, host.Adviser.Current);
    }

    [Fact]
    public void WithoutHandymenTheFirstLitterAndANewMachineAskForOne()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        checks.Observe(host.Simulation, 0, null, 0);

        host.Simulation.Hospitals[0].Littered = true;
        var machine = host.Simulation.AddEntity(id => new RoomObject(id, "inflator", new TilePosition(5, 5), Direction.North, 0) { Machine = new MachineState(10) });
        checks.Observe(host.Simulation, 0, null, 0);

        Assert.Equal(AdviserChecks.NeedHandymanLitter.Fallback, host.Adviser.Current);
        host.Adviser.Next(0);
        Assert.Equal(AdviserChecks.NeedHandymanMachines.Fallback, host.Adviser.Current);

        // Worn below six uses left it calls for repair: with no handyman, the adviser asks for one.
        host.Adviser.Next(0);
        machine.Machine!.TimesUsed = 5;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Equal(AdviserChecks.MachineryDamaged2.Fallback, host.Adviser.Current);
    }

    [Fact]
    public void AMachineWornStraightBelowFourUsesIsFallingApart()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        host.Hire(StaffClass.Handyman);
        var machine = host.Simulation.AddEntity(id => new RoomObject(id, "inflator", new TilePosition(5, 5), Direction.North, 0) { Machine = new MachineState(10) });
        checks.Observe(host.Simulation, 0, null, 0);

        // Below six (a handyman is there), then below four after it: no advice.
        machine.Machine!.TimesUsed = 5;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Null(host.Adviser.Current);

        machine.Machine.TimesUsed = 7;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Null(host.Adviser.Current);

        var second = host.Simulation.AddEntity(id => new RoomObject(id, "inflator", new TilePosition(6, 6), Direction.North, 0) { Machine = new MachineState(10) });
        checks.Observe(host.Simulation, 0, null, 0);
        second.Machine!.TimesUsed = 8;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Equal(AdviserChecks.MachinesFallingApart.Fallback, host.Adviser.Current);
    }

    [Fact]
    public void TheFirstCureAndTheFirstRaiseRequestAreAnnounced()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        var doctor = host.Hire(StaffClass.Doctor);
        checks.Observe(host.Simulation, 0, null, 0);

        host.Simulation.Hospitals[0].Cured = 1;
        doctor.RaiseRequest = 50;
        checks.Observe(host.Simulation, 0, null, 0);

        Assert.Equal(AdviserChecks.FirstCure.Fallback, host.Adviser.Current);
        Assert.Equal(1, host.Adviser.Queued);
        host.Adviser.Next(0);
        Assert.Equal(AdviserChecks.PayRise.Fallback, host.Adviser.Current);

        // Only the first request is explained.
        host.Adviser.Next(0);
        doctor.RaiseRequest = 0;
        checks.Observe(host.Simulation, 0, null, 0);
        doctor.RaiseRequest = 50;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Null(host.Adviser.Current);
    }

    [Fact]
    public void PromotionsAreAnnounced()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        var doctor = host.Hire(StaffClass.Doctor);
        var profile = doctor.Profile!;
        checks.Observe(host.Simulation, 0, null, 0);

        var specialism = profile.IsSurgeon ? (profile.IsPsychiatrist ? Specialism.Researcher : Specialism.Psychiatrist) : Specialism.Surgeon;
        Assert.True(profile.TrainSpecialism(specialism, 1m));
        checks.Observe(host.Simulation, 0, null, 0);
        var title = specialism switch { Specialism.Surgeon => "surgeon", Specialism.Psychiatrist => "psychiatrist", _ => "researcher" };
        Assert.Equal(AdviserChecks.PromotionToSpecialist.Text(null, title), host.Adviser.Current);
        host.Adviser.Next(0);

        if (!profile.IsConsultant)
        {
            profile.TrainSkill(1m, host.Simulation.Rules.Staff);
            checks.Observe(host.Simulation, 0, null, 0);
            Assert.Contains(host.Adviser.Current, new[] { AdviserChecks.PromotionToDoctor.Fallback, AdviserChecks.PromotionToConsultant.Fallback });
        }
    }

    [Fact]
    public void AFinishedRoomSaysWhichStaffItLacks()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        var theatre = host.Simulation.AddEntity(id => new Room(id, "operating_theatre", new TileRect(2, 2, 4, 4), new OriginalTile[16]));
        var ward = host.Simulation.AddEntity(id => new Room(id, "ward", new TileRect(8, 2, 4, 4), new OriginalTile[16]));
        checks.Observe(host.Simulation, 0, null, 0);

        // operating_theatre.lua: no ward and no surgeon.
        theatre.IsActive = true;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Equal(AdviserChecks.OpNeedWard.Fallback, host.Adviser.Current);
        host.Adviser.Next(0);
        Assert.Equal(AdviserChecks.OpNeedTwoSurgeons.Fallback, host.Adviser.Current);
        host.Adviser.Next(0);

        // ward.lua: a nurse; once there is one, nothing more is said.
        ward.IsActive = true;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Equal(AdviserChecks.WardNeedNurse.Fallback, host.Adviser.Current);
        host.Adviser.Next(0);
        host.Hire(StaffClass.Nurse);
        ward.IsActive = false;
        checks.Observe(host.Simulation, 0, null, 0);
        ward.IsActive = true;
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Null(host.Adviser.Current);
    }

    [Fact]
    public void ThereIsNoAdviceBeforeTheFifthMonthAndNoRadiatorAdviceOnceThereAreSome()
    {
        var host = new WindowTestHost();
        var checks = new AdviserChecks(host.Adviser, new Random(1));
        host.Simulation.Hospitals[0].Open();
        checks.Observe(host.Simulation, 0, null, 0);

        host.Simulation.SkipToLastHour(new GameDate(1, 2, 5, 0));
        host.Simulation.Step();
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.DoesNotContain(AdviserChecks.PlaceRadiators.Fallback, host.Adviser.History);

        host.Simulation.SkipToLastHour(new GameDate(1, 6, 5, 0));
        host.Simulation.Step();
        checks.Observe(host.Simulation, 0, null, 0);
        Assert.Contains(AdviserChecks.PlaceRadiators.Fallback, host.Adviser.History);
    }

    [Fact]
    public void StaffPutIntoTheWrongRoomAreToldWhoWorksThere()
    {
        var host = new WindowTestHost();
        var doctor = host.Hire(StaffClass.Doctor).Profile!;
        var nurse = host.Hire(StaffClass.Nurse).Profile!;
        var handyman = host.Hire(StaffClass.Handyman).Profile!;
        var x = 2;
        TilePosition AddRoom(string type)
        {
            var room = host.Simulation.AddEntity(id => new Room(id, type, new TileRect(x, 10, 2, 2), new OriginalTile[4]) { IsActive = true });
            host.Simulation.Map[x, 10].RoomId = room.Id;
            x += 3;
            return new TilePosition(x - 3, 10);
        }

        (Advice, string?)? Say(StaffProfile profile, TilePosition tile) => AdviserChecks.StaffPlacement(host.Simulation, profile, tile, 0, _ => 0);

        var research = AddRoom("research");
        var theatre = AddRoom("operating_theatre");
        var pharmacy = AddRoom("pharmacy");
        var toilets = AddRoom("toilets");
        var gp = AddRoom("gp");
        var name = host.Simulation.Content.FindRoom("pharmacy")!.Name;

        Assert.Equal(doctor.IsResearcher ? null : (AdviserChecks.OnlyResearchers, null), Say(doctor, research));
        Assert.Equal(doctor.IsSurgeon ? null : (AdviserChecks.OnlySurgeons, null), Say(doctor, theatre));
        Assert.Equal((AdviserChecks.OnlyNursesInRoom, name), Say(doctor, pharmacy));
        Assert.Null(Say(nurse, pharmacy));
        Assert.Equal((AdviserChecks.NursesCannotWorkInRoom, host.Simulation.Content.FindRoom("gp")!.Name), Say(nurse, gp));
        Assert.Null(Say(doctor, gp));
        Assert.Equal(AdviserChecks.DoctorsCannotWorkInRoom, Say(doctor, toilets)?.Item1);
        Assert.Null(Say(handyman, toilets));
        Assert.Null(Say(doctor, new TilePosition(6, 6)));

        // A researcher put into a research department whose desks are all taken is told to give them one each.
        if (doctor.IsResearcher)
        {
            var room = (Room)host.Simulation.FindEntity(host.Simulation.Map[research.X, research.Y].RoomId)!;
            room.StaffId = 1;
            Assert.Equal((AdviserChecks.ResearcherNeedsDesk[0], null), Say(doctor, research));
        }
    }

    [Fact]
    public void TheBottomPanelShowsTheAdviserAndTheHistoryCanBeCleared()
    {
        var host = new WindowTestHost();
        var panel = new BottomPanel(_ => { });
        host.Adviser.Say("Build a staff room.", 0);
        host.Adviser.Say("Hire a handyman.", 0);

        Assert.Contains(panel.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Adviser: Build a staff room.");
        host.Click(panel, "Next");
        Assert.Equal("Hire a handyman.", host.Adviser.Current);
        host.Click(panel, "Quiet");
        Assert.Null(host.Adviser.Current);

        var history = new AdviserHistoryWindow();
        host.Click(history, "Delete");
        Assert.Equal(["Build a staff room."], host.Adviser.History);
        host.Click(history, "Delete all");
        Assert.Equal(["The adviser has said nothing yet."], host.Lines(history));
    }

    [Fact]
    public void LongSpeechWraps()
    {
        var lines = BottomPanel.Wrap(string.Join(' ', Enumerable.Repeat("word", 50)), 30);

        Assert.All(lines, line => Assert.True(line.Length <= 30));
        Assert.Equal(50, lines.Sum(line => line.Split(' ').Length));
    }
}
