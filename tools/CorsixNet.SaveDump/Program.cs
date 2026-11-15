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
    }

    Console.WriteLine($"Entities in use: {save.Entities.Count} of {OriginalSaveGame.EntityCount}");
    foreach (var group in save.Entities.GroupBy(e => e.Kind).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  kind {group.Key,2} ({KindName(group.Key)}): {group.Count()}");
    }

    var staff = save.Entities.Where(e => e.Kind == 4).ToList();
    if (staff.Count > 0)
    {
        Console.WriteLine("Staff (tile 0,0 = for hire):");
        foreach (var member in staff)
        {
            Console.WriteLine($"  #{member.Slot,-4} {StaffName(member.Type),-12} ({member.TileX},{member.TileY})");
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
            6 => ObjectName(entity.Type),
            _ => $"type {entity.Type}",
        };
        var bytes = Convert.ToHexString(entity.Raw.Span[..48]).ToLowerInvariant();
        Console.WriteLine($"#{entity.Slot,-4} kind {entity.Kind,2} {name,-22} ({entity.X / 256.0,6:0.00},{entity.Y / 256.0,6:0.00}) frame {entity.Frame,5}  {bytes}");
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
        while (end + 1 < OriginalSaveGame.Length && x[end + 1] != y[end + 1] && OriginalSaveGame.DescribeOffset(end + 1).StartsWith(Area(offset), StringComparison.Ordinal))
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

// Groups a run of changed bytes only while it stays in one tile, entity or hospital block.
static string Area(int offset)
{
    var description = OriginalSaveGame.DescribeOffset(offset);
    var cut = description.LastIndexOf(description.StartsWith("tile", StringComparison.Ordinal) ? " byte" : " +", StringComparison.Ordinal);
    return cut < 0 ? description : description[..cut];
}

static string Numbers(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, int offset, int length) => length switch
{
    2 => $"  ({BitConverter.ToUInt16(x.Slice(offset, 2))} -> {BitConverter.ToUInt16(y.Slice(offset, 2))})",
    >= 3 and <= 4 when offset + 4 <= x.Length => $"  (int32 {BitConverter.ToInt32(x.Slice(offset, 4))} -> {BitConverter.ToInt32(y.Slice(offset, 4))})",
    _ => string.Empty,
};

static string KindName(byte kind) => kind switch
{
    2 => "patients?",
    4 => "staff",
    6 => "objects",
    _ => "unknown",
};

static string StaffName(byte type) => type switch
{
    0 => "nurse",
    1 => "doctor",
    2 => "handyman",
    3 => "receptionist",
    4 => "doctor (4)?",
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
