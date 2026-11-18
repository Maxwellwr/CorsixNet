using Microsoft.Xna.Framework;
using OpenWards.Client.Ui;
using OpenWards.Sim;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;

namespace OpenWards.Client.Maps;

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

    /// <summary>
    /// The object picked up off the floor (a right click, <c>Object:onClick</c>) while the pick up command is on its way,
    /// so that it is not drawn where it stood meanwhile; 0 otherwise.
    /// </summary>
    public int MovingId { get; private set; }

    /// <summary>
    /// Whether the object on the cursor is one the hospital carries (<see cref="Hospital.Carried"/>) rather than one being
    /// bought: put down, it stands there; Esc puts it back (<c>UIPlaceObjects:undo</c>); the sell key sells it.
    /// </summary>
    public bool IsCarrying { get; private set; }

    /// <summary>The tick a put down, put back or sale was sent at, while waiting for it to be done; −1 otherwise.</summary>
    private long _finishSent = -1;

    /// <summary>How many ticks a put down may take to reach the simulation before it is taken as refused.</summary>
    private const int FinishTicks = 30;

    /// <summary>The object on the cursor, or null.</summary>
    public string? PlacingId => _placing?.Id;

    /// <summary>The object to draw at the cursor, if any.</summary>
    public (string ObjectType, TilePosition Tile, Direction Orientation, Color Tint)? Ghost =>
        _placing is not null && _ghostTile is { } tile
            ? (_placing.Id, tile, _orientation, _problem == ObjectPlacementProblem.None && !_cannotAfford ? Color.White : BlockedGhost)
            : null;

    /// <summary>The objects still to place after the one on the cursor (UIPlaceObjects' list), bought as each is placed.</summary>
    private readonly Queue<ObjectDefinition> _queue = new();

    /// <summary>How many objects are still to place after the one on the cursor.</summary>
    public int Queued => _queue.Count;

    /// <summary>Puts the first of the objects on the cursor and the rest in line after it (the buy window's confirm).</summary>
    public void Start(IEnumerable<ObjectDefinition> definitions)
    {
        _queue.Clear();
        foreach (var definition in definitions)
        {
            _queue.Enqueue(definition);
        }

        if (_queue.TryDequeue(out var first))
        {
            Start(first);
        }
    }

    /// <summary>
    /// <c>Object:onClick</c> with the right button on a corridor object: it comes onto the cursor turned as it stood, and
    /// <c>pickup.wav</c> plays.
    /// </summary>
    public void StartMove(ObjectDefinition definition, RoomObject placed)
    {
        StartCarrying(definition, placed.Orientation);
        MovingId = placed.Id;
        PlaySound?.Invoke("pickup.wav");
    }

    /// <summary>The object the hospital carries comes (back) onto the cursor, as after loading a game saved meanwhile.</summary>
    public void StartCarrying(ObjectDefinition definition, Direction orientation)
    {
        _queue.Clear();
        Start(definition);
        _orientation = definition.Orientations.ContainsKey(orientation) ? orientation : _orientation;
        IsCarrying = true;
    }

    public void Start(ObjectDefinition definition)
    {
        MovingId = 0;
        IsCarrying = false;
        _finishSent = -1;
        _placing = definition;
        _orientation = Turns.First(definition.Orientations.ContainsKey);
        _ghostTile = null;
    }

    public void Stop()
    {
        _placing = null;
        MovingId = 0;
        IsCarrying = false;
        _finishSent = -1;
        _queue.Clear();
    }

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

        if (IsCarrying)
        {
            var carried = player >= 0 && player < simulation.Hospitals.Count ? simulation.Hospitals[player].Carried : null;
            if (carried is not null)
            {
                MovingId = 0;
            }
            else if (MovingId == 0 || simulation.FindEntity(MovingId) is not RoomObject)
            {
                // Put down, put back or sold (or the object went before the pick up reached the simulation).
                Stop();
                return null;
            }

            if (_finishSent >= 0)
            {
                // Waiting for the put down to be done; once its time is past and the object is still carried, it was refused.
                if (simulation.Tick - _finishSent < FinishTicks)
                {
                    _ghostTile = null;
                    return null;
                }

                _finishSent = -1;
            }

            if (input.Sell || input.Back)
            {
                // UIPlaceObjects:sell, or undo: the object goes back where it stood.
                _finishSent = simulation.Tick;
                _ghostTile = null;
                return input.Sell ? new SellCarriedObjectCommand() : new PutBackCarriedObjectCommand();
            }
        }

        if (input.Back || input.Sell)
        {
            // UIPlaceObjects:undo (a picked up object stays where it stood) or sell (nothing bought yet).
            Stop();
            return null;
        }

        if (input.RightClicked || input.Rotate)
        {
            // UIPlaceObjects:tryNextOrientation, on a right click or the rotate object key.
            PlaySound?.Invoke("swoosh.wav");
            Turn();
        }

        _ghostTile = input.Hovered;
        var moving = simulation.FindEntity(MovingId) as RoomObject;
        _problem = _ghostTile is not { } tile ? ObjectPlacementProblem.OutsideMap
            : moving is not null ? CorridorObjects.CheckMove(simulation, moving, tile, _orientation, player)
            : ObjectPlacer.Check(simulation, _placing.Id, tile, _orientation, player);
        _cannotAfford = !IsCarrying && !RoomPurchases.CanAffordObject(simulation, _placing.Id, player);
        if (IsCarrying && input.LeftPressed && _ghostTile is { } spot && _problem == ObjectPlacementProblem.None)
        {
            // Put down: the object stands there, at no cost.
            _finishSent = simulation.Tick;
            _ghostTile = null;
            PlaySound?.Invoke("place_r.wav");
            return new PutDownCarriedObjectCommand(spot, _orientation);
        }

        if (!IsCarrying && input.LeftPressed && _ghostTile is { } target && _problem == ObjectPlacementProblem.None && !_cannotAfford)
        {
            var command = new PlaceObjectCommand(_placing.Id, target, _orientation);
            if (_queue.TryDequeue(out var next))
            {
                // The next object of the list comes onto the cursor, turned as the last one was if it can be.
                var orientation = _orientation;
                Start(next);
                if (next.Orientations.ContainsKey(orientation))
                {
                    _orientation = orientation;
                }
            }
            else
            {
                Stop();
            }
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

        if (IsCarrying)
        {
            var reason = _problem is ObjectPlacementProblem.None or ObjectPlacementProblem.OutsideMap ? string.Empty : "  " + BuildRoomTool.ExplainPlacement(_problem);
            return UiText.Format("openwards.build_tool.move_object", "%1%: click to put it down, right click turns it, Delete sells it%2%   Esc: put it back", UiText.Object(_placing), reason);
        }

        var price = simulation is null ? string.Empty : $" ({MoneyText.Format(simulation.Rules.ObjectCost(_placing.Id))})";
        var problem = _cannotAfford
            ? "   (" + UiText.Get("openwards.build_tool.not_enough_money", "not enough money") + ")"
            : _problem is ObjectPlacementProblem.None or ObjectPlacementProblem.OutsideMap ? string.Empty : "  " + BuildRoomTool.ExplainPlacement(_problem);
        return UiText.Format("openwards.build_tool.corridor_object", "%1%%2%: click to buy and place it, right click turns it, Tab: next item%3%   Esc: cancel", UiText.Object(_placing), price, problem);
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
