// A syntax reader for the Lua that CorsixTH's language files (CorsixTH/Lua/languages/*.lua) are written in: their
// assignments, calls and table constructors are kept; local functions and control blocks are stepped over.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;

namespace CorsixNet.Formats;

/// <summary>A Lua expression as written.</summary>
public abstract record LuaExpression;

/// <summary>A string literal.</summary>
public sealed record LuaStringLiteral(string Value) : LuaExpression;

/// <summary>A number literal.</summary>
public sealed record LuaNumberLiteral(decimal Value) : LuaExpression;

/// <summary><c>true</c> or <c>false</c>.</summary>
public sealed record LuaBooleanLiteral(bool Value) : LuaExpression;

/// <summary><c>nil</c>.</summary>
public sealed record LuaNilLiteral : LuaExpression;

/// <summary><c>...</c>, the chunk's arguments.</summary>
public sealed record LuaVarArgs : LuaExpression;

/// <summary>A name: a local or a global.</summary>
public sealed record LuaName(string Name) : LuaExpression;

/// <summary><c>target[key]</c>, or <c>target.key</c> with a string key.</summary>
public sealed record LuaIndex(LuaExpression Target, LuaExpression Key) : LuaExpression;

/// <summary>A call, <c>function(arguments)</c>, <c>function "text"</c> or <c>function { ... }</c>.</summary>
public sealed record LuaCall(LuaExpression Function, IReadOnlyList<LuaExpression> Arguments) : LuaExpression;

/// <summary>A binary operation, such as <c>..</c>.</summary>
public sealed record LuaBinary(string Operator, LuaExpression Left, LuaExpression Right) : LuaExpression;

/// <summary>A unary operation: <c>not</c>, <c>-</c> or <c>#</c>.</summary>
public sealed record LuaUnary(string Operator, LuaExpression Operand) : LuaExpression;

/// <summary>A function body, which is not read.</summary>
public sealed record LuaFunction : LuaExpression;

/// <summary>A table constructor's field: <c>[key] = value</c>, <c>name = value</c> (a string key), or a list item (no key).</summary>
public sealed record LuaField(LuaExpression? Key, LuaExpression Value);

/// <summary>A table constructor.</summary>
public sealed record LuaTableConstructor(IReadOnlyList<LuaField> Fields) : LuaExpression;

/// <summary>A statement of the chunk's top level.</summary>
public abstract record LuaStatement(int Line);

/// <summary><c>targets = values</c>, or <c>local names = values</c>.</summary>
public sealed record LuaAssignment(int Line, IReadOnlyList<LuaExpression> Targets, IReadOnlyList<LuaExpression> Values, bool IsLocal) : LuaStatement(Line);

/// <summary>A call made as a statement.</summary>
public sealed record LuaCallStatement(int Line, LuaCall Call) : LuaStatement(Line);

/// <summary>A function definition or a control block (<c>do</c>, <c>if</c>, <c>for</c>, <c>while</c>, <c>repeat</c>), stepped over.</summary>
public sealed record LuaSkippedBlock(int Line) : LuaStatement(Line);

/// <summary>Reads a Lua chunk's top-level statements.</summary>
public static class LuaChunk
{
    /// <summary>The chunk's top-level statements, in order.</summary>
    /// <exception cref="InvalidDataException">The text is not Lua this reader understands.</exception>
    public static IReadOnlyList<LuaStatement> Parse(string text) => new LuaChunkParser(LuaTokenizer.Tokenize(text)).ParseChunk();
}

internal enum LuaTokenKind
{
    Name,
    String,
    Number,
    Symbol,
    End,
}

internal readonly record struct LuaToken(LuaTokenKind Kind, string Text, int Line)
{
    public bool Is(string symbolOrKeyword) => Kind is LuaTokenKind.Symbol or LuaTokenKind.Name && Text == symbolOrKeyword;
}

internal static class LuaTokenizer
{
    private static readonly string[] Symbols = ["...", "..", "==", "~=", "<=", ">=", "::", "//", "<<", ">>"];

