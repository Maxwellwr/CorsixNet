using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Serialization;

namespace CorsixNet.Content.Saves;

/// <summary>A command as the game ran it: the tick it was scheduled before, and whose it was.</summary>
public sealed record ReplayCommand(long Tick, int Player, Command Command);

/// <summary>
/// A replay: the game's state when it started (a save of the simulation) and every command after it, so that the same
/// game plays out again on any machine, the simulation being deterministic. It ends with the tick reached and the state
/// hash there, to tell whether the replay played out the same.
/// </summary>
public sealed class Replay
{
    /// <summary>Replays' file extension.</summary>
    public const string Extension = ".cnreplay";

    /// <summary>The folder for replays in the saves folder.</summary>
    public const string Folder = "Replays";

    private const int MaxCommands = 10_000_000;
    private static readonly byte[] Magic = "CNETRPLY"u8.ToArray();
    private const int Version = 1;

    private readonly byte[] _start;
    private readonly List<ReplayCommand> _commands = [];

    private Replay(string levelName, byte[] start)
    {
        LevelName = levelName;
        _start = start;
    }

    /// <summary>The level the game was on, as saves name it.</summary>
    public string LevelName { get; }

    /// <summary>The commands, in the order they were run.</summary>
    public IReadOnlyList<ReplayCommand> Commands => _commands;

    /// <summary>The last tick the recording reached, with the state hash there; 0 and 0 if never ended.</summary>
    public (long Tick, ulong Hash) End { get; private set; }

    /// <summary>Starts recording from the simulation as it is now.</summary>
    public static Replay Start(string levelName, Simulation simulation)
    {
        using var stream = new MemoryStream();
        simulation.Save(stream);
        return new Replay(levelName, stream.ToArray());
    }

    /// <summary>Notes a command scheduled now, to run in the next step.</summary>
    public void Record(Simulation simulation, int player, Command command) => _commands.Add(new ReplayCommand(simulation.Tick, player, command));

    /// <summary>Notes where the game has got to, so that a playback can be checked against it.</summary>
    public void Finish(Simulation simulation) => End = (simulation.Tick, simulation.ComputeStateHash());

    /// <summary>The game as it was when the recording started.</summary>
    public Simulation StartingState(GameContent content)
    {
        using var stream = new MemoryStream(_start, writable: false);
        return Simulation.Load(stream, content);
    }

    /// <summary>Writes the replay; a file already there is only replaced once it is written.</summary>
    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
        {
            Directory.CreateDirectory(folder);
        }

        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
        {
            Write(stream);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Writes the replay to a stream.</summary>
    public void Write(Stream stream)
    {
        var writer = new SimWriter(stream);
        writer.WriteBytes(Magic);
        writer.WriteInt32(Version);
        writer.WriteString(LevelName);
        writer.WriteInt32(_start.Length);
        writer.WriteBytes(_start);
        writer.WriteInt32(_commands.Count);
        foreach (var entry in _commands)
        {
            writer.WriteInt64(entry.Tick);
            writer.WriteInt32(entry.Player);
            entry.Command.Write(writer);
        }

        writer.WriteInt64(End.Tick);
        writer.WriteUInt64(End.Hash);
    }

    /// <summary>Reads a replay written by <see cref="Save"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a replay, or one of another version.</exception>
    public static Replay Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a replay from a stream.</summary>
    /// <exception cref="InvalidDataException">The data is not a replay, or one of another version.</exception>
    public static Replay Read(Stream stream)
    {
        var reader = new SimReader(stream);
        try
        {
            if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            {
                throw new InvalidDataException("This is not a replay.");
            }

            var version = reader.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Replay version {version} is not supported (expected {Version}).");
            }

            var levelName = reader.ReadString();
            var start = reader.ReadBytes(reader.ReadCount(int.MaxValue));
            var replay = new Replay(levelName, start);
            var count = reader.ReadCount(MaxCommands);
            for (var i = 0; i < count; i++)
            {
                var tick = reader.ReadInt64();
                var player = reader.ReadInt32();
                replay._commands.Add(new ReplayCommand(tick, player, Command.Read(reader)));
            }

            replay.End = (reader.ReadInt64(), reader.ReadUInt64());
            return replay;
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("The replay is cut short.", e);
        }
    }
}
