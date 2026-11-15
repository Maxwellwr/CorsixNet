using CorsixNet.Client.Ui.Windows;
using CorsixNet.Content.Settings;
using CorsixNet.Net.Protocol;

namespace CorsixNet.Client.Tests;

public class ChatWindowTests
{
    private readonly List<ChatMessage> _lines = [];
    private readonly List<string> _said = [];

    private ChatWindow NewWindow() => new(() => _lines, _said.Add);

    [Fact]
    public void TypesALineAndSaysItOnReturn()
    {
        var window = NewWindow();
        window.Type('x');
        Assert.Equal(string.Empty, window.TypedText);

        window.StartTyping();
        foreach (var character in "Привет!\r")
        {
            window.Type(character);
        }

        window.Capture(new Hotkey("backspace"));
        window.Type('?');
        Assert.True(window.IsCapturing);
        Assert.Equal("Привет?", window.TypedText);

        window.Capture(new Hotkey("return"));

        Assert.Equal(["Привет?"], _said);
        Assert.Equal(string.Empty, window.TypedText);
        Assert.False(window.IsCapturing);
    }

    [Fact]
    public void EscapeStopsTypingAndAnEmptyLineIsNotSaid()
    {
        var window = NewWindow();
        window.StartTyping();
        window.Capture(new Hotkey("escape"));
        Assert.False(window.IsCapturing);

        window.StartTyping();
        window.Type(' ');
        window.Capture(new Hotkey("return"));

        Assert.Empty(_said);
    }

    [Fact]
    public void ShowsTheNewestLinesWithTheOwnPlayersHighlighted()
    {
        var host = new WindowTestHost();
        for (var i = 0; i < ChatWindow.ShownLines + 2; i++)
        {
            _lines.Add(new ChatMessage(i % 2, i % 2 == 0 ? "Alice" : "Bob", $"line {i}"));
        }

        var cells = NewWindow().Rows(host).SelectMany(row => row.Cells).ToList();

        Assert.DoesNotContain(cells, cell => cell.Text == "Alice: line 0");
        Assert.Contains(cells, cell => cell.Text == "Alice: line 2" && cell.Highlight);
        Assert.Contains(cells, cell => cell.Text == "Bob: line 9" && !cell.Highlight);
    }
}
