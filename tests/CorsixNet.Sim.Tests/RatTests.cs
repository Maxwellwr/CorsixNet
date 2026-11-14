using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Disasters;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Tests;

/// <summary>Rat holes and rats (CorsixTH's dailyUpdateRatholes, dailyUpdateRats and entities/rat.lua).</summary>
public class RatTests
{
    private const int Size = 16;

    [Fact]
    public void ACleanHospitalGetsTwoHolesWhenARatComesAndTheRatRunsBetweenThem()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        Rat? rat = null;
        for (var day = 0; day < 400 && rat is null; day++)
        {
            StepDay(simulation);
            rat = simulation.Entities.OfType<Rat>().FirstOrDefault();
        }

        Assert.NotNull(rat);
        Assert.Equal(2, hospital.Ratholes.Count);
        Assert.NotEqual(hospital.Ratholes[0].Tile, hospital.Ratholes[1].Tile);
        Assert.All(hospital.Ratholes, hole => Assert.Contains(hole, Rats.WallsAround(simulation.Map, hole.Tile)));
        Assert.NotEqual(rat!.Origin.Tile, rat.Target.Tile);

        using var stream = new MemoryStream();
        simulation.Save(stream);
        stream.Position = 0;
        var restored = Simulation.Load(stream);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());

        // It runs a tile every four ticks and is gone once it is in the other hole.
        var seen = new List<TilePosition>();
        for (var tick = 0; tick < 400 && simulation.FindEntity(rat.Id) is not null; tick++)
        {
            simulation.Step();
            restored.Step();
            if (rat.Phase == RatPhase.Running)
            {
                seen.Add(rat.From);
            }
        }

        Assert.Null(simulation.FindEntity(rat.Id));
        // Found a day after it came, it may be on its way already: the tiles left are the end of its path.
        var left = seen.Distinct().ToList();
        Assert.NotEmpty(left);
        Assert.Equal(rat.Path.Skip(rat.Path.Count - left.Count - 1).Take(left.Count), left);
        Assert.Equal(rat.Target.Tile, rat.Path[^1]);
        Assert.Equal(simulation.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void LitterBringsMoreHoles()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];

        // 12 of the 144 corridor tiles: a twelfth, so round(200 / 12) = 17 holes are wanted.
        for (var x = 2; x < 14; x++)
        {
            Litter.Drop(simulation, 0, LitterType.Paper, new TilePosition(x, 8), isMirrored: false);
        }

        Assert.Equal(12m / 144m, Rats.LitterFraction(simulation, 0));
        for (var day = 0; day < 60; day++)
        {
            StepDay(simulation);
        }

        Assert.InRange(hospital.Ratholes.Count, 5, 17);
        Assert.Equal(hospital.Ratholes.Count, hospital.Ratholes.Select(hole => (hole.Tile, hole.Wall)).Distinct().Count());

        // Building over a hole closes it.
        var hole = hospital.Ratholes[0];
        Rats.RemoveRatholesAt(simulation, hole.Tile);
        Assert.DoesNotContain(hospital.Ratholes, other => other.Tile == hole.Tile);
    }

    [Fact]
    public void AShotRatLeavesADeadRat()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        hospital.AddRathole(new Rathole(new TilePosition(3, 2), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(12, 2), Direction.North, 1));
        var rat = Rat.Spawn(simulation, 0, hospital.Ratholes[0], hospital.Ratholes)!;
        for (var tick = 0; tick < Rat.LeaveHoleTicks + 9; tick++)
        {
            simulation.Step();
        }

        Assert.Equal(RatPhase.Running, rat.Phase);
        simulation.Schedule(0, new ShootRatCommand(rat.Id));
        simulation.Step();

        Assert.Null(simulation.FindEntity(rat.Id));
        var dead = Assert.Single(simulation.Entities.OfType<Litter>());
        Assert.Equal(LitterType.DeadRat, dead.Type);
        Assert.Equal(2, dead.Tile.Y);
    }

    [Fact]
    public void TheHolesInARoomsWallsCloseWhenItIsTakenOutOfUse()
    {
        var simulation = NewSimulation();
        var hospital = simulation.Hospitals[0];
        var room = new Room(99, "gp", new TileRect(5, 5, 3, 3), [.. Enumerable.Repeat(new OriginalTile(1, TileFlags.Passable), 9)]);

        // Holes in the corridor tiles round the room, in the walls towards it, and two that are not.
        hospital.AddRathole(new Rathole(new TilePosition(6, 4), Direction.South, 1));
        hospital.AddRathole(new Rathole(new TilePosition(5, 8), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(4, 7), Direction.East, 1));
        hospital.AddRathole(new Rathole(new TilePosition(8, 5), Direction.West, 1));
        hospital.AddRathole(new Rathole(new TilePosition(6, 4), Direction.North, 1));
        hospital.AddRathole(new Rathole(new TilePosition(3, 2), Direction.North, 1));

        // Hospital:removeRatholesAroundRoom.
        Rats.RemoveRatholesAroundRoom(simulation, room);

        Assert.Equal([new Rathole(new TilePosition(6, 4), Direction.North, 1), new Rathole(new TilePosition(3, 2), Direction.North, 1)], hospital.Ratholes);
    }

    private static void StepDay(Simulation simulation)
    {
        for (var tick = 0; tick < GameDate.HoursPerDay; tick++)
        {
            simulation.Step();
        }
    }

    /// <summary>A hospital of 12 by 12 floor tiles (2 to 13) whose north edge is an outside wall.</summary>
    private static Simulation NewSimulation()
    {
        var map = new LevelMap(Size, Size, playerCount: 1);
        map.ParcelCount = 2;
        map.SetParcelOwner(1, 1);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                ref var tile = ref map[x, y];
                tile.Floor = 1;
                tile.Set(TileFlags.Passable, true);
                if (x is >= 2 and < 14 && y is >= 2 and < 14)
                {
                    tile.Parcel = 1;
                    tile.Set(TileFlags.Buildable | TileFlags.Hospital, true);
                    if (y == 2)
                    {
                        tile.NorthWall = 90;
                    }
                }
            }
        }

        map.UpdatePathfinding();
        return new Simulation(map, seed: 4);
    }
}
