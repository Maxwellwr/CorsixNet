// What a campaign file holds, ported from CorsixTH CorsixTH/Lua/app.lua (App:readCampaignFile, App:loadCampaign),
// CorsixTH/Lua/strings.lua (Strings:getLocalisedText) and CorsixTH/Campaigns/example.campaign.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;

namespace CorsixNet.Formats;

/// <summary>
/// A CorsixTH campaign (<c>.campaign</c>): a name, a description, the level files to play in order, the text shown on
/// winning the last one and whether the level advance movies play.
/// </summary>
/// <remarks>
/// CorsixTH runs the file as Lua. Campaign files only assign strings (joined with <c>..</c>), numbers, booleans and
/// tables of them to names and table fields, so this reads that much of Lua and refuses anything else.
/// </remarks>
public sealed record CustomCampaign(string? Name, string? Description, IReadOnlyList<string> Levels, string? WinningText, bool Movie)
{
    /// <summary>
    /// Reads a campaign file, with the description and winning text in <paramref name="language"/> where the file gives
    /// them (<c>description_table</c>, <c>winning_text_table</c>), else in English, else the plain ones.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a campaign file of the kind CorsixTH ships.</exception>
    public static CustomCampaign Parse(string text, string language = "en")
    {
        var globals = new LuaReader(text).ReadChunk();
        string? Localised(string plain, string table)
        {
            var value = globals.GetValueOrDefault(plain) as string;
            if (globals.GetValueOrDefault(table) is not LuaTable localised)
            {
                return value;
            }

            return localised.Fields.GetValueOrDefault(language) as string ?? localised.Fields.GetValueOrDefault("en") as string ?? value;
        }

        var levels = globals.GetValueOrDefault("levels") is LuaTable table ? table.Items.OfType<string>().ToList() : [];
        return new CustomCampaign(
            globals.GetValueOrDefault("name") as string,
            Localised("description", "description_table"),
            levels,
            Localised("winning_text", "winning_text_table"),
            globals.GetValueOrDefault("movie") is true);
    }

    /// <summary>A Lua table: its list part (keys 1, 2, ...) and its named fields.</summary>
    private sealed class LuaTable
    {
        public List<object?> Items { get; } = [];

