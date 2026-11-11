// Clearing the area of a new blueprint ported from CorsixTH CorsixTH/Lua/dialogs/edit_room.lua
// (_clearAreaOfHumanoids, _humanoidsInArea, _checkIsWaitingHumanoidsToLeaveArea, _enterWaitingClearAreaPhase).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Economy;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Rooms;

/// <summary>
/// A player's room blueprint from the door step on, while the room is not built yet. Its tiles are marked
/// <see cref="TileFlags.AvoidTile"/> and the people on it are told to leave, as CorsixTH does on entering the door
/// phase. Once the plan is complete (<see cref="Plan"/>), the site waits until those people are gone (CorsixTH's
/// "clear_area" phase) and then the room is built.
/// </summary>
/// <remarks>Each player has at most one site, as each player has at most one room edit dialog in CorsixTH.</remarks>
public sealed class ConstructionSite : Entity
{
    private const int MaxWatched = 1 << 20;

    /// <summary>Hours between two looks at whether the people have left, once the first look found some still there.</summary>
    private const int CheckInterval = 10;

    private readonly List<int> _watched = [];

    internal ConstructionSite(int id, int player, TileRect area)
        : base(id)
    {
        Player = player;
        Area = area;
    }

    public override EntityKind Kind => EntityKind.ConstructionSite;

    /// <summary>The player (0-based) building here.</summary>
    public int Player { get; }

    /// <summary>The blueprint's tiles.</summary>
    public TileRect Area { get; }

    /// <summary>The complete plan, once the player is done with door and windows; null before.</summary>
    public RoomPlan? Plan { get; private set; }

    /// <summary>Whether the room waits for people to leave its area before it is built.</summary>
    public bool IsWaiting => Plan is not null;

    /// <summary>The people told to leave who have not left yet (CorsixTH's <c>humanoids_to_watch</c>).</summary>
    public IReadOnlyList<int> Watched => _watched;

    /// <summary>Hours until the next look at <see cref="Watched"/> (CorsixTH's <c>check_for_clear_area_timer</c>), or 0.</summary>
    private int Timer { get; set; }

    /// <summary>Whether the site is gone: its room was built or it was given up. It leaves the simulation at the end of the tick.</summary>
    internal bool IsDone { get; private set; }

