// Choosing which sound plays and how loud, ported from CorsixTH CorsixTH/Lua/audio.lua (Audio:playSound,
// cacheSoundFilenamesAssociatedWithName, resolveFilenameWildcard, setSoundStage) and CorsixTH/Src/th_sound.cpp
// (sound_player::play_at).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;

namespace CorsixNet.Content.Audio;

/// <summary>
/// The sound archive's names as the game asks for them: a name with <c>*</c> wildcards stands for any of the sounds it
/// matches, picked at random each time, and a sound heard at a place on the screen is quieter the further it is from
/// the centre.
/// </summary>
public sealed class SoundChoice(SoundArchive archive)
{
    private readonly Dictionary<string, IReadOnlyList<string>> _wildcards = new(StringComparer.Ordinal);

    /// <summary>The archive the names come from.</summary>
    public SoundArchive Archive { get; } = archive;

    /// <summary>
    /// <c>Audio:resolveFilenameWildcard</c>: a name without wildcards as it is; otherwise one of the matching names
    /// (upper case), chosen with <paramref name="pick"/> (given the count, it returns an index), or the name itself
    /// when nothing matches. The matches are looked up once for each name.
    /// </summary>
    public string Resolve(string name, Func<int, int> pick)
    {
        if (!name.Contains('*', StringComparison.Ordinal))
        {
            return name;
        }

        if (!_wildcards.TryGetValue(name, out var matches))
        {
            matches = Archive.Matching(name);
            _wildcards[name] = matches;
        }

        return matches.Count > 0 ? matches[pick(matches.Count)] : name;
    }

    /// <summary>
    /// <c>sound_player::play_at</c>: the volume of a sound <paramref name="dx"/>, <paramref name="dy"/> screen pixels
    /// from the centre of a view whose half-diagonal is <paramref name="radius"/> (<c>Audio:setSoundStage</c>): full at
    /// the centre, a fifth at the corners, and not heard at all (null) further out.
    /// </summary>
    public static double? VolumeAt(double volume, double dx, double dy, double radius)
    {
        radius = Math.Max(radius, 0.001);
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance > radius)
        {
            return null;
        }

        return (1.0 - (distance / radius * 0.8)) * volume;
    }

    /// <summary><c>Audio:setSoundStage</c>: the half-diagonal of a view, the radius sounds are heard in.</summary>
    public static double StageRadius(int width, int height)
    {
        var w = width / 2.0;
        var h = height / 2.0;
        return Math.Floor(Math.Sqrt((w * w) + (h * h)));
    }
}
