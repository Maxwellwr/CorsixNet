namespace CorsixNet.Formats.Tests;

public class LuaChunkTests
{
    [Fact]
    public void ReadsCallsAssignmentsAndTables()
    {
        var statements = LuaChunk.Parse("""
            --[[ A long
            comment ]]
            Font("cp437")
            Language(utf8 "Français", "French", "fr")
            Inherit("original_strings", 1)
            main_menu = {
              new_game = "New" .. ' game', -- trailing comment
              [2] = [[long
            string]],
              "first";
            }
            adviser.warnings.money_low = S[11][2]
            """);

        Assert.Equal(5, statements.Count);
        var font = Assert.IsType<LuaCallStatement>(statements[0]);
        Assert.Equal(new LuaName("Font"), font.Call.Function);
        Assert.Equal(new LuaStringLiteral("cp437"), Assert.Single(font.Call.Arguments));
        Assert.Equal(3, font.Line);

        var language = Assert.IsType<LuaCallStatement>(statements[1]);
        var utf8 = Assert.IsType<LuaCall>(language.Call.Arguments[0]);
        Assert.Equal(new LuaName("utf8"), utf8.Function);
        Assert.Equal(new LuaStringLiteral("Français"), Assert.Single(utf8.Arguments));

        var menu = Assert.IsType<LuaAssignment>(statements[3]);
        var table = Assert.IsType<LuaTableConstructor>(Assert.Single(menu.Values));
        Assert.Equal(3, table.Fields.Count);
        Assert.Equal(new LuaStringLiteral("new_game"), table.Fields[0].Key);
        Assert.Equal(new LuaBinary("..", new LuaStringLiteral("New"), new LuaStringLiteral(" game")), table.Fields[0].Value);
        Assert.Equal(new LuaNumberLiteral(2), table.Fields[1].Key);
        Assert.Equal(new LuaStringLiteral("long\nstring"), table.Fields[1].Value);
        Assert.Null(table.Fields[2].Key);

        var money = Assert.IsType<LuaAssignment>(statements[4]);
        Assert.Equal(new LuaIndex(new LuaIndex(new LuaName("adviser"), new LuaStringLiteral("warnings")), new LuaStringLiteral("money_low")), money.Targets[0]);
        Assert.Equal(new LuaIndex(new LuaIndex(new LuaName("S"), new LuaNumberLiteral(11)), new LuaNumberLiteral(2)), money.Values[0]);
    }

    [Fact]
    public void StepsOverFunctionsAndControlBlocks()
    {
        var statements = LuaChunk.Parse("""
            local S = LoadStrings("Lang-" .. ... .. ".dat")
            local function fix(n)
              if n ~= 2 then return end
              for i = 1, 3 do S[i] = S[i] .. " " end
              repeat n = n - 1 until n == 0
            end
            fix(...)
            local M = {{}}
            do
              local i = 2
              while true do
                if S[23][i] == ".." then break end
                i = i + 1
              end
            end
            menu = { restart = #M[2] == 4 and M[2][3] or "(no string) restart" }
            """);

        Assert.Collection(
            statements,
            s => Assert.True(s is LuaAssignment { IsLocal: true }),
            s => Assert.IsType<LuaSkippedBlock>(s),
            s => Assert.IsType<LuaCallStatement>(s),
            s => Assert.True(s is LuaAssignment { IsLocal: true }),
            s => Assert.IsType<LuaSkippedBlock>(s),
            s => Assert.True(s is LuaAssignment { IsLocal: false }));
        var restart = ((LuaTableConstructor)((LuaAssignment)statements[5]).Values[0]).Fields[0].Value;
        Assert.Equal("or", Assert.IsType<LuaBinary>(restart).Operator);
    }

    [Fact]
    public void ReadsEscapes()
    {
        var statements = LuaChunk.Parse("""a = "tab\there\nnew \"quoted\" \65\\" """);

        Assert.Equal(new LuaStringLiteral("tab\there\nnew \"quoted\" A\\"), ((LuaAssignment)statements[0]).Values[0]);
    }

    [Theory]
    [InlineData("a = \"unfinished")]
    [InlineData("a = {")]
    [InlineData("function f() return 1")]
    [InlineData("a b")]
    public void RefusesBrokenText(string text) => Assert.Throws<InvalidDataException>(() => LuaChunk.Parse(text));
}
