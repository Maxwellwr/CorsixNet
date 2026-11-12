using CorsixNet.Client.Maps;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Tests;

public sealed class ObjectDrawingTests
{
    // CorsixTH puts radiation_shield_b on the shield's solid tile nearest its use position, (0, -1), and draws it from
    // its render attach tile: (0, -1) + (0, 2) facing north, (0, -1) + (1, 1) facing east.
    [Theory]
    [InlineData(Direction.North, 10, 11)]
    [InlineData(Direction.East, 11, 10)]
    public void The_radiation_shield_front_is_drawn_from_the_tile_in_front_of_the_console(Direction facing, int x, int y)
    {
        Assert.Equal(new TilePosition(x, y), ObjectDrawing.SlaveDrawTile("radiation_shield", new TilePosition(10, 10), facing));
    }

    [Fact]
    public void Objects_in_one_part_have_no_second_part()
    {
        Assert.Null(ObjectDrawing.SlaveDrawTile("x_ray", new TilePosition(10, 10), Direction.North));
        Assert.Null(ObjectDrawing.Split("x_ray", new TilePosition(10, 10), Direction.North));
    }

    // crash_trolley.lua's render_attach_position { {0, 0}, {-1, 1} } facing north: each tile draws the 64 pixels around
    // its own column (x - y) of the trolley, which is drawn from the anchor of (10, 12), x = 32 * (10 - 12).
    [Fact]
    public void The_crash_trolley_is_drawn_split_over_two_tiles()
    {
        var parts = ObjectDrawing.Split("crash_trolley", new TilePosition(10, 12), Direction.North)!.ToList();

        Assert.Equal([(new TilePosition(10, 12), -96, -32), (new TilePosition(9, 13), -160, -96)], parts);

        var east = ObjectDrawing.Split("crash_trolley", new TilePosition(10, 12), Direction.East)!.ToList();
        Assert.Equal([(new TilePosition(10, 12), -96, -32), (new TilePosition(11, 11), -32, 32)], east);
    }
}
