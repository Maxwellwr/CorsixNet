// Ported from CorsixTH CorsixTH/Lua/entities/humanoid.lua (Humanoid:despawn) for staff who leave the hospital.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Entities;
using CorsixNet.Sim.Maps;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.People;

/// <summary>A member of staff who was fired or quit, walking off the map through a spawn point.</summary>
public sealed class LeavingBehaviour : HumanoidBehaviour
{
    public LeavingBehaviour(TilePosition homeTile) => HomeTile = homeTile;

    public override BehaviourKind Kind => BehaviourKind.Leaving;

    /// <summary>Where they leave the map.</summary>
    public TilePosition HomeTile { get; private set; }

    internal override void Update(Simulation simulation, Humanoid humanoid)
    {
        if (humanoid.IsMoving)
        {
            return;
        }

        if (humanoid.Tile == HomeTile || !humanoid.WalkTo(simulation, HomeTile))
        {
            humanoid.IsDespawning = true;
        }
    }

    protected override void WriteState(SimWriter writer) => writer.WriteTile(HomeTile);

    internal static LeavingBehaviour ReadState(SimReader reader) => new(reader.ReadTile());
}
