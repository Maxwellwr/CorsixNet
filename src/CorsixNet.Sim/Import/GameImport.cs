using CorsixNet.Sim.Calendar;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;

namespace CorsixNet.Sim.Import;

/// <summary>A room of an imported save.</summary>
/// <param name="RoomType">Id of the room's definition.</param>
/// <param name="Area">The room's tiles.</param>
/// <param name="Door">Where its door is.</param>
public sealed record ImportedRoom(string RoomType, TileRect Area, DoorPlacement Door);

/// <summary>An object of an imported save, in a room or in a corridor.</summary>
/// <param name="ObjectType">Id of the object's definition.</param>
/// <param name="Tile">Tile it stands on.</param>
/// <param name="Orientation">Way it faces.</param>
/// <param name="Strength">Its strength, for machines.</param>
/// <param name="TimesUsed">How many times it has been used, for machines.</param>
public sealed record ImportedObject(string ObjectType, TilePosition Tile, Direction Orientation, int Strength, int TimesUsed);

/// <summary>
/// The state of a hospital taken from a save of another game (the original Theme Hospital), to be rebuilt on a
/// simulation started on the same level.
/// </summary>
public sealed record ImportedGame
{
    /// <summary>Month of the game, from 1.</summary>
    public int Month { get; init; } = 1;

    /// <summary>Day of the month, from 1.</summary>
    public int Day { get; init; } = 1;

    public long Balance { get; init; }

    public long Loan { get; init; }

    /// <summary>Parcels the player owns, besides those the level starts with.</summary>
    public IReadOnlyList<int> OwnedParcels { get; init; } = [];

    public IReadOnlyList<ImportedRoom> Rooms { get; init; } = [];

    public IReadOnlyList<ImportedObject> Objects { get; init; } = [];

    /// <summary>Policy values as <see cref="HospitalPolicies"/> keeps them; missing ones are left as they are.</summary>
    public IReadOnlyList<(PolicyKind Kind, int Value)> Policies { get; init; } = [];

    /// <summary>Treatment prices in percent of the standard price, by disease id.</summary>
    public IReadOnlyList<(string DiseaseId, int Percent)> Prices { get; init; } = [];

    /// <summary>Radiator heat in tenths, or null to keep the level's.</summary>
    public int? RadiatorHeat { get; init; }
}

/// <summary>What could not be rebuilt from an imported save.</summary>
/// <param name="Problems">One line for each room or object left out.</param>
public sealed record ImportReport(IReadOnlyList<string> Problems);

/// <summary>
/// Rebuilds an imported hospital on a simulation freshly started on the save's level: the player buys the parcels,
/// builds and furnishes the rooms and places the corridor objects through the usual commands, so the result holds
/// together as if it had been played; the money, date and settings are then set to the save's.
/// </summary>
public static class GameImport
{
    /// <summary>Rebuilds <paramref name="game"/> for player 0 of <paramref name="simulation"/>, which must not have run yet.</summary>
    public static ImportReport Apply(Simulation simulation, ImportedGame game)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(game);
        const int player = 0;
        var problems = new List<string>();
        var hospital = simulation.Hospitals[player];

        // Enough money to build everything again; the save's own balance is set at the end.
        hospital.ImportFinances(long.MaxValue / 4, 0);

        foreach (var parcel in game.OwnedParcels)
        {
            if (simulation.Map.GetParcelOwner(parcel) == 0)
            {
                simulation.SetParcelOwner(parcel, player + 1);
            }
        }

        var placed = new HashSet<ImportedObject>();
        foreach (var room in game.Rooms)
        {
            hospital.DiscoverRoom(room.RoomType);
            new BuildRoomCommand(new RoomPlan(room.RoomType, room.Area, room.Door)).Execute(simulation, player);
            var built = simulation.Entities.OfType<Room>().LastOrDefault(r => r.Area == room.Area && r.RoomType == room.RoomType);
            if (built is null)
            {
                problems.Add($"room {room.RoomType} at {room.Area} could not be built");
                continue;
            }

            foreach (var obj in game.Objects.Where(o => room.Area.Contains(o.Tile)))
            {
                if (Place(simulation, obj, player))
                {
                    placed.Add(obj);
                }
                else
                {
                    problems.Add($"{obj.ObjectType} at {obj.Tile} facing {obj.Orientation} in room {room.RoomType} could not be placed: {Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, obj.Orientation, player)}");
                }
            }

            new ConfirmRoomCommand(built.Id).Execute(simulation, player);
            if (!built.IsActive)
            {
                problems.Add($"room {room.RoomType} at {room.Area} could not be finished");
            }
        }

        foreach (var obj in game.Objects.Where(o => !placed.Contains(o) && !game.Rooms.Any(r => r.Area.Contains(o.Tile))))
        {
            if (!Place(simulation, obj, player))
            {
                problems.Add($"{obj.ObjectType} at {obj.Tile} facing {obj.Orientation} could not be placed: {Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, obj.Orientation, player)}");
            }
        }

        foreach (var (kind, value) in game.Policies)
        {
            hospital.Policies.Set(kind, value);
        }

        foreach (var (diseaseId, percent) in game.Prices)
        {
            new SetTreatmentPriceCommand(diseaseId, percent).Execute(simulation, player);
        }

        if (game.RadiatorHeat is { } heat)
        {
            hospital.SetRadiatorHeat(heat);
        }

        hospital.ImportFinances(game.Balance, game.Loan);
        if (game.Month > 1 || game.Day > 1)
        {
            simulation.SkipToLastHour(new GameDate(1, game.Month, game.Day, 0));
        }

        return new ImportReport(problems);
    }

    private static bool Place(Simulation simulation, ImportedObject obj, int player)
    {
        // The original's direction codes do not always match CorsixNet's footprints: take the save's way if the object
        // fits so, else the opposite way, else any way it fits on the same tile.
        var orientation = obj.Orientation;
        Direction[] tries = [obj.Orientation, (Direction)(((int)obj.Orientation + 2) % 4), (Direction)(((int)obj.Orientation + 3) % 4), (Direction)(((int)obj.Orientation + 1) % 4)];
        foreach (var candidate in tries)
        {
            if (Objects.ObjectPlacer.Check(simulation, obj.ObjectType, obj.Tile, candidate, player) == Objects.ObjectPlacementProblem.None)
            {
                orientation = candidate;
                break;
            }
        }

        var before = simulation.Entities.Count;
        new PlaceObjectCommand(obj.ObjectType, obj.Tile, orientation).Execute(simulation, player);
        var created = simulation.Entities.Skip(before).OfType<RoomObject>().FirstOrDefault(o => o.Tile == obj.Tile && o.ObjectType == obj.ObjectType);
        if (created is null)
        {
            return false;
        }

        if (created.Machine is { } machine)
        {
            machine.Strength = obj.Strength;
            machine.TimesUsed = obj.TimesUsed;
        }

        return true;
    }
}
