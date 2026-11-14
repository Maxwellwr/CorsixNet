// The adviser's figure, ported from CorsixTH CorsixTH/Lua/dialogs/adviser.lua (UIAdviser:show, talk, idle, hide, onTick,
// draw).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Ui;

/// <summary>The adviser's phases (UIAdviser's <c>phase</c> 0 to 4).</summary>
internal enum AdviserPhase
{
    Hidden,
    GettingUp,
    Talking,
    Idle,
    GettingDown,
}

/// <summary>
/// The adviser who pops up from the bottom panel while <see cref="Adviser"/> has something to say: he gets up (four
/// frames of animation 438), talks with his speech in a balloon (45 frames of 460, then he holds the last one), waits a
/// while once there is nothing more to say, and gets down again (four frames of 440). A message that comes while he
/// is getting down makes him get up again afterwards. Time is the interface's (milliseconds).
/// </summary>
internal sealed class AdviserFigure
{
    /// <summary>The animation of getting up from behind the panel.</summary>
    public const int GetUpAnimation = 438;

    /// <summary>The animation of talking.</summary>
    public const int TalkAnimation = 460;

    /// <summary>The animation of getting down behind the panel.</summary>
    public const int GetDownAnimation = 440;

    /// <summary>
    /// One frame of his animations: CorsixTH steps them once every <c>tick_rate</c> + 1 interface ticks, so that the 45
    /// frames of talking last as long as <see cref="Adviser.SpeakMilliseconds"/>.
    /// </summary>
    public const long FrameMilliseconds = Adviser.SpeakMilliseconds / TalkFrames;

    /// <summary>How long he stays up with nothing to say (<c>UIAdviser:idle</c>'s timer of 150 ticks).</summary>
    public const long IdleMilliseconds = 150 * FrameMilliseconds / 4;

    private const int TalkFrames = 45;
    private const int UpDownFrames = 4;

    private long _phaseStart;
    private string? _said;
    private bool _upAgain;

    /// <summary>What he is doing.</summary>
    public AdviserPhase Phase { get; private set; }

    /// <summary>The animation shown, or null while he is hidden.</summary>
    public int? Animation => Phase switch
    {
        AdviserPhase.GettingUp => GetUpAnimation,
        AdviserPhase.Talking or AdviserPhase.Idle => TalkAnimation,
        AdviserPhase.GettingDown => GetDownAnimation,
        _ => null,
    };

    /// <summary>The frame of <see cref="Animation"/>, from 0; each stops on its last frame.</summary>
    public int Frame { get; private set; }

    /// <summary>The speech in his balloon, shown only while he talks.</summary>
    public string? Speech => Phase == AdviserPhase.Talking ? _said : null;

    /// <summary>Follows what the adviser is saying (<paramref name="current"/>, or null when he has nothing to say).</summary>
    public void Update(long now, string? current)
    {
        switch (Phase)
        {
            case AdviserPhase.Hidden:
                if (current is not null)
                {
                    Start(AdviserPhase.GettingUp, now);
                }

                break;
            case AdviserPhase.GettingUp:
                if (Elapsed(now) >= UpDownFrames - 1)
                {
                    if (current is null)
                    {
                        Start(AdviserPhase.GettingDown, now);
                    }
                    else
                    {
                        Talk(current, now);
                    }
                }

                break;
            case AdviserPhase.Talking:
                if (current is null)
                {
                    // Dismissed, or said long enough: he waits without the balloon.
                    Start(AdviserPhase.Idle, now);
                    Frame = TalkFrames - 1;
                }
                else if (current != _said)
                {
                    Talk(current, now);
                }

                break;
            case AdviserPhase.Idle:
                if (current is not null)
                {
                    Talk(current, now);
                }
                else if (now - _phaseStart >= IdleMilliseconds)
                {
                    Start(AdviserPhase.GettingDown, now);
                }

                break;
            case AdviserPhase.GettingDown:
                _upAgain |= current is not null;
                if (Elapsed(now) >= UpDownFrames - 1)
                {
                    if (_upAgain)
                    {
                        _upAgain = false;
                        Start(AdviserPhase.GettingUp, now);
                    }
                    else
                    {
                        Phase = AdviserPhase.Hidden;
                    }
                }

                break;
        }

        if (Phase is AdviserPhase.GettingUp or AdviserPhase.GettingDown)
        {
            Frame = (int)Math.Min(UpDownFrames - 1, Elapsed(now));
        }
        else if (Phase == AdviserPhase.Talking)
        {
            Frame = (int)Math.Min(TalkFrames - 1, Elapsed(now));
        }
    }

    /// <summary>
    /// A right click on him, or the adviser told to be quiet: he gets down at once (<c>UIAdviser:hide</c>), unless he
    /// is hidden or already getting down.
    /// </summary>
    public void Hide(long now)
    {
        if (Phase is not (AdviserPhase.Hidden or AdviserPhase.GettingDown))
        {
            Start(AdviserPhase.GettingDown, now);
            Frame = 0;
        }
    }

    /// <summary>Whether he is up, so that clicks on him count (not while hidden or getting down).</summary>
    public bool IsUp => Phase is AdviserPhase.GettingUp or AdviserPhase.Talking or AdviserPhase.Idle;

    /// <summary>He disappears at once, for a new game.</summary>
    public void Reset()
    {
        Phase = AdviserPhase.Hidden;
        _said = null;
        _upAgain = false;
        Frame = 0;
    }

    /// <summary>
    /// The balloon's width for a speech (<c>UIAdviser:talk</c>): its length over its lines, 7 pixels a character, from 40
    /// to 420; each "/" at the end of the speech is a blank line fewer of the three.
    /// </summary>
    public static int BalloonWidth(string speech)
    {
        var trimmed = speech.TrimEnd('/');
        var lines = Math.Max(1, 3 - (speech.Length - trimmed.Length));
        return Math.Clamp(trimmed.Length / lines * 7, 40, 420);
    }

    private void Talk(string speech, long now)
    {
        _said = speech;
        Start(AdviserPhase.Talking, now);
    }

    private void Start(AdviserPhase phase, long now)
    {
        Phase = phase;
        _phaseStart = now;
        Frame = 0;
    }

    private long Elapsed(long now) => (now - _phaseStart) / FrameMilliseconds;
}
