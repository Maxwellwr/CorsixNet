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
        new(OriginalSaveArea.File, OriginalSaveGame.DateOffset, 1, "day", true),
        new(OriginalSaveArea.File, OriginalSaveGame.DateOffset + 1, 1, "month", true),

        new(OriginalSaveArea.Tile, 0, 2, "entity", true),
        new(OriginalSaveArea.Tile, 2, 1, "floor", true),
        new(OriginalSaveArea.Tile, 3, 1, "north wall", true),
        new(OriginalSaveArea.Tile, 4, 1, "west wall", true),
        new(OriginalSaveArea.Tile, 5, 1, "flags", true),
        new(OriginalSaveArea.Tile, 6, 1, "shadow/litter", false),
        new(OriginalSaveArea.Tile, 7, 1, "owner", true),

        new(OriginalSaveArea.Entity, 4, 2, "x", true),
        new(OriginalSaveArea.Entity, 6, 2, "y", true),
        new(OriginalSaveArea.Entity, 8, 2, "frame", true),
        new(OriginalSaveArea.Entity, 12, 1, "kind", true),
        new(OriginalSaveArea.Entity, 14, 1, "type", true),
        new(OriginalSaveArea.Entity, 17, 1, "direction", true),
        new(OriginalSaveArea.Entity, 108, 2, "salary", true),
        new(OriginalSaveArea.Entity, 124, 2, "staff flags", true),
        new(OriginalSaveArea.Entity, 126, 2, "skill", true),
        new(OriginalSaveArea.Entity, 132, 1, "initial", true),
        new(OriginalSaveArea.Entity, 133, 1, "name start", true),
        new(OriginalSaveArea.Entity, 134, 1, "name end", true),
        new(OriginalSaveArea.Entity, 138, 2, "description", true),

        new(OriginalSaveArea.Room, 0, 1, "type", true),
        new(OriginalSaveArea.Room, 1, 1, "width", true),
        new(OriginalSaveArea.Room, 2, 1, "height", true),
        new(OriginalSaveArea.Room, 4, 2, "outside tile", true),
        new(OriginalSaveArea.Room, 6, 2, "door tile", true),
        new(OriginalSaveArea.Room, 8, 2, "origin tile", true),

        new(OriginalSaveArea.Hospital, 0, 42, "name", true),
        new(OriginalSaveArea.Hospital, 42, 1, "kind", true),
        new(OriginalSaveArea.Hospital, 45, 4, "salary?", false),
        new(OriginalSaveArea.Hospital, 53, 4, "money", true),
        new(OriginalSaveArea.Hospital, 61, 4, "loan", true),
        new(OriginalSaveArea.Hospital, 65, 4, "max loan?", false),
        new(OriginalSaveArea.Hospital, 221, 4, "research points?", false),
        new(OriginalSaveArea.Hospital, 225, 188, "expertise research?", false),
        new(OriginalSaveArea.Hospital, 433, 10, "research distribution?", false),
        new(OriginalSaveArea.Hospital, 443, 4, "value", true),
        new(OriginalSaveArea.Hospital, 459, 2, "population share?", false),
        new(OriginalSaveArea.Hospital, 477, 12, "emergency?", false),
        new(OriginalSaveArea.Hospital, 489, 2, "cured", true),
        new(OriginalSaveArea.Hospital, 491, 2, "deaths", true),
        new(OriginalSaveArea.Hospital, 493, 2, "visitors", true),
        new(OriginalSaveArea.Hospital, 497, 2, "size", true),
        new(OriginalSaveArea.Hospital, 507, 1, "vip score?", false),
        new(OriginalSaveArea.Hospital, 515, 18, "epidemic/disaster?", false),
        new(OriginalSaveArea.Hospital, 535, 9, "policy?", false),
        new(OriginalSaveArea.Hospital, 3121, 9, "reputation?", false),
        new(OriginalSaveArea.Hospital, 3228, 10, "staff counts", true),
        new(OriginalSaveArea.Hospital, 3242, 129, "litter/rats?", false),
        new(OriginalSaveArea.Hospital, 4600, 6, "ai timers?", false),
        new(OriginalSaveArea.Hospital, 4677, 1, "ai index?", false),
    ];

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
