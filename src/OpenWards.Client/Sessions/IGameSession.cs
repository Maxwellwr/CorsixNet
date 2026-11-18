using OpenWards.Content.Interface;
using OpenWards.Content.Saves;
using OpenWards.Sim;
using OpenWards.Sim.Commands;

namespace OpenWards.Client.Sessions;

/// <summary>
/// Where the simulation shown on screen comes from, and where the player's commands go:
/// a local game, or a lockstep game on a server.
/// </summary>
internal interface IGameSession : IDisposable
{
    /// <summary>Short name for the status line, such as the level file.</summary>
    string Name { get; }

    /// <summary>The simulation, or null while it is not available yet (for example still joining).</summary>
    Simulation? Simulation { get; }

    /// <summary>The local player's number (0-based).</summary>
    int Player { get; }

    /// <summary>Whether the player can pause and change the game speed (in a network game, for everyone).</summary>
    bool CanChangeSpeed { get; }

    /// <summary>
    /// Whether other players share the game, so that its speed is theirs too: windows that pause a local game
    /// (<c>World:mustPauseWindowAdd</c>) do not pause it.
    /// </summary>
    bool IsShared => false;

    /// <summary>The current game speed; a network game's is the server's.</summary>
    GameSpeed Speed { get; }

    bool IsPaused => Speed == GameSpeed.Pause;

    /// <summary>Changes the game speed, if <see cref="CanChangeSpeed"/>.</summary>
    void SetSpeed(GameSpeed speed);

    /// <summary>Ends a pause or a speed-up: back to the speed before it.</summary>
    void ResumeSpeed();

    /// <summary>One line describing the session's state for the status bar.</summary>
    string Status { get; }

    /// <summary>The replay of this game so far, if it is recorded (local and network games are).</summary>
    Replay? Recording => null;

    /// <summary>Issues a command on behalf of the local player.</summary>
    void Submit(Command command);

    /// <summary>Advances the simulation as far as the session allows.</summary>
    void Update(TimeSpan elapsed);
}
