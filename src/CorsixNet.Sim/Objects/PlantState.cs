// Plants drooping without water, ported from CorsixTH CorsixTH/Lua/objects/plant.lua (Plant:Plant, setNextState,
// tickDay, needsWatering, isPleasingFactor, isDying).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Objects;

/// <summary>
/// How thirsty a plant is: healthy, drooping twice, dying, then dead (CorsixTH's <c>current_state</c> 0 to 4, drawn as
/// the idle animation's first frame plus the state). Every 64 days, sooner where it is warm, it gets one state worse.
/// </summary>
/// <remarks>The adviser does not warn about thirsty plants yet.</remarks>
public sealed class PlantState
{
    /// <summary>Days between states at 0 °C (CorsixTH's <c>days_between_states</c>, to match TH's rough 50 at ideal warmth).</summary>
    public const int DaysBetweenStates = 64;

    /// <summary>The healthy state.</summary>
    public const int Healthy = 0;

    /// <summary>The dead state (<c>phases</c> − 1).</summary>
    public const int Dead = Phases - 1;

    /// <summary>How many states there are (<c>phases</c>).</summary>
    public const int Phases = 5;

    /// <summary>The state, from <see cref="Healthy"/> to <see cref="Dead"/>.</summary>
    public int State { get; private set; }

    /// <summary>Days left until the next state, counted down by 1 plus the tile's temperature each day (<c>days_left</c>).</summary>
    public decimal DaysLeft { get; private set; } = DaysBetweenStates;

    /// <summary>Whether a handyman should water it (<c>needsWatering</c>).</summary>
    public bool NeedsWatering => State != Healthy;

    /// <summary>How pleasing the plant is to look at, 1 (dead) to 5 (healthy) (<c>isPleasingFactor</c>).</summary>
    public int PleasingFactor => Phases - State;

    /// <summary>Whether the plant droops already or will within three days (<c>isDying</c>).</summary>
    public bool IsDying => State != Healthy || DaysLeft < 3;

    /// <summary>One day (<c>Plant:tickDay</c>): a warm tile makes the plant thirsty sooner.</summary>
    /// <param name="temperature">The tile's temperature, 0 to 1 for 0 to 50 °C.</param>
    internal void OnEndDay(decimal temperature)
    {
        DaysLeft -= 1 + temperature;
        if (DaysLeft < 1)
        {
            DaysLeft = DaysBetweenStates;
            if (State < Dead)
            {
                State++;
            }
        }
    }

    /// <summary><c>Plant:restoreToFullHealth</c>: watered, the plant is healthy again and starts its days afresh.</summary>
    internal void Restore()
    {
        State = Healthy;
        DaysLeft = DaysBetweenStates;
    }

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(State);
        writer.WriteDecimal(DaysLeft);
    }

    internal static PlantState Read(SimReader reader)
    {
        var state = reader.ReadInt32();
        if (state is < Healthy or > Dead)
        {
            throw new InvalidDataException($"Plant state {state} is out of range.");
        }

        return new PlantState { State = state, DaysLeft = reader.ReadDecimal() };
    }
}
