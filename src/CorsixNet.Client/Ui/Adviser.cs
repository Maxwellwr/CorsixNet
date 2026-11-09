// The adviser's queue and history, ported from CorsixTH CorsixTH/Lua/dialogs/adviser.lua (UIAdviser:say, talk,
// addMessageToHistory, onMouseDown) and resizables/adviser_history.lua.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Ui;

/// <summary>
/// The adviser who pops up from the bottom panel: messages wait in a queue and are said one at a time for a few
/// seconds each; what was said is kept in a history of the last twenty, newest first. Time is the interface's own
/// (milliseconds), not the game's, so the adviser talks at the same pace at any speed.
/// </summary>
internal sealed class Adviser
{
    /// <summary>The messages kept in the history (CorsixTH's <c>message_history_limit</c>).</summary>
    public const int HistoryLimit = 20;

    /// <summary>
    /// How long a message is shown: CorsixTH's talking animation is 45 frames, one every few ticks, some ten seconds
    /// at normal speed.
    /// </summary>
    public const long SpeakMilliseconds = 9000;

    private readonly List<string> _queue = [];
    private readonly List<string> _history = [];
    private long _until;

    /// <summary>Whether the adviser speaks at all (CorsixTH's <c>adviser_disabled</c> setting, inverted).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>What the adviser is saying now, or null.</summary>
    public string? Current { get; private set; }

    /// <summary>Messages waiting to be said after the current one.</summary>
    public int Queued => _queue.Count;

    /// <summary>The last messages said, newest first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// <c>UIAdviser:say</c>: queues a message, unless the same one is already waiting; <paramref name="interrupt"/>
    /// drops the waiting messages and says this one at once.
    /// </summary>
    public void Say(string text, long now, bool interrupt = false)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (interrupt)
        {
            _queue.Clear();
            _queue.Add(text);
            Talk(now);
            return;
        }

        if (_queue.Contains(text, StringComparer.Ordinal) || text == Current)
        {
            return;
        }

        _queue.Add(text);
        if (Current is null)
        {
            Talk(now);
        }
    }

    /// <summary>Moves on to the next message when the current one has been shown long enough.</summary>
    public void Update(long now)
    {
        if (Current is not null && now >= _until)
        {
            Next(now);
        }
    }

    /// <summary>A left click on the adviser: the next message, or he goes away.</summary>
    public void Next(long now)
    {
        if (_queue.Count > 0)
        {
            Talk(now);
        }
        else
        {
            Current = null;
        }
    }

    /// <summary>A right click on the adviser: he goes away, and the waiting messages go to the history unsaid.</summary>
    public void DismissAll()
    {
        Current = null;
        foreach (var message in _queue)
        {
            AddToHistory(message);
        }

        _queue.Clear();
    }

    /// <summary>Deletes one message from the history (the history window's bin).</summary>
    public void RemoveFromHistory(int index)
    {
        if (index >= 0 && index < _history.Count)
        {
            _history.RemoveAt(index);
        }
    }

    /// <summary>Forgets everything, for a new game.</summary>
    public void Clear()
    {
        Current = null;
        _queue.Clear();
        _history.Clear();
    }

    /// <summary>Deletes the whole history.</summary>
    public void ClearHistory() => _history.Clear();

    private void Talk(long now)
    {
        Current = _queue[0];
        _queue.RemoveAt(0);
        _until = now + SpeakMilliseconds;
        AddToHistory(Current);
    }

    /// <summary><c>addMessageToHistory</c>: a message said again moves to the top.</summary>
    private void AddToHistory(string message)
    {
        _history.Remove(message);
        _history.Insert(0, message);
        if (_history.Count > HistoryLimit)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }
}
