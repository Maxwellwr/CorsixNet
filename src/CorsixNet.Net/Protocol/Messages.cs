using CorsixNet.Content.Interface;
using CorsixNet.Sim.Commands;

namespace CorsixNet.Net.Protocol;

/// <summary>A message between a lockstep client and the server.</summary>
public abstract record Message;

/// <summary>Client → server: first message on a connection.</summary>
/// <param name="ProtocolVersion">The client's <see cref="MessageCodec.ProtocolVersion"/>.</param>
/// <param name="PlayerName">Name shown to the server.</param>
/// <param name="ContentHash">The <see cref="ContentFingerprint"/> of the client's game content.</param>
public sealed record HelloMessage(int ProtocolVersion, string PlayerName, ulong ContentHash) : Message;

/// <summary>
/// Server → client: the client has joined. Carries the complete simulation state, and the
/// tick up to which the client may run straight away.
/// </summary>
/// <param name="Player">The client's player number (0-based).</param>
/// <param name="State">Output of <c>Simulation.Save</c>.</param>
/// <param name="AllowedTick">The client may step while its simulation's tick is below this.</param>
public sealed record WelcomeMessage(int Player, byte[] State, long AllowedTick) : Message;

/// <summary>Server → client: the client cannot join; the connection will close.</summary>
public sealed record RejectMessage(string Reason) : Message;

/// <summary>Client → server: the player wants to issue a command.</summary>
public sealed record CommandRequestMessage(Command Command) : Message;

/// <summary>Server → client: a command with the tick and order in which every client must execute it.</summary>
public sealed record ScheduledCommandMessage(ScheduledCommand Command) : Message;

/// <summary>Server → client: every command up to this tick has been sent; the client may step while below it.</summary>
public sealed record AdvanceMessage(long AllowedTick) : Message;

/// <summary>Client → server: hash of the client's state after completing <paramref name="Tick"/> ticks.</summary>
public sealed record StateHashMessage(long Tick, ulong Hash) : Message;

/// <summary>Server → client: a client's state diverged from the server's.</summary>
public sealed record DesyncMessage(long Tick, int Player, ulong Expected, ulong Actual) : Message;

/// <summary>
/// Server → client: after a desync, the server's complete state, which replaces the client's, and the tick up to which
/// the client may run, as in a <see cref="WelcomeMessage"/>.
/// </summary>
/// <param name="State">Output of <c>Simulation.Save</c>.</param>
/// <param name="AllowedTick">The client may step while its simulation's tick is below this.</param>
public sealed record ResyncMessage(byte[] State, long AllowedTick) : Message;

/// <summary>
/// Client → server: the player wants the game to run at <paramref name="Speed"/>, or, when it is null, to go back to
/// the speed before a pause or speed-up (CorsixTH's <c>World:previousSpeed</c>).
/// </summary>
public sealed record SpeedRequestMessage(GameSpeed? Speed) : Message;

/// <summary>
/// Server → client: the game now runs at <paramref name="Speed"/>. Sent to everyone when it changes, and to a client
/// when it joins.
/// </summary>
/// <param name="Speed">The server's speed.</param>
/// <param name="Player">The player who changed it, or -1 when no player did (the speed a client joins at).</param>
public sealed record SpeedMessage(GameSpeed Speed, int Player) : Message;

/// <summary>Client → server: the player says something to everyone.</summary>
public sealed record ChatRequestMessage(string Text) : Message;

/// <summary>Server → client: a player said something.</summary>
/// <param name="Player">The player who said it (0-based).</param>
/// <param name="Name">The player's name.</param>
/// <param name="Text">What they said.</param>
public sealed record ChatMessage(int Player, string Name, string Text) : Message;

/// <summary>A player in the game: their number (0-based) and name.</summary>
public sealed record PlayerInfo(int Player, string Name);

/// <summary>Server → client: the players in the game now, by number. Sent to everyone when someone joins or leaves.</summary>
public sealed record PlayersMessage(IReadOnlyList<PlayerInfo> Players) : Message
{
    public bool Equals(PlayersMessage? other) => other is not null && Players.SequenceEqual(other.Players);

    public override int GetHashCode() => Players.Count;
}