    public static List<LuaToken> Tokenize(string text)
    {
        var tokens = new List<LuaToken>();
        var position = 0;
        var line = 1;
        while (true)
        {
            // Spaces and comments.
            while (position < text.Length)
            {
                var c = text[position];
                if (c == '\n')
                {
                    line++;
                    position++;
                }
                else if (char.IsWhiteSpace(c) || c == '﻿')
                {
                    position++;
                }
                else if (c == '-' && At(text, position + 1) == '-')
                {
                    position += 2;
                    if (LongBracket(text, position) is { } level)
                    {
                        var (_, end) = ReadLong(text, position, level, line);
                        line += Lines(text, position, end);
                        position = end;
                    }
                    else
                    {
                        while (position < text.Length && text[position] != '\n')
                        {
                            position++;
                        }
                    }
                }
                else
                {
                    break;
                }
            }

            if (position >= text.Length)
            {
                tokens.Add(new LuaToken(LuaTokenKind.End, string.Empty, line));
                return tokens;
            }

            var start = position;
            var first = text[position];
            if (char.IsAsciiLetter(first) || first == '_')
            {
                while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
                {
                    position++;
                }

                tokens.Add(new LuaToken(LuaTokenKind.Name, text[start..position], line));
            }
            else if (char.IsAsciiDigit(first) || (first == '.' && char.IsAsciiDigit(At(text, position + 1))))
            {
                if (first == '0' && At(text, position + 1) is 'x' or 'X')
                {
                    position += 2;
                    while (char.IsAsciiHexDigit(At(text, position)))
                    {
                        position++;
                    }

                    var hex = long.Parse(text.AsSpan(start + 2, position - start - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    tokens.Add(new LuaToken(LuaTokenKind.Number, hex.ToString(CultureInfo.InvariantCulture), line));
                    continue;
                }

                while (char.IsAsciiDigit(At(text, position)) || (At(text, position) == '.' && At(text, position + 1) != '.'))
                {
                    position++;
                }

                if (At(text, position) is 'e' or 'E')
                {
                    position++;
                    if (At(text, position) is '+' or '-')
                    {
                        position++;
                    }

                    while (char.IsAsciiDigit(At(text, position)))
                    {
                        position++;
                    }
                }

                tokens.Add(new LuaToken(LuaTokenKind.Number, text[start..position], line));
            }
            else if (first is '"' or '\'')
            {
                var (value, end) = ReadQuoted(text, position, line);
                tokens.Add(new LuaToken(LuaTokenKind.String, value, line));
                line += Lines(text, position, end);
                position = end;
            }
            else if (first == '[' && LongBracket(text, position) is { } level)
            {
                var (value, end) = ReadLong(text, position, level, line);
                tokens.Add(new LuaToken(LuaTokenKind.String, value, line));
                line += Lines(text, position, end);
                position = end;
            }
            else
            {
                var symbol = Symbols.FirstOrDefault(s => string.CompareOrdinal(text, position, s, 0, s.Length) == 0) ?? first.ToString();
                position += symbol.Length;
                tokens.Add(new LuaToken(LuaTokenKind.Symbol, symbol, line));
            }
        }
    }

    private static char At(string text, int position) => position < text.Length ? text[position] : '\0';

    private static int Lines(string text, int start, int end) => text.AsSpan(start, end - start).Count('\n');

    /// <summary>At <c>[[</c> or <c>[==[</c>: its level; null otherwise.</summary>
    private static int? LongBracket(string text, int position)
    {
        if (At(text, position) != '[')
        {
            return null;
        }

        var level = 0;
        while (At(text, position + 1 + level) == '=')
        {
            level++;
        }

        return At(text, position + 1 + level) == '[' ? level : null;
    }

    private static (string Value, int End) ReadLong(string text, int position, int level, int line)
    {
        var open = position + level + 2;
        var close = "]" + new string('=', level) + "]";
        var end = text.IndexOf(close, open, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidDataException($"Unfinished long string or comment on line {line}.");
        }

        var content = text[open..end];

        // A line break straight after the opening bracket is not part of the string.
        content = content.StartsWith("\r\n", StringComparison.Ordinal) ? content[2..] : content.StartsWith('\n') ? content[1..] : content;
        return (content, end + close.Length);
    }

    private static (string Value, int End) ReadQuoted(string text, int position, int line)
    {
        var quote = text[position++];
        var result = new StringBuilder();
        while (true)
        {
            if (position >= text.Length || text[position] == '\n')
            {
                throw new InvalidDataException($"Unfinished string on line {line}.");
            }

            var c = text[position++];
            if (c == quote)
            {
                return (result.ToString(), position);
            }

            if (c != '\\')
            {
                result.Append(c);
                continue;
            }

            var escaped = At(text, position++);
            switch (escaped)
            {
                case 'n':
                    result.Append('\n');
                    break;
                case 't':
                    result.Append('\t');
                    break;
                case 'r':
                    result.Append('\r');
                    break;
                case 'a':
                    result.Append('\a');
                    break;
                case 'b':
                    result.Append('\b');
                    break;
                case 'f':
                    result.Append('\f');
                    break;
                case 'v':
                    result.Append('\v');
                    break;
                case '\n':
                    result.Append('\n');
                    break;
                case '\\' or '"' or '\'' or '[' or ']':
                    result.Append(escaped);
                    break;
                case 'z':
                    while (position < text.Length && char.IsWhiteSpace(text[position]))
                    {
                        position++;
                    }

                    break;
                case var digit when char.IsAsciiDigit(digit):
                    {
                        // \ddd: a byte, read here as the character of that code.
                        var value = digit - '0';
                        for (var i = 0; i < 2 && char.IsAsciiDigit(At(text, position)); i++)
                        {
                            value = (value * 10) + (text[position++] - '0');
                        }

                        result.Append((char)value);
                        break;
                    }

                default:
                    throw new InvalidDataException($"Escape \\{escaped} is not read on line {line}.");
            }
        }
    }
}

internal sealed class LuaChunkParser(List<LuaToken> tokens)
{
    private static readonly Dictionary<string, int> BinaryPriority = new(StringComparer.Ordinal)
    {
        ["or"] = 1,
        ["and"] = 2,
        ["<"] = 3,
        [">"] = 3,
        ["<="] = 3,
        [">="] = 3,
        ["~="] = 3,
        ["=="] = 3,
        [".."] = 5,
        ["+"] = 6,
        ["-"] = 6,
        ["*"] = 7,
        ["/"] = 7,
        ["//"] = 7,
        ["%"] = 7,
        ["^"] = 9,
    };

    private const int UnaryPriority = 8;

    private int _position;

    private LuaToken Current => tokens[_position];

    public List<LuaStatement> ParseChunk()
    {
        var statements = new List<LuaStatement>();
        while (Current.Kind != LuaTokenKind.End)
        {
            if (Accept(";"))
            {
                continue;
            }

            var line = Current.Line;
            if (Current.Is("local"))
            {
                Next();
                if (Current.Is("function"))
                {
                    SkipBlock();
                    statements.Add(new LuaSkippedBlock(line));
                    continue;
                }

                var names = new List<LuaExpression> { new LuaName(ExpectName()) };
                while (Accept(","))
                {
                    names.Add(new LuaName(ExpectName()));
                }

                var values = Accept("=") ? ParseExpressionList() : [];
                statements.Add(new LuaAssignment(line, names, values, IsLocal: true));
            }
            else if (Current.Is("function") || Current.Is("do") || Current.Is("if") || Current.Is("for") || Current.Is("while") || Current.Is("repeat"))
            {
                SkipBlock();
                statements.Add(new LuaSkippedBlock(line));
            }
            else if (Current.Is("return") || Current.Is("break"))
            {
                Next();
                if (Current.Kind != LuaTokenKind.End && !Current.Is(";"))
                {
                    ParseExpressionList();
                }

                statements.Add(new LuaSkippedBlock(line));
            }
            else
            {
                var target = ParseSuffixed();
                if (target is LuaCall call && !Current.Is("=") && !Current.Is(","))
                {
                    statements.Add(new LuaCallStatement(line, call));
                    continue;
                }

                var targets = new List<LuaExpression> { target };
                while (Accept(","))
                {
                    targets.Add(ParseSuffixed());
                }

                Expect("=");
                statements.Add(new LuaAssignment(line, targets, ParseExpressionList(), IsLocal: false));
            }
        }

        return statements;
    }

    private List<LuaExpression> ParseExpressionList()
    {
        var list = new List<LuaExpression> { ParseExpression() };
        while (Accept(","))
        {
            list.Add(ParseExpression());
        }

        return list;
    }

    private LuaExpression ParseExpression(int limit = 0)
    {
        LuaExpression left;
        if (Current.Is("not") || Current.Is("-") || Current.Is("#") || Current.Is("~"))
        {
            var op = Next().Text;
            left = new LuaUnary(op, ParseExpression(UnaryPriority));
        }
        else
        {
            left = ParseSimple();
        }

        while (Current.Kind is LuaTokenKind.Symbol or LuaTokenKind.Name && BinaryPriority.TryGetValue(Current.Text, out var priority) && priority > limit)
        {
            var op = Next().Text;

            // .. and ^ are right associative.
            var right = ParseExpression(op is ".." or "^" ? priority - 1 : priority);
            left = new LuaBinary(op, left, right);
        }

        return left;
    }

    private LuaExpression ParseSimple()
    {
        var token = Current;
        switch (token.Kind)
        {
            case LuaTokenKind.String:
                Next();
                return new LuaStringLiteral(token.Text);
            case LuaTokenKind.Number:
                Next();
                return new LuaNumberLiteral(decimal.Parse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        if (Accept("true"))
        {
            return new LuaBooleanLiteral(true);
        }

        if (Accept("false"))
        {
            return new LuaBooleanLiteral(false);
        }

        if (Accept("nil"))
        {
            return new LuaNilLiteral();
        }

        if (Accept("..."))
        {
            return new LuaVarArgs();
        }

        if (Current.Is("{"))
        {
            return ParseTable();
        }

        if (Current.Is("function"))
        {
            SkipBlock();
            return new LuaFunction();
        }

        return ParseSuffixed();
    }

    /// <summary>A name or a parenthesised expression, then any indexing and calls.</summary>
    private LuaExpression ParseSuffixed()
    {
        LuaExpression expression;
        if (Accept("("))
        {
            expression = ParseExpression();
            Expect(")");
        }
        else
        {
            expression = new LuaName(ExpectName());
        }

        while (true)
        {
            if (Accept("."))
            {
                expression = new LuaIndex(expression, new LuaStringLiteral(ExpectName()));
            }
            else if (Accept("["))
            {
                var key = ParseExpression();
                Expect("]");
                expression = new LuaIndex(expression, key);
            }
            else if (Accept(":"))
            {
                var method = ExpectName();
                expression = new LuaCall(new LuaIndex(expression, new LuaStringLiteral(method)), [expression, .. ParseCallArguments()]);
            }
            else if (Current.Is("(") || Current.Is("{") || Current.Kind == LuaTokenKind.String)
            {
                expression = new LuaCall(expression, ParseCallArguments());
            }
            else
            {
                return expression;
            }
        }
    }

    private List<LuaExpression> ParseCallArguments()
    {
        if (Current.Kind == LuaTokenKind.String)
        {
            return [new LuaStringLiteral(Next().Text)];
        }

        if (Current.Is("{"))
        {
            return [ParseTable()];
        }

        Expect("(");
        if (Accept(")"))
        {
            return [];
        }

        var arguments = ParseExpressionList();
        Expect(")");
        return arguments;
    }

    private LuaTableConstructor ParseTable()
    {
        Expect("{");
        var fields = new List<LuaField>();
        while (!Accept("}"))
        {
            if (Accept("["))
            {
                var key = ParseExpression();
                Expect("]");
                Expect("=");
                fields.Add(new LuaField(key, ParseExpression()));
            }
            else if (Current.Kind == LuaTokenKind.Name && tokens[_position + 1].Is("="))
            {
                var name = Next().Text;
                Next();
                fields.Add(new LuaField(new LuaStringLiteral(name), ParseExpression()));
            }
            else
            {
                fields.Add(new LuaField(null, ParseExpression()));
            }

            if (!Accept(",") && !Accept(";"))
            {
                Expect("}");
                break;
            }
        }

        return new LuaTableConstructor(fields);
    }

    /// <summary>
    /// Steps over a function or control block from its first keyword to its matching <c>end</c> (or <c>until</c> and
    /// its condition): <c>do</c>, <c>if</c>, <c>function</c> and <c>repeat</c> open one, as <c>for</c> and
    /// <c>while</c> open theirs with <c>do</c>.
    /// </summary>
    private void SkipBlock()
    {
        var depth = 0;
        var opened = false;
        while (Current.Kind != LuaTokenKind.End)
        {
            var token = Next();
            if (token.Kind != LuaTokenKind.Name)
            {
                continue;
            }

            switch (token.Text)
            {
                case "do" or "if" or "function" or "repeat":
                    depth++;
                    opened = true;
                    break;
                case "end":
                    depth--;
                    break;
                case "until":
                    depth--;
                    if (depth == 0)
                    {
                        ParseExpression();
                    }

                    break;
            }

            if (opened && depth == 0)
            {
                return;
            }
        }

        throw new InvalidDataException("Unfinished block at the end of the file.");
    }

    private LuaToken Next() => tokens[_position < tokens.Count - 1 ? _position++ : _position];

    private bool Accept(string symbolOrKeyword)
    {
        if (!Current.Is(symbolOrKeyword))
        {
            return false;
        }

        Next();
        return true;
    }

    private void Expect(string symbol)
    {
        if (!Accept(symbol))
        {
            throw Error($"Expected '{symbol}'");
        }
    }

    private string ExpectName() => Current.Kind == LuaTokenKind.Name ? Next().Text : throw Error("Expected a name");

    private InvalidDataException Error(string message) =>
        new($"{message} on line {Current.Line}, found '{Current.Text}'.");
}
