using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Sim;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Import;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using CorsixNet.Sim.Staff;

namespace CorsixNet.Content.Saves;

/// <summary>
/// Starts a game from a save of the original Theme Hospital (<see cref="OriginalSaveGame"/>): the save's level is
/// started as usual and the player's hospital rebuilt on it (<see cref="GameImport"/>).
/// </summary>
public static class OriginalSaveImporter
{
    /// <summary>
    /// Objects not bought and placed by the player: entrance doors (the level's map places them), the second halves of
    /// two-part objects (op_sink2, operating_table_b; placed with their first half), litter, the helicopter and ratholes.
    /// </summary>
    private static readonly byte[] SkippedTypes = [58, 59, 34, 12, 62, 63, 64];

    /// <summary>Training progress as the original stores it for a whole ability (assumed; seen growing a few points a month).</summary>
    private const decimal TrainingScale = 100m;

    /// <summary>The radiation shield: both halves carry its type; the first one in the save is placed.</summary>
    private const byte RadiationShield = 28;

    /// <summary>Starts a simulation from <paramref name="save"/> with the level files of <paramref name="install"/>.</summary>
    /// <exception cref="InvalidDataException">The save names no level of the campaign.</exception>
    public static (Simulation Simulation, ImportReport Report) Start(ThemeHospitalInstall install, OriginalSaveGame save, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(save);
        if (save.Level is < 1 or > 12)
        {
            throw new InvalidDataException($"The save is of level {save.Level}, not one of the campaign's 12.");
        }

        var level = LevelMapLoader.LoadOriginal(install, $"LEVEL.L{save.Level}", DifficultyOf(save.Difficulty));
        var simulation = level.StartSimulation(seed);
        var report = GameImport.Apply(simulation, ToImportedGame(save, simulation));
        return (simulation, report);
    }

    /// <summary>The difficulty the save's level files are named after.</summary>
    public static Difficulty DifficultyOf(string name) => name.ToUpperInvariant() switch
    {
        "EASY" => Difficulty.Easy,
        "HARD" => Difficulty.Hard,
        _ => Difficulty.Medium,
    };

    /// <summary>What of <paramref name="save"/>'s player hospital is rebuilt, in the terms of <paramref name="simulation"/>'s content.</summary>
    public static ImportedGame ToImportedGame(OriginalSaveGame save, Simulation simulation)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(simulation);
        var content = simulation.Content;
        var hospital = save.Hospitals[0];

        var rooms = new List<ImportedRoom>();
        foreach (var room in save.Rooms.Where(r => r.Width > 1 && r.Height > 1))
        {
            if (content.Rooms.FirstOrDefault(r => r.LevelConfigId == room.Type) is { } definition)
            {
                var area = new TileRect(room.Origin.X, room.Origin.Y, room.Width, room.Height);
                rooms.Add(new ImportedRoom(definition.Id, area, DoorOf(room)));
            }
        }

        var objects = new List<ImportedObject>();
        foreach (var entity in save.Entities.Where(e => e.Kind == 6 && e.TileX != 0 && !SkippedTypes.Contains(e.Type)))
        {
            if (entity.Type == RadiationShield && objects.Any(o => o.ObjectType == "radiation_shield"
                && Math.Abs(o.Tile.X - entity.TileX) + Math.Abs(o.Tile.Y - entity.TileY) == 1))
            {
                continue;
            }

            if (content.Objects.FirstOrDefault(o => o.ObjectType == entity.Type && !o.Id.EndsWith("_b", StringComparison.Ordinal)) is { } definition)
            {
                objects.Add(new ImportedObject(
                    definition.Id,
                    new TilePosition(entity.TileX, entity.TileY),
                    DirectionOf(entity.Direction),
                    entity.Strength,
                    entity.TimesUsed));
            }
        }

