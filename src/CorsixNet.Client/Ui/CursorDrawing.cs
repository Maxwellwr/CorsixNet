// The mouse cursors of the original's DATA/MPointer and QData/SPointer sheets, after CorsixTH CorsixTH/Lua/graphics.lua (cursor_data,
// Graphics:loadMainCursor) and game_ui.lua (GameUI:onCursorWorldPositionChange's choice of cursor).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui;

/// <summary>
/// The cursors of <c>DATA/MPointer</c>, by their sprite in the sheet (graphics.lua's cursor_data); those above 20 are
/// sprite (id - 20) of <c>QData/SPointer</c>, each with its dialog's palette (cursors_palette).
/// </summary>
internal enum GameCursor
{
    Default = 1,
    Clicked = 2,
    ResizeRoom = 3,
    EditRoom = 4,
    NorthSouthArrow = 5,
    WestEastArrow = 6,
    AllArrows = 7,
    MoveRoom = 8,
    Sleep = 9,
    KillRat = 10,
    KillRatHover = 11,
    EpidemicHover = 12,
    Epidemic = 13,
    Grab = 14,
    Quit = 15,
    Staff = 16,
    Repair = 17,
    Patient = 18,
    Queue = 19,
    QueueDrag = 20,

    /// <summary>The bank manager's dollar sign (SPointer 16, <c>Bank01V.pal</c>).</summary>
    Bank = 36,

    /// <summary>The bank statement's pie chart (SPointer 24, <c>Stat01V.pal</c>).</summary>
    BankSummary = 44,
}

/// <summary>Draws the game's own mouse cursor in place of the system's.</summary>
internal sealed class CursorDrawing : IDisposable
{
    private readonly SpriteAtlas _sheet;
    private readonly GraphicsDevice _device;
    private readonly ThemeHospitalInstall _install;

    /// <summary>QData/SPointer with each palette it is drawn in, loaded when first needed; null if it cannot be read.</summary>
    private readonly Dictionary<string, SpriteAtlas?> _dialogSheets = new(StringComparer.Ordinal);

    public CursorDrawing(GraphicsDevice device, ThemeHospitalInstall install, Palette palette)
    {
        _device = device;
        _install = install;
        _sheet = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "MPOINTER", complex: false), palette);
    }

    /// <summary>cursors_palette: the palette an SPointer cursor is drawn in.</summary>
    private static string DialogPalette(GameCursor cursor) => cursor == GameCursor.Bank ? "BANK01V" : "STAT01V";

    /// <summary>The cursor's hot spot, the pixel that points (cursor_data's x and y).</summary>
    public static Point HotSpot(GameCursor cursor) => cursor switch
    {
        GameCursor.EditRoom => new(8, 9),
        GameCursor.NorthSouthArrow or GameCursor.WestEastArrow or GameCursor.AllArrows => new(16, 7),
        GameCursor.MoveRoom => new(5, 3),
        GameCursor.KillRat or GameCursor.KillRatHover => new(8, 8),
        GameCursor.Grab => new(8, 14),
        GameCursor.Bank => new(5, 8),
        GameCursor.BankSummary => new(8, 7),
        _ => Point.Zero,
    };

    /// <summary>Draws the cursor with its hot spot at <paramref name="position"/>.</summary>
    public void Draw(SpriteBatch batch, GameCursor cursor, Point position)
    {
        if ((int)cursor > 20)
        {
            if (DialogSheet(DialogPalette(cursor)) is { } dialogSheet)
            {
                var spot = HotSpot(cursor);
                dialogSheet.Draw(batch, (int)cursor - 20, position.X - spot.X, position.Y - spot.Y, Color.White);
                return;
            }

            cursor = GameCursor.Default;
        }

        var hot = HotSpot(cursor);
        _sheet.Draw(batch, (int)cursor, position.X - hot.X, position.Y - hot.Y, Color.White);
    }

    private SpriteAtlas? DialogSheet(string palette)
    {
        if (!_dialogSheets.TryGetValue(palette, out var sheet))
        {
            try
            {
                sheet = new SpriteAtlas(_device, _install.LoadSpriteSheet("QDATA", "SPOINTER", complex: false), _install.LoadPalette("QDATA", palette + ".PAL"));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                sheet = null;
            }

            _dialogSheets[palette] = sheet;
        }

        return sheet;
    }

    public void Dispose()
    {
        _sheet.Dispose();
        foreach (var sheet in _dialogSheets.Values)
        {
            sheet?.Dispose();
        }
    }
}
