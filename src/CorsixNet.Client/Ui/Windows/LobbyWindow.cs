namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The network game's levels (F3): those the server offers, the one being played marked. The host, player 1, picks
/// another after a question, and everyone starts it from the beginning, paused; the others only see the list. CorsixTH
/// has no network game; the original's let the host choose the town.
/// </summary>
/// <param name="levels">The levels offered.</param>
/// <param name="current">The level being played.</param>
/// <param name="isHost">Whether this player chooses.</param>
/// <param name="choose">Asks the server for a level.</param>
internal sealed class LobbyWindow(Func<IReadOnlyList<string>> levels, Func<string?> current, Func<bool> isHost, Action<string> choose) : UiWindow
{
    public override string Title => UiText.Get("corsixnet.lobby.caption", "Levels");

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var offered = levels();
        var playing = current();
        if (offered.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.lobby.none", "The server offers no other levels."), dim: true);
            yield break;
        }

        var host1 = isHost();
        if (!host1)
        {
            yield return WindowCell.Line(UiText.Get("corsixnet.lobby.host_only", "Only player 1 chooses the level."), dim: true);
        }

        foreach (var level in offered)
        {
            var isCurrent = string.Equals(level, playing, StringComparison.OrdinalIgnoreCase);
            if (!host1 || isCurrent)
            {
                yield return WindowCell.Line(level, highlight: isCurrent);
                continue;
            }

            yield return WindowRow.Of(WindowCell.Button(level, () => host.Open(new ConfirmWindow(
                UiText.Format("corsixnet.lobby.confirm", "Everyone starts %1% from the beginning. Go ahead?", level),
                () =>
                {
                    host.Close(this);
                    choose(level);
                }))));
        }
    }
}
