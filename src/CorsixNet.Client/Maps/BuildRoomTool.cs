using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Objects;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>What the build tool reads from the mouse and keyboard in one frame.</summary>
/// <param name="Hovered">The map tile under the cursor, if any.</param>
/// <param name="FractionX">Where in that tile the cursor is along x, from 0 to 255.</param>
/// <param name="FractionY">Where in that tile the cursor is along y, from 0 to 255.</param>
/// <param name="LeftDown">The left button is held.</param>
/// <param name="LeftPressed">The left button went down this frame.</param>
/// <param name="LeftReleased">The left button came up this frame.</param>
/// <param name="RightClicked">The right button was clicked (not dragged) this frame.</param>
/// <param name="Confirm">The player confirms the current step (Enter).</param>
/// <param name="Back">The player steps back (Esc).</param>
/// <param name="PickUp">The player toggles picking objects up (P).</param>
internal readonly record struct BuildInput(
    TilePosition? Hovered,
    int FractionX,
    int FractionY,
    bool LeftDown,
    bool LeftPressed,
    bool LeftReleased,
    bool RightClicked,
    bool Confirm,
    bool Back,
    bool PickUp);

/// <summary>
/// The player's room building flow, after the original game and CorsixTH's <c>UIEditRoom</c>:
/// drag out a blueprint (drag its edges to resize it, its middle to move it), place the door,
/// optionally windows, then furnish the room and confirm it. The blueprint, door and windows stay
/// in the client until the player is done with them; from then on the room exists in the
/// simulation, inactive, and every step is a command. All checks are the simulation's own.
/// </summary>
internal sealed class BuildRoomTool
{
    private static readonly Color FootprintTint = Color.White * 0.22f;

    /// <summary>How the original shows an object that cannot go where it is: flat and grey.</summary>
    private static readonly Color BlockedGhost = new Color(120, 120, 120) * 0.9f;

    /// <summary>How long to wait for a command to show in the simulation before giving up on it.</summary>
    private const long CommandTimeoutTicks = 200;

    private static readonly Direction[] Turns = [Direction.North, Direction.East, Direction.South, Direction.West];

    /// <summary>Plays one of the original's interface sounds (CorsixTH's <c>UI:playSound</c>), if set.</summary>
    public Action<string>? PlaySound { get; set; }

    private readonly List<DoorPlacement> _windows = [];
    private readonly List<Pending> _pending = [];

    private Phase _phase;
    private RoomDefinition? _room;
    private TileRect? _area;
    private Drag _drag;
    private TilePosition _dragStart;
    private TileRect _dragArea;
    private DoorPlacement? _door;
    private DoorPlacement? _hoverEdge;
    private RoomPlanProblem _problem;
    private RoomPlan? _sentPlan;
    private long _sentTick;
    private int _roomId;
    private string? _message;
    private RoomBlueprint? _blueprint;
    private TileRect? _markedArea;
    private int _player;

    private ObjectDefinition? _placing;
    private Direction _orientation;
    private TilePosition? _ghostTile;
    private ObjectPlacementProblem _placeProblem;
    private bool _pickingUp;
    private int _highlightedObject;

    private enum Phase
    {
        Inactive,
        Blueprint,
        Door,
        Windows,
        Waiting,
        Objects,
    }

    private enum Drag
    {
        None,
        New,
        Move,
        Resize,
    }

    public bool IsActive => _phase != Phase.Inactive;

    /// <summary>The blueprint to draw on the map, with its door and windows, until the room is built.</summary>
    public RoomBlueprint? Blueprint => _blueprint;

    /// <summary>The object under the cursor while picking up, to draw highlighted; 0 if none.</summary>
    public int HighlightedObject => _pickingUp ? _highlightedObject : 0;

