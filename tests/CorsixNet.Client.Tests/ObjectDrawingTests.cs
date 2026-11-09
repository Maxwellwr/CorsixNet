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
    }
}
