// The map editor's sprite pages and their button layout, ported from CorsixTH
// CorsixTH/Lua/dialogs/resizables/map_editor.lua (the foliage ... helipad tables, PAGES, normalizeEditSprite,
// getHighestRowcol, layoutButtons).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Maps;

/// <summary>Where an editor sprite goes on its tile (the <c>type</c> of map_editor.lua's sprites).</summary>
internal enum EditorSpriteKind
{
    /// <summary>A floor sprite outside the hospital that nobody walks on (<c>floor</c>).</summary>
    Floor,

    /// <summary>A floor sprite outside that people walk on (<c>road</c>).</summary>
    Road,

    /// <summary>A floor sprite inside the hospital (<c>hospital</c>).</summary>
    Hospital,

    /// <summary>The tile's north wall (<c>north</c>).</summary>
    North,

    /// <summary>The tile's west wall (<c>west</c>).</summary>
    West,
}

/// <summary>One sprite of an editor button, at its tile counted from 1 (<c>xpos</c>, <c>ypos</c>).</summary>
internal readonly record struct EditorSpritePart(ushort Sprite, int X, int Y, EditorSpriteKind Kind);

/// <summary>An entrance door an editor button places, at its tile counted from 1, in a north or west wall.</summary>
internal readonly record struct EditorDoor(int X, int Y, bool West);

/// <summary>
/// One button of a sprite page (<c>normalizeEditSprite</c>): its sprites, the doors it places, its size in tiles, its
/// size on the page in columns and rows, and where on the page it goes (<c>layoutButtons</c>, from 1).
/// </summary>
internal sealed record EditorButton(IReadOnlyList<EditorSpritePart> Parts, IReadOnlyList<EditorDoor> Doors, int Height)
{
    public int SizeX => Parts.Max(part => part.X);

    public int SizeY => Parts.Max(part => part.Y);

    public EditorSpriteKind Kind => Parts[0].Kind;

    /// <summary>Columns taken on the page: a single sprite takes two.</summary>
    public int Width => Parts.Count == 1 ? 2 : Math.Max(SizeY - 1 + SizeX - 1 + 2, SizeY);

    /// <summary>Where the sprite of tile (1, 1) is drawn on the button, across.</summary>
    public int OriginX => Parts.Count == 1 ? 0 : (SizeY - 1) * 32;

    public int Column { get; init; }

    public int Row { get; init; }

    /// <summary>
    /// <c>getCursorDragCapabilities</c> for a sprite: only a one-tile sprite drags, a floor one over an area, a north wall
    /// along x and a west wall along y.
    /// </summary>
    public (bool AlongX, bool AlongY) Drag => SizeX != 1 || SizeY != 1
        ? (false, false)
        : Kind switch
        {
            EditorSpriteKind.North => (true, false),
            EditorSpriteKind.West => (false, true),
            _ => (true, true),
        };
}

/// <summary>A page of sprite buttons, laid out.</summary>
internal sealed record EditorPage(string Name, IReadOnlyList<EditorButton> Buttons);

/// <summary>The map editor's sprite pages (map_editor.lua's <c>PAGES</c>).</summary>
internal static class MapEditorPages
{
    /// <summary>The tallest button, in rows (<c>MAX_HEIGHT</c>).</summary>
    public const int MaxHeight = 5;

    /// <summary>Sprite button columns on a page (<c>EDITOR_COLUMNS</c>).</summary>
    public const int Columns = 10;

    /// <summary>Width of a sprite button column in pixels (<c>EDITOR_COLUMN_SIZE</c>).</summary>
    public const int ColumnSize = 32 + 3;

    /// <summary>Height of a sprite button row in pixels (<c>EDITOR_ROW_SIZE</c>).</summary>
    public const int RowSize = 32 + 4;

    private const EditorSpriteKind F = EditorSpriteKind.Floor;
    private const EditorSpriteKind R = EditorSpriteKind.Road;
    private const EditorSpriteKind H = EditorSpriteKind.Hospital;
    private const EditorSpriteKind N = EditorSpriteKind.North;
    private const EditorSpriteKind W = EditorSpriteKind.West;

