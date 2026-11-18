// A FLAC decoder for the player's own music, which CorsixTH plays through SDL_mixer (CorsixTH/Lua/audio.lua's
// allowed_waveform_formats has "FLAC"). Written from the format's specification (RFC 9639): the STREAMINFO block,
// frames with their headers, constant, verbatim, fixed and linear prediction subframes, Rice-coded residuals and the
// stereo decorrelation modes. Checksums are not verified.

namespace OpenWards.Client.Audio;

/// <summary>Decodes a FLAC stream a frame at a time into interleaved floats from -1 to 1.</summary>
internal sealed class FlacDecoder : IPcmSource
{
    private static readonly int[] SampleRates = [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000];
    private static readonly int[] SampleSizes = [0, 8, 12, 0, 16, 20, 24, 32];

    private readonly BitReader _bits;
    private readonly Stream _stream;
    private readonly int _streamBitsPerSample;
    private float[] _decoded = [];
    private int _decodedAt;
    private long[][] _channels = [];

    /// <exception cref="InvalidDataException">Not a FLAC stream.</exception>
    public FlacDecoder(Stream stream)
    {
        _stream = stream;
        _bits = new BitReader(stream);
        if (_bits.Read(32) != 0x664C6143)
        {
            throw new InvalidDataException("Not a FLAC stream (no fLaC marker).");
        }

        var last = false;
        var sawStreamInfo = false;
        while (!last)
        {
            last = _bits.Read(1) == 1;
            var type = (int)_bits.Read(7);
            var length = (int)_bits.Read(24);
            if (type == 0)
            {
                // STREAMINFO: block and frame sizes, then the sample rate, channels, bits per sample and length.
                _bits.Read(16);
                _bits.Read(16);
                _bits.Read(24);
                _bits.Read(24);
                SampleRate = (int)_bits.Read(20);
                Channels = (int)_bits.Read(3) + 1;
                _streamBitsPerSample = (int)_bits.Read(5) + 1;
                _bits.Read(36);
                _bits.Skip(16 * 8);
                sawStreamInfo = true;
            }
            else
            {
                _bits.Skip(length * 8L);
            }
        }

        if (!sawStreamInfo)
        {
            throw new InvalidDataException("A FLAC stream without STREAMINFO.");
        }
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int Read(float[] buffer)
    {
        var written = 0;
        while (written < buffer.Length)
        {
            if (_decodedAt >= _decoded.Length && !DecodeFrame())
            {
                break;
            }

            var count = Math.Min(buffer.Length - written, _decoded.Length - _decodedAt);
            Array.Copy(_decoded, _decodedAt, buffer, written, count);
            _decodedAt += count;
            written += count;
        }

        return written;
    }

    public void Dispose() => _stream.Dispose();

    /// <summary>Decodes the next frame into <see cref="_decoded"/>; false at the end of the stream.</summary>
    private bool DecodeFrame()
    {
        _bits.AlignToByte();
        if (_bits.AtEnd)
        {
            return false;
        }

        var sync = _bits.TryRead(14);
        if (sync is null)
        {
            return false;
        }

        if (sync != 0x3FFE)
        {
            throw new InvalidDataException("Lost the FLAC frame sync.");
        }

        _bits.Read(1); // reserved
        _bits.Read(1); // blocking strategy
        var blockSizeCode = (int)_bits.Read(4);
        var sampleRateCode = (int)_bits.Read(4);
        var assignment = (int)_bits.Read(4);
        var sampleSizeCode = (int)_bits.Read(3);
        _bits.Read(1);
        ReadCodedNumber();
        var blockSize = blockSizeCode switch
        {
            0 => throw new InvalidDataException("A reserved FLAC block size."),
            1 => 192,
            >= 2 and <= 5 => 576 << (blockSizeCode - 2),
            6 => (int)_bits.Read(8) + 1,
            7 => (int)_bits.Read(16) + 1,
            _ => 256 << (blockSizeCode - 8),
        };
        switch (sampleRateCode)
        {
            case 12:
                _bits.Read(8);
                break;
            case 13:
            case 14:
                _bits.Read(16);
                break;
            case 15:
                throw new InvalidDataException("An invalid FLAC sample rate.");
        }

        _bits.Read(8); // CRC-8
        var bitsPerSample = sampleSizeCode == 0 ? _streamBitsPerSample : SampleSizes[sampleSizeCode];
        if (bitsPerSample == 0 || assignment > 10)
        {
            throw new InvalidDataException("A reserved FLAC sample size or channel assignment.");
        }

        var channels = assignment < 8 ? assignment + 1 : 2;
        if (_channels.Length != channels || _channels[0].Length < blockSize)
        {
            _channels = [.. Enumerable.Range(0, channels).Select(_ => new long[Math.Max(blockSize, 4096)])];
        }

        for (var channel = 0; channel < channels; channel++)
        {
            // The side channel of a stereo pair has one bit more.
            var side = (assignment == 8 && channel == 1) || (assignment == 9 && channel == 0) || (assignment == 10 && channel == 1);
            DecodeSubframe(_channels[channel], blockSize, bitsPerSample + (side ? 1 : 0));
        }

        _bits.AlignToByte();
        _bits.Read(16); // CRC-16

        Decorrelate(assignment, blockSize);
        var scale = 1f / (1L << (bitsPerSample - 1));
        var outputChannels = Math.Min(channels, Channels);
        if (_decoded.Length != blockSize * Channels)
        {
            _decoded = new float[blockSize * Channels];
        }

        for (var i = 0; i < blockSize; i++)
        {
            for (var channel = 0; channel < Channels; channel++)
            {
                _decoded[(i * Channels) + channel] = _channels[Math.Min(channel, outputChannels - 1)][i] * scale;
            }
        }

        _decodedAt = 0;
        return true;
    }

    /// <summary>The frame or sample number, coded as UTF-8 is (up to seven bytes); only skipped here.</summary>
    private void ReadCodedNumber()
    {
        var first = (int)_bits.Read(8);
        var extra = first < 0x80 ? 0 : first < 0xE0 ? 1 : first < 0xF0 ? 2 : first < 0xF8 ? 3 : first < 0xFC ? 4 : first < 0xFE ? 5 : 6;
        for (var i = 0; i < extra; i++)
        {
            _bits.Read(8);
        }
    }

    private void DecodeSubframe(long[] samples, int blockSize, int bitsPerSample)
    {
        if (_bits.Read(1) != 0)
        {
            throw new InvalidDataException("A FLAC subframe without its zero bit.");
        }

        var type = (int)_bits.Read(6);
        var wasted = 0;
        if (_bits.Read(1) == 1)
        {
            wasted = 1;
            while (_bits.Read(1) == 0)
            {
                wasted++;
            }

            bitsPerSample -= wasted;
        }

        switch (type)
        {
            case 0:
                var constant = _bits.ReadSigned(bitsPerSample);
                Array.Fill(samples, constant, 0, blockSize);
                break;
            case 1:
                for (var i = 0; i < blockSize; i++)
                {
                    samples[i] = _bits.ReadSigned(bitsPerSample);
                }

                break;
            case >= 8 and <= 12:
                DecodeFixed(samples, blockSize, bitsPerSample, type - 8);
                break;
            case >= 32:
                DecodeLpc(samples, blockSize, bitsPerSample, type - 31);
                break;
            default:
                throw new InvalidDataException($"A reserved FLAC subframe type {type}.");
        }

        if (wasted > 0)
        {
            for (var i = 0; i < blockSize; i++)
            {
                samples[i] <<= wasted;
            }
        }
    }

    private void DecodeFixed(long[] samples, int blockSize, int bitsPerSample, int order)
    {
        for (var i = 0; i < order; i++)
        {
            samples[i] = _bits.ReadSigned(bitsPerSample);
        }

        DecodeResidual(samples, blockSize, order);
        for (var i = order; i < blockSize; i++)
        {
            samples[i] += order switch
            {
                0 => 0,
                1 => samples[i - 1],
                2 => (2 * samples[i - 1]) - samples[i - 2],
                3 => (3 * samples[i - 1]) - (3 * samples[i - 2]) + samples[i - 3],
                _ => (4 * samples[i - 1]) - (6 * samples[i - 2]) + (4 * samples[i - 3]) - samples[i - 4],
            };
        }
    }

    private void DecodeLpc(long[] samples, int blockSize, int bitsPerSample, int order)
    {
        for (var i = 0; i < order; i++)
        {
            samples[i] = _bits.ReadSigned(bitsPerSample);
        }

        var precision = (int)_bits.Read(4) + 1;
        if (precision == 16)
        {
            throw new InvalidDataException("An invalid FLAC coefficient precision.");
        }

        var shift = (int)_bits.ReadSigned(5);
        if (shift < 0)
        {
            throw new InvalidDataException("A negative FLAC prediction shift.");
        }

        Span<long> coefficients = stackalloc long[order];
        for (var i = 0; i < order; i++)
        {
            coefficients[i] = _bits.ReadSigned(precision);
        }

        DecodeResidual(samples, blockSize, order);
        for (var i = order; i < blockSize; i++)
        {
            long prediction = 0;
            for (var j = 0; j < order; j++)
            {
                prediction += coefficients[j] * samples[i - 1 - j];
            }

            samples[i] += prediction >> shift;
        }
    }

    /// <summary>The Rice-coded residual after the warm-up samples, written over <paramref name="samples"/> from <paramref name="order"/>.</summary>
    private void DecodeResidual(long[] samples, int blockSize, int order)
    {
        var method = (int)_bits.Read(2);
        if (method > 1)
        {
            throw new InvalidDataException("A reserved FLAC residual coding method.");
        }

        var parameterBits = method == 0 ? 4 : 5;
        var escape = (1 << parameterBits) - 1;
        var partitionOrder = (int)_bits.Read(4);
        var partitions = 1 << partitionOrder;
        var perPartition = blockSize >> partitionOrder;
        var at = order;
        for (var partition = 0; partition < partitions; partition++)
        {
            var count = partition == 0 ? perPartition - order : perPartition;
            var parameter = (int)_bits.Read(parameterBits);
            if (parameter == escape)
            {
                var bits = (int)_bits.Read(5);
                for (var i = 0; i < count; i++)
                {
                    samples[at++] = bits == 0 ? 0 : _bits.ReadSigned(bits);
                }

                continue;
            }

            for (var i = 0; i < count; i++)
            {
                long quotient = 0;
                while (_bits.Read(1) == 0)
                {
                    quotient++;
                }

                var value = (quotient << parameter) | (parameter == 0 ? 0 : (long)_bits.Read(parameter));
                samples[at++] = (value >> 1) ^ -(value & 1);
            }
        }
    }

    /// <summary>Left/side, side/right and mid/side back to left and right.</summary>
    private void Decorrelate(int assignment, int blockSize)
    {
        if (assignment < 8)
        {
            return;
        }

        var first = _channels[0];
        var second = _channels[1];
        for (var i = 0; i < blockSize; i++)
        {
            switch (assignment)
            {
                case 8:
                    second[i] = first[i] - second[i];
                    break;
                case 9:
                    first[i] += second[i];
                    break;
                default:
                    var side = second[i];
                    var mid = (first[i] << 1) | (side & 1);
                    first[i] = (mid + side) >> 1;
                    second[i] = (mid - side) >> 1;
                    break;
            }
        }
    }

    /// <summary>Reads a stream a bit at a time, most significant bit first.</summary>
    private sealed class BitReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[64 * 1024];
        private int _length;
        private int _position;
        private ulong _cache;
        private int _cached;

        public bool AtEnd => _cached == 0 && !Fill();

        public ulong Read(int count) => TryRead(count) ?? throw new InvalidDataException("The FLAC stream ends in the middle of a frame.");

        public ulong? TryRead(int count)
        {
            ulong value = 0;
            while (count > 0)
            {
                if (_cached == 0 && !Fill())
                {
                    return null;
                }

                var take = Math.Min(count, _cached);
                var shifted = _cache >> (_cached - take);
                value = (value << take) | (shifted & ((1UL << take) - 1));
                _cached -= take;
                count -= take;
            }

            return value;
        }

        public long ReadSigned(int count)
        {
            var value = (long)Read(count);
            return count == 64 ? value : (value << (64 - count)) >> (64 - count);
        }

        public void Skip(long bits)
        {
            while (bits > 0)
            {
                var take = (int)Math.Min(bits, 32);
                Read(take);
                bits -= take;
            }
        }

        public void AlignToByte() => _cached -= _cached % 8;

        private bool Fill()
        {
            // A byte at a time into the cache; the cache holds whole bytes until bits are taken.
            if (_position >= _length)
            {
                _length = stream.Read(_buffer, 0, _buffer.Length);
                _position = 0;
                if (_length <= 0)
                {
                    _length = 0;
                    return false;
                }
            }

            _cache = (_cache << 8) | _buffer[_position++];
            _cached += 8;
            return true;
        }
    }
}