    /// <summary>The object to draw at the cursor, if any.</summary>
    public (string ObjectType, TilePosition Tile, Direction Orientation, Color Tint)? Ghost =>
        _placing is not null && _ghostTile is { } tile && !_pickingUp
            ? (_placing.Id, tile, _orientation, _placeProblem == ObjectPlacementProblem.None ? Color.White : BlockedGhost)
            : null;

    /// <summary>The room being built, while building.</summary>
    public RoomDefinition? RoomType => IsActive ? _room : null;

    /// <summary>Whether the room is being furnished: the step with the edit panel's items and buttons.</summary>
    public bool IsFurnishing => _phase == Phase.Objects;

    /// <summary>Whether the pick up button is down.</summary>
    public bool IsPickingUp => _pickingUp;

    /// <summary>The objects the room still needs, counting commands on their way.</summary>
    public IReadOnlyList<(string ObjectType, int Count)> Missing(Simulation simulation) =>
        _phase == Phase.Objects && simulation.FindEntity(_roomId) is Room room ? Remaining(simulation, room) : [];

    /// <summary>Whether Enter would confirm the room now.</summary>
    public bool CanConfirm(Simulation simulation) =>
        _phase == Phase.Objects && !_pickingUp && _pending.Count == 0 && simulation.FindEntity(_roomId) is Room room
        && Remaining(simulation, room).Count == 0 && RoomBuilder.CanConfirm(simulation, room, _player);

    /// <summary>
    /// Puts an extra object the room allows on the cursor while furnishing (the edit panel's buy list, CorsixTH's
    /// <c>UIPlaceObjects:addObjects</c>); it is bought when placed and sold back if the room is taken down.
    /// </summary>
    public void PlaceExtra(ObjectDefinition definition)
    {
        if (_phase != Phase.Objects || _room is null || !_room.Allows(definition.Id) || definition.Orientations.Count == 0)
        {
            return;
        }

        _pickingUp = false;
        StartPlacing(definition, Turns.First(definition.Orientations.ContainsKey));
    }

    public void Start(RoomDefinition room)
    {
        _room = room;
        _phase = Phase.Blueprint;
        _area = null;
        _drag = Drag.None;
        _door = null;
        _windows.Clear();
        _pending.Clear();
        _problem = RoomPlanProblem.None;
        _message = null;
        _blueprint = null;
        _markedArea = null;
        StopPlacing();
    }

    /// <summary>Follows the input; returns a command for the simulation when a step needs one.</summary>
    public Command? Update(Simulation simulation, int player, BuildInput input)
    {
        if (_room is null)
        {
            return null;
        }

        _player = player;
        var command = _phase switch
        {
            Phase.Blueprint => UpdateBlueprint(simulation, player, input),
            Phase.Door => UpdateDoor(simulation, player, input),
            Phase.Windows => UpdateWindows(simulation, player, input),
            Phase.Waiting => UpdateWaiting(simulation, player, input),
            Phase.Objects => UpdateObjects(simulation, player, input),
            _ => null,
        };
        _blueprint = CreateBlueprint(simulation, player);
        return command;
    }

    public string Status(Simulation? simulation)
    {
        if (_room is null)
        {
            return string.Empty;
        }

        var name = simulation is null ? _room.Name : $"{_room.Name} ({MoneyText.Format(simulation.Rules.BuildCost(_room))})";
        var message = _message is null ? string.Empty : $"   ({_message})";
        return _phase switch
        {
            Phase.Blueprint => _area is null
                ? $"{name}: drag out the blueprint, at least {_room.MinimumSize} by {_room.MinimumSize}{message}   Esc: cancel"
                : $"{name}: drag the blueprint's edges to resize it, its middle to move it{Explain(_problem)}{message}   Enter: done   Esc: cancel",
            Phase.Door => $"{name}: place the door{Explain(_problem)}{message}   Esc: back to the blueprint",
            Phase.Windows => $"{name}: place windows if you like, click one again to remove it{Explain(_problem)}{message}   Enter: done   Esc: back to the blueprint",
            Phase.Waiting => simulation is not null && ConstructionSite.Of(simulation, _player) is { IsWaiting: true, Watched.Count: > 0 }
                ? $"{name}: waiting for people to leave the site   Esc: back to the blueprint"
                : $"{name}: building...",
            Phase.Objects => ObjectsStatus(simulation),
            _ => string.Empty,
        };
    }

