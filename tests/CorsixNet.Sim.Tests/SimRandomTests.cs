namespace CorsixNet.Sim.Tests;

public class SimRandomTests
{
    [Fact]
    public void MatchesPcg32ReferenceOutput()
    {
        // First outputs of pcg32-demo from the PCG reference implementation (seed 42, sequence 54).
        var random = new SimRandom(42, 54);

        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        var actual = expected.Select(_ => random.NextUInt32()).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RestoredStateContinuesTheSameSequence()
    {
        var original = new SimRandom(1234, 7);
        original.NextUInt32();
        original.NextUInt32();

        var restored = SimRandom.FromState(original.State, original.Increment);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(original.NextUInt32(), restored.NextUInt32());
        }
    }

    [Fact]
    public void FromStateRejectsEvenIncrement()
    {
        Assert.Throws<ArgumentException>(() => SimRandom.FromState(1, 2));
    }

    [Fact]
    public void NextStaysInRangeAndCoversIt()
    {
        var random = new SimRandom(99);
        var counts = new int[6];

        for (var i = 0; i < 6000; i++)
        {
            counts[random.Next(6)]++;
        }

        Assert.All(counts, count => Assert.InRange(count, 850, 1150));
    }

    [Fact]
    public void NextInclusiveHandlesBoundsAndFullRange()
    {
        var random = new SimRandom(5);

        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(random.NextInclusive(-3, 3), -3, 3);
        }

        Assert.Equal(17, random.NextInclusive(17, 17));
        random.NextInclusive(int.MinValue, int.MaxValue);
    }

    [Fact]
    public void ChanceRespectsExtremes()
    {
        var random = new SimRandom(8);

        for (var i = 0; i < 100; i++)
        {
            Assert.False(random.Chance(0, 4));
            Assert.True(random.Chance(4, 4));
        }
    }

    [Fact]
    public void RejectsInvalidArguments()
    {
        var random = new SimRandom(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInclusive(2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Chance(1, 0));
    }
}
