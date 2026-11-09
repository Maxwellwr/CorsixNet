using CorsixNet.Client.Sessions;
using CorsixNet.Content.Saves;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Client.Tests;

/// <summary>Recording a local game and playing it back.</summary>
public class ReplayTests
{
    [Fact]
    public void AReplayPlaysTheGameOutTheSameWay()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var local = new LocalSession("TEST.L1", simulation);
        for (var i = 0; i < 30; i++)
        {
            simulation.Step();
        }

        local.Submit(new BorrowCommand(false));
        local.Submit(new HireStaffCommand(StaffClass.Doctor, 0, new TilePosition(6, 6)));
        for (var i = 0; i < 200; i++)
        {
            simulation.Step();
        }

        local.Submit(new SetPolicyCommand(PolicyKind.GrantWageIncrease, 1));
        local.Submit(new HireStaffCommand(StaffClass.Nurse, 0, new TilePosition(7, 7)));
        for (var i = 0; i < 300; i++)
        {
            simulation.Step();
        }

        local.Recording.Finish(simulation);
        using var file = new MemoryStream();
        local.Recording.Write(file);
        file.Position = 0;
        var read = Replay.Read(file);
        Assert.Equal("TEST.L1", read.LevelName);
        Assert.Equal(4, read.Commands.Count);

        var replay = new ReplaySession("replay", read, simulation.Content);
        Assert.Equal(0, replay.Simulation.Tick);
        replay.Submit(new BorrowCommand(true));
        while (!replay.IsFinished)
        {
            replay.Step();
        }

        Assert.True(replay.Matches);
        Assert.Equal(simulation.ComputeStateHash(), replay.Simulation.ComputeStateHash());
        Assert.Equal(simulation.Hospitals[0].Loan, replay.Simulation.Hospitals[0].Loan);
    }

    [Fact]
    public void AReplayOutOfStepIsNoticed()
    {
        var host = new WindowTestHost();
        var local = new LocalSession("TEST.L1", host.Simulation);
        for (var i = 0; i < 50; i++)
        {
            host.Simulation.Step();
        }

        local.Recording.Finish(host.Simulation);
        using var file = new MemoryStream();
        local.Recording.Write(file);

        // Change the recorded hash: the playback must notice.
        var bytes = file.ToArray();
        bytes[^1] ^= 0xFF;
        var replay = new ReplaySession("replay", Replay.Read(new MemoryStream(bytes)), host.Simulation.Content);
        while (!replay.IsFinished)
        {
            replay.Step();
        }

        Assert.False(replay.Matches);
    }

    [Fact]
    public void SomethingElseIsNotAReplay()
    {
        Assert.Throws<InvalidDataException>(() => Replay.Read(new MemoryStream("CNETSAVE...."u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => Replay.Read(new MemoryStream("CNETRPLY"u8.ToArray())));
    }
}
