using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using Microsoft.Xna.Framework;

namespace CorsixNet.Client.Maps;

/// <summary>
/// Buying an object for a corridor, such as a reception desk, after CorsixTH's <c>UIFurnishCorridor</c>
/// and <c>UIPlaceObjects</c>: the object follows the cursor, a right click turns it and a left click buys
/// and places it. Until the furnish corridor window exists, <see cref="Cycle"/> goes through the
/// corridor objects in that window's order.
/// </summary>
internal sealed class FurnishCorridorTool
{
    private static readonly Color BlockedGhost = new Color(120, 120, 120) * 0.9f;
    private static readonly Direction[] Turns = [Direction.North, Direction.East, Direction.South, Direction.West];

    /// <summary>Plays one of the original's interface sounds (CorsixTH's <c>UI:playSound</c>), if set.</summary>
    public Action<string>? PlaySound { get; set; }

    private ObjectDefinition? _placing;
    private Direction _orientation;
    private TilePosition? _ghostTile;
    private ObjectPlacementProblem _problem;
    private bool _cannotAfford;

    public bool IsActive => _placing is not null;

    /// <summary>The object on the cursor, or null.</summary>
    public string? PlacingId => _placing?.Id;

    /// <summary>The object to draw at the cursor, if any.</summary>
    public (string ObjectType, TilePosition Tile, Direction Orientation, Color Tint)? Ghost =>
        _placing is not null && _ghostTile is { } tile
            ? (_placing.Id, tile, _orientation, _problem == ObjectPlacementProblem.None && !_cannotAfford ? Color.White : BlockedGhost)
            : null;

    public void Start(ObjectDefinition definition)
    {
        _placing = definition;
        _orientation = Turns.First(definition.Orientations.ContainsKey);
        _ghostTile = null;
    }

    public void Stop() => _placing = null;

    /// <summary>Switches to the next (or previous) object that may stand in a corridor.</summary>
    public void Cycle(GameContent content, bool backwards)
    {
        var items = content.Objects.Where(o => o.CorridorObject).ToList();
        if (_placing is null || items.Count == 0)
        {
            return;
        }

        var index = items.FindIndex(o => o.Id == _placing.Id);
        var next = items[(index + (backwards ? items.Count - 1 : 1)) % items.Count];
        var orientation = _orientation;
        Start(next);
        if (next.Orientations.ContainsKey(orientation))
        {
            _orientation = orientation;
        }
    }

    /// <summary>Follows the input; returns the command that buys and places the object when the player clicks.</summary>
    public Command? Update(Simulation simulation, int player, BuildInput input)
    {
        if (_placing is null)
        {
            return null;
        }

        if (input.Back)
        {
            Stop();
            return null;
        }

        if (input.RightClicked)
        {
            // UIPlaceObjects:tryNextOrientation.
            PlaySound?.Invoke("swoosh.wav");
            Turn();
        }

        _ghostTile = input.Hovered;
        _problem = _ghostTile is { } tile ? ObjectPlacer.Check(simulation, _placing.Id, tile, _orientation, player) : ObjectPlacementProblem.OutsideMap;
        _cannotAfford = !RoomPurchases.CanAffordObject(simulation, _placing.Id, player);
        if (input.LeftPressed && _ghostTile is { } target && _problem == ObjectPlacementProblem.None && !_cannotAfford)
        {
            var command = new PlaceObjectCommand(_placing.Id, target, _orientation);
            Stop();
            PlaySound?.Invoke("place_r.wav"); // UIPlaceObjects:placeObject
            return command;
        }

        return null;
    }

    public string Status(Simulation? simulation)
    {
        if (_placing is null)
        {
            return string.Empty;
        }

        var price = simulation is null ? string.Empty : $" ({MoneyText.Format(simulation.Rules.ObjectCost(_placing.Id))})";
        var problem = _cannotAfford ? "   (not enough money)" : _problem is ObjectPlacementProblem.None or ObjectPlacementProblem.OutsideMap ? string.Empty : $"   ({_problem})";
        return $"{_placing.Name}{price}: click to buy and place it, right click turns it, Tab: next item{problem}   Esc: cancel";
    }

    /// <summary>Turns the object clockwise to its next available orientation.</summary>
    private void Turn()
    {
        var index = Array.IndexOf(Turns, _orientation);
        for (var i = 1; i <= Turns.Length; i++)
        {
            var next = Turns[(index + i) % Turns.Length];
            if (_placing!.Orientations.ContainsKey(next))
            {
                _orientation = next;
                return;
            }
        }
    }
}
