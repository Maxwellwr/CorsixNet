// The announcer's subtitles, ported from CorsixTH CorsixTH/Lua/dialogs/subtitles.lua (Subtitles:queueSubtitle, draw,
// onTick, SubtitleQueue).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content;

namespace CorsixNet.Client.Ui;

/// <summary>
/// What the announcer says, as text in the top left corner (<c>enable_announcer_subtitles</c>): each announcement's line
/// of the translation's <c>subtitles</c> (by its sound's name), for about five seconds, the oldest first.
/// </summary>
internal sealed class Subtitles
{
    /// <summary>A subtitle's lifetime: CorsixTH's 280 ticks, "about five seconds".</summary>
    public const long Lifetime = 5000;

    private readonly List<(string Text, long Until)> _shown = [];

    public IReadOnlyList<string> Shown => _shown.Select(subtitle => subtitle.Text).ToList();

    /// <summary>The subtitle of an announcement's sound, such as <c>subtitles.epid001</c> for EPID001.wav, or null.</summary>
    public static string? TextOf(OriginalTexts? texts, string sound)
    {
        var key = Path.GetFileNameWithoutExtension(sound).ToLowerInvariant();
        return key.Length > 0 ? texts?.Named("subtitles." + key) : null;
    }

    /// <summary>queueSubtitle: the announcement's subtitle shows from <paramref name="now"/>, if the translation has one.</summary>
    public void Queue(OriginalTexts? texts, string sound, long now)
    {
        if (TextOf(texts, sound) is { } text)
        {
            _shown.Add((text, now + Lifetime));
        }
    }

    /// <summary>onTick: subtitles whose time is up go.</summary>
    public void Update(long now) => _shown.RemoveAll(subtitle => now >= subtitle.Until);
}
