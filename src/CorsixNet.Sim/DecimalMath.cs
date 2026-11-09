// Normally distributed random numbers, ported from CorsixTH CorsixTH/Lua/app.lua (math.n_random, math.round).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Sim;

/// <summary>Square roots, logarithms and cosines in decimals, so that the simulation stays deterministic.</summary>
internal static class DecimalMath
{
    /// <summary>π to the precision of a decimal.</summary>
    internal const decimal Pi = 3.1415926535897932384626433833m;

    private const decimal Ln2 = 0.6931471805599453094172321215m;

    /// <summary>The steps of the uniform numbers that <see cref="NormalRandom"/> draws.</summary>
    private const int UniformSteps = 1_000_000;

    /// <summary>A square root by Newton's method; 0 for zero or less.</summary>
    internal static decimal Sqrt(decimal value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var guess = value >= 1 ? value : 1;
        for (var i = 0; i < 100; i++)
        {
            var next = (guess + (value / guess)) / 2;
            if (next == guess)
            {
                break;
            }

            guess = next;
        }

        return guess;
    }

    /// <summary>The natural logarithm of a positive number.</summary>
    internal static decimal Ln(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        if (value < 1)
        {
            return -Ln(1 / value);
        }

        // value = 2^halvings * rest, rest in [1, 2); ln(rest) = 2 atanh((rest - 1) / (rest + 1)).
        var halvings = 0;
        while (value >= 2)
        {
            value /= 2;
            halvings++;
        }

        var t = (value - 1) / (value + 1);
        var square = t * t;
        var term = t;
        var sum = 0m;
        for (var n = 1; n < 200 && term != 0; n += 2)
        {
            sum += term / n;
            term *= square;
        }

        return (halvings * Ln2) + (2 * sum);
    }

    /// <summary>The cosine of an angle in radians.</summary>
    internal static decimal Cos(decimal angle)
    {
        const decimal twoPi = 2 * Pi;
        angle %= twoPi;
        if (angle > Pi)
        {
            angle -= twoPi;
        }
        else if (angle < -Pi)
        {
            angle += twoPi;
        }

        var square = angle * angle;
        var term = 1m;
        var sum = 1m;
        for (var n = 1; n < 100; n++)
        {
            term = -term * square / ((2 * n) - 1) / (2 * n);
            if (term == 0)
            {
                break;
            }

            sum += term;
        }

        return sum;
    }

    /// <summary>
    /// <c>math.n_random(mean, variance)</c>: a normally distributed number by the Box-Muller transform. As in CorsixTH,
    /// <paramref name="variance"/> is used as the standard deviation.
    /// </summary>
    internal static decimal NormalRandom(SimRandom random, decimal mean, decimal variance)
    {
        // math.random() is in [0, 1); 1 - it keeps the logarithm finite.
        var u1 = (random.Next(UniformSteps) + 1) / (decimal)UniformSteps;
        var u2 = random.Next(UniformSteps) / (decimal)UniformSteps;
        return mean + (Sqrt(-2 * Ln(u1)) * Cos(2 * Pi * u2) * variance);
    }

    /// <summary><c>math.round</c>: <c>math.floor(x + 0.5)</c>.</summary>
    internal static long Round(decimal value) => (long)Math.Floor(value + 0.5m);
}
