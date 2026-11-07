// Layout and interpretation ported from CorsixTH CorsixTH/Src/th_gfx.cpp (animation_manager::load_from_th_file).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;

namespace CorsixNet.Formats;

/// <summary>One frame of an animation.</summary>
/// <param name="FirstElement">Index into the element list where this frame's elements start.</param>
/// <param name="Sound">Sound effect to play on this frame, or 0.</param>
/// <param name="Flags">Frame flags from the data file.</param>
/// <param name="Next">Index of the following frame; the last frame of a loop points back to the first.</param>
public readonly record struct AnimationFrame(int FirstElement, byte Sound, byte Flags, int Next);

/// <summary>One sprite drawn as part of a frame.</summary>
/// <param name="Sprite">Sprite number in the animation sprite sheet (<c>VSPR-0</c>).</param>
/// <param name="X">Horizontal offset from the animation's anchor point.</param>
/// <param name="Y">Vertical offset from the animation's anchor point.</param>
/// <param name="Flags">Draw flags: 1 flip horizontally, 2 flip vertically, 4 50% transparent, 8 75% transparent.</param>
/// <param name="Layer">Layer class, 0–12 (e.g. head, clothes); see <paramref name="LayerId"/>.</param>
/// <param name="LayerId">Variant within the layer; 0 means the element is always drawn.</param>
public readonly record struct AnimationElement(int Sprite, int X, int Y, byte Flags, byte Layer, byte LayerId);

/// <summary>
/// The animations of the original game: <c>VSTART-1.ANI</c>, <c>VFRA-1.ANI</c>, <c>VLIST-1.ANI</c>
/// and <c>VELE-1.ANI</c>, drawn with the sprites of <c>VSPR-0</c>.
/// </summary>
/// <remarks>
/// An animation names its first frame; frames link to the next frame and to a run of the element
/// list, which ends at an out-of-range element number. Elements place sprites relative to an anchor
/// point, which is the top corner of the tile the animation stands on.
/// </remarks>
public sealed class AnimationSet
{
    /// <summary>Number of layer classes an element can belong to.</summary>
    public const int LayerCount = 13;

    private const int StartEntrySize = 4;
    private const int FrameEntrySize = 10;
    private const int ElementEntrySize = 6;
    private const int SpriteTableEntrySize = 6;

    /// <summary>Elements store offsets biased by these amounts.</summary>
    private const int OffsetBiasX = 141;

    private const int OffsetBiasY = 186;

    private readonly int[] _firstFrames;
    private readonly AnimationFrame[] _frames;
    private readonly int[] _elementList;
    private readonly AnimationElement[] _elements;

    private AnimationSet(int[] firstFrames, AnimationFrame[] frames, int[] elementList, AnimationElement[] elements)
    {
        _firstFrames = firstFrames;
        _frames = frames;
        _elementList = elementList;
        _elements = elements;
    }

    public int AnimationCount => _firstFrames.Length;

    public int FrameCount => _frames.Length;

    /// <summary>First frame of animation <paramref name="animation"/>.</summary>
    public int FirstFrame(int animation) => _firstFrames[animation];

    public AnimationFrame Frame(int frame) => _frames[frame];

    /// <summary>Number of frames in the loop starting at <paramref name="firstFrame"/>.</summary>
    public int Length(int firstFrame)
    {
        var length = 1;
        for (var frame = _frames[firstFrame].Next; frame != firstFrame && length <= _frames.Length; frame = _frames[frame].Next)
        {
            length++;
        }

        return length;
    }

    /// <summary>The frame reached by following <paramref name="steps"/> links from <paramref name="firstFrame"/>.</summary>
    public int Advance(int firstFrame, int steps)
    {
        var frame = firstFrame;
        for (var i = 0; i < steps; i++)
        {
            frame = _frames[frame].Next;
        }

        return frame;
    }

    /// <summary>The elements of a frame, in drawing order.</summary>
    public IEnumerable<AnimationElement> Elements(int frame)
    {
        for (var i = _frames[frame].FirstElement; i < _elementList.Length; i++)
        {
            var element = _elementList[i];
            if (element < 0)
            {
                yield break;
            }

            yield return _elements[element];
        }
    }

    /// <summary>Parses the four decompressed animation files.</summary>
    /// <exception cref="InvalidDataException">A file is empty.</exception>
    public static AnimationSet Parse(ReadOnlySpan<byte> starts, ReadOnlySpan<byte> frames, ReadOnlySpan<byte> lists, ReadOnlySpan<byte> elements)
    {
        var animationCount = starts.Length / StartEntrySize;
        var frameCount = frames.Length / FrameEntrySize;
        var listCount = lists.Length / 2;
        var elementCount = elements.Length / ElementEntrySize;
        if (animationCount == 0 || frameCount == 0 || listCount == 0 || elementCount == 0)
        {
            throw new InvalidDataException("Animation data is empty.");
        }

        // Out-of-range references fall back to 0, as in CorsixTH.
        var firstFrames = new int[animationCount];
        for (var i = 0; i < animationCount; i++)
        {
            int first = BinaryPrimitives.ReadUInt16LittleEndian(starts[(i * StartEntrySize)..]);
            firstFrames[i] = first < frameCount ? first : 0;
        }

        var frameArray = new AnimationFrame[frameCount];
        for (var i = 0; i < frameCount; i++)
        {
            var raw = frames.Slice(i * FrameEntrySize, FrameEntrySize);
            var listIndex = BinaryPrimitives.ReadUInt32LittleEndian(raw);
            int next = BinaryPrimitives.ReadUInt16LittleEndian(raw[8..]);
            frameArray[i] = new AnimationFrame(
                listIndex < (uint)listCount ? (int)listIndex : 0,
                raw[6],
                raw[7],
                next < frameCount ? next : 0);
        }

        // The element list gets a terminator so that every run ends.
        var elementList = new int[listCount + 1];
        for (var i = 0; i < listCount; i++)
        {
            int element = BinaryPrimitives.ReadUInt16LittleEndian(lists[(i * 2)..]);
            elementList[i] = element < elementCount ? element : -1;
        }

        elementList[listCount] = -1;

        var elementArray = new AnimationElement[elementCount];
        for (var i = 0; i < elementCount; i++)
        {
            var raw = elements.Slice(i * ElementEntrySize, ElementEntrySize);
            var layer = (byte)(raw[4] >> 4);
            if (layer >= LayerCount)
            {
                // CorsixTH moves out-of-range layers to layer 6, where nothing else lives.
                layer = 6;
            }

            elementArray[i] = new AnimationElement(
                BinaryPrimitives.ReadUInt16LittleEndian(raw) / SpriteTableEntrySize,
                raw[2] - OffsetBiasX,
                raw[3] - OffsetBiasY,
                (byte)(raw[4] & 0x0F),
                layer,
                raw[5]);
        }

        return new AnimationSet(firstFrames, frameArray, elementList, elementArray);
    }

    /// <summary>Loads the original game's animations from the DATA folder.</summary>
    public static AnimationSet Load(ThemeHospitalInstall install)
    {
        return Parse(
            install.ReadDataFile("DATA", "VSTART-1.ANI"),
            install.ReadDataFile("DATA", "VFRA-1.ANI"),
            install.ReadDataFile("DATA", "VLIST-1.ANI"),
            install.ReadDataFile("DATA", "VELE-1.ANI"));
    }
}