        var prices = new List<(string, int)>();
        for (var index = 1; index < OriginalSaveHospital.DiseaseCount; index++)
        {
            var record = hospital.Disease(index);
            if (record.ChargePercent > 0 && DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == index && !d.IsPseudo) is { } disease)
            {
                prices.Add((disease.Id, record.ChargePercent / 100));
            }
        }

        var staff = new List<ImportedStaff>();
        foreach (var entity in save.Entities.Where(e => e.Kind == 4 && e.TileX != 0))
        {
            // Type 4 is a doctor dressed for surgery.
            var staffClass = entity.Type is 4 ? StaffClass.Doctor : (StaffClass)Math.Min((int)entity.Type, 3);
            var flags = entity.StaffFlags;
            var (surgery, psychiatry, research) = entity.TrainingProgress;
            var name = new ImportedStaffName(entity.Initial, entity.NameStart, entity.NameEnd, entity.Description.Misc, entity.Description.Character);
            var profile = StaffProfile.Import(
                staffClass,
                entity.Skill,
                entity.AttentionToDetail * 100,
                psychiatrist: (flags & 64) != 0,
                surgeon: (flags & 32) != 0,
                researcher: (flags & 256) != 0,
                entity.Salary,
                (byte)entity.Layers[5],
                [surgery / TrainingScale, psychiatry / TrainingScale, research / TrainingScale],
                name,
                simulation.Rules.Staff,
                simulation.Random);
            staff.Add(new ImportedStaff(profile, new TilePosition(entity.TileX, entity.TileY), entity.Morale / 100m, 1m - (entity.Energy / 1000m)));
        }

        return new ImportedGame
        {
            Staff = staff,
            Month = save.Month + 1,
            Day = Math.Max(1, save.Day),
            Balance = hospital.Money,
            Loan = hospital.Loan,
            OwnedParcels = [.. Enumerable.Range(1, OriginalSaveGame.MaxPlots - 1).Where(p => save.PlotOwner(p) == 1 && p < simulation.Map.ParcelCount)],
            Rooms = rooms,
            Objects = objects,
            Policies =
            [
                (PolicyKind.SendHome, hospital.SendHomePercent * 10),
                (PolicyKind.GuessCure, hospital.GuessCurePercent * 10),
                (PolicyKind.StopProcedure, hospital.StopProcedure),
                (PolicyKind.GoToStaffRoom, hospital.StaffRestLevel),
                (PolicyKind.StaffAllowedToMove, hospital.StaffLeaveRooms ? 1 : 0),
            ],
            Prices = prices,
            RadiatorHeat = hospital.HeatingLevel is > 0 and var heat ? Math.Clamp(heat, Hospital.MinRadiatorHeat, Hospital.MaxRadiatorHeat) : null,
        };
    }

    /// <summary>
    /// The original's object direction code in CorsixNet's terms: 0 north, 1 east, 2 south, 3 west, as for the
    /// objects of level files. Some two-tile objects only fit another way; <see cref="GameImport"/> then tries the others.
    /// </summary>
    public static Direction DirectionOf(byte code) => (Direction)(code & 3);

    /// <summary>
    /// The door as a room plan wants it: on the north or west edge of the door's tile when the outside lies north or
    /// west, otherwise on the edge of the tile outside.
    /// </summary>
    private static DoorPlacement DoorOf(OriginalSaveRoom room)
    {
        var (dx, dy) = (room.Outside.X - room.Door.X, room.Outside.Y - room.Door.Y);
        return (dx, dy) switch
        {
            (0, -1) => new DoorPlacement(new TilePosition(room.Door.X, room.Door.Y), EdgeOrientation.North),
            (-1, 0) => new DoorPlacement(new TilePosition(room.Door.X, room.Door.Y), EdgeOrientation.West),
            (0, 1) => new DoorPlacement(new TilePosition(room.Outside.X, room.Outside.Y), EdgeOrientation.North),
            _ => new DoorPlacement(new TilePosition(room.Outside.X, room.Outside.Y), EdgeOrientation.West),
        };
    }
}
