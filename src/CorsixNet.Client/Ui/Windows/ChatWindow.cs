using CorsixNet.Content.Settings;
using CorsixNet.Net.Protocol;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>A window that takes typed text (MonoGame's text input) while it captures the keyboard.</summary>
internal interface ITextInputWindow : IKeyCaptureWindow
{
    /// <summary>A character typed, as the keyboard layout gives it.</summary>
    void Type(char character);
}

/// <summary>
/// The network game's chat: what the players said, the newest last, and a line to type in. Return starts typing and
/// says the line; Escape stops typing; Backspace takes the last character away. CorsixTH has no network game; the
/// original's had a chat, which this stands in for.
/// </summary>
internal sealed class ChatWindow(Func<IReadOnlyList<ChatMessage>> lines, Action<string> say) : UiWindow, ITextInputWindow
{
    /// <summary>The lines shown, the newest.</summary>
    public const int ShownLines = 8;

    private string _text = string.Empty;

    /// <summary>Whether the player is typing a line.</summary>
    public bool IsCapturing { get; private set; }

    /// <summary>The line being typed.</summary>
    public string TypedText => _text;

    public override string Title => UiText.Get("corsixnet.chat.caption", "Chat");

    /// <summary>Starts typing a line, as Return on the map does.</summary>
    public void StartTyping() => IsCapturing = true;

    public void Capture(Hotkey key)
    {
        switch (key.Key)
        {
            case "escape":
                IsCapturing = false;
                break;
            case "backspace":
                _text = _text.Length > 0 ? _text[..^(char.IsLowSurrogate(_text[^1]) && _text.Length > 1 ? 2 : 1)] : _text;
                break;
            case "return":
                if (_text.Trim().Length > 0)
                {
                    say(_text);
                }

                _text = string.Empty;
                IsCapturing = false;
                break;
        }
    }

    public void Type(char character)
    {
        if (IsCapturing && !char.IsControl(character) && _text.Length < Net.Lockstep.LockstepServer.MaxChatLength)
        {
            _text += character;
        }
    }

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var said = lines();
        if (said.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.chat.none", "Nobody has said anything yet."), dim: true);
        }

        foreach (var line in said.Skip(Math.Max(0, said.Count - ShownLines)))
        {
            yield return WindowRow.Of(WindowCell.Line($"{line.Name}: {line.Text}", highlight: line.Player == host.Player));
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(WindowCell.Button(
            IsCapturing ? $"> {_text}_" : UiText.Get("corsixnet.chat.start", "Press Return to talk"),
            () => IsCapturing = !IsCapturing,
            highlight: IsCapturing));
    }
}
