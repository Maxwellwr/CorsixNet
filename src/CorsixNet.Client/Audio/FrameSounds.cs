// Sounds on animation frames, after CorsixTH CorsixTH/Src/th_gfx.cpp (animation::tick's sound_to_play, played at the
// animation when it is drawn, and frame_sound_replacements with sound_pair) and th_sound.cpp (sound_player::play_at).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Formats;

namespace CorsixNet.Client.Audio;

/// <summary>
/// Finds the sounds of the animation frames drawn: when something drawn has moved on to a frame with a sound (VFRA's,
/// or CorsixTH's replacement for a few frames), that sound is due at the point it was drawn. Something seen for the
/// first time, or not drawn, makes no sound, as CorsixTH plays a frame's sound only when the animation is drawn.
/// </summary>
internal sealed class FrameSounds
{
    /// <summary>th_gfx.cpp's frame_sound_replacements: frame, sound, and a second sound picked half the time (or 0).</summary>
    private static readonly Dictionary<int, (int A, int B)> Replacements = new()
    {
        [6987] = (123, 0),
        [4213] = (35, 0),
        [4215] = (35, 0),
        [4224] = (35, 0),
        [4230] = (35, 0),
        [1902] = (58, 114),
        [4149] = (58, 114),
        [6901] = (58, 114),
        [8819] = (58, 114),
        [9105] = (58, 114),
        [9565] = (58, 114),
        [9654] = (58, 114),
        [9944] = (58, 114),
        [10007] = (58, 114),
        [10989] = (58, 114),
        [11136] = (35, 0),
        [11138] = (35, 0),
        [11147] = (35, 0),
        [11152] = (35, 0),
        [11153] = (35, 0),
        [11154] = (35, 0),
        [7153] = (9, 0),
        [7155] = (9, 0),
        [7158] = (9, 0),
        [7159] = (9, 0),
        [7162] = (9, 0),
        [7163] = (9, 0),
        [7166] = (9, 0),
    };

    private readonly Dictionary<int, int> _lastFrames = [];
    private readonly HashSet<int> _seen = [];
    private readonly List<(int Sound, int X, int Y)> _due = [];
    private readonly Random _random = new();

    /// <summary>Starts a round of drawing.</summary>
    public void BeginFrame() => _seen.Clear();

    /// <summary>Notes that <paramref name="key"/> was drawn on <paramref name="frame"/> at a world point.</summary>
    public void Drawn(AnimationSet animations, int key, int frame, int x, int y)
    {
        _seen.Add(key);
        if (_lastFrames.TryGetValue(key, out var last) && last != frame && SoundOf(animations, frame) is > 0 and var sound)
        {
            _due.Add((sound, x, y));
        }

        _lastFrames[key] = frame;
    }

    /// <summary>The sounds due since the last call (CorsixTH's 1-based sound numbers), and forgets what was not drawn.</summary>
    public List<(int Sound, int X, int Y)> TakeDue()
    {
        foreach (var gone in _lastFrames.Keys.Where(key => !_seen.Contains(key)).ToList())
        {
            _lastFrames.Remove(gone);
        }

        var due = _due.ToList();
        _due.Clear();
        return due;
    }

    /// <summary>The frame's sound: a replacement if CorsixTH has one (sound_pair picks either half the time), else VFRA's.</summary>
    public int SoundOf(AnimationSet animations, int frame)
    {
        if (Replacements.TryGetValue(frame, out var pair))
        {
            return pair.B == 0 || _random.Next(2) == 0 ? pair.A : pair.B;
        }

        return frame >= 0 && frame < animations.FrameCount ? animations.Frame(frame).Sound : 0;
    }
}
