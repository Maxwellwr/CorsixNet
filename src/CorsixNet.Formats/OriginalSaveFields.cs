namespace CorsixNet.Formats;

/// <summary>The repeating parts of an original save.</summary>
public enum OriginalSaveArea
{
    /// <summary>Bytes outside any repeating table.</summary>
    File,

    /// <summary>One 8-byte map tile.</summary>
    Tile,

    /// <summary>One 175-byte entity record.</summary>
    Entity,

    /// <summary>One 35-byte room record.</summary>
    Room,

    /// <summary>One 4697-byte hospital block.</summary>
    Hospital,
}

/// <summary>A known field of an original save.</summary>
/// <param name="Area">Where the field lives.</param>
/// <param name="Offset">Offset in the file (<see cref="OriginalSaveArea.File"/>) or in one record of the area.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="Name">Short name.</param>
/// <param name="Verified">Whether the field was checked against the game; unverified fields come from the community description only.</param>
public sealed record OriginalSaveField(OriginalSaveArea Area, int Offset, int Length, string Name, bool Verified);

/// <summary>
/// Every field of <see cref="OriginalSaveGame"/> that is known, verified or not, so that tools can name changed
/// bytes and measure how much of a save is understood.
/// </summary>
public static class OriginalSaveFields
{
    /// <summary>All known fields.</summary>
    public static IReadOnlyList<OriginalSaveField> All { get; } =
    [
        new(OriginalSaveArea.File, 2, 1, "level", true),
        new(OriginalSaveArea.File, 3, 10, "difficulty", true),
        new(OriginalSaveArea.File, OriginalSaveGame.PlotOffset, OriginalSaveGame.MaxPlots, "plot owners", true),
        new(OriginalSaveArea.File, OriginalSaveGame.ChartOffset, 8 * 3 * 12 * 4, "chart history", true),
        new(OriginalSaveArea.File, OriginalSaveGame.StatementOffset, OriginalSaveGame.StatementCount * 8, "bank statement", true),
        new(OriginalSaveArea.File, OriginalSaveGame.ObjectListOffset, OriginalSaveGame.ObjectTypes * 2, "first object of each type", true),
        new(OriginalSaveArea.File, OriginalSaveGame.DateOffset, 1, "day", true),
        new(OriginalSaveArea.File, OriginalSaveGame.DateOffset + 1, 1, "month", true),

        new(OriginalSaveArea.Tile, 0, 2, "entity", true),
        new(OriginalSaveArea.Tile, 2, 1, "floor", true),
        new(OriginalSaveArea.Tile, 3, 1, "north wall", true),
        new(OriginalSaveArea.Tile, 4, 1, "west wall", true),
        new(OriginalSaveArea.Tile, 5, 1, "flags", true),
        new(OriginalSaveArea.Tile, 6, 1, "shadow/litter", false),
        new(OriginalSaveArea.Tile, 7, 1, "owner", true),

        new(OriginalSaveArea.Entity, 0, 2, "next on tile", true),
        new(OriginalSaveArea.Entity, 4, 2, "x", true),
        new(OriginalSaveArea.Entity, 6, 2, "y", true),
        new(OriginalSaveArea.Entity, 8, 2, "frame", true),
        new(OriginalSaveArea.Entity, 12, 1, "kind", true),
        new(OriginalSaveArea.Entity, 29, 13, "animation layers", true),
        new(OriginalSaveArea.Entity, 14, 1, "type", true),
        new(OriginalSaveArea.Entity, 17, 1, "direction", true),
        new(OriginalSaveArea.Entity, 100, 2, "plant water (plants)", true),
        new(OriginalSaveArea.Entity, 108, 2, "salary", true),
        new(OriginalSaveArea.Entity, 124, 2, "staff flags", true),
        new(OriginalSaveArea.Entity, 126, 2, "skill", true),
        new(OriginalSaveArea.Entity, 132, 1, "initial", true),
        new(OriginalSaveArea.Entity, 133, 1, "name start", true),
        new(OriginalSaveArea.Entity, 138, 2, "description", true),
        new(OriginalSaveArea.Entity, 151, 3, "doctor training progress", true),
        new(OriginalSaveArea.Entity, 158, 10, "patient health history", true),
        new(OriginalSaveArea.Entity, 146, 1, "patient diagnosis certainty", true),
        new(OriginalSaveArea.Entity, 147, 1, "patient diagnosis steps?", false),
        new(OriginalSaveArea.Entity, 148, 1, "patient diagnosis started?", false),
        new(OriginalSaveArea.Entity, 168, 1, "patient disease", true),
        new(OriginalSaveArea.Entity, 169, 1, "patient diagnosis so far? (1 = none)", false),
        new(OriginalSaveArea.Entity, 171, 1, "patient next room purpose?", false),
        new(OriginalSaveArea.Entity, 55, 4, "times used (objects) / room + 1 (people)", true),
        new(OriginalSaveArea.Entity, 59, 4, "object strength", true),
        new(OriginalSaveArea.Entity, 75, 2, "next object of the same type", true),
        new(OriginalSaveArea.Entity, 81, 1, "morale / happiness", true),
        new(OriginalSaveArea.Entity, 120, 2, "staff energy", true),
        new(OriginalSaveArea.Entity, 83, 1, "patient warmth", true),
        new(OriginalSaveArea.Entity, 134, 1, "name end / patient thirst", true),

        new(OriginalSaveArea.Room, 0, 1, "type", true),
        new(OriginalSaveArea.Room, 1, 1, "width", true),
        new(OriginalSaveArea.Room, 2, 1, "height", true),
        new(OriginalSaveArea.Room, 4, 2, "outside tile", true),
        new(OriginalSaveArea.Room, 6, 2, "door tile", true),
        new(OriginalSaveArea.Room, 8, 2, "origin tile", true),
        new(OriginalSaveArea.Room, 14, 2, "someone in the room? (patient or staff)", false),
        new(OriginalSaveArea.Room, 16, 4, "staff slots", true),
        new(OriginalSaveArea.Room, 20, 2, "visitor count", true),
        new(OriginalSaveArea.Room, 30, 1, "max queue size", true),
        new(OriginalSaveArea.Room, 31, 1, "queue size", true),
        new(OriginalSaveArea.Room, 32, 1, "expected patients", true),

        new(OriginalSaveArea.Hospital, 0, 42, "name", true),
        new(OriginalSaveArea.Hospital, 42, 1, "kind", true),
        new(OriginalSaveArea.Hospital, 45, 4, "player salary (score)", true),
        new(OriginalSaveArea.Hospital, 53, 4, "money", true),
        new(OriginalSaveArea.Hospital, 61, 4, "loan", true),
        new(OriginalSaveArea.Hospital, 65, 4, "max loan?", false),
        new(OriginalSaveArea.Hospital, 161, 60, "yearly per-category sums? (reset on 1 January; +205 matched one insurer's debt once)", false),
        new(OriginalSaveArea.Hospital, 221, 4, "research points", true),
        new(OriginalSaveArea.Hospital, 225, 188, "expertise research", true),
        new(OriginalSaveArea.Hospital, 433, 10, "research distribution", true),
        new(OriginalSaveArea.Hospital, 443, 4, "value", true),
        new(OriginalSaveArea.Hospital, 455, 2, "visitors at the last 1 January", true),
        new(OriginalSaveArea.Hospital, 459, 2, "population share?", false),
        new(OriginalSaveArea.Hospital, 477, 4, "emergency bonus per cure", true),
        new(OriginalSaveArea.Hospital, 481, 3, "emergency timers?", false),
        new(OriginalSaveArea.Hospital, 484, 3, "emergency disease, cured, patients", true),
        new(OriginalSaveArea.Hospital, 487, 1, "emergency patients left?", false),
        new(OriginalSaveArea.Hospital, 488, 1, "emergency pass percent", true),
        new(OriginalSaveArea.Hospital, 489, 2, "cured", true),
        new(OriginalSaveArea.Hospital, 491, 2, "deaths", true),
        new(OriginalSaveArea.Hospital, 493, 2, "visitors", true),
        new(OriginalSaveArea.Hospital, 497, 2, "size", true),
        new(OriginalSaveArea.Hospital, 507, 1, "vip score?", false),
        new(OriginalSaveArea.Hospital, 515, 18, "epidemic/disaster?", false),
        new(OriginalSaveArea.Hospital, 535, 9, "policy", true),
        new(OriginalSaveArea.Hospital, 3198, 4, "reputation", true),
        new(OriginalSaveArea.Hospital, 3220, 1, "heating level", true),
        new(OriginalSaveArea.Hospital, 3228, 10, "staff counts", true),
        .. DiseaseFields(),
        new(OriginalSaveArea.Hospital, 3242, 129, "litter/rats?", false),
        new(OriginalSaveArea.Hospital, 4600, 6, "ai timers?", false),
        new(OriginalSaveArea.Hospital, 4677, 1, "ai index?", false),
    ];

