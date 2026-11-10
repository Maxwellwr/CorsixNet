using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Entities;
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