    /// <summary>Draws the footprint of the object being placed, in world coordinates.</summary>
    public void DrawPreview(SpriteBatch batch, Texture2D diamond)
    {
        if (_phase == Phase.Objects && _placing is not null && _ghostTile is { } tile && !_pickingUp
            && _placing.Orientations.TryGetValue(_orientation, out var layout))
        {
            foreach (var part in layout.Footprint)
            {
                DrawDiamond(batch, diamond, new TilePosition(tile.X + part.X, tile.Y + part.Y), FootprintTint);
            }
        }
    }

    /// <summary>
    /// The blueprint as the original shows it: while dragging, good tiles blue and bad ones red;
    /// from the door step on, a solid blue floor with the door and windows drawn into the wall.
    /// </summary>
    private RoomBlueprint? CreateBlueprint(Simulation simulation, int player)
    {
        if (_area is not { } area || _phase is not (Phase.Blueprint or Phase.Door or Phase.Windows or Phase.Waiting))
        {
            return null;
        }

        if (_phase == Phase.Blueprint)
        {
            var map = simulation.Map;
            var allBad = _problem is RoomPlanProblem.TooSmall or RoomPlanProblem.UnknownRoomType;
            return RoomBlueprint.Create(
                area,
                tile => !allBad && map.Contains(tile.X, tile.Y) && RoomBuilder.CheckTile(map, area, tile, player) == RoomPlanProblem.None,
                placed: false);
        }

        var blueprint = RoomBlueprint.Create(area, _ => true, placed: true);
        if (_phase == Phase.Door)
        {
            if (_door is { } hovered)
            {
                blueprint.SetDoor(hovered, _problem == RoomPlanProblem.None);
            }

            return blueprint;
        }

        blueprint.SetDoor(_door!.Value, valid: true);
        foreach (var window in _windows)
        {
            blueprint.SetWindow(window, valid: true);
        }

        if (_phase == Phase.Windows && _hoverEdge is { } edge && edge != _door && !_windows.Contains(edge))
        {
            blueprint.SetWindow(edge, _problem == RoomPlanProblem.None);
        }

        return blueprint;
    }

    private Command? UpdateBlueprint(Simulation simulation, int player, BuildInput input)
    {
        if (input.Back)
        {
            _phase = Phase.Inactive;
            return null;
        }

        if (input.LeftPressed && input.Hovered is { } start)
        {
            BeginDrag(start);
        }

        if (input.LeftDown && _drag != Drag.None && input.Hovered is { } at)
        {
            _area = Dragged(at);
        }

        if (input.LeftReleased)
        {
            _drag = Drag.None;
        }

        _problem = _area is { } area ? RoomBuilder.CheckArea(simulation.Map, _room, area, player) : RoomPlanProblem.None;
        if (input.Confirm && _area is not null && _problem == RoomPlanProblem.None)
        {
            _phase = Phase.Door;
            _door = null;
        }

        return null;
    }

    /// <summary>
    /// Starts a drag as CorsixTH does: inside the blueprint away from its border moves it, on its
    /// border resizes it from that side, and outside it starts a new blueprint.
    /// </summary>
    private void BeginDrag(TilePosition start)
    {
        _dragStart = start;
        if (_area is not { } area || !area.Contains(start))
        {
            _drag = Drag.New;
            _area = new TileRect(start.X, start.Y, 1, 1);
            return;
        }

        _dragArea = area;
        var onBorder = start.X == area.X || start.X == area.Right - 1 || start.Y == area.Y || start.Y == area.Bottom - 1;
        _drag = onBorder ? Drag.Resize : Drag.Move;
    }

