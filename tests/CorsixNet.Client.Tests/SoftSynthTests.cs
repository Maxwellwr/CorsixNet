using System.Text;
using CorsixNet.Client.Audio;
using MeltySynth;

namespace CorsixNet.Client.Tests;

/// <summary>The software synthesiser the music plays on by default (CorsixTH's FluidSynth with a SoundFont).</summary>
public class SoftSynthTests
{
    [Fact]
    public void TheSoundFontIsLookedForWhereCorsixThLooks()
    {
        var game = Path.Combine("game");
        Assert.Equal("my.sf2", SoundFonts.Find("my.sf2", game, _ => true));
        Assert.Equal(Path.Combine(game, "FluidR3_GM.sf2"), SoundFonts.Find(string.Empty, game, path => path.EndsWith("FluidR3_GM.sf2", StringComparison.Ordinal)));
        Assert.Equal("/usr/share/sounds/sf2/FluidR3_GM.sf2", SoundFonts.Find(null, game, path => path == "/usr/share/sounds/sf2/FluidR3_GM.sf2"));
        Assert.Null(SoundFonts.Find("missing.sf2", game, _ => false));
    }

    [Fact]
    public void ANotePlaysUntilItIsLetGoAndSystemExclusiveIsLeftOut()
    {
        using var output = new SoftSynthMidiOutput(new SoundFont(new MemoryStream(TinySoundFont())));
        var buffer = new short[4096];
        output.Render(buffer);
        Assert.All(buffer, sample => Assert.Equal(0, sample));

        output.Send([0xF0, 0x7E, 0x7F, 0x09, 0x01, 0xF7]);
        output.Send([0x90, 60, 100]);
        output.Render(buffer);
        var peak = buffer.Max(sample => Math.Abs((int)sample));
        Assert.True(peak > 100);

        // Let go, the note ends; only the reverb's tail dies away after it.
        output.Send([0x80, 60, 0]);
        for (var i = 0; i < 40; i++)
        {
            output.Render(buffer);
        }

        Assert.True(buffer.Max(sample => Math.Abs((int)sample)) < peak / 4);
    }

    /// <summary>
    /// A SoundFont of one preset with one instrument whose one sample is a looped square wave, after the SoundFont 2.01
    /// specification's chunks.
    /// </summary>
    private static byte[] TinySoundFont()
    {
        const int length = 200;
        var samples = new List<byte>();
        for (var i = 0; i < length + 46; i++)
        {
            var value = (short)(i < length ? ((i / 25 % 2 == 0) ? 8000 : -8000) : 0);
            samples.AddRange(BitConverter.GetBytes(value));
        }

        static byte[] Name(string text) => [.. Encoding.ASCII.GetBytes(text.PadRight(20, '\0'))];
        static byte[] Chunk(string id, byte[] data) => [.. Encoding.ASCII.GetBytes(id), .. BitConverter.GetBytes(data.Length), .. data, .. data.Length % 2 == 1 ? new byte[1] : []];
        static byte[] List(string type, params byte[][] chunks) => Chunk("LIST", [.. Encoding.ASCII.GetBytes(type), .. chunks.SelectMany(c => c)]);
        static byte[] U16(int value) => BitConverter.GetBytes((ushort)value);
        static byte[] U32(int value) => BitConverter.GetBytes(value);

        var info = List("INFO", Chunk("ifil", [.. U16(2), .. U16(1)]), Chunk("isng", Encoding.ASCII.GetBytes("EMU8000\0")), Chunk("INAM", Encoding.ASCII.GetBytes("Tiny\0\0")));
        var sdta = List("sdta", Chunk("smpl", [.. samples]));
        var phdr = Chunk("phdr", [.. Name("Square"), .. U16(0), .. U16(0), .. U16(0), .. U32(0), .. U32(0), .. U32(0),
            .. Name("EOP"), .. U16(0), .. U16(0), .. U16(1), .. U32(0), .. U32(0), .. U32(0)]);
        var pbag = Chunk("pbag", [.. U16(0), .. U16(0), .. U16(1), .. U16(0)]);
        var pmod = Chunk("pmod", new byte[10]);
        var pgen = Chunk("pgen", [.. U16(41), .. U16(0), .. U16(0), .. U16(0)]);
        var inst = Chunk("inst", [.. Name("Square"), .. U16(0), .. Name("EOI"), .. U16(1)]);
        var ibag = Chunk("ibag", [.. U16(0), .. U16(0), .. U16(2), .. U16(0)]);
        var imod = Chunk("imod", new byte[10]);
        var igen = Chunk("igen", [.. U16(54), .. U16(1), .. U16(53), .. U16(0), .. U16(0), .. U16(0)]);
        var shdr = Chunk("shdr", [.. Name("Square"), .. U32(0), .. U32(length), .. U32(0), .. U32(length), .. U32(22050), 60, 0, .. U16(0), .. U16(1),
            .. Name("EOS"), .. U32(0), .. U32(0), .. U32(0), .. U32(0), .. U32(0), 0, 0, .. U16(0), .. U16(0)]);
        var pdta = List("pdta", phdr, pbag, pmod, pgen, inst, ibag, imod, igen, shdr);
        byte[] body = [.. Encoding.ASCII.GetBytes("sfbk"), .. info, .. sdta, .. pdta];
        return [.. Encoding.ASCII.GetBytes("RIFF"), .. BitConverter.GetBytes(body.Length), .. body];
    }
}
