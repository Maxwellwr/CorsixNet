using CorsixNet.Client.Maps;
using CorsixNet.Content.Levels;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Tests;

public sealed class MapEditorToolTests
{
    private static EditorButton Button(string page, int index) => MapEditorPages.All.Single(p => p.Name == page).Buttons[index];

    private static void Drag(MapEditorTool tool, TilePosition from, TilePosition to)
    {
        tool.Update(from, leftDown: true, leftPressed: true, leftReleased: false, rightPressed: false, rightReleased: false);
        tool.Update(to, leftDown: true, leftPressed: false, leftReleased: false, rightPressed: false, rightReleased: false);
        tool.Update(to, leftDown: false, leftPressed: false, leftReleased: true, rightPressed: false, rightReleased: false);
    }

    [Fact]
    public void Pages_lay_their_buttons_out_in_ten_columns_tallest_first()
    {
        Assert.Equal(9, MapEditorPages.All.Count);
        foreach (var page in MapEditorPages.All)
        {
            Assert.All(page.Buttons, button =>
            {
                Assert.InRange(button.Column, 1, MapEditorPages.Columns - button.Width + 1);
                Assert.True(button.Row >= 1);
            });
        }

        // Foliage: the dead tree (four rows high) goes first, top left.
        var foliage = MapEditorPages.All.Single(p => p.Name == "Foliage").Buttons;
        Assert.Equal((197, 1, 1), (foliage[0].Parts[0].Sprite, foliage[0].Column, foliage[0].Row));
    }

    [Fact]
    public void A_left_drag_paints_a_floor_sprite_over_the_area()
    {
        var editor = MapEditor.Blank();
        var tool = new MapEditorTool(editor);
        var carpet = Button("Inside", 0);
        tool.Choose(EditorMode.Sprite, carpet);

        Drag(tool, new(10, 10), new(12, 11));

        foreach (var tile in editor.Area(new(10, 10), new(12, 11)))
        {
            Assert.Equal(carpet.Parts[0].Sprite, editor.Map[tile.X, tile.Y].Floor);
            Assert.True(editor.Map[tile.X, tile.Y].Has(TileFlags.Hospital));
        }

        Assert.False(editor.Map[13, 10].Has(TileFlags.Hospital));
    }

    [Fact]
    public void A_north_wall_drags_only_along_x_and_a_doorway_places_an_entrance_door()
    {
        var editor = MapEditor.Blank();
        var tool = new MapEditorTool(editor);
        var wall = Button("North wall", 2);
        Assert.Equal((true, false), wall.Drag);
        tool.Choose(EditorMode.Sprite, wall);

        Drag(tool, new(10, 10), new(13, 14));

        Assert.All(Enumerable.Range(10, 4), x => Assert.Equal(wall.Parts[0].Sprite, editor.Map[x, 10].NorthWall));
        Assert.Equal(0, editor.Map[13, 14].NorthWall);

        var doorway = MapEditorPages.All.Single(p => p.Name == "North wall").Buttons.First(b => b.Doors.Count > 0);
        tool.Choose(EditorMode.Sprite, doorway);
        Drag(tool, new(20, 20), new(20, 20));

        Assert.Contains(editor.Objects, o => o.ObjectType == MapEditor.EntranceRightDoor && o.Tile == new TilePosition(21, 20));
        Assert.Contains(editor.Objects, o => o.ObjectType == MapEditor.EntranceLeftDoor && o.Tile == new TilePosition(20, 20));
    }

    [Fact]
    public void A_right_drag_copies_and_a_left_click_pastes()
    {
        var editor = MapEditor.Blank();
        var tool = new MapEditorTool(editor);
        editor.PaintFloor(editor.Area(new(5, 5), new(6, 6)), 17, EditorFloorKind.Hospital);

        tool.Update(new(5, 5), false, false, false, rightPressed: true, rightReleased: false);
        tool.Update(new(6, 6), false, false, false, false, false);
        tool.Update(new(6, 6), false, false, false, false, rightReleased: true);
        Assert.Equal(EditorMode.Paste, tool.Mode);
        Assert.Equal((2, 2), (tool.Clip!.Width, tool.Clip.Height));

        Drag(tool, new(30, 30), new(30, 30));

        Assert.Equal(17, editor.Map[31, 31].Floor);
        Assert.NotEqual(17, editor.Map[32, 32].Floor);
    }

    [Fact]
    public void Moving_back_to_the_start_of_a_drag_cancels_it()
    {
        var editor = MapEditor.Blank();
        var tool = new MapEditorTool(editor);
        tool.Choose(EditorMode.Parcel, number: 3);

        tool.Update(new(10, 10), true, true, false, false, false);
        tool.Update(new(12, 12), true, false, false, false, false);
        tool.Update(new(10, 10), true, false, false, false, false);
        tool.Update(new(10, 10), false, false, true, false, false);

        Assert.Equal(0, editor.Map[10, 10].Parcel);
    }
}
