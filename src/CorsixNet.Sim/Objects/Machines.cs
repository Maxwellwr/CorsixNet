// Machines wearing out, breaking down and being repaired, ported from CorsixTH CorsixTH/Lua/entities/machine.lua
// (Machine:Machine, machineUsed, incrementUsageCounts, callHandymanForRepairIfNecessary, calculateIsMachineMustExplode,
// explodeMachine, machineRepaired, reduceStrengthOnRepair, getRemainingUses) and room.lua (Room:crashRoom).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Research;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Objects;

/// <summary>
/// A machine's wear: how many uses it stands between repairs (<c>strength</c>, from the hospital's research of it) and how
/// many it has had since the last repair (<c>times_used</c>).
/// </summary>
public sealed class MachineState
{
    internal MachineState(int strength) => Strength = strength;

    /// <summary>Uses it stands between repairs.</summary>
    public int Strength { get; internal set; }

    /// <summary>Uses since it was built or last repaired.</summary>
    public int TimesUsed { get; internal set; }

    /// <summary>Uses since it was built (<c>total_usage</c>).</summary>
    public int TotalUsage { get; internal set; }

    /// <summary><c>getRemainingUses</c>: below 6 it wants a handyman, below 4 urgently and it smokes, below 1 it may explode.</summary>
    public int RemainingUses => Strength - TimesUsed;

    /// <summary><c>isBreaking</c>: smoking, needing a repair urgently.</summary>
    public bool IsBreaking => RemainingUses < 4;

    /// <summary>
    /// The player called a handyman from the machine window (<c>setRepairingStatus</c>, the room's <c>manual_repair</c> and
    /// <c>needs_repair</c>): the room lets no patient in until the machine is repaired.
    /// </summary>
    public bool IsRepairCalled { get; internal set; }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Strength);
        writer.WriteInt32(TimesUsed);
        writer.WriteInt32(TotalUsage);
        writer.WriteBool(IsRepairCalled);
    }

    internal static MachineState Read(SimReader reader) =>
        new(reader.ReadInt32())
        {
            TimesUsed = reader.ReadInt32(),
            TotalUsage = reader.ReadInt32(),
            IsRepairCalled = reader.Version is 0 or >= 73 && reader.ReadBool(),
        };
}

/// <summary>What happens to machines as they are used.</summary>
internal static class Machines
{
    /// <summary>A machine that is placed starts as strong as the hospital's research has made it (<c>start_strength</c>).</summary>
    internal static void OnPlaced(Simulation simulation, RoomObject placed, int player)
    {
        if (ResearchObjects.Find(placed.ObjectType) is not { IsMachine: true } research
            || simulation.Content.FindObject(placed.ObjectType)?.Machine is null)
        {
            return;
        }

        var strength = player >= 0 && player < simulation.Hospitals.Count
            && simulation.Hospitals[player].Research.Objects.TryGetValue(placed.ObjectType, out var progress) && progress.StartStrength > 0
                ? progress.StartStrength
                : research.DefaultStrength;
        placed.Machine = new MachineState(strength);
    }

    /// <summary>
    /// <c>Machine:machineUsed</c>, after someone other than a handyman has used it: one use more; worn out it may
    /// explode, which crashes the room.
    /// </summary>
    internal static void Used(Simulation simulation, RoomObject placed, Humanoid user)
    {
        if (placed.Machine is not { } machine || user.Profile?.Class == Staff.StaffClass.Handyman
            || simulation.FindEntity(placed.RoomId) is not Room { IsCrashed: false } room)
        {
            return;
        }

        machine.TotalUsage++;
        machine.TimesUsed++;
        if (MustExplode(simulation, room, machine))
        {
            Crash(simulation, room);
        }
    }

    /// <summary>
    /// <c>calculateIsMachineMustExplode</c>: worn out (no uses left), it explodes without a fire extinguisher in the room or
    /// four uses past; otherwise the chance is 2 over its strength, plus a fifth for each use past, less a twentieth for
    /// each extinguisher up to four, plus a twentieth, kept between 5 and 95 percent.
    /// </summary>
    private static bool MustExplode(Simulation simulation, Room room, MachineState machine)
    {
        var remaining = machine.RemainingUses;
        if (remaining >= 1)
        {
            return false;
        }

        var extinguishers = Math.Min(4, simulation.Entities.Count(e => e is RoomObject { ObjectType: "extinguisher" } o && o.RoomId == room.Id));
        if (extinguishers == 0 || remaining < -3)
        {
            return true;
        }

        var chance = (2m / machine.Strength) + (remaining * -0.2m) - (extinguishers * 0.05m) + 0.05m;
        chance = Math.Clamp(chance, 0.05m, 0.95m);
        return simulation.Random.Next(1_000_000) / 1_000_000m < chance;
    }

