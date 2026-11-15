using CorsixNet.Content;
using CorsixNet.Net.Protocol;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Net.Tests;

public class ContentFingerprintTests
{
    [Fact]
    public void TheSameContentHasTheSameFingerprint()
    {
        Assert.Equal(ContentFingerprint.Compute(BaseGameContent.Create()), ContentFingerprint.Compute(BaseGameContent.Create()));
    }

    [Fact]
    public void AnyChangeInTheContentChangesTheFingerprint()
    {
        var gp = new RoomDefinition("gp", "GP's office", 4, 1, new WallSet(1, 2, 3, 4), new Dictionary<string, int> { ["desk"] = 1 });
        var fingerprint = ContentFingerprint.Compute(new GameContent([gp], [7]));

        Assert.NotEqual(fingerprint, ContentFingerprint.Compute(new GameContent([gp with { MinimumSize = 3 }], [7])));
        Assert.NotEqual(fingerprint, ContentFingerprint.Compute(new GameContent([gp with { NeededObjects = new Dictionary<string, int> { ["desk"] = 2 } }], [7])));
        Assert.NotEqual(fingerprint, ContentFingerprint.Compute(new GameContent([gp], [8])));
        Assert.NotEqual(fingerprint, ContentFingerprint.Compute(new GameContent([gp], [7]) { SwingDoor = new(new(1, 0), new(2, 0), new(3, 0), new(4, 0)) }));
        Assert.NotEqual(fingerprint, ContentFingerprint.Compute(BaseGameContent.Create()));
    }

    [Fact]
    public void DictionariesAndSetsDoNotDependOnTheirOrder()
    {
        var ab = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var ba = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };

        Assert.Equal(ContentFingerprint.Describe(ab), ContentFingerprint.Describe(ba));
        Assert.Equal(ContentFingerprint.Describe(new HashSet<int> { 1, 2, 3 }), ContentFingerprint.Describe(new HashSet<int> { 3, 2, 1 }));
        Assert.NotEqual(ContentFingerprint.Describe(new[] { 1, 2 }), ContentFingerprint.Describe(new[] { 2, 1 }));
    }
}