    private TileRect Dragged(TilePosition at)
    {
        var area = _dragArea;
        switch (_drag)
        {
            case Drag.Move:
                return area with { X = area.X + at.X - _dragStart.X, Y = area.Y + at.Y - _dragStart.Y };
            case Drag.Resize:
                // Each side the drag started on follows the cursor; the opposite side stays.
                var (x1, y1, x2, y2) = (area.X, area.Y, area.Right - 1, area.Bottom - 1);
                if (_dragStart.X == area.X)
                {
                    x1 = at.X;
                }
                else if (_dragStart.X == area.Right - 1)
                {
                    x2 = at.X;
                }

                if (_dragStart.Y == area.Y)
                {
                    y1 = at.Y;
                }
                else if (_dragStart.Y == area.Bottom - 1)
                {
                    y2 = at.Y;
                }

                return TileRect.FromCorners(new(x1, y1), new(x2, y2));
            default:
                return TileRect.FromCorners(_dragStart, at);
        }
    }

    private Command? UpdateDoor(Simulation simulation, int player, BuildInput input)
    {
        if (input.Back)
        {
            return BackToBlueprint();
        }

        var area = _area!.Value;
        if (_markedArea != area)
        {
            // As CorsixTH's door phase begins: people are told to leave the blueprint (again, after the room was taken down).
            _markedArea = area;
            return new MarkBlueprintCommand(_room!.Id, area);
        }

        _door = input.Hovered is { } tile ? RoomBuilder.WallNear(area, tile, input.FractionX, input.FractionY) : null;
        _problem = _door is { } door ? RoomBuilder.CheckDoor(simulation.Map, simulation.Content, area, door) : RoomPlanProblem.DoorNotOnPerimeter;
        if (!input.LeftPressed || _door is null || _problem != RoomPlanProblem.None)
        {
            return null;
        }

        // UIEditRoom:placeDoorBlueprint.
        PlaySound?.Invoke("buildclk.wav");
        _windows.Clear();
        if (_room!.Walls.HasWindows)
        {
            _phase = Phase.Windows;
            return null;
        }

        return Submit(simulation, player);
    }

    private Command? UpdateWindows(Simulation simulation, int player, BuildInput input)
    {
        if (input.Back)
        {
            _windows.Clear();
            return BackToBlueprint();
        }

        var area = _area!.Value;
        _hoverEdge = input.Hovered is { } tile ? RoomBuilder.WallNear(area, tile, input.FractionX, input.FractionY) : null;
        _problem = RoomPlanProblem.None;
        if (_hoverEdge is { } edge && !_windows.Contains(edge))
        {
            _problem = RoomBuilder.CheckWindow(simulation.Map, _room!, area, _door!.Value, edge);
        }

        if (input.LeftPressed && _hoverEdge is { } clicked)
        {
            // UIEditRoom:placeWindowBlueprint, and taking a window away again (de_build.wav).
            if (_windows.Remove(clicked))
            {
                PlaySound?.Invoke("de_build.wav");
            }
            else if (_problem == RoomPlanProblem.None)
            {
                _windows.Add(clicked);
                PlaySound?.Invoke("buildclk.wav");
            }
        }

        if (input.Confirm)
        {
            var plan = new RoomPlan(_room!.Id, area, _door!.Value, [.. _windows]);
            _problem = RoomBuilder.Check(simulation.Map, simulation.Content, plan, player);
            if (_problem == RoomPlanProblem.None)
            {
                return Submit(simulation, player);
            }
        }

        return null;
    }

    private BuildRoomCommand? Submit(Simulation simulation, int player)
    {
        if (!RoomPurchases.CanAfford(simulation, _room!, player))
        {
            _message = $"not enough money: the room costs {MoneyText.Format(simulation.Rules.BuildCost(_room!))}";
            return null;
        }

        _sentPlan = new RoomPlan(_room!.Id, _area!.Value, _door!.Value, [.. _windows]);
        _sentTick = simulation.Tick;
        _phase = Phase.Waiting;
        _message = null;
        return new BuildRoomCommand(_sentPlan);
    }

