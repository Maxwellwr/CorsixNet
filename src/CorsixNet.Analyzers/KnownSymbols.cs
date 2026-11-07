using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace CorsixNet.Analyzers;

/// <summary>
/// Framework symbols the determinism rules refer to, resolved once per compilation.
/// </summary>
internal sealed class KnownSymbols
{
    private const string WallClock = "wall-clock time differs between machines; use simulation ticks";
    private const string Randomness = "unseeded or implementation-defined randomness; use SimRandom";
    private const string Concurrency = "concurrent execution order is not deterministic";

    private readonly Dictionary<ISymbol, string> _bannedTypes = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, string> _bannedMembers = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _unorderedTypes = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _enumerableInterfaces = new(SymbolEqualityComparer.Default);
    private readonly INamedTypeSymbol? _enumerable;

    /// <summary>
    /// LINQ operators whose result does not depend on the order of the source.
    /// </summary>
    private static readonly HashSet<string> OrderIndependentLinqMethods = new(StringComparer.Ordinal)
    {
        "All", "Any", "Contains", "Count", "LongCount", "Max", "Min", "Sum",
        "OrderBy", "OrderByDescending", "ToDictionary", "ToHashSet",
    };

    public KnownSymbols(Compilation compilation)
    {
        AddType(compilation, "System.Random", Randomness);
        AddType(compilation, "System.Security.Cryptography.RandomNumberGenerator", Randomness);
        AddType(compilation, "System.HashCode", "hash codes are seeded randomly per process");
        AddType(compilation, "System.DateTime", WallClock);
        AddType(compilation, "System.DateTimeOffset", WallClock);
        AddType(compilation, "System.TimeProvider", WallClock);
        AddType(compilation, "System.Diagnostics.Stopwatch", WallClock);
        AddType(compilation, "System.Threading.Thread", Concurrency);
        AddType(compilation, "System.Threading.ThreadPool", Concurrency);
        AddType(compilation, "System.Threading.Timer", Concurrency);
        AddType(compilation, "System.Timers.Timer", Concurrency);
        AddType(compilation, "System.Threading.Tasks.Task", Concurrency);
        AddType(compilation, "System.Threading.Tasks.Task`1", Concurrency);
        AddType(compilation, "System.Threading.Tasks.ValueTask", Concurrency);
        AddType(compilation, "System.Threading.Tasks.ValueTask`1", Concurrency);
        AddType(compilation, "System.Threading.Tasks.Parallel", Concurrency);

        AddMember(compilation, "System.Environment", "TickCount", WallClock);
        AddMember(compilation, "System.Environment", "TickCount64", WallClock);
        AddMember(compilation, "System.Guid", "NewGuid", Randomness);
        AddMember(compilation, "System.Guid", "CreateVersion7", Randomness);

        AddUnordered(compilation, "System.Collections.Hashtable");
        AddUnordered(compilation, "System.Collections.Generic.Dictionary`2");
        AddUnordered(compilation, "System.Collections.Generic.Dictionary`2+KeyCollection");
        AddUnordered(compilation, "System.Collections.Generic.Dictionary`2+ValueCollection");
        AddUnordered(compilation, "System.Collections.Generic.HashSet`1");
        AddUnordered(compilation, "System.Collections.Generic.IDictionary`2");
        AddUnordered(compilation, "System.Collections.Generic.IReadOnlyDictionary`2");
        AddUnordered(compilation, "System.Collections.Generic.ISet`1");
        AddUnordered(compilation, "System.Collections.Generic.IReadOnlySet`1");
        AddUnordered(compilation, "System.Collections.Concurrent.ConcurrentDictionary`2");
        AddUnordered(compilation, "System.Collections.Concurrent.ConcurrentBag`1");
        AddUnordered(compilation, "System.Collections.Immutable.ImmutableDictionary`2");
        AddUnordered(compilation, "System.Collections.Immutable.ImmutableHashSet`1");
        AddUnordered(compilation, "System.Collections.Frozen.FrozenDictionary`2");
        AddUnordered(compilation, "System.Collections.Frozen.FrozenSet`1");

        AddEnumerableInterface(compilation, "System.Collections.IEnumerable");
        AddEnumerableInterface(compilation, "System.Collections.ICollection");
        AddEnumerableInterface(compilation, "System.Collections.Generic.IEnumerable`1");
        AddEnumerableInterface(compilation, "System.Collections.Generic.ICollection`1");
        AddEnumerableInterface(compilation, "System.Collections.Generic.IReadOnlyCollection`1");

        _enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
    }

    public string? GetBannedTypeReason(ITypeSymbol type)
    {
        return _bannedTypes.TryGetValue(type.OriginalDefinition, out var reason) ? reason : null;
    }

    public string? GetBannedMemberReason(ISymbol member)
    {
        return _bannedMembers.TryGetValue(member.OriginalDefinition, out var reason) ? reason : null;
    }

    /// <summary>
    /// Finds a banned type anywhere in <paramref name="type"/>, including
    /// array elements and generic type arguments.
    /// </summary>
    public (ITypeSymbol Type, string Reason)? FindBannedType(ITypeSymbol type)
    {
        var found = FindInTypeTree(type, t => GetBannedTypeReason(t) is not null);
        return found is null ? null : (found, GetBannedTypeReason(found)!);
    }

    public bool IsUnordered(ITypeSymbol type)
    {
        return _unorderedTypes.Contains(type.OriginalDefinition);
    }

    public bool IsEnumerableInterface(ITypeSymbol type)
    {
        return _enumerableInterfaces.Contains(type.OriginalDefinition);
    }

    public bool IsNonDeterministicGetHashCode(IMethodSymbol method)
    {
        return method is { Name: nameof(GetHashCode), ContainingType.SpecialType: SpecialType.System_Object or SpecialType.System_String or SpecialType.System_ValueType };
    }

    public bool IsOrderIndependentLinqMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return SymbolEqualityComparer.Default.Equals(definition.ContainingType, _enumerable)
            && OrderIndependentLinqMethods.Contains(definition.Name);
    }

    public static ITypeSymbol? FindInTypeTree(ITypeSymbol type, Func<ITypeSymbol, bool> predicate)
    {
        if (predicate(type))
        {
            return type;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindInTypeTree(array.ElementType, predicate);
            case IPointerTypeSymbol pointer:
                return FindInTypeTree(pointer.PointedAtType, predicate);
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                {
                    if (FindInTypeTree(argument, predicate) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private void AddType(Compilation compilation, string metadataName, string reason)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is { } type)
        {
            _bannedTypes[type] = reason;
        }
    }

    private void AddMember(Compilation compilation, string typeMetadataName, string memberName, string reason)
    {
        if (compilation.GetTypeByMetadataName(typeMetadataName) is not { } type)
        {
            return;
        }

        foreach (var member in type.GetMembers(memberName))
        {
            _bannedMembers[member] = reason;
        }
    }

    private void AddUnordered(Compilation compilation, string metadataName)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is { } type)
        {
            _unorderedTypes.Add(type);
        }
    }

    private void AddEnumerableInterface(Compilation compilation, string metadataName)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is { } type)
        {
            _enumerableInterfaces.Add(type);
        }
    }
}