    /// <summary>The pages, each laid out for <see cref="Columns"/> columns.</summary>
    public static IReadOnlyList<EditorPage> All { get; } =
    [
        Page("Inside", Singles(H, 1, 17, 70, 18, 19, 23, 16, 21, 22, 66, 76, 20)),
        Page("Outside", [
            .. Singles(F, 1, 1, 2, 3), .. Singles(R, 1, 4, 15, 5),
            .. Singles(F, 1, 6, 8, 10, 12, 7, 9, 11, 13, 14, 61, 62, 63, 64, 205, 206, 207, 208)]),
        Page("Foliage", [
            Single(192, 3, F), Single(193, 2, F), Single(194, 2, F), Single(195, 3, F), Single(196, 3, F), Single(197, 4, F),
            Single(198, 2, F), Single(199, 3, F), Single(200, 2, F), .. Singles(F, 1, 201, 202, 203, 204)]),
        Page("Hedgerow", Singles(F, 2, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191)),
        Page("Pond", [.. Singles(F, 1, 60, 65, 68, 78, 59, 77, 79, 80, 69, 71), .. Singles(F, 2, 72, 73)]),
        Page("Road", Singles(R, 1, 41, 45, 42, 43, 44, 47, 49, 48, 53, 52, 51, 57, 55, 56, 50, 58)),
        Page("North wall", [
            new([new(159, 1, 1, N), new(157, 3, 1, N)], [new(2, 1, false)], 3),
            new([new(163, 1, 1, N), new(161, 3, 1, N)], [new(2, 1, false)], 3),
            .. Singles(N, 3, 114, 116, 120, 118, 122, 124, 126)]),
        Page("West wall", [
            new([new(158, 1, 3, W), new(160, 1, 1, W)], [new(1, 2, true)], 3),
            new([new(162, 1, 3, W), new(164, 1, 1, W)], [new(1, 2, true)], 3),
            .. Singles(W, 3, 115, 119, 117, 123, 127, 125, 210, 209)]),
        Page("Helipad", [Helipad()]),
    ];

    /// <summary><c>layoutButtons</c>: the tallest buttons first, each as high up as its columns allow.</summary>
    private static EditorPage Page(string name, IReadOnlyList<EditorButton> buttons)
    {
        var columns = Enumerable.Repeat(1, Columns).ToArray();
        var laidOut = new List<EditorButton>();
        for (var height = MaxHeight; height >= 1; height--)
        {
            foreach (var button in buttons.Where(button => button.Height == height))
            {
                var (column, row) = HighestRowColumn(columns, button.Width);
                laidOut.Add(button with { Column = column, Row = row });
                for (var i = column; i < column + button.Width; i++)
                {
                    columns[i - 1] = row + height;
                }
            }
        }

        return new EditorPage(name, laidOut);
    }

    /// <summary><c>getHighestRowcol</c>: the first column and the row where <paramref name="width"/> columns are free highest up.</summary>
    private static (int Column, int Row) HighestRowColumn(int[] columns, int width)
    {
        var best = columns[0] + 100000;
        var bestColumn = 0;
        for (var left = 1; left <= columns.Length - width + 1; left++)
        {
            if (columns[left - 1] >= best)
            {
                continue;
            }

            var top = columns[left - 1];
            for (var i = 1; i < width; i++)
            {
                top = Math.Max(top, columns[left - 1 + i]);
            }

            if (top < best)
            {
                best = top;
                bestColumn = left;
            }
        }

        return (bestColumn, best);
    }

    private static EditorButton Single(ushort sprite, int height, EditorSpriteKind kind) => new([new(sprite, 1, 1, kind)], [], height);

    private static EditorButton[] Singles(EditorSpriteKind kind, int height, params ushort[] sprites) =>
        [.. sprites.Select(sprite => Single(sprite, height, kind))];

    /// <summary>The helipad: dark concrete round the edge and in the H, light concrete for the H.</summary>
    private static EditorButton Helipad()
    {
        var parts = new List<EditorSpritePart>();
        for (var y = 1; y <= 5; y++)
        {
            for (var x = 1; x <= 5; x++)
            {
                var light = x is 2 or 4 && y is >= 2 and <= 4 || (x == 3 && y == 3);
                parts.Add(new(light ? (ushort)4 : (ushort)5, x, y, R));
            }
        }

        return new EditorButton(parts, [], 5);
    }
}
