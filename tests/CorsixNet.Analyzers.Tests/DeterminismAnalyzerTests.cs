using static CorsixNet.Analyzers.DeterminismAnalyzer;

namespace CorsixNet.Analyzers.Tests;

public class DeterminismAnalyzerTests
{
    [Fact]
    public async Task IsSilentWhenProjectIsNotDeterministic()
    {
        const string source = """
            class C
            {
                double M() => new System.Random().NextDouble();
            }
            """;

        Assert.Empty(await AnalyzerHarness.AnalyzeAsync(source, enabled: false));
    }

    [Fact]
    public async Task AcceptsIntegerCodeAndOrderedCollections()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                private readonly List<int> _items = new();
                private readonly SortedDictionary<int, string> _names = new();
                private readonly Dictionary<int, string> _lookup = new();
                private readonly decimal _money = 1.5m;

                int Sum()
                {
                    var total = System.Math.Max(0, 1);
                    foreach (var item in _items) { total += item; }
                    foreach (var pair in _names) { total += pair.Key; }
                    if (_lookup.TryGetValue(1, out var name)) { total += name.Length; }
                    total += _lookup.Count + 7.GetHashCode();
                    return total + _lookup.Keys.Count();
                }
            }
            """;

        Assert.Empty(await AnalyzerHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task ReportsRandomAtItsDeclarationOnly()
    {
        const string source = """
            class C
            {
                private readonly System.Random _random = new();
                int Roll() => _random.Next(6);
            }
            """;

        Assert.Equal([(BannedApiId, "_random")], await AnalyzerHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task ReportsWallClockAndIdentityApis()
    {
        const string source = """
            class C
            {
                long A() => System.DateTime.Now.Ticks;
                int B() => System.Environment.TickCount;
                System.Guid G() => System.Guid.NewGuid();
                int H(string s, object o) => s.GetHashCode() + o.GetHashCode();
                int R() => new System.Random(1).Next();
            }
            """;

        Assert.Equal(
            [
                (BannedApiId, "System.DateTime.Now"),
                (BannedApiId, "System.Environment.TickCount"),
                (BannedApiId, "System.Guid.NewGuid()"),
                (BannedApiId, "s.GetHashCode()"),
                (BannedApiId, "o.GetHashCode()"),
                (BannedApiId, "new System.Random(1)"),
            ],
            await AnalyzerHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task ReportsAsyncMethods()
    {
        const string source = """
            class C
            {
                async System.Threading.Tasks.Task Run() => await System.Threading.Tasks.Task.Yield();
            }
            """;

        Assert.Equal(
            [
                (BannedApiId, "Run"),
                (BannedApiId, "System.Threading.Tasks.Task.Yield()"),
            ],
            await AnalyzerHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task ReportsFloatingPoint()
    {
        const string source = """
            using System.Collections.Generic;

            class C
            {
                private double _speed;
                private readonly List<float> _weights = new();

                int Scale(int value)
                {
                    var factor = 1.5;
                    var root = System.Math.Sqrt(value);
                    return (int)(value * factor) + (int)root + (int)(double)value;
                }
            }
            """;

        Assert.Equal(
            [
                (FloatingPointId, "_speed"),
                (FloatingPointId, "_weights"),
                (FloatingPointId, "factor = 1.5"),
                (FloatingPointId, "root = System.Math.Sqrt(value)"),
                (FloatingPointId, "System.Math.Sqrt(value)"),
                (FloatingPointId, "(double)value"),
            ],
            await AnalyzerHarness.AnalyzeAsync(source));
    }

    [Fact]
    public async Task ReportsIterationOverUnorderedCollections()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                int Walk(Dictionary<int, int> map, HashSet<int> set)
                {
                    var total = 0;
                    foreach (var pair in map) { total += pair.Value; }
                    foreach (var key in map.Keys) { total += key; }
                    total += map.Select(p => p.Key).First();
                    total += new List<int>(set).Count;
                    total += set.Count() + (set.Any() ? 1 : 0) + map.OrderBy(p => p.Key).First().Key;
                    return total;
                }
            }
            """;

        Assert.Equal(
            [
                (UnorderedIterationId, "map"),
                (UnorderedIterationId, "map.Keys"),
                (UnorderedIterationId, "map"),
                (UnorderedIterationId, "set"),
            ],
            await AnalyzerHarness.AnalyzeAsync(source));
    }
}