    /// <summary>
    /// <c>machineRepaired</c> and <c>reduceStrengthOnRepair</c>: as good as new, though a well worn machine (more uses over
    /// its strength) may come out one use weaker, never below 2.
    /// </summary>
    internal static void Repaired(Simulation simulation, RoomObject placed)
    {
        if (placed.Machine is not { } machine)
        {
            return;
        }

        if (machine.Strength > 2 && simulation.Random.Next(1_000_000) / 1_000_000m < (decimal)machine.TimesUsed / machine.Strength)
        {
            machine.Strength--;
        }

        machine.TimesUsed = 0;
        machine.IsRepairCalled = false;
    }

    /// <summary>
    /// The machine window's call button (<c>UIMachine:callHandyman</c>): unless the machine is as good as new, its repair
    /// becomes an urgent task and its room lets no patient in until then.
    /// </summary>
    internal static void CallHandyman(RoomObject placed)
    {
        if (placed.Machine is { TimesUsed: not 0 } machine)
        {
            machine.IsRepairCalled = true;
        }
    }

    /// <summary>
    /// The machine window's replace button (<c>UIMachine:replaceMachine</c>, <c>Machine:replaceMachine</c>): the hospital pays
    /// what the machine costs now and gets a new one, as strong as its research has made it. False without the money.
    /// </summary>
    internal static bool Replace(Simulation simulation, Hospital hospital, RoomObject placed)
    {
        if (placed.Machine is not { } machine)
        {
            return false;
        }

        var cost = hospital.Research.ObjectCost(simulation.Rules, placed.ObjectType);
        if (hospital.Balance < cost)
        {
            return false;
        }

        hospital.Spend(simulation.Hours, cost, TransactionKind.MachineReplacement, placed.ObjectType);
        machine.TotalUsage = 0;
        machine.TimesUsed = 0;
        machine.IsRepairCalled = false;
        machine.Strength = hospital.Research.Objects.TryGetValue(placed.ObjectType, out var progress) && progress.StartStrength > 0
            ? progress.StartStrength
            : ResearchObjects.Find(placed.ObjectType)?.DefaultStrength ?? machine.Strength;
        return true;
    }

    /// <summary>
    /// <c>explodeMachine</c> and <c>Room:crashRoom</c>: everyone in the room dies, its other objects go, soot covers its floor
    /// and walls, the hospital loses the room's value and 50 reputation, and the room stays a wreck until it is taken down.
    /// </summary>
    internal static void Crash(Simulation simulation, Room room)
    {
        var owner = RoomVisits.Owner(simulation, room);
        room.IsCrashed = true;
        room.IsActive = false;
        room.StaffId = 0;
        room.StaffPresent = false;
        room.PatientId = 0;

        // Patients queueing at the door look for another room.
        if (room.DoorQueue is { } queue)
        {
            foreach (var id in queue.Members.ToList())
            {
                if (simulation.FindEntity(id) is Humanoid { Behaviour: PatientBehaviour patient } waiting)
                {
                    patient.LeaveClosedRoom(simulation, waiting, room);
                }
            }
        }

        foreach (var humanoid in simulation.Entities.OfType<Humanoid>().ToList())
        {
            var inside = room.Area.Contains(humanoid.Tile) || (humanoid.IsCrossingDoor && room.Area.Contains(humanoid.StepTarget));
            if (!inside || humanoid.IsDespawning)
            {
                continue;
            }

            humanoid.ClearActionAnimation();
            if (humanoid.Behaviour is PatientBehaviour patient)
            {
                patient.Die(simulation, humanoid, animated: false);
            }
            else
            {
                humanoid.StopWalking();
                humanoid.IsDespawning = true;
            }
        }

        foreach (var placed in simulation.Entities.OfType<RoomObject>().Where(o => o.RoomId == room.Id && o.Machine is null))
        {
            placed.IsDestroyed = true;
        }

        ObjectPlacer.Reoccupy(simulation, room);

        for (var x = room.Area.X; x < room.Area.Right; x++)
        {
            for (var y = room.Area.Y; y < room.Area.Bottom; y++)
            {
                Litter.Drop(simulation, owner, LitterType.SootFloor, new(x, y), false);
            }
        }

        for (var x = room.Area.X; x < room.Area.Right; x++)
        {
            var tile = new Maps.TilePosition(x, room.Area.Y);
            Litter.Drop(simulation, owner, simulation.Content.IsWindowWall(simulation.Map[tile.X, tile.Y].NorthWall) ? LitterType.SootWindow : LitterType.SootWall, tile, true);
        }

        for (var y = room.Area.Y; y < room.Area.Bottom; y++)
        {
            var tile = new Maps.TilePosition(room.Area.X, y);
            Litter.Drop(simulation, owner, simulation.Content.IsWindowWall(simulation.Map[tile.X, tile.Y].WestWall) ? LitterType.SootWindow : LitterType.SootWall, tile, false);
        }

        if (owner >= 0 && owner < simulation.Hospitals.Count)
        {
            var hospital = simulation.Hospitals[owner];
            if (simulation.Content.FindRoom(room.RoomType) is { } definition)
            {
                hospital.ChangeValue(-RoomPurchases.BuildCost(simulation, hospital, definition));
            }

            hospital.ChangeReputation(simulation.Random, -50, null);
        }
    }
}
