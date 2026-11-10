// Training settings, ported from CorsixTH CorsixTH/Lua/base_config.lua (gbv.TrainingRate, gbv.AbilityThreshold,
// gbv.TrainingValue), as the level files may override them.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Serialization;

namespace CorsixNet.Sim.Staff;

/// <summary>How fast doctors learn in the training room.</summary>
/// <param name="Rate">What a student's skill gains from teaching (<c>gbv.TrainingRate</c>, 1 to 255).</param>
/// <param name="SurgeonThreshold">The higher, the slower surgery is learnt (<c>gbv.AbilityThreshold[0]</c>).</param>
/// <param name="PsychiatristThreshold">The same for psychiatry (<c>gbv.AbilityThreshold[1]</c>).</param>
/// <param name="ResearcherThreshold">The same for research (<c>gbv.AbilityThreshold[2]</c>).</param>
/// <param name="ProjectorValue">What a projector adds to the room's training factor (<c>gbv.TrainingValue[0]</c>).</param>
/// <param name="SkeletonValue">What a skeleton adds (<c>gbv.TrainingValue[1]</c>).</param>
/// <param name="BookcaseValue">What a bookcase adds (<c>gbv.TrainingValue[2]</c>).</param>
public sealed record TrainingRules(
    int Rate,
    int SurgeonThreshold,
    int PsychiatristThreshold,
    int ResearcherThreshold,
    int ProjectorValue,
    int SkeletonValue,
    int BookcaseValue)
{
    /// <summary>base_config.lua's values.</summary>
    public static TrainingRules Default { get; } = new(40, 75, 60, 45, 10, 15, 20);

    internal void Write(SimWriter writer)
    {
        writer.WriteInt32(Rate);
        writer.WriteInt32(SurgeonThreshold);
        writer.WriteInt32(PsychiatristThreshold);
        writer.WriteInt32(ResearcherThreshold);
        writer.WriteInt32(ProjectorValue);
        writer.WriteInt32(SkeletonValue);
        writer.WriteInt32(BookcaseValue);
    }

    internal static TrainingRules Read(SimReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
}
