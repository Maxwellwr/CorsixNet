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
            .SelectMany(o => new[] { o.UsageNorth, o.UsageEast })
            .Where(table => table is not null)
            .SelectMany(table => table!.Values)
            .SelectMany(use => use.InUse.Append(use.Begin ?? use.InUse[0]).Append(use.Finish ?? use.InUse[0]))
            .Distinct()
            .ToList();

        Assert.NotEmpty(animations);
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

        Assert.NotEmpty(animations);
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

        Assert.NotEmpty(animations);
        foreach (var animation in animations)
        {
            Assert.Equal(set.Length(set.FirstFrame(animation.Id)), animation.Length);
        }
    }
}