    /// <summary>Back from the door or windows to the blueprint: the area is no longer avoided.</summary>
    private UnmarkBlueprintCommand BackToBlueprint()
    {
        _phase = Phase.Blueprint;
        _markedArea = null;
        return new UnmarkBlueprintCommand();
    }

    /// <summary>
    /// Waits for the room to appear in the simulation, which happens a few ticks after the command is sent, or
    /// later if people have to leave the site first.
    /// </summary>
    private Command? UpdateWaiting(Simulation simulation, int player, BuildInput input)
    {
        var site = ConstructionSite.Of(simulation, player);
        if (site is { IsWaiting: true })
        {
            if (input.Back)
            {
                // CorsixTH lets the player go back to the blueprint while waiting for the area to clear.
                _windows.Clear();
                return BackToBlueprint();
            }

            _sentTick = simulation.Tick;
            return null;
        }

        var room = simulation.Entities.OfType<Room>().FirstOrDefault(r =>
            !r.IsActive && r.Area == _sentPlan!.Area && r.RoomType == _sentPlan.RoomType && RoomBuilder.IsOwnedBy(simulation.Map, r, player));
        if (room is not null)
        {
            _roomId = room.Id;
            _markedArea = null;
            _phase = Phase.Objects;
            _pending.Clear();
            StopPlacing();
            return null;
        }

        if (simulation.Tick > _sentTick + CommandTimeoutTicks)
        {
            // Something got there first, for example another player's room, or the money ran out.
            _phase = Phase.Blueprint;
            _message = RoomPurchases.CanAfford(simulation, _room!, player) ? "the room could not be built there" : "not enough money";
        }

        return null;
    }

    private Command? UpdateObjects(Simulation simulation, int player, BuildInput input)
    {
        if (simulation.FindEntity(_roomId) is not Room room || room.IsActive)
        {
            _phase = Phase.Inactive;
            return null;
        }

        ForgetSettledCommands(simulation, room);

        if (input.Back)
        {
            if (_pickingUp)
            {
                _pickingUp = false;
                return null;
            }

            // As in CorsixTH, cancelling the furnishing takes the room down and goes back to placing its door.
            _phase = Phase.Door;
            StopPlacing();
            return new CancelRoomCommand(room.Id);
        }

        if (input.PickUp)
        {
            _pickingUp = !_pickingUp;
            StopPlacing();
        }

        if (_pickingUp)
        {
            return UpdatePickingUp(simulation, room, input);
        }

        // An object whose last missing piece got placed meanwhile (say, by a command still on its way) is dropped; an
        // extra object the room does not need stays on the cursor until it is placed.
        if (_placing is not null && (_room!.NeededObjects?.ContainsKey(_placing.Id) ?? false) && !Remaining(simulation, room).Any(r => r.ObjectType == _placing.Id))
        {
            StopPlacing();
        }

        if (_placing is null && NextToPlace(simulation, room) is { } next)
        {
            StartPlacing(next, Turns.First(next.Orientations.ContainsKey));
        }

        if (input.Confirm && _placing is null && _pending.Count == 0 && RoomBuilder.CanConfirm(simulation, room, player))
        {
            _phase = Phase.Inactive;
            return new ConfirmRoomCommand(room.Id);
        }

        if (_placing is null)
        {
            return null;
        }

        if (input.RightClicked)
        {
            // UIPlaceObjects:tryNextOrientation.
            PlaySound?.Invoke("swoosh.wav");
            Turn();
        }

        FollowCursor(room, input.Hovered);
        _placeProblem = _ghostTile is { } tile
            ? ObjectPlacer.Check(simulation, _placing.Id, tile, _orientation, player)
            : ObjectPlacementProblem.OutsideMap;
        if (input.LeftPressed && _ghostTile is { } target && _placeProblem == ObjectPlacementProblem.None)
        {
            var command = new PlaceObjectCommand(_placing.Id, target, _orientation);
            var newer = simulation.Entities.Count == 0 ? 1 : simulation.Entities.Max(e => e.Id) + 1;
            _pending.Add(new Pending(_placing.Id, +1, simulation.Tick, newer, target, _orientation));
            StopPlacing();
            PlaySound?.Invoke("place_r.wav"); // UIPlaceObjects:placeObject
            return command;
        }

        return null;
    }

