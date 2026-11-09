// Playing the original's sound effects, ported from CorsixTH CorsixTH/Lua/audio.lua (Audio:init's sound archive,
// Audio:playSound), ui.lua (UI:playSound), entity.lua (Entity:playSound) and CorsixTH/Src/th_sound.cpp
// (sound_player::play, play_at).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Content.Audio;
using CorsixNet.Content.Settings;
using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace CorsixNet.Client.Audio;

/// <summary>
/// The sounds of <c>SOUND-n.DAT</c>, played through MonoGame. Without the archive, with sounds turned off in the
/// settings (<c>play_sounds</c>), or without a sound device, nothing is played and nothing fails.
/// </summary>
internal sealed class SoundEffects : IDisposable
{
    private readonly SoundChoice? _choice;
    private readonly float _volume;
    private readonly Dictionary<string, SoundEffect?> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();
    private bool _broken;

    private SoundEffects(SoundChoice? choice, float volume)
    {
        _choice = choice;
        _volume = volume;
    }

    /// <summary>The sound archive of the settings' language (the English one if it has none), if sounds are on.</summary>
    public static SoundEffects Load(ThemeHospitalInstall install, GameConfig config)
    {
        if (!config.PlaySounds)
        {
            return new SoundEffects(null, 0);
        }

        SoundArchive? archive = null;
        try
        {
            archive = install.LoadSounds(config.OriginalLanguageNumber() ?? StringTable.English);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"The sound effects could not be loaded: {e.Message}");
        }

        return new SoundEffects(archive is null ? null : new SoundChoice(archive), (float)Math.Clamp(config.SoundVolume, 0m, 1m));
    }

    /// <summary><c>UI:playSound</c>: a sound heard everywhere alike; the name may hold <c>*</c> wildcards.</summary>
    public void Play(string name) => PlayAtVolume(name, _volume);

    /// <summary>
    /// <c>Entity:playSound</c>: a sound at a point of the view, quieter away from its centre and not heard off it
    /// (<c>sound_player::play_at</c>).
    /// </summary>
    public void PlayAt(string name, Vector2 screen, Point viewSize)
    {
        var dx = screen.X - Math.Floor(viewSize.X / 2.0);
        var dy = screen.Y - Math.Floor(viewSize.Y / 2.0);
        if (SoundChoice.VolumeAt(_volume, dx, dy, SoundChoice.StageRadius(viewSize.X, viewSize.Y)) is { } volume)
        {
            PlayAtVolume(name, (float)volume);
        }
    }

    public void Dispose()
    {
        foreach (var effect in _loaded.Values)
        {
            effect?.Dispose();
        }

        _loaded.Clear();
    }

    private void PlayAtVolume(string name, float volume)
    {
        if (_choice is null || _broken || volume <= 0)
        {
            return;
        }

        name = _choice.Resolve(name, _random.Next);
        if (Effect(name) is not { } effect)
        {
            return;
        }

        try
        {
            effect.Play(Math.Clamp(volume, 0f, 1f), 0f, 0f);
        }
        catch (Exception e) when (e is NoAudioHardwareException or InstancePlayLimitException or InvalidOperationException)
        {
            // As CorsixTH without a sound device, or with every channel in use, the sound is not heard.
            _broken = e is NoAudioHardwareException;
        }
    }

    /// <summary>The sound loaded once; null for one that is not in the archive or that MonoGame cannot read.</summary>
    private SoundEffect? Effect(string name)
    {
        if (_loaded.TryGetValue(name, out var effect))
        {
            return effect;
        }

        if (_choice!.Archive.Find(name) is { } entry)
        {
            try
            {
                using var stream = new MemoryStream(_choice.Archive.Read(entry).ToArray());
                effect = SoundEffect.FromStream(stream);
            }
            catch (NoAudioHardwareException)
            {
                _broken = true;
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                Console.Error.WriteLine($"The sound {name} could not be read: {e.Message}");
            }
        }

        _loaded[name] = effect;
        return effect;
    }
}
