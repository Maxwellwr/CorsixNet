using OpenWards.Client.Sessions;
using OpenWards.Content.Saves;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Staff;

namespace OpenWards.Client.Tests;

/// <summary>Recording a local game and playing it back.</summary>
public class ReplayTests
{
    [Fact]
    public void AReplayCanBeWoundBackAndForward()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var local = new LocalSession("TEST.L1", simulation);
        local.Submit(new BorrowCommand(false));
        for (var i = 0; i < 12_000; i++)
        {
            if (i == 4_000)
            {
                local.Submit(new HireStaffCommand(StaffClass.Doctor, 0, new TilePosition(6, 6)));
            }

            if (i == 10_500)
            {
                local.Submit(new HireStaffCommand(StaffClass.Nurse, 0, new TilePosition(7, 7)));
            }

            simulation.Step();
        }

        local.Recording.Finish(simulation);
        var reference = new ReplaySession("reference", local.Recording, simulation.Content);
        var hashes = new Dictionary<long, ulong>();
        while (!reference.IsFinished)
        {
            reference.Step();
            if (reference.Simulation.Tick is 3_000 or 11_000)
            {
                hashes[reference.Simulation.Tick] = reference.Simulation.ComputeStateHash();
            }
        }

        var replay = new ReplaySession("replay", local.Recording, simulation.Content);
        replay.Seek(11_000);
        Assert.Equal(hashes[11_000], replay.Simulation.ComputeStateHash());

        replay.Seek(3_000);
        Assert.Equal(3_000, replay.Simulation.Tick);
        Assert.Equal(hashes[3_000], replay.Simulation.ComputeStateHash());

        // Forward again from the state kept at tick 10,000, then past the end, which stops there.
        replay.Seek(11_000);
        Assert.Equal(hashes[11_000], replay.Simulation.ComputeStateHash());
        replay.Seek(-5);
        Assert.Equal(0, replay.Simulation.Tick);
        replay.Seek(long.MaxValue);
        Assert.Equal(12_000, replay.Simulation.Tick);
        Assert.True(replay.Matches);
    }

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