        public Dictionary<string, object?> Fields { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Reads assignments of constants and table constructors, the part of Lua campaign files use.</summary>
    private sealed class LuaReader(string text)
    {
        private int _position;

        public Dictionary<string, object?> ReadChunk()
        {
            var globals = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (true)
            {
                SkipSpace();
                if (_position >= text.Length)
                {
                    return globals;
                }

                if (Accept(';'))
                {
                    continue;
                }

                // name { . name | [ "key" ] } = expression
                var path = new List<string> { ReadName() };
                while (true)
                {
                    SkipSpace();
                    if (Accept('.'))
                    {
                        path.Add(ReadName());
                    }
                    else if (Peek() == '[' && PeekAt(1) != '[' && PeekAt(1) != '=')
                    {
                        _position++;
                        SkipSpace();
                        path.Add(ReadExpression() is string key ? key : throw Error("Only string keys are read"));
                        Expect(']');
                    }
                    else
                    {
                        break;
                    }
                }

                Expect('=');
                Assign(globals, path, ReadExpression());
            }
        }

        private void Assign(Dictionary<string, object?> globals, List<string> path, object? value)
        {
            if (path.Count == 1)
            {
                globals[path[0]] = value;
                return;
            }

            var table = globals.GetValueOrDefault(path[0]) as LuaTable ?? throw Error($"{path[0]} is not a table");
            for (var i = 1; i < path.Count - 1; i++)
            {
                table = table.Fields.GetValueOrDefault(path[i]) as LuaTable ?? throw Error($"{path[i]} is not a table");
            }

            table.Fields[path[^1]] = value;
        }

        private object? ReadExpression()
        {
            var value = ReadTerm();
            SkipSpace();
            while (Peek() == '.' && PeekAt(1) == '.')
            {
                _position += 2;
                var right = ReadTerm();
                value = ToConcatenable(value) + ToConcatenable(right);
                SkipSpace();
            }

            return value;
        }

        private string ToConcatenable(object? value) => value switch
        {
            string text => text,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => throw Error("Only strings and numbers can be joined"),
        };

        private object? ReadTerm()
        {
            SkipSpace();
            var c = Peek();
            if (c is '"' or '\'')
            {
                return ReadQuoted(c);
            }

            if (c == '[' && (PeekAt(1) == '[' || PeekAt(1) == '='))
            {
                return ReadLongString();
            }

            if (c == '{')
            {
                return ReadTable();
            }

            if (char.IsAsciiDigit(c) || (c == '-' && char.IsAsciiDigit(PeekAt(1))))
            {
                var start = _position++;
                while (char.IsAsciiDigit(Peek()) || Peek() == '.' && PeekAt(1) != '.')
                {
                    _position++;
                }

                return decimal.Parse(text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            var name = ReadName();
            return name switch
            {
                "true" => true,
                "false" => false,
                "nil" => null,
                _ => throw Error($"Unexpected {name}"),
            };
        }

        private LuaTable ReadTable()
        {
            Expect('{');
            var table = new LuaTable();
            while (true)
            {
                SkipSpace();
                if (Accept('}'))
                {
                    return table;
                }

                var start = _position;
                if (char.IsAsciiLetter(Peek()) || Peek() == '_')
                {
                    var name = ReadName();
                    SkipSpace();
                    if (Peek() == '=' && PeekAt(1) != '=')
                    {
                        _position++;
                        table.Fields[name] = ReadExpression();
                    }
                    else
                    {
                        _position = start;
                        table.Items.Add(ReadExpression());
                    }
                }
                else if (Peek() == '[' && PeekAt(1) != '[' && PeekAt(1) != '=')
                {
                    _position++;
                    var key = ReadExpression() as string ?? throw Error("Only string keys are read");
                    Expect(']');
                    Expect('=');
                    table.Fields[key] = ReadExpression();
                }
                else
                {
                    table.Items.Add(ReadExpression());
                }

                SkipSpace();
                if (!Accept(',') && !Accept(';'))
                {
                    Expect('}');
                    return table;
                }
            }
        }

        private string ReadQuoted(char quote)
        {
            _position++;
            var result = new StringBuilder();
            while (true)
            {
                if (_position >= text.Length || text[_position] == '\n')
                {
                    throw Error("Unfinished string");
                }

                var c = text[_position++];
                if (c == quote)
                {
                    return result.ToString();
                }

                if (c != '\\')
                {
                    result.Append(c);
                    continue;
                }

                var escaped = _position < text.Length ? text[_position++] : throw Error("Unfinished string");
                result.Append(escaped switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '\n' => '\n',
                    '\\' or '"' or '\'' => escaped,
                    _ => throw Error($"Escape \\{escaped} is not read"),
                });
            }
        }

        private string ReadLongString()
        {
            var level = LongBracketLevel() ?? throw Error("Bad long string");
            var close = "]" + new string('=', level) + "]";
            var end = text.IndexOf(close, _position, StringComparison.Ordinal);
            if (end < 0)
            {
                throw Error("Unfinished long string");
            }

            var content = text[_position..end];
            _position = end + close.Length;

            // A line break straight after the opening bracket is not part of the string.
            return content.StartsWith("\r\n", StringComparison.Ordinal) ? content[2..] : content.StartsWith('\n') ? content[1..] : content;
        }

        /// <summary>At <c>[[</c> or <c>[==[</c>: its level, with the position moved past it; null otherwise.</summary>
        private int? LongBracketLevel()
        {
            if (Peek() != '[')
            {
                return null;
            }

            var level = 0;
            while (PeekAt(level + 1) == '=')
            {
                level++;
            }

            if (PeekAt(level + 1) != '[')
            {
                return null;
            }

            _position += level + 2;
            return level;
        }

        private string ReadName()
        {
            SkipSpace();
            var start = _position;
            while (_position < text.Length && (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] == '_'))
            {
                _position++;
            }

            return _position > start && !char.IsAsciiDigit(text[start]) ? text[start.._position] : throw Error("Expected a name");
        }

        private void SkipSpace()
        {
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position]))
                {
                    _position++;
                }
                else if (Peek() == '-' && PeekAt(1) == '-')
                {
                    _position += 2;
                    if (LongBracketLevel() is { } level)
                    {
                        var close = "]" + new string('=', level) + "]";
                        var end = text.IndexOf(close, _position, StringComparison.Ordinal);
                        _position = end < 0 ? text.Length : end + close.Length;
                    }
                    else
                    {
                        var end = text.IndexOf('\n', _position);
                        _position = end < 0 ? text.Length : end + 1;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private char Peek() => PeekAt(0);

        private char PeekAt(int offset) => _position + offset < text.Length ? text[_position + offset] : '\0';

        private bool Accept(char c)
        {
            if (Peek() != c)
            {
                return false;
            }

            _position++;
            return true;
        }

        private void Expect(char c)
        {
            SkipSpace();
            if (!Accept(c))
            {
                throw Error($"Expected '{c}'");
            }
        }

        private InvalidDataException Error(string message)
        {
            var line = 1 + text.AsSpan(0, Math.Min(_position, text.Length)).Count('\n');
            return new InvalidDataException($"{message} on line {line} of the campaign file.");
        }
    }
}
