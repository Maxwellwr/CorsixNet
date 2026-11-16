// Prints what is known of an original Theme Hospital save (SAVE/<name>.H<n>) and compares saves,
// to check the format against the game and to find fields that are not decoded yet.
//
//   dotnet run --project tools/CorsixNet.SaveDump -- summary <save>
//   dotnet run --project tools/CorsixNet.SaveDump -- entities <save> [<kind>]
//   dotnet run --project tools/CorsixNet.SaveDump -- diff <save a> <save b>
//
// For diff, save twice with one action in between (buy an object, hire someone): the changed bytes
// show where that state lives.
// Object names follow the object type numbers of CorsixTH's CorsixTH/Lua/objects (MIT; see THIRD-PARTY-NOTICES.md).

using System.Globalization;
using CorsixNet.Formats;

const string Usage = """
    usage: CorsixNet.SaveDump summary <save>
           CorsixNet.SaveDump entities <save> [<kind>]
           CorsixNet.SaveDump diff <save a> <save b>
           CorsixNet.SaveDump coverage <save> [<save>...]
    """;

try
{
    switch (args)
    {
        case ["summary", var file]:
            Summary(Load(file));
            return 0;
        case ["entities", var file]:
            Entities(Load(file), null);
            return 0;
        case ["entities", var file, var kind] when byte.TryParse(kind, CultureInfo.InvariantCulture, out var k):
            Entities(Load(file), k);
            return 0;
        case ["diff", var a, var b]:
            Diff(Load(a), Load(b));
            return 0;
        case ["coverage", _, ..]:
            Coverage(args[1..].Select(Load).ToList());
            return 0;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception e) when (e is InvalidDataException or IOException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static OriginalSaveGame Load(string file) => OriginalSaveGame.Parse(File.ReadAllBytes(file));

static void Summary(OriginalSaveGame save)
{
    Console.WriteLine($"Level {save.Level} ({save.Difficulty}), day {save.Day}, month {save.Month + 1}");
    foreach (var (hospital, player) in save.Hospitals.Select((h, i) => (h, i)).Where(p => p.h.Kind != 0))
    {
        var who = hospital.Kind == 1 ? "player" : "computer";
        Console.WriteLine($"Hospital {player} \"{hospital.Name}\" ({who})");
        Console.WriteLine($"  money {hospital.Money}, loan {hospital.Loan}, value {hospital.Value}, size {hospital.Size} tiles");
        Console.WriteLine($"  cured {hospital.PeopleCured}, deaths {hospital.Deaths}, visitors {hospital.Visitors}");
        Console.WriteLine($"  staff {hospital.Staff}: {hospital.Doctors} doctors, {hospital.Nurses} nurses, {hospital.Handymen} handymen, {hospital.Receptionists} receptionists");
        Console.WriteLine($"  reputation {hospital.Reputation}, research (cure/diagnosis/drugs/improvements/specialisation) {string.Join('/', hospital.ResearchDistribution)}");
        Console.WriteLine($"  policy: guess cure {hospital.GuessCurePercent}%, send home {hospital.SendHomePercent}%, stop at {hospital.StopProcedure / 10}%, rest at {hospital.StaffRestLevel / 10}%, leave rooms {(hospital.StaffLeaveRooms ? "yes" : "no")}");
        foreach (var disease in Enumerable.Range(0, OriginalSaveHospital.DiseaseCount).Select(hospital.Disease).Where(d => d.Known && d.Charge > 0))
        {
            Console.WriteLine($"  expertise {disease.Index,2}: charge ${disease.Charge} ({disease.ChargePercent / 100}%), earned ${disease.MoneyEarned}, cured {disease.Recoveries}, died {disease.Fatalities}, turned away {disease.TurnedAway}");
        }
    }

    var plots = Enumerable.Range(1, OriginalSaveGame.MaxPlots - 1).Where(p => save.PlotOwner(p) == 1).ToList();
    Console.WriteLine($"Plots bought: {string.Join(", ", plots)}");
    if (save.Statement.Count > 0)
    {
        Console.WriteLine($"Bank statement: {save.Statement.Count} lines; totals by kind: " + string.Join(", ", save.Statement.GroupBy(l => l.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Sum(l => l.Amount)}")));
    }

    Console.WriteLine($"Entities in use: {save.Entities.Count} of {OriginalSaveGame.EntityCount}");
    foreach (var group in save.Entities.GroupBy(e => e.Kind).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  kind {group.Key,2} ({KindName(group.Key)}): {group.Count()}");
    }

    if (save.Rooms.Count > 0)
    {
        Console.WriteLine("Rooms:");
        foreach (var room in save.Rooms)
        {
            Console.WriteLine($"  #{room.Index,-3} {RoomName(room.Type),-20} {room.Width}x{room.Height} at ({room.Origin.X},{room.Origin.Y}), door ({room.Door.X},{room.Door.Y})");
        }
    }

    var staff = save.Entities.Where(e => e.Kind == 4).ToList();
    if (staff.Count > 0)
    {
        Console.WriteLine("Staff (tile 0,0 = for hire):");
        var strings = LoadStrings();
        foreach (var member in staff)
        {
            var name = strings is null
                ? $"{member.Initial}. #{member.NameStart}/{member.NameEnd}"
                : $"{member.Initial}. {strings.Get(8, member.NameStart)}{strings.Get(9, member.NameEnd)}";
            Console.WriteLine($"  #{member.Slot,-4} {StaffName(member.Type),-12} {name,-16} {Roles(member.StaffFlags),-24} skill {member.Skill / 10.0,5:0.0}%  ${member.Salary,-4} ({member.TileX},{member.TileY})");
        }
    }

    var objects = save.Entities.Where(e => e.Kind == 6).GroupBy(e => e.Type).OrderBy(g => g.Key).ToList();
    if (objects.Count > 0)
    {
        Console.WriteLine("Objects:");
        foreach (var group in objects)
        {
            Console.WriteLine($"  {ObjectName(group.Key),-22} x{group.Count()}");
        }
    }
}

static void Entities(OriginalSaveGame save, byte? kind)
{
    foreach (var entity in save.Entities.Where(e => kind is null || e.Kind == kind))
    {
        var name = entity.Kind switch
        {
            4 => StaffName(entity.Type),
            6 or 8 => ObjectName(entity.Type),
            _ => $"type {entity.Type}",
        };
        var bytes = Convert.ToHexString(entity.Raw.Span[..48]).ToLowerInvariant();
        Console.WriteLine($"#{entity.Slot,-4} kind {entity.Kind,2} {name,-22} ({entity.X / 256.0,6:0.00},{entity.Y / 256.0,6:0.00}) frame {entity.Frame,5} dir {entity.Direction}  {bytes}");
    }
}

static void Diff(OriginalSaveGame a, OriginalSaveGame b)
{
    var x = a.Data.Span;
    var y = b.Data.Span;
    var changed = 0;
    for (var offset = 0; offset < OriginalSaveGame.Length; offset++)
    {
        if (x[offset] == y[offset])
        {
            continue;
        }

        changed++;
        var end = offset;
        while (end + 1 < OriginalSaveGame.Length && x[end + 1] != y[end + 1] && Group(end + 1) == Group(offset))
        {
            end++;
            changed++;
        }

        var length = end - offset + 1;
        Console.WriteLine($"0x{offset:X5} {OriginalSaveGame.DescribeOffset(offset),-28} {Convert.ToHexString(x.Slice(offset, length)).ToLowerInvariant()} -> {Convert.ToHexString(y.Slice(offset, length)).ToLowerInvariant()}{Numbers(x, y, offset, length)}");
        offset = end;
    }

    Console.WriteLine($"{changed} bytes differ");
}

// Groups a run of changed bytes only while it stays in one field, or in the unknown bytes of one record.
static (OriginalSaveArea Area, int Index, OriginalSaveField? Field) Group(int offset)
{
    var (area, relative) = OriginalSaveGame.Locate(offset, out var index);
    return (area, index, OriginalSaveFields.Find(area, relative));
}

// Of the bytes that differ between the given saves (the bytes that carry state), how many lie in known fields.
static void Coverage(IReadOnlyList<OriginalSaveGame> saves)
{
    var counts = new Dictionary<(OriginalSaveArea Area, string Status), int>();
    var first = saves[0].Data.Span;
    for (var offset = 0; offset < OriginalSaveGame.Length; offset++)
    {
        var value = first[offset];
        var varies = saves.Any(save => save.Data.Span[offset] != value) || (saves.Count == 1 && value != 0);
        if (!varies)
        {
            continue;
        }

        var (area, _) = OriginalSaveGame.Locate(offset);
        var status = OriginalSaveFields.FindInFile(offset) switch
        {
            { Verified: true } => "known",
            { } => "spec only",
            null => "unknown",
        };
        counts[(area, status)] = counts.GetValueOrDefault((area, status)) + 1;
    }

    var basis = saves.Count == 1 ? "non-zero" : "varying";
    var total = counts.Values.Sum();
    Console.WriteLine($"{total} {basis} bytes of {OriginalSaveGame.Length}");
    foreach (var area in Enum.GetValues<OriginalSaveArea>())
    {
        var known = counts.GetValueOrDefault((area, "known"));
        var spec = counts.GetValueOrDefault((area, "spec only"));
        var unknown = counts.GetValueOrDefault((area, "unknown"));
        var all = known + spec + unknown;
        if (all > 0)
        {
            Console.WriteLine($"  {area,-9} {all,7}: known {Percent(known, all)}, spec only {Percent(spec, all)}, unknown {Percent(unknown, all)}");
        }
    }

    var allKnown = counts.Where(c => c.Key.Status == "known").Sum(c => c.Value);
    Console.WriteLine($"  total     {total,7}: known {Percent(allKnown, total)}");
}

static string Percent(int part, int whole) => $"{100.0 * part / whole,5:0.0}%";

static string Numbers(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, int offset, int length) => length switch
{
    2 => $"  ({BitConverter.ToUInt16(x.Slice(offset, 2))} -> {BitConverter.ToUInt16(y.Slice(offset, 2))})",
    >= 3 and <= 4 when offset + 4 <= x.Length => $"  (int32 {BitConverter.ToInt32(x.Slice(offset, 4))} -> {BitConverter.ToInt32(y.Slice(offset, 4))})",
    _ => string.Empty,
};

// Surnames are pieces of string sections 8 and 9 of the game's LANG-0.DAT; without the game only their numbers are shown.
static StringTable? LoadStrings()
{
    var install = ThemeHospitalInstall.Locate();
    return install?.FileExists("DATA", "LANG-0.DAT") == true ? StringTable.Parse(install.ReadDataFile("DATA", "LANG-0.DAT")) : null;
}

static string Roles(int flags)
{
    var roles = new List<string>();
    if ((flags & 8) != 0)
    {
        roles.Add("junior");
    }

    if ((flags & 128) != 0)
    {
        roles.Add("consultant");
    }

    if ((flags & 32) != 0)
    {
        roles.Add("surgeon");
    }

    if ((flags & 64) != 0)
    {
        roles.Add("psychiatrist");
    }

    if ((flags & 256) != 0)
    {
        roles.Add("researcher");
    }

    return string.Join(' ', roles);
}

// Numbered as #rooms[n] in the level .SAM files.
static string RoomName(byte type) => type switch
{
    1 => "(hospital?)",
    4 => "(unknown 4)",
    7 => "GP's office",
    8 => "psychiatry",
    9 => "ward",
    10 => "operating theatre",
    11 => "pharmacy",
    12 => "cardiogram",
    13 => "scanner",
    14 => "ultrascan",
    15 => "blood machine",
    16 => "x-ray",
    17 => "inflation",
    18 => "dna fixer",
    19 => "hair restoration",
    20 => "slack tongue",
    21 => "fracture clinic",
    22 => "training",
    23 => "electrolysis",
    24 => "jelly vat",
    25 => "staff room",
    26 => "tv",
    27 => "general diagnosis",
    28 => "research",
    29 => "toilets",
    30 => "decontamination",
    _ => $"room type {type}",
};

static string KindName(byte kind) => kind switch
{
    2 => "patients",
    4 => "staff",
    6 => "objects",
    8 => "doors",
    20 => "VIP",
    _ => "unknown",
};

static string StaffName(byte type) => type switch
{
    0 => "nurse",
    1 => "doctor",
    2 => "handyman",
    3 => "receptionist",
    4 => "doctor (op)",
    _ => $"staff {type}",
};

static string ObjectName(byte type) => type switch
{
    1 => "desk",
    2 => "cabinet",
    3 => "door",
    4 => "bench",
    6 => "chair",
    7 => "drinks machine",
    8 => "bed",
    9 => "inflator",
    10 => "pool table",
    11 => "reception desk",
    12 => "operating table b",
    13 => "cardio",
    14 => "scanner",
    15 => "console",
    16 => "screen",
    18 => "couch",
    19 => "sofa",
    20 => "crash trolley",
    21 => "tv",
    22 => "ultrascanner",
    23 => "dna fixer",
    24 => "cast remover",
    25 => "hair restorer",
    26 => "slicer",
    27 => "x-ray",
    28 => "radiation shield",
    29 => "x-ray viewer",
    30 => "operating table",
    32 => "sink",
    33 => "op sink 1",
    34 => "op sink 2",
    35 => "surgeon screen",
    36 => "lecture chair",
    37 => "projector",
    39 => "pharmacy cabinet",
    40 => "computer",
    41 => "analyser",
    42 => "blood machine",
    43 => "fire extinguisher",
    44 => "radiator",
    45 => "plant",
    46 => "electrolyser",
    47 => "jelly moulder",
    48 => "gates to hell",
    50 => "bin",
    51 => "loo",
    52 => "swing door (left)",
    53 => "swing door (right)",
    54 => "shower",
    55 => "autopsy",
    56 => "bookcase",
    57 => "video game",
    58 => "entrance door (left)",
    59 => "entrance door (right)",
    60 => "skeleton",
    61 => "comfortable chair",
    62 => "litter",
    63 => "helicopter",
    64 => "rathole",
    _ => $"object {type}",
};
