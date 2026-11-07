using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Maps;

/// <summary>
/// The player's room building flow: drag out the room's area, then click its perimeter to
/// place the door. Validity is checked with the simulation's own <see cref="RoomBuilder"/>,
/// so a plan shown as valid is the plan the simulation accepts.
/// </summary>
internal sealed class BuildRoomTool
{
    private static readonly Color ValidTint = new Color(0, 220, 0) * 0.45f;
    private static readonly Color InvalidTint = new Color(230, 0, 0) * 0.45f;
    private static readonly Color DoorTint = new Color(255, 230, 0) * 0.7f;

    private Phase _phase;
    private RoomDefinition? _room;
    private TilePosition? _anchor;
    private TileRect? _area;
    private DoorPlacement? _door;
    private RoomPlanProblem _problem;

    private enum Phase
    {
        Inactive,
        Area,
        Door,
    }

    public bool IsActive => _phase != Phase.Inactive;

    public string Status => _phase switch
    {
        Phase.Area => $"Build {_room!.Name}: drag out the room, at least {_room.MinimumSize} by {_room.MinimumSize}{Explain(_problem)}   Esc: cancel",
        Phase.Door => $"Build {_room!.Name}: click a wall to place the door{Explain(_problem)}   Esc: back",
        _ => string.Empty,
    };

    public void Start(RoomDefinition room)
    {
        _room = room;
        _phase = Phase.Area;
        _anchor = null;
        _area = null;
        _door = null;
        _problem = RoomPlanProblem.None;
    }

    /// <summary>Steps back: from the door to the area, or out of building altogether.</summary>
    public void Back()
    {
        if (_phase == Phase.Door)
        {
            _phase = Phase.Area;
            _door = null;
            _problem = RoomPlanProblem.None;
        }
        else
        {
            _phase = Phase.Inactive;
        }
    }

    /// <summary>Follows the mouse; returns a command when the player confirms a valid room.</summary>
    public BuildRoomCommand? Update(Simulation simulation, int player, TilePosition? hovered, bool leftDown, bool leftPressed, bool leftReleased)
    {
        if (_room is null)
        {
            return null;
        }

        var map = simulation.Map;
        switch (_phase)
        {
            case Phase.Area:
                if (leftPressed && hovered is { } start)
                {
                    _anchor = start;
                }

                if (_anchor is { } anchor && hovered is { } corner && leftDown)
                {
                    _area = TileRect.FromCorners(anchor, corner);
                    _problem = RoomBuilder.CheckArea(map, _room, _area.Value, player);
                }

                if (leftReleased && _anchor is not null)
                {
                    _anchor = null;
                    if (_area is not null && _problem == RoomPlanProblem.None)
                    {
                        _phase = Phase.Door;
                    }
                }

                break;

            case Phase.Door:
                _door = hovered is { } tile ? RoomBuilder.DoorNear(_area!.Value, tile) : null;
                var plan = _door is { } door ? new RoomPlan(_room.Id, _area!.Value, door) : null;
                _problem = plan is null ? RoomPlanProblem.DoorNotOnPerimeter : RoomBuilder.Check(map, simulation.Content, plan, player);
                if (leftPressed && plan is not null && _problem == RoomPlanProblem.None)
                {
                    _phase = Phase.Inactive;
                    return new BuildRoomCommand(plan);
                }

                break;
        }

        return null;
    }

    /// <summary>Draws the planned area and door over the map, in world coordinates.</summary>
    public void DrawPreview(SpriteBatch batch, Texture2D diamond, TilePosition? hovered)
    {
        if (!IsActive)
        {
            return;
        }

        var area = _area ?? (hovered is { } tile ? new TileRect(tile.X, tile.Y, 1, 1) : (TileRect?)null);
        if (area is not { } rect)
        {
            return;
        }

        var tint = _area is null || (_phase == Phase.Area && _problem != RoomPlanProblem.None) ? InvalidTint : ValidTint;
        if (_area is null)
        {
            tint = ValidTint * 0.6f;
        }

        for (var y = rect.Y; y < rect.Bottom; y++)
        {
            for (var x = rect.X; x < rect.Right; x++)
            {
                batch.Draw(diamond, new Vector2((32 * (x - y)) - 32, 16 * (x + y)), tint);
            }
        }

        if (_phase == Phase.Door && _door is { } door)
        {
            var doorTint = _problem == RoomPlanProblem.None ? DoorTint : InvalidTint;
            batch.Draw(diamond, new Vector2((32 * (door.Tile.X - door.Tile.Y)) - 32, 16 * (door.Tile.X + door.Tile.Y)), doorTint);
        }
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
            RoomPlanProblem.DoorNotOnPerimeter => "point at the room's walls",
            RoomPlanProblem.DoorInWall => "cannot put a door in that wall",
            RoomPlanProblem.DoorBlocked => "the door would open onto a blocked tile",
            RoomPlanProblem.UnknownRoomType => "unknown room",
            _ => null,
        };
        return text is null ? string.Empty : $"   ({text})";
    }
}