    /// <summary>The site of <paramref name="player"/>, if any.</summary>
    public static ConstructionSite? Of(Simulation simulation, int player)
    {
        foreach (var entity in simulation.Entities)
        {
            if (entity is ConstructionSite { IsDone: false } site && site.Player == player)
            {
                return site;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts a site on <paramref name="area"/> for <paramref name="player"/>, replacing their previous one, as
    /// CorsixTH's <c>_enterDoorPhase</c> marks the blueprint and asks the people on it to leave
    /// (<c>_clearAreaOfHumanoids</c>).
    /// </summary>
    internal static ConstructionSite Mark(Simulation simulation, int player, TileRect area)
    {
        Of(simulation, player)?.Unmark(simulation);
        var site = simulation.AddEntity(id => new ConstructionSite(id, player, area));
        site.SetAvoid(simulation.Map, true);
        site.WatchAndExpel(simulation);
        return site;
    }

    /// <summary>Gives the site up and frees its tiles, as CorsixTH's <c>returnToWallPhase</c>.</summary>
    internal void Unmark(Simulation simulation)
    {
        if (IsDone)
        {
            return;
        }

        SetAvoid(simulation.Map, false);
        IsDone = true;
        _watched.Clear();
        Timer = 0;
    }

    /// <summary>
    /// The plan is complete: as CorsixTH's <c>_enterWaitingClearAreaPhase</c>, the room is built at once if the
    /// people told to leave are gone and nobody else is in the way; otherwise those in the way are told to leave
    /// and the site waits for them.
    /// </summary>
    internal void Complete(Simulation simulation, RoomPlan plan)
    {
        Plan = plan;
        if (_watched.Count == 0)
        {
            if (HumanoidsInArea(simulation).Count == 0)
            {
                Finish(simulation);
                return;
            }

            WatchAndExpel(simulation);
        }

        Timer = 1;
    }

    public override void Update(Simulation simulation)
    {
        if (IsDone || Plan is null || Timer == 0)
        {
            return;
        }

        Timer--;
        if (Timer > 0)
        {
            return;
        }

        var (x1, x2, y1, y2) = Perimeter;
        _watched.RemoveAll(id => simulation.FindEntity(id) is not Humanoid { IsDespawning: false } humanoid
            || !humanoid.IsObscuringArea(x1, x2, y1, y2));
        if (_watched.Count > 0)
        {
            Timer = CheckInterval;
            return;
        }

        // As in CorsixTH, only the people told to leave are looked at here; anyone who walked in meanwhile is
        // walled in.
        Finish(simulation);
    }

    /// <summary>The blueprint's tiles plus its walls, as CorsixTH's <c>_humanoidsInArea</c> passes them.</summary>
    private (int X1, int X2, int Y1, int Y2) Perimeter => (Area.X - 1, Area.Right, Area.Y - 1, Area.Bottom);

    /// <summary>Builds the room (CorsixTH's <c>_enterObjectsPhase</c>), if the plan still holds, and frees the tiles.</summary>
    private void Finish(Simulation simulation)
    {
        var plan = Plan!;
        Unmark(simulation);
        if (RoomBuilder.Check(simulation.Map, simulation.Content, plan, Player) == RoomPlanProblem.None
            && simulation.Content.FindRoom(plan.RoomType) is { } room)
        {
            var built = RoomBuilder.Build(simulation, plan, room);
            RoomRebuild.OnBuilt(simulation, built, Player);
            RoomPurchases.BuyNeededObjects(simulation, room, Player);
        }
    }

    private void WatchAndExpel(Simulation simulation)
    {
        _watched.Clear();
        foreach (var humanoid in HumanoidsInArea(simulation))
        {
            _watched.Add(humanoid.Id);
            humanoid.LeaveArea(simulation);
        }
    }

    private List<Humanoid> HumanoidsInArea(Simulation simulation)
    {
        var (x1, x2, y1, y2) = Perimeter;
        var humanoids = new List<Humanoid>();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { IsDespawning: false } humanoid && humanoid.IsObscuringArea(x1, x2, y1, y2))
            {
                humanoids.Add(humanoid);
            }
        }

        return humanoids;
    }

    private void SetAvoid(LevelMap map, bool avoid)
    {
        for (var y = Area.Y; y < Area.Bottom; y++)
        {
            for (var x = Area.X; x < Area.Right; x++)
            {
                if (map.Contains(x, y))
                {
                    map[x, y].Set(TileFlags.AvoidTile, avoid);
                }
            }
        }
    }

    internal static ConstructionSite ReadState(int id, SimReader reader)
    {
        var player = reader.ReadInt32();
        var area = new TileRect(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        var site = new ConstructionSite(id, player, area)
        {
            Plan = reader.ReadBool() ? RoomPlanSerializer.Read(reader) : null,
            Timer = reader.ReadInt32(),
        };
        var count = reader.ReadCount(MaxWatched);
        for (var i = 0; i < count; i++)
        {
            site._watched.Add(reader.ReadInt32());
        }

        return site;
    }

    protected override void WriteState(SimWriter writer)
    {
        writer.WriteInt32(Player);
        writer.WriteInt32(Area.X);
        writer.WriteInt32(Area.Y);
        writer.WriteInt32(Area.Width);
        writer.WriteInt32(Area.Height);
        writer.WriteBool(Plan is not null);
        if (Plan is not null)
        {
            RoomPlanSerializer.Write(writer, Plan);
        }

        writer.WriteInt32(Timer);
        writer.WriteInt32(_watched.Count);
        foreach (var id in _watched)
        {
            writer.WriteInt32(id);
        }
    }
}
