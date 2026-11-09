// The adviser's message history, ported from CorsixTH CorsixTH/Lua/dialogs/resizables/adviser_history.lua
// (newest first, a bin for each message and one for all).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Ui.Windows;

/// <summary>The last things the adviser said, newest first, to read again or delete.</summary>
internal sealed class AdviserHistoryWindow : UiWindow
{
    public override string Title => "Adviser's messages";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var adviser = host.Adviser;
        if (adviser.History.Count == 0)
        {
            yield return WindowCell.Line("The adviser has said nothing yet.", dim: true);
            yield break;
        }

        for (var i = 0; i < adviser.History.Count; i++)
        {
            var index = i;
            var lines = BottomPanel.Wrap(adviser.History[i], BottomPanel.WrapLength);
            yield return WindowRow.Of(WindowCell.Button("Delete", () => adviser.RemoveFromHistory(index)), WindowCell.Line(lines[0]));
            foreach (var line in lines.Skip(1))
            {
                yield return WindowRow.Of(new WindowCell(string.Empty), WindowCell.Line(line));
            }
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(WindowCell.Button("Delete all", adviser.ClearHistory));
    }
}
