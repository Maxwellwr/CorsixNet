using CorsixNet.Content.Interface;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;

namespace CorsixNet.Client.Sessions;

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

    /// <summary>Whether the player can pause and change the game speed; only local games can.</summary>
    bool CanChangeSpeed { get; }

    /// <summary>The current game speed; a network game runs at the server's Normal speed.</summary>
    GameSpeed Speed { get; }

    bool IsPaused => Speed == GameSpeed.Pause;

    /// <summary>Changes the game speed, if <see cref="CanChangeSpeed"/>.</summary>
    void SetSpeed(GameSpeed speed);

    /// <summary>Ends a pause or a speed-up: back to the speed before it.</summary>
    void ResumeSpeed();

    /// <summary>One line describing the session's state for the status bar.</summary>
    string Status { get; }

    /// <summary>Issues a command on behalf of the local player.</summary>
    void Submit(Command command);

    /// <summary>Advances the simulation as far as the session allows.</summary>
    void Update(TimeSpan elapsed);
}
