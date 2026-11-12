using CorsixNet.Content.Levels;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;

namespace CorsixNet.Content.Tests;

/// <summary>The map editor's model and saving maps (<c>map_editor.lua</c>, <c>level_map::save</c>).</summary>
public sealed class MapEditorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CorsixNetMapEditorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheBlankMapIsCheckeredGrassForOnePlayer()
    {
        var editor = MapEditor.Blank();

        Assert.Equal(1, editor.Map.PlayerCount);
        Assert.Equal(2, editor.Map[0, 5].Floor);
        Assert.Equal(3, editor.Map[1, 5].Floor);
        Assert.Equal(new TilePosition(63, 63), editor.Map.GetCameraTile(0));
        Assert.Empty(editor.Objects);
    }

    [Fact]
    public void PaintingTheFloorSetsItsFlags()
    {
        var editor = MapEditor.Blank();

        editor.PaintFloor(editor.Area(new TilePosition(12, 11), new TilePosition(10, 10)), 17, EditorFloorKind.Hospital);
        editor.PaintFloor([new TilePosition(9, 10)], 4, EditorFloorKind.Road);
        editor.PaintFloor([new TilePosition(12, 11)], 2, EditorFloorKind.Outside);

        Assert.Equal(17, editor.Map[10, 10].Floor);
        Assert.True(editor.Map[11, 11].Has(TileFlags.Passable | TileFlags.Buildable | TileFlags.Hospital));
        Assert.True(editor.Map[9, 10].Has(TileFlags.Passable));
        Assert.False(editor.Map[9, 10].Has(TileFlags.Buildable));
        Assert.False(editor.Map[9, 10].Has(TileFlags.Hospital));
        Assert.False(editor.Map[12, 11].Has(TileFlags.Passable));
        Assert.Equal(2, editor.Map[12, 11].Floor);
    }

    [Fact]
    public void DeletingWallsKeepsTheFloorAndTakesTheObjects()
    {
        var editor = MapEditor.Blank();
        editor.PaintNorthWall([new TilePosition(5, 5)], 120);
        editor.PaintWestWall([new TilePosition(5, 5), new TilePosition(6, 5)], 121);
        editor.PlaceObject(new MapObjectPlacement(new TilePosition(5, 5), 61, 2));
        editor.PlaceObject(new MapObjectPlacement(new TilePosition(8, 8), 61, 2));

        editor.DeleteWalls(editor.Area(new TilePosition(5, 5), new TilePosition(5, 5)));

        Assert.Equal(0, editor.Map[5, 5].NorthWall);
        Assert.Equal(0, editor.Map[5, 5].WestWall);
        Assert.Equal(121, editor.Map[6, 5].WestWall);
        Assert.Equal(3, editor.Map[5, 5].Floor);
        Assert.Equal(new TilePosition(8, 8), Assert.Single(editor.Objects).Tile);
    }

    [Fact]
    public void ParcelsCameraAndHeliportAreSet()
    {
        var editor = MapEditor.Blank();

        editor.SetParcel(editor.Area(new TilePosition(20, 20), new TilePosition(21, 22)), 3);
        editor.SetCamera(0, new TilePosition(21, 21));
        editor.SetHeliport(0, new TilePosition(30, 2));

        Assert.Equal(4, editor.Map.ParcelCount);
        Assert.Equal(3, editor.Map[21, 22].Parcel);
        Assert.Equal(0, editor.Map[22, 22].Parcel);
        Assert.Equal(new TilePosition(21, 21), editor.Map.GetCameraTile(0));
        Assert.Equal(new TilePosition(30, 2), editor.Map.GetHeliportTile(0));
    }

    [Fact]
    public void APastedAreaRepeatsAndIsCutAtTheTargetsEdge()
    {
        var editor = MapEditor.Blank();
        editor.PaintFloor([new TilePosition(0, 0)], 17, EditorFloorKind.Hospital);
        editor.PaintFloor([new TilePosition(1, 0)], 4, EditorFloorKind.Road);
        editor.PlaceObject(new MapObjectPlacement(new TilePosition(1, 0), 61, 1));
        var clip = editor.Copy(new TilePosition(1, 0), new TilePosition(0, 0));
        editor.PlaceObject(new MapObjectPlacement(new TilePosition(50, 40), 62, 0));

        editor.Paste(clip, new TilePosition(50, 40), new TilePosition(54, 40));

        Assert.Equal(2, clip.Width);
        Assert.Equal(1, clip.Height);
        Assert.Equal([17, 4, 17, 4, 17], Enumerable.Range(50, 5).Select(x => (int)editor.Map[x, 40].Floor));
        Assert.Equal(3, editor.Map[55, 40].Floor);
        Assert.True(editor.Map[52, 40].Has(TileFlags.Buildable));
        Assert.False(editor.Map[53, 40].Has(TileFlags.Buildable));
        var pasted = editor.Objects.Where(placement => placement.Tile.Y == 40).OrderBy(placement => placement.Tile.X).ToList();
        Assert.Equal([51, 53], pasted.Select(placement => placement.Tile.X));
        Assert.All(pasted, placement => Assert.Equal(61, placement.ObjectType));
    }

    [Fact]
    public void ASavedMapLoadsAgainAsItWasEdited()
    {
        var editor = MapEditor.Blank();
        editor.PaintFloor(editor.Area(new TilePosition(10, 10), new TilePosition(20, 20)), 17, EditorFloorKind.Hospital);
        editor.PaintFloor(editor.Area(new TilePosition(9, 10), new TilePosition(9, 20)), 4, EditorFloorKind.Road);
        editor.PaintNorthWall(editor.Area(new TilePosition(10, 10), new TilePosition(20, 10)), 120);
        editor.SetParcel(editor.Area(new TilePosition(10, 10), new TilePosition(20, 20)), 1);
        editor.PlaceObject(new MapObjectPlacement(new TilePosition(15, 21), 58, 3));
        editor.SetCamera(0, new TilePosition(15, 15));
        editor.SetHeliport(0, new TilePosition(30, 30));
        var path = Path.Combine(_folder, "test.map");

        editor.Save(path);
        var file = MapFile.Parse(File.ReadAllBytes(path));
        var loaded = LevelMapLoader.Load(file);
        var map = loaded.Map;

        Assert.Equal(MapFile.MinimumLength, new FileInfo(path).Length);
        Assert.Equal(17, map[15, 15].Floor);
        Assert.True(map[15, 15].Has(TileFlags.Hospital | TileFlags.Buildable | TileFlags.Passable));
        Assert.True(map[9, 15].Has(TileFlags.Passable));
        Assert.False(map[9, 15].Has(TileFlags.Hospital));
        Assert.False(map[5, 5].Has(TileFlags.Passable));
        Assert.Equal(3, map[5, 5].Floor);
        Assert.Equal(120, map[12, 10].NorthWall);
        Assert.False(map[12, 10].Has(TileFlags.CanTravelNorth));
        Assert.Equal(1, map[20, 20].Parcel);
        Assert.Equal(2, map.ParcelCount);
        Assert.Equal(new TilePosition(15, 15), map.GetCameraTile(0));
        Assert.Equal(new TilePosition(30, 30), map.GetHeliportTile(0));
        Assert.Equal(new MapObjectPlacement(new TilePosition(15, 21), 58, 3), Assert.Single(loaded.Objects));
    }

    [Fact]
    public void PondFoliageGoesBackToTheFloorWhenSaved()
    {
        var data = new MapFileBuilder().Tile(4, 4, floor: 0x46).Build();
        var foliage = MapFile.BlockSprite(0x46);
        Assert.InRange(foliage, (byte)71, (byte)73);

        var saved = MapEditor.Open(MapFile.Parse(data)).ToMapFile();

        Assert.Equal(0x46, saved[4, 4].Floor);
        Assert.Equal(0, saved[4, 4].NorthWall);
    }

    [Fact]
    public void EveryOriginalLevelSavesAndLoadsTheSame()
    {
        var install = RealGameData.Require();
        foreach (var path in Directory.EnumerateFiles(install.FindDirectory("LEVELS")!, "LEVEL.L*"))
        {
            var file = MapFile.Parse(install.ReadDataFile("LEVELS", Path.GetFileName(path)));
            var first = LevelMapLoader.Load(file, editing: true);
            var again = LevelMapLoader.Load(MapFile.Parse(new MapEditor(first.Map, first.Objects).ToMapFile().ToBytes()), editing: true);

            for (var y = 0; y < MapFile.Size; y++)
            {
                for (var x = 0; x < MapFile.Size; x++)
                {
                    var (a, b) = (first.Map[x, y], again.Map[x, y]);
                    Assert.True(a.Floor == b.Floor && a.NorthWall == b.NorthWall && a.WestWall == b.WestWall && a.Parcel == b.Parcel && Kind(a) == Kind(b), $"{path}: tile {x},{y} differs");
                }
            }

            Assert.Equal(first.Objects.ToHashSet(), again.Objects.ToHashSet());
            Assert.Equal(first.Map.GetCameraTile(0), again.Map.GetCameraTile(0));
        }

        static TileFlags Kind(MapTile tile) => tile.Flags & (TileFlags.Passable | TileFlags.Buildable | TileFlags.Hospital);
    }

    [Fact]
    public void An_entrance_door_replaces_the_doors_on_its_tiles_with_their_other_halves()
    {
        var editor = MapEditor.Blank();
        editor.PlaceEntranceDoor(new TilePosition(10, 10), west: false);
        Assert.Equal(2, editor.Objects.Count);

        // A west door whose left half lands on the north door's right half: the north door goes, both halves.
        editor.PlaceEntranceDoor(new TilePosition(10, 11), west: true);

        Assert.Equal(
            new[] { (new TilePosition(10, 10), MapEditor.EntranceLeftDoor, (byte)1), (new TilePosition(10, 11), MapEditor.EntranceRightDoor, (byte)1) },
            editor.Objects.Select(o => (o.Tile, o.ObjectType, o.Direction)).OrderBy(o => o.Tile.Y).ToArray());
    }
}