    private Command? UpdatePickingUp(Simulation simulation, Room room, BuildInput input)
    {
        _highlightedObject = 0;
        if (input.Hovered is { } tile
            && simulation.Map[tile.X, tile.Y].ObjectId is var id and not 0
            && simulation.FindEntity(id) is RoomObject placed
            && placed.RoomId == room.Id)
        {
            _highlightedObject = placed.Id;
            if (input.LeftPressed && simulation.Content.FindObject(placed.ObjectType) is { } definition)
            {
                // The object comes off the floor onto the cursor, keeping its orientation.
                _pickingUp = false;
                _pending.Add(new Pending(placed.ObjectType, -1, simulation.Tick, placed.Id, placed.Tile, placed.Orientation));
                StartPlacing(definition, placed.Orientation);
                _ghostTile = placed.Tile;
                return new PickUpObjectCommand(placed.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps the object on the cursor: its anchor tile is always the tile under the cursor, as in
    /// CorsixTH's <c>calculateBestPlacementPosition</c>, so it follows every step of the cursor. It
    /// is hidden while the cursor is outside the room.
    /// </summary>
    private void FollowCursor(Room room, TilePosition? hovered)
    {
        _ghostTile = hovered is { } tile && room.Area.Contains(tile) ? tile : null;
    }

    /// <summary>The next object still missing from the room, biggest first as in CorsixTH.</summary>
    private ObjectDefinition? NextToPlace(Simulation simulation, Room room)
    {
        return Remaining(simulation, room)
            .Select(missing => simulation.Content.FindObject(missing.ObjectType))
            .OfType<ObjectDefinition>()
            .OrderByDescending(definition => definition.Orientations.Values.First().Footprint.Count)
            .ThenBy(definition => definition.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>What is still to be placed, counting commands sent but not yet carried out.</summary>
    private List<(string ObjectType, int Count)> Remaining(Simulation simulation, Room room)
    {
        var remaining = RoomBuilder.MissingObjects(simulation, room).ToDictionary(m => m.ObjectType, m => m.Count, StringComparer.Ordinal);
        foreach (var pending in _pending)
        {
            remaining[pending.ObjectType] = remaining.GetValueOrDefault(pending.ObjectType) - pending.Change;
        }

        return remaining.Where(pair => pair.Value > 0).Select(pair => (pair.Key, pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Forgets sent commands once the room shows their effect, or once they are long overdue
    /// (the simulation refused them, for example because something else changed meanwhile).
    /// </summary>
    private void ForgetSettledCommands(Simulation simulation, Room room)
    {
        _pending.RemoveAll(p => p.IsSettled(simulation, room) || simulation.Tick > p.Tick + CommandTimeoutTicks);
    }

    private void StartPlacing(ObjectDefinition definition, Direction orientation)
    {
        _placing = definition;
        _orientation = orientation;
        _ghostTile = null;
        _placeProblem = ObjectPlacementProblem.None;
    }

    private void StopPlacing()
    {
        _placing = null;
        _ghostTile = null;
        _highlightedObject = 0;
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

    private string ObjectsStatus(Simulation? simulation)
    {
        var name = _room!.Name;
        if (_pickingUp)
        {
            return $"{name}: click an object to pick it up   P or Esc: stop picking up";
        }

        if (simulation?.FindEntity(_roomId) is not Room room)
        {
            return $"{name}: ...";
        }

        var remaining = Remaining(simulation, room);
        if (remaining.Count == 0 && _placing is null)
        {
            var price = MoneyText.Format(simulation.Rules.RoomCost(_room.Id));
            return $"{name}: you can confirm the room ({price}), or move objects   Enter: confirm   P: pick up   Esc: take the room down, selling the objects back";
        }

        var list = string.Join(", ", remaining.Select(r => $"{simulation.Content.FindObject(r.ObjectType)?.Name ?? r.ObjectType} {r.Count}"));
        var placing = _placing is null ? string.Empty : $"   placing {_placing.Name}{ExplainPlacement(_placeProblem)}";
        return $"{name}: place the objects ({list}){placing}   Right click: turn   P: pick up   Esc: take the room down, selling the objects back";
    }

    private static void DrawDiamond(SpriteBatch batch, Texture2D diamond, TilePosition tile, Color tint)
    {
        batch.Draw(diamond, new Vector2((32 * (tile.X - tile.Y)) - 32, 16 * (tile.X + tile.Y)), tint);
    }

    private static string Explain(RoomPlanProblem problem)
    {
        var text = problem switch
        {
            RoomPlanProblem.TooSmall => "too small",
            RoomPlanProblem.OutsideMap => "outside the map",
            RoomPlanProblem.NotBuildable => "cannot build there",
            RoomPlanProblem.NotOwned => "not your land",
            RoomPlanProblem.OverlapsRoom => "overlaps a room",
            RoomPlanProblem.WallInside => "a wall is in the way",
            RoomPlanProblem.DoorNotOnPerimeter or RoomPlanProblem.WindowNotOnPerimeter => "point at the room's walls",
            RoomPlanProblem.DoorInCorner or RoomPlanProblem.WindowInCorner => "not in a corner",
            RoomPlanProblem.DoorInWall => "cannot put a door in that wall",
            RoomPlanProblem.WindowInWall => "cannot put a window in that wall",
            RoomPlanProblem.WindowOnDoor => "the door is there",
            RoomPlanProblem.DoorBlocked => "the door would open onto a blocked tile",
            RoomPlanProblem.NoWindows => "this room has no windows",
            RoomPlanProblem.TooManyWindows => "too many windows",
            RoomPlanProblem.UnknownRoomType => "unknown room",
            _ => null,
        };
        return text is null ? string.Empty : $"   ({text})";
    }

    private static string ExplainPlacement(ObjectPlacementProblem problem)
    {
        var text = problem switch
        {
            ObjectPlacementProblem.OutsideMap => "point into the room",
            ObjectPlacementProblem.NotAllowedHere => "must stand inside the room",
            ObjectPlacementProblem.NotOwned => "not your land",
            ObjectPlacementProblem.Blocked => "something is in the way",
            ObjectPlacementProblem.WouldBlockAccess => "would block access",
            _ => null,
        };
        return text is null ? string.Empty : $" ({text})";
    }

    /// <summary>
    /// A command sent but perhaps not yet carried out: one more (+1) object of a type in the room,
    /// placed at <paramref name="Tile"/> with an id of at least <paramref name="ObjectId"/>, or one
    /// fewer (−1): the object <paramref name="ObjectId"/>.
    /// </summary>
    private readonly record struct Pending(string ObjectType, int Change, long Tick, int ObjectId, TilePosition Tile, Direction Orientation)
    {
        public bool IsSettled(Simulation simulation, Room room)
        {
            if (Change < 0)
            {
                return simulation.FindEntity(ObjectId) is null;
            }

            var (minimumId, type, tile, orientation) = (ObjectId, ObjectType, Tile, Orientation);
            return simulation.Entities.OfType<RoomObject>().Any(o => o.Id >= minimumId && o.RoomId == room.Id && o.ObjectType == type && o.Tile == tile && o.Orientation == orientation);
        }
    }
}
