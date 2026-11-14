using CorsixNet.Client.Audio;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;

namespace CorsixNet.Client.Tests;

/// <summary>The entrance doors' sound (CorsixTH's EntranceDoor:onOccupantChange).</summary>
public class WorldSoundsTests
{
    [Fact]
    public void AnEntranceDoorWhooshesWhenItOpensAndCloses()
    {
        var host = new WindowTestHost();
        var simulation = host.Simulation;
        var door = simulation.AddEntity(id => new EntranceDoor(id, new TilePosition(10, 10), EdgeOrientation.North, isRight: true));
        var sounds = new WorldSounds(new Random(1));
        var played = new List<(string Name, TilePosition Tile)>();
        void Update() => sounds.Update(simulation, 0, paused: false, (name, tile) => played.Add((name, tile)), _ => TimeSpan.Zero);

        Update();
        door.OnOccupantChange(simulation, 1);
        Update();
        door.OnOccupantChange(simulation, 1);
        Update();
        door.OnOccupantChange(simulation, -2);
        Update();

        Assert.Equal([("eledoor2.wav", new TilePosition(10, 10)), ("eledoor2.wav", new TilePosition(10, 10))], played);
    }
}
