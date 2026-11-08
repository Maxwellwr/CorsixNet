using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Places one object: the footprint follows the mouse, E turns it, a click places it.
/// Validity comes from the simulation's own <see cref="ObjectPlacer"/>.
/// </summary>
internal sealed class PlaceObjectTool
{
    private static readonly Color ValidTint = new Color(0, 220, 0) * 0.45f;
    private static readonly Color InvalidTint = new Color(230, 0, 0) * 0.45f;
    private static readonly Color UseTint = new Color(0, 160, 255) * 0.45f;
    private static readonly Color GhostTint = Color.White * 0.7f;

    private static readonly Direction[] Turns = [Direction.North, Direction.East, Direction.South, Direction.West];

    private ObjectDefinition? _object;
    private Direction _orientation;
    private TilePosition? _tile;
    private ObjectPlacementProblem _problem;

    public bool IsActive => _object is not null;

    public string Status => _object is null
        ? string.Empty
        : $"Place {_object.Name} facing {_orientation}: click to place   E: turn{Explain(_problem)}   Esc: cancel";

    public void Start(ObjectDefinition definition)
    {
        _object = definition;
        _orientation = Turns.First(definition.Orientations.ContainsKey);
        _tile = null;
        _problem = ObjectPlacementProblem.None;
    }

    public void Cancel() => _object = null;

    /// <summary>Turns the object clockwise to its next available orientation.</summary>
    public void Turn()
    {
        if (_object is null)
        {
            return;
        }

        var index = Array.IndexOf(Turns, _orientation);
        for (var i = 1; i <= Turns.Length; i++)
        {
            var next = Turns[(index + i) % Turns.Length];
            if (_object.Orientations.ContainsKey(next))
            {
                _orientation = next;
                return;
            }
        }
    }

    /// <summary>Follows the mouse; returns a command when the player clicks a valid placement.</summary>
    public PlaceObjectCommand? Update(Simulation simulation, int player, TilePosition? hovered, bool clicked)
    {
        if (_object is null)
        {
            return null;
        }

        _tile = hovered;
        _problem = hovered is { } tile ? ObjectPlacer.Check(simulation, _object.Id, tile, _orientation, player) : ObjectPlacementProblem.OutsideMap;
        if (clicked && hovered is { } target && _problem == ObjectPlacementProblem.None)
        {
            var command = new PlaceObjectCommand(_object.Id, target, _orientation);
            _object = null;
            return command;
        }

        return null;
    }

    /// <summary>Draws the footprint (use positions in blue) in world coordinates.</summary>
    public void DrawFootprint(SpriteBatch batch, Texture2D diamond)
    {
        if (_object is null || _tile is not { } tile || !_object.Orientations.TryGetValue(_orientation, out var layout))
        {
            return;
        }

        foreach (var part in layout.Footprint)
        {
            var x = tile.X + part.X;
            var y = tile.Y + part.Y;
            var tint = _problem != ObjectPlacementProblem.None ? InvalidTint : part.IsUsePosition ? UseTint : ValidTint;
            batch.Draw(diamond, new Vector2((32 * (x - y)) - 32, 16 * (x + y)), tint);
        }
    }

    /// <summary>The object to draw as a ghost at the cursor, if any: type, tile and orientation.</summary>
    public (string ObjectType, TilePosition Tile, Direction Orientation, Color Tint)? Ghost =>
        _object is not null && _tile is { } tile ? (_object.Id, tile, _orientation, GhostTint) : null;

    private static string Explain(ObjectPlacementProblem problem)
    {
        var text = problem switch
        {
            ObjectPlacementProblem.OutsideMap => "outside the map",
            ObjectPlacementProblem.NotAllowedHere => "does not belong here",
            ObjectPlacementProblem.NotOwned => "not your land",
            ObjectPlacementProblem.Blocked => "something is in the way",
            ObjectPlacementProblem.WouldBlockAccess => "would block access",
            _ => null,
        };
        return text is null ? string.Empty : $"   ({text})";
    }
}
