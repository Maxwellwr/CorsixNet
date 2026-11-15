using System.Buffers.Binary;
using System.Text;

namespace CorsixNet.AnnouncerVoice;

/// <summary>Reading 16-bit PCM WAV files (as Piper and Vosk TTS write them) and writing the original's format, 8-bit mono.</summary>
internal static class Wav
{
    /// <summary>The samples of a 16-bit PCM WAV file, between -1 and 1, its channels mixed, and their rate.</summary>
    /// <exception cref="InvalidDataException">The file is not 16-bit PCM WAV.</exception>
    public static (float[] Samples, int Rate) Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        int channels = 0, rate = 0, bits = 0;
        var position = 12;
        while (position + 8 <= data.Length)
        {
            var id = data.Slice(position, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position + 4, 4)), (uint)(data.Length - position - 8));
            var body = data.Slice(position + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(body) != 1)
                {
                    throw new InvalidDataException("The WAV file is not PCM.");
                }

                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (bits != 16 || channels < 1 || rate <= 0)
                {
                    throw new InvalidDataException("The WAV file is not 16-bit PCM.");
                }

                var frames = size / (2 * channels);
                var samples = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    var sum = 0f;
                    for (var c = 0; c < channels; c++)
                    {
                        sum += BinaryPrimitives.ReadInt16LittleEndian(body[(((i * channels) + c) * 2)..]) / 32768f;
                    }

                    samples[i] = sum / channels;
                }

                return (samples, rate);
            }

            position += 8 + size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no data.");
    }

    /// <summary>
    /// An 8-bit unsigned mono WAV file of samples between -1 and 1 at <paramref name="rate"/>, as the original's
    /// announcements are; <paramref name="dither"/> gives each sample's dither, under one step either way.
    /// </summary>
    public static byte[] Write8Bit(ReadOnlySpan<float> samples, int rate, Func<float> dither)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + samples.Length + (samples.Length & 1));
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate);
            writer.Write((short)1);
            writer.Write((short)8);
            writer.Write("data"u8);
            writer.Write(samples.Length);
            foreach (var sample in samples)
            {
                var value = Math.Round((Math.Clamp(sample, -1f, 1f) * 127f) + dither() + 128f);
                writer.Write((byte)Math.Clamp(value, 0, 255));
            }

            if ((samples.Length & 1) != 0)
            {
                writer.Write((byte)128);
            }
        }

        return stream.ToArray();
    }
}
