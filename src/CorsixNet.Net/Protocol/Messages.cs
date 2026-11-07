using CorsixNet.Sim.Commands;

namespace CorsixNet.Net.Protocol;

/// <summary>A message between a lockstep client and the server.</summary>
public abstract record Message;

/// <summary>Client → server: first message on a connection.</summary>
public sealed record HelloMessage(int ProtocolVersion, string PlayerName) : Message;

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
