using OpenWards.Content.Levels;
using OpenWards.Formats;
using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Diseases;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Import;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Research;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;

namespace OpenWards.Content.Saves;

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

        var patients = new List<ImportedPatient>();
        foreach (var entity in save.Entities.Where(e => e.Kind == 2 && e.TileX != 0 && e.Disease is > 0 and < OriginalSaveHospital.DiseaseCount))
        {
            if (DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == entity.Disease && !d.IsPseudo) is not { } disease)
            {
                continue;
            }

            // The newest recorded point of the health graph (values above 100 are points not recorded yet).
            var health = entity.HealthHistory.ToArray().Where(v => v <= 100).Select(v => (int)v).LastOrDefault(90);
            patients.Add(new ImportedPatient(
                disease.Id,
                new TilePosition(entity.TileX, entity.TileY),
                health / 100m,
                entity.Happiness / 100m,
                1m - (entity.Thirst / 100m),
                entity.Warmth / 100m,
                entity.DiagnosisCertainty * 1000 / 255));
        }

        var casebook = new List<ImportedCase>();
        for (var index = 1; index < OriginalSaveHospital.DiseaseCount; index++)
        {
            var record = hospital.Disease(index);
            foreach (var disease in DiseaseCatalog.All.Where(d => d.ExpertiseId == index))
            {
                casebook.Add(new ImportedCase(disease.Id, record.Discovered, record.DrugEffectiveness, record.Recoveries, record.Fatalities, record.TurnedAway, record.MoneyEarned));
            }
        }

        var distribution = hospital.ResearchDistribution;
        var shares = new List<(ResearchCategory, int)>();
        for (var i = 0; i < distribution.Count && i < 5; i++)
        {
            shares.Add(((ResearchCategory)i, Math.Max(0, distribution[i])));
        }

        return new ImportedGame
        {
            Staff = staff,
            Patients = patients,
            Visitors = hospital.Visitors,
            Cured = hospital.PeopleCured,
            Deaths = hospital.Deaths,
            Reputation = hospital.Reputation,
            Value = hospital.Value,
            Months = Months(save),
            Statement = Statement(save, hospital.Money),
            Casebook = casebook,
            ResearchShares = shares,
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
    /// The original's object direction code in OpenWards's terms: 0 north, 1 east, 2 south, 3 west, as for the
    /// objects of level files. Some two-tile objects only fit another way; <see cref="GameImport"/> then tries the others.
    /// </summary>
    public static Direction DirectionOf(byte code) => (Direction)(code & 3);

    /// <summary>The graphs' figures of the months played, oldest first.</summary>
    private static List<MonthStatistics> Months(OriginalSaveGame save)
    {
        var series = Enum.GetValues<OriginalSaveChart>().ToDictionary(s => s, save.Chart);
        var months = new List<MonthStatistics>();
        for (var i = 0; i < 12; i++)
        {
            var month = new MonthStatistics(
                series[OriginalSaveChart.MoneyIn][i],
                series[OriginalSaveChart.MoneyOut][i],
                series[OriginalSaveChart.Wages][i],
                series[OriginalSaveChart.Balance][i],
                series[OriginalSaveChart.Visitors][i],
                series[OriginalSaveChart.Cures][i],
                series[OriginalSaveChart.Deaths][i],
                series[OriginalSaveChart.Reputation][i]);
            if (month != default)
            {
                months.Add(month);
            }
        }

        return months;
    }

    /// <summary>
    /// This year's bank statement lines, oldest first, as transactions; balances are worked back from today's. Lines of
    /// kinds without a counterpart are left out.
    /// </summary>
    private static List<Transaction> Statement(OriginalSaveGame save, long balance)
    {
        var lines = save.Statement.Select((line, order) => (line, order))
            .Where(item => item.line.Month <= save.Month && item.line.Day >= 1)
            .OrderBy(item => item.line.Month).ThenBy(item => item.line.Day).ThenBy(item => item.order)
            .Select(item => item.line)
            .ToList();
        var transactions = new List<Transaction>();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (KindOf(line.Kind) is not { } kind)
            {
                continue;
            }

            var subject = kind == TransactionKind.Treatment
                ? DiseaseCatalog.All.FirstOrDefault(d => d.ExpertiseId == line.Detail)?.Id ?? string.Empty
                : string.Empty;
            var hour = new GameDate(1, line.Month + 1, line.Day, 0).ToHours();
            transactions.Insert(0, new Transaction(hour, kind, subject, line.Amount, balance));
            balance -= line.Amount;
        }

        return transactions;
    }

    private static TransactionKind? KindOf(byte kind) => kind switch
    {
        1 => TransactionKind.Wages,
        3 => TransactionKind.BuyObject,
        4 => TransactionKind.BuildRoom,
        7 => TransactionKind.Treatment,
        13 => TransactionKind.Drinks,
        16 => TransactionKind.Heating,
        17 => TransactionKind.Insurance,
        18 => TransactionKind.Loan,
        20 => TransactionKind.LoanInterest,
        22 => TransactionKind.DrugCost,
        23 => TransactionKind.Overdraft,
        27 => TransactionKind.PersonalBonus,
        34 => TransactionKind.AwardBonus,
        35 => TransactionKind.TrophyBonus,
        _ => null,
    };

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
