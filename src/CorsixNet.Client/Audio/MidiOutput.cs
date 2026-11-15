// The system's MIDI output, in place of CorsixTH's RtMidi port (CorsixTH/Src/midi_player.cpp's midi_out): on Windows the
// MIDI mapper of the Windows multimedia API (winmm), which RtMidi's "Windows MM" API uses too.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Runtime.InteropServices;

namespace CorsixNet.Client.Audio;

/// <summary>Somewhere MIDI messages go: a status byte and its data, or a whole system exclusive message.</summary>
internal interface IMidiOutput : IDisposable
{
    /// <summary>Sends one message (RtMidiOut::sendMessage).</summary>
    void Send(ReadOnlySpan<byte> message);
}

/// <summary>The Windows MIDI mapper (<c>midiOutOpen(MIDI_MAPPER)</c>), the default synthesiser or the device chosen in Windows.</summary>
internal sealed partial class WinMmMidiOutput : IMidiOutput
{
    private const uint MidiMapper = 0xFFFFFFFF;
    private const uint MhdrDone = 0x00000001;

    private nint _handle;

    private WinMmMidiOutput(nint handle) => _handle = handle;

    /// <summary>The API's name in the settings and the sound dialog (RtMidi's <c>midi_api_to_name</c> for WINDOWS_MM).</summary>
    public const string ApiName = "Windows MM";

    /// <summary>
    /// Opens the output named <paramref name="port"/> (<c>midi_port</c>), or the MIDI mapper for none or one not found
    /// (<c>midi_player::set_port</c> and <c>open_default_port</c>); null when this is not Windows or it cannot be opened.
    /// </summary>
    public static WinMmMidiOutput? Open(string? port = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var device = MidiMapper;
            if (port is { Length: > 0 })
            {
                var index = Ports().ToList().FindIndex(name => string.Equals(name, port, StringComparison.Ordinal));
                if (index >= 0)
                {
                    device = (uint)index;
                }
                else
                {
                    Console.Error.WriteLine($"MIDI device not found: {port}");
                }
            }

            return midiOutOpen(out var handle, device, 0, 0, 0) == 0 ? new WinMmMidiOutput(handle) : null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The names of the system's MIDI outputs (<c>midi_player::port_list</c>); none when this is not Windows.</summary>
    public static IReadOnlyList<string> Ports()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            var names = new List<string>();
            var count = midiOutGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                var caps = default(MidiOutCaps);
                unsafe
                {
                    if (midiOutGetDevCapsW(i, ref caps, (uint)sizeof(MidiOutCaps)) == 0)
                    {
                        names.Add(new string(caps.Name, 0, 32).TrimEnd('\0'));
                    }
                }
            }

            return names;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return [];
        }
    }

    public void Send(ReadOnlySpan<byte> message)
    {
        if (_handle == 0 || message.IsEmpty)
        {
            return;
        }

        if (message[0] is 0xF0 or 0xF7)
        {
            SendLong(message);
            return;
        }

        // A short message: the status byte and up to two data bytes, packed from the low byte up.
        uint packed = message[0];
        if (message.Length > 1)
        {
            packed |= (uint)message[1] << 8;
        }

        if (message.Length > 2)
        {
            packed |= (uint)message[2] << 16;
        }

        _ = midiOutShortMsg(_handle, packed);
    }

    /// <summary>A system exclusive message: prepared in a header, sent, and released once the device is done with it.</summary>
    private unsafe void SendLong(ReadOnlySpan<byte> message)
    {
        var buffer = Marshal.AllocHGlobal(message.Length);
        try
        {
            message.CopyTo(new Span<byte>((void*)buffer, message.Length));
            var header = new MidiHeader { Data = buffer, BufferLength = (uint)message.Length, BytesRecorded = (uint)message.Length };
            var size = (uint)sizeof(MidiHeader);
            if (midiOutPrepareHeader(_handle, ref header, size) != 0)
            {
                return;
            }

            if (midiOutLongMsg(_handle, ref header, size) == 0)
            {
                // The mapper copies the message at once; wait a little for it to say so, at most a second.
                for (var i = 0; i < 100 && (Volatile.Read(ref header.Flags) & MhdrDone) == 0; i++)
                {
                    Thread.Sleep(10);
                }
            }

            _ = midiOutUnprepareHeader(_handle, ref header, size);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            _ = midiOutReset(_handle);
            _ = midiOutClose(_handle);
            _handle = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct MidiOutCaps
    {
        public ushort Manufacturer;
        public ushort Product;
        public uint DriverVersion;
        public fixed char Name[32];
        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHeader
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public nint Next;
        public nint Reserved;
        public uint Offset;
        public nint Reserved0;
        public nint Reserved1;
        public nint Reserved2;
        public nint Reserved3;
        public nint Reserved4;
        public nint Reserved5;
        public nint Reserved6;
        public nint Reserved7;
    }

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutOpen(out nint handle, uint deviceId, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutShortMsg(nint handle, uint message);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutGetNumDevs();

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutGetDevCapsW(uint deviceId, ref MidiOutCaps caps, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutPrepareHeader(nint handle, ref MidiHeader header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutLongMsg(nint handle, ref MidiHeader header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutUnprepareHeader(nint handle, ref MidiHeader header, uint size);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutReset(nint handle);

    [LibraryImport("winmm.dll")]
    private static partial uint midiOutClose(nint handle);
}
