// The movie player, ported from CorsixTH CorsixTH/Lua/movie_player.lua (calculateSize, playMovie, onMovieOver, stop,
// togglePause, destroyMovie and the music held while a movie with sound plays). The frames come from our own Smacker
// decoder (OpenWards.Formats.Smacker) where CorsixTH uses FFmpeg (CorsixTH/Src/th_movie.cpp).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Formats;

namespace OpenWards.Client.Movies;

/// <summary>
/// Plays one Smacker movie over the whole screen: its frames at the movie's rate, its first audio track through a
/// streamed sound, letterboxed to the screen with 320 by 200 movies stretched to 4:3. A movie that waits for a stop
/// (<c>wait_for_stop</c>) keeps its last frame until a click or key; the others end by themselves.
/// </summary>
internal sealed class MoviePlayer : IDisposable
{
    /// <summary>How far ahead of the picture the sound is decoded, in frames.</summary>
    private const int AudioLead = 2;

    private readonly GraphicsDevice _device;
    private readonly Stopwatch _clock = new();
    private SmackerMovie? _movie;
    private SmackerDecoder? _decoder;
    private Texture2D? _texture;
    private Color[]? _colours;
    private DynamicSoundEffectInstance? _sound;
    private Action? _onDone;
    private bool _waitForStop;
    private bool _waitForOver;
    private int _shown = -1;
    private readonly Queue<SmackerFrame> _ahead = new();
    private Texture2D? _overlay;
    private TimeSpan _overlayFrom;

    public MoviePlayer(GraphicsDevice device) => _device = device;

    /// <summary>Whether a movie is on the screen (<c>playing</c>).</summary>
    public bool IsPlaying => _movie is not null;

    /// <summary>Whether the movie is paused (<c>togglePause</c>).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Whether the movie playing has a sound track (<c>hasAudioTrack</c>), so the music is held meanwhile.</summary>
    public bool HasAudio => _sound is not null;

    /// <summary>How long the movie lasts (<c>getLength</c>).</summary>
    public TimeSpan Length => _movie is null ? TimeSpan.Zero : TimeSpan.FromMilliseconds(_movie.FrameCount * _movie.FrameMilliseconds);

    /// <summary>How far into the movie it is (the presentation time of <c>refresh</c>).</summary>
    public TimeSpan Position => _clock.Elapsed;

    /// <summary>
    /// <c>playMovie</c>: starts the movie at <paramref name="path"/>; false when it cannot be read, and then
    /// <paramref name="onDone"/> runs at once, as CorsixTH's callback does.
    /// </summary>
    public bool Play(string path, bool waitForStop, Action? onDone = null, float volume = 1f)
    {
        Unload();
        try
        {
            _movie = SmackerMovie.Parse(File.ReadAllBytes(path));
            _decoder = new SmackerDecoder(_movie);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"MoviePlayer:playMovie - Warning: {e.Message}");
            _movie = null;
            _decoder = null;
            onDone?.Invoke();
            return false;
        }

