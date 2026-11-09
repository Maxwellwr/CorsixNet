namespace CorsixNet.Sim.Tests;

/// <summary>The decimal maths behind CorsixTH's math.n_random and math.round (CorsixTH/Lua/app.lua).</summary>
public class DecimalMathTests
{
    [Theory]
    [InlineData("1", "0")]
    [InlineData("2", "0.693147180559945")]
    [InlineData("0.000001", "-13.815510557964274")]
    [InlineData("1000", "6.907755278982137")]
    public void LogarithmsAreAccurate(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Math.Round(DecimalMath.Ln(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)), 15));

    [Fact]
    public void CosinesAreAccurate()
    {
        Assert.Equal(1m, Math.Round(DecimalMath.Cos(0), 15));
        Assert.Equal(-1m, Math.Round(DecimalMath.Cos(DecimalMath.Pi), 15));
        Assert.Equal(0m, Math.Round(DecimalMath.Cos(DecimalMath.Pi / 2), 15));
        Assert.Equal(0.5m, Math.Round(DecimalMath.Cos(5 * DecimalMath.Pi / 3), 15));
        Assert.Equal(-0.5m, Math.Round(DecimalMath.Cos(-2 * DecimalMath.Pi / 3), 15));
    }

    [Fact]
    public void NormalNumbersHaveTheirMeanAndDeviation()
    {
        var random = new SimRandom(7);
        const int Count = 20_000;
        var values = Enumerable.Range(0, Count).Select(_ => DecimalMath.NormalRandom(random, 180, 30)).ToList();
        var mean = values.Sum() / Count;
        var deviation = DecimalMath.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / Count);

        Assert.InRange(mean, 179m, 181m);
        Assert.InRange(deviation, 29m, 31m);
    }

    [Fact]
    public void RoundingGoesHalfUp()
    {
        Assert.Equal(3, DecimalMath.Round(2.5m));
        Assert.Equal(-2, DecimalMath.Round(-2.5m));
        Assert.Equal(2, DecimalMath.Round(2.49m));
    }
}
