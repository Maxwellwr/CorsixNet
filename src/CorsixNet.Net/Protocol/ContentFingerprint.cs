using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CorsixNet.Sim.Definitions;

namespace CorsixNet.Net.Protocol;

/// <summary>
/// A hash of everything in a <see cref="GameContent"/>: the rooms, objects and animations the simulation is defined
/// by. A client joins only when its content's fingerprint is the server's, since different content would desync at
/// once.
/// </summary>
/// <remarks>
/// The content is walked by its fields (including the backing fields of properties) in name order, collections in
/// their order, dictionaries and sets sorted, so the result does not depend on hash ordering or the platform.
/// </remarks>
public static class ContentFingerprint
{
    /// <summary>Deeper than any content definition nests.</summary>
    private const int MaxDepth = 32;

    public static ulong Compute(GameContent content)
    {
        var text = new StringBuilder();
        Write(text, content, depth: 0);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return BitConverter.ToUInt64(hash, 0);
    }

    /// <summary>The canonical text the fingerprint hashes; for tests.</summary>
    internal static string Describe(object? value)
    {
        var text = new StringBuilder();
        Write(text, value, depth: 0);
        return text.ToString();
    }

    private static void Write(StringBuilder text, object? value, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidOperationException("Game content nests too deeply to fingerprint.");
        }

        switch (value)
        {
            case null:
                text.Append('~');
                return;
            case string s:
                text.Append('"').Append(s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                return;
            case Enum e:
                text.Append(e.GetType().Name).Append('.').Append(e.ToString());
                return;
            case IFormattable formattable when value.GetType().IsPrimitive || value is decimal:
                text.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return;
            case bool b:
                text.Append(b ? "true" : "false");
                return;
            case char c:
                text.Append('\'').Append(c).Append('\'');
                return;
            case Delegate:
                text.Append("fn");
                return;
            case MemberInfo member:
                text.Append(member.Name);
                return;
            case IDictionary dictionary:
                WriteSorted(text, dictionary.Keys.Cast<object?>().Select(key => Describe(key, depth) + ":" + Describe(dictionary[key!], depth)), '{', '}');
                return;
            case IEnumerable sequence when IsSet(value.GetType()):
                WriteSorted(text, sequence.Cast<object?>().Select(item => Describe(item, depth)), '{', '}');
                return;
            case IEnumerable sequence:
                text.Append('[');
                foreach (var item in sequence)
                {
                    Write(text, item, depth + 1);
                    text.Append(',');
                }

                text.Append(']');
                return;
        }

        var type = value.GetType();
        text.Append(type.Name).Append('(');
        foreach (var field in Fields(type))
        {
            text.Append(field.Name).Append('=');
            Write(text, field.GetValue(value), depth + 1);
            text.Append(';');
        }

        text.Append(')');
    }

    private static string Describe(object? value, int depth)
    {
        var text = new StringBuilder();
        Write(text, value, depth + 1);
        return text.ToString();
    }

    private static void WriteSorted(StringBuilder text, IEnumerable<string> items, char open, char close)
    {
        text.Append(open);
        foreach (var item in items.Order(StringComparer.Ordinal))
        {
            text.Append(item).Append(',');
        }

        text.Append(close);
    }

    private static bool IsSet(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ISet<>) || i.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)));

    /// <summary>The instance fields of the type and its base types, by name, other than delegates.</summary>
    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        var fields = new List<FieldInfo>();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            fields.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !field.FieldType.IsAssignableTo(typeof(Delegate))));
        }

        return fields.OrderBy(field => field.DeclaringType!.Name, StringComparer.Ordinal).ThenBy(field => field.Name, StringComparer.Ordinal);
    }
}