    private static IEnumerable<OriginalSaveField> DiseaseFields()
    {
        (int Offset, int Length, string Name)[] fields =
        [
            (0, 4, "drug research"), (12, 4, "charge"), (16, 4, "charge percent"), (20, 4, "money earned"), (27, 1, "drug effectiveness"), (28, 1, "known?"), (29, 1, "discovered"),
            (30, 2, "recoveries"), (32, 2, "fatalities"), (36, 2, "turned away"),
        ];
        for (var disease = 0; disease < OriginalSaveHospital.DiseaseCount; disease++)
        {
            foreach (var (offset, length, name) in fields)
            {
                yield return new(OriginalSaveArea.Hospital, OriginalSaveHospital.DiseaseOffset + (disease * OriginalSaveHospital.DiseaseLength) + offset, length, $"disease {disease} {name}", true);
            }
        }
    }

    /// <summary>The field covering a byte of an area, or null.</summary>
    public static OriginalSaveField? Find(OriginalSaveArea area, int offset) =>
        All.FirstOrDefault(field => field.Area == area && offset >= field.Offset && offset < field.Offset + field.Length);

    /// <summary>The field covering a byte of the file, or null.</summary>
    public static OriginalSaveField? FindInFile(int offset)
    {
        var (area, relative) = OriginalSaveGame.Locate(offset);
        return Find(area, relative);
    }
}
