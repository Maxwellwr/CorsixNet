using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Content.Tests;

public class BaseGameContentTests
{
    [Fact]
    public void UseAnimationsLastAsLongAsTheOriginalsAnimations()
    {
        var set = AnimationSet.Load(RealGameData.Require());

        var animations = BaseGameContent.Create().Objects
            .SelectMany(o => o.UsageTables)
            .SelectMany(table => table.Values)
            .SelectMany(use => use.All)
            .Distinct()
            .ToList();

        // operating_table.lua shortens its in-use animations to one frame (anim_mgr:setAnimLength).
        var shortened = new HashSet<int> { 2348, 2938 };
        Assert.NotEmpty(animations);
        foreach (var animation in animations)
        {
            Assert.Equal(shortened.Contains(animation.Id) ? 1 : set.Length(set.FirstFrame(animation.Id)), animation.Length);
        }
    }

    [Fact]
    public void MachineAnimationsLastAsLongAsTheOriginalsAnimations()
    {
        var set = AnimationSet.Load(RealGameData.Require());

        var machines = BaseGameContent.Create().Objects.Select(o => o.Machine).OfType<MachineDefinition>().ToList();
        var animations = machines.SelectMany(m => m.Repair.All.Append(m.Smoke).Append(m.Crashed)).Distinct().ToList();

        Assert.Equal(13, machines.Count);
        foreach (var animation in animations)
        {
            Assert.Equal(set.Length(set.FirstFrame(animation.Id)), animation.Length);
        }
    }

    [Fact]
    public void DoorAnimationsLastAsLongAsTheOriginalsAnimations()
    {
        var set = AnimationSet.Load(RealGameData.Require());

        var animations = BaseGameContent.DoorAnimations.Values.SelectMany(door => new[] { door.Entering, door.Leaving }).ToList();

        // operating_table.lua shortens its in-use animations to one frame (anim_mgr:setAnimLength).
        var shortened = new HashSet<int> { 2348, 2938 };
        Assert.NotEmpty(animations);
        foreach (var animation in animations)
        {
            Assert.Equal(shortened.Contains(animation.Id) ? 1 : set.Length(set.FirstFrame(animation.Id)), animation.Length);
        }
    }

    [Fact]
    public void VomitAndPeeAnimationsLastAsLongAsTheOriginalsAnimations()
    {
        var set = AnimationSet.Load(RealGameData.Require());

        var animations = BaseGameContent.PatientAnimations.Values.SelectMany(a => new[] { a.Vomit, a.Pee }).OfType<AnimationRef>().ToList();

        // A vomit and a pee animation for each kind of patient with them.
        Assert.Equal(2 * BaseGameContent.PatientAnimations.Count, animations.Count);
        foreach (var animation in animations)
        {
            Assert.Equal(set.Length(set.FirstFrame(animation.Id)), animation.Length);
        }
    }

    [Fact]
    public void MultiUseAnimationsLastAsLongAsTheOriginalsAnimations()
    {
        var set = AnimationSet.Load(RealGameData.Require());

        var animations = BaseGameContent.Create().Objects
            .Where(o => o.MultiUsage is not null)
            .SelectMany(o => o.MultiUsage!.Values)
            .SelectMany(table => table.Values)
            .SelectMany(variants => variants)
            .SelectMany(use => use.Phases)
            .SelectMany(phase => phase.Secondary is { } secondary ? new[] { phase.Primary, secondary } : [phase.Primary])
            .Distinct()
            .ToList();

        // operating_table.lua shortens its in-use animations to one frame (anim_mgr:setAnimLength).
        var shortened = new HashSet<int> { 2348, 2938 };
        Assert.NotEmpty(animations);
        foreach (var animation in animations)
        {
            Assert.Equal(shortened.Contains(animation.Id) ? 1 : set.Length(set.FirstFrame(animation.Id)), animation.Length);
        }
    }
}
