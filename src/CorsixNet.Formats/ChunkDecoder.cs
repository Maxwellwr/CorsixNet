// Ported from CorsixTH CorsixTH/Src/th_gfx.cpp (chunk_renderer).
// Copyright (c) 2009 Peter "Corsix" Cawley and the CorsixTH contributors.
// Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Formats;

/// <summary>
/// Decodes the run-length "chunk" encoding of Theme Hospital sprites into palette indices.
/// </summary>
/// <remarks>
/// Simple encoding (most sheets): <c>0</c> fills to the end of the line with transparency,
/// <c>1–127</c> copies that many literal pixels, <c>128–255</c> skips <c>256 - b</c> transparent pixels.
/// Complex encoding (UI sheets) adds runs of a single colour.
/// Pixels not covered by the data are transparent.
/// </remarks>
internal ref struct ChunkDecoder
{
    private readonly Span<byte> _pixels;
    private readonly int _width;
    private int _position;
    private bool _skipEndOfLine;

    private ChunkDecoder(Span<byte> pixels, int width)
    {
        _pixels = pixels;
        _width = width;
        _position = 0;
        _skipEndOfLine = false;
    }

    public static byte[] Decode(ReadOnlySpan<byte> data, int width, int height, bool complex)
    {
        var pixels = new byte[width * height];
        var decoder = new ChunkDecoder(pixels, width);
        if (complex)
        {
            decoder.DecodeComplex(data);
        }
        else
        {
            decoder.DecodeSimple(data);
        }

        decoder.Fill(pixels.Length - decoder._position, Palette.TransparentIndex);
        return pixels;
    }

    private readonly bool IsDone => _position >= _pixels.Length;

    private void DecodeSimple(ReadOnlySpan<byte> data)
    {
        while (!IsDone && !data.IsEmpty)
        {
            var b = data[0];
            data = data[1..];
            if (b == 0)
            {
                FillToEndOfLine(Palette.TransparentIndex);
            }
            else if (b < 0x80)
            {
                var count = Math.Min(b, data.Length);
                Copy(data[..count]);
                data = data[count..];
            }
            else
            {
                Fill(0x100 - b, Palette.TransparentIndex);
            }
        }
    }

    private void DecodeComplex(ReadOnlySpan<byte> data)
    {
        while (!IsDone && !data.IsEmpty)
        {
            var b = data[0];
            data = data[1..];
            if (b == 0)
            {
                FillToEndOfLine(Palette.TransparentIndex);
            }
            else if (b < 0x40)
            {
                var count = Math.Min(b, data.Length);
                Copy(data[..count]);
                data = data[count..];
            }
            else if ((b & 0xC0) == 0x80)
            {
                Fill(b - 0x80, Palette.TransparentIndex);
            }
            else
            {
                int count;
                byte colour = 0;
                if (b == 0xFF)
                {
                    if (data.Length < 2)
                    {
                        break;
                    }

                    count = data[0];
                    colour = data[1];
                    data = data[2..];
                }
                else
                {
                    // 0x40–0x7F: 4–67 pixels; 0xC0–0xFE: 68–130 pixels.
                    count = b - 60 - ((b & 0x80) / 2);
                    if (!data.IsEmpty)
                    {
                        colour = data[0];
                        data = data[1..];
                    }
                }

                Fill(count, colour);
            }
        }
    }

    private void FillToEndOfLine(byte value)
    {
        // A line that was just completed exactly is not filled again.
        var x = _position % _width;
        if (x != 0 || !_skipEndOfLine)
        {
            Fill(_width - x, value);
        }

        _skipEndOfLine = false;
    }

    private void Fill(int count, byte value)
    {
        count = Math.Min(count, _pixels.Length - _position);
        if (count > 0)
        {
            _pixels.Slice(_position, count).Fill(value);
            Advance(count);
        }
    }

    private void Copy(ReadOnlySpan<byte> source)
    {
        var count = Math.Min(source.Length, _pixels.Length - _position);
        if (count > 0)
        {
            source[..count].CopyTo(_pixels[_position..]);
            Advance(count);
        }
    }

    private void Advance(int count)
    {
        _position += count;
        _skipEndOfLine = true;
    }
}
