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
}