        _texture = new Texture2D(_device, _movie.Width, _movie.Height);
        _colours = new Color[_movie.Width * _movie.Height];
        if (_movie.Audio[0] is { } audio)
        {
            try
            {
                _sound = new DynamicSoundEffectInstance(audio.SampleRate, audio.Channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono) { Volume = Math.Clamp(volume, 0f, 1f) };
            }
            catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or ArgumentException)
            {
                _sound = null;
            }
        }

        _onDone = onDone;
        _waitForStop = waitForStop;
        _waitForOver = true;
        IsPaused = false;
        _shown = -1;
        _clock.Restart();
        Advance();
        _sound?.Play();
        return true;
    }

    /// <summary>Shows the frame that is due and queues the sound ahead of it; reports the end (<c>onMovieOver</c>).</summary>
    public void Update()
    {
        if (_movie is null || IsPaused)
        {
            return;
        }

        Advance();
        if (_waitForOver && _decoder!.IsFinished && _ahead.Count == 0 && _clock.Elapsed >= Length)
        {
            // onMovieOver: a movie that does not wait for a stop goes now.
            _waitForOver = false;
            if (!_waitForStop)
            {
                Destroy();
            }
        }
    }

    /// <summary><c>stop</c>: a click or the stop key ends the movie (once it is over, if it waits for that).</summary>
    public void Stop()
    {
        if (_movie is null)
        {
            return;
        }

        _waitForStop = false;
        _sound?.Stop();
        _waitForOver = false;
        Destroy();
    }

    /// <summary><c>togglePause</c>.</summary>
    public void TogglePause()
    {
        if (_movie is null)
        {
            return;
        }

        IsPaused = !IsPaused;
        if (IsPaused)
        {
            _clock.Stop();
            _sound?.Pause();
        }
        else
        {
            _clock.Start();
            _sound?.Resume();
        }
    }

    /// <summary>Draws the frame shown over the screen, black around it (<c>refresh</c> with <c>calculateSize</c>).</summary>
    public void Draw(SpriteBatch batch, Texture2D pixel, Rectangle screen)
    {
        if (_movie is null || _texture is null)
        {
            return;
        }

        batch.Draw(pixel, screen, Color.Black);
        if (_shown >= 0)
        {
            var area = Fit(_movie.Width, _movie.DisplayHeight, screen);
            batch.Draw(_texture, area, Color.White);
            if (_overlay is not null && Position >= _overlayFrom)
            {
                // refresh_overlay: drawn over the movie, scaled with it.
                batch.Draw(_overlay, area, Color.White);
            }
        }
    }

    /// <summary>
    /// <c>refresh_overlay</c>: a picture laid over the movie from <paramref name="from"/> on, stretched over it as the movie
    /// is; the player owns it from now and disposes of it with the movie.
    /// </summary>
    public void SetOverlay(Texture2D overlay, TimeSpan from)
    {
        _overlay?.Dispose();
        _overlay = overlay;
        _overlayFrom = from;
    }

    /// <summary>
    /// <c>calculateSize</c>: the movie as large as fits the screen at its aspect ratio, in the middle; a 320 by 200 movie
    /// is shown as 320 by 240, as it was meant for a 4:3 screen.
    /// </summary>
    public static Rectangle Fit(int width, int height, Rectangle screen)
    {
        if (width == 320 && height == 200)
        {
            height = 240;
        }

        if (width <= 0 || height <= 0)
        {
            return screen;
        }

        if (Math.Abs(((double)screen.Width / screen.Height) - ((double)width / height)) < 0.001)
        {
            return screen;
        }

        if (screen.Width > (double)screen.Height / height * width)
        {
            var w = (int)Math.Floor((double)screen.Height / height * width);
            return new Rectangle(screen.X + ((screen.Width - w) / 2), screen.Y, w, screen.Height);
        }

        var h = (int)Math.Floor((double)screen.Width / width * height);
        return new Rectangle(screen.X, screen.Y + ((screen.Height - h) / 2), screen.Width, h);
    }

    public void Dispose() => Unload();

    private void Advance()
    {
        var movie = _movie!;
        var due = Math.Min(movie.FrameCount - 1, (int)(_clock.Elapsed.TotalMilliseconds / movie.FrameMilliseconds));

        // Decode so that the sound stays a little ahead of the picture.
        while (!_decoder!.IsFinished && _decoder.Position <= due + AudioLead)
        {
            var frame = _decoder.Next();
            if (_sound is not null && frame.Audio.TryGetValue(0, out var samples) && samples.Length > 0)
            {
                _sound.SubmitBuffer(samples);
            }

            _ahead.Enqueue(frame);
        }

        SmackerFrame? latest = null;
        while (_ahead.Count > 0 && _shown < due)
        {
            latest = _ahead.Dequeue();
            _shown++;
        }

        if (latest is not null)
        {
            Show(latest);
        }
    }

    private void Show(SmackerFrame frame)
    {
        var palette = frame.Palette;
        var colours = _colours!;
        for (var i = 0; i < colours.Length; i++)
        {
            var index = frame.Pixels[i] * 3;
            colours[i] = new Color(palette[index], palette[index + 1], palette[index + 2]);
        }

        _texture!.SetData(colours);
    }

    /// <summary><c>destroyMovie</c>: the movie goes, then what was to happen after it.</summary>
    private void Destroy()
    {
        var done = _onDone;
        _onDone = null;
        Unload();
        done?.Invoke();
    }

    private void Unload()
    {
        _sound?.Stop();
        _sound?.Dispose();
        _sound = null;
        _overlay?.Dispose();
        _overlay = null;
        _texture?.Dispose();
        _texture = null;
        _colours = null;
        _movie = null;
        _decoder = null;
        _ahead.Clear();
        _clock.Reset();
        IsPaused = false;
    }
}
