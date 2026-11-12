// The mouse cursors of the original's DATA/MPointer sheet, after CorsixTH CorsixTH/Lua/graphics.lua (cursor_data,
// Graphics:loadMainCursor) and game_ui.lua (GameUI:onCursorWorldPositionChange's choice of cursor).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui;

/// <summary>The cursors of <c>DATA/MPointer</c>, by their sprite in the sheet (graphics.lua's cursor_data).</summary>
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
}

/// <summary>Draws the game's own mouse cursor in place of the system's.</summary>
internal sealed class CursorDrawing : IDisposable
{
    private readonly SpriteAtlas _sheet;

    public CursorDrawing(GraphicsDevice device, ThemeHospitalInstall install, Palette palette)
    {
        _sheet = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "MPOINTER", complex: false), palette);
    }

    /// <summary>The cursor's hot spot, the pixel that points (cursor_data's x and y).</summary>
    public static Point HotSpot(GameCursor cursor) => cursor switch
    {
        GameCursor.EditRoom => new(8, 9),
        GameCursor.NorthSouthArrow or GameCursor.WestEastArrow or GameCursor.AllArrows => new(16, 7),
        GameCursor.MoveRoom => new(5, 3),
        GameCursor.KillRat or GameCursor.KillRatHover => new(8, 8),
        GameCursor.Grab => new(8, 14),
        _ => Point.Zero,
    };

    /// <summary>Draws the cursor with its hot spot at <paramref name="position"/>.</summary>
    public void Draw(SpriteBatch batch, GameCursor cursor, Point position)
    {
        var hot = HotSpot(cursor);
        _sheet.Draw(batch, (int)cursor, position.X - hot.X, position.Y - hot.Y, Color.White);
    }

    public void Dispose() => _sheet.Dispose();
}
