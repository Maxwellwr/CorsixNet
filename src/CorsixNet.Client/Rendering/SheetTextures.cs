using CorsixNet.Formats;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>GPU textures for every sprite of a sheet, drawn with one palette.</summary>
internal sealed class SheetTextures : IDisposable
{
    private readonly Texture2D?[] _textures;

    public SheetTextures(GraphicsDevice device, SpriteSheet sheet, Palette palette)
    {
        Sheet = sheet;
        _textures = new Texture2D?[sheet.Count];
        for (var i = 0; i < sheet.Count; i++)
        {
            _textures[i] = TextureFactory.Create(device, sheet[i], palette);
        }
    }

    public SpriteSheet Sheet { get; }

    public int Count => _textures.Length;

    /// <summary>The texture of sprite <paramref name="index"/>, or null if the sprite is empty.</summary>
    public Texture2D? this[int index] => _textures[index];

    public void Dispose()
    {
        foreach (var texture in _textures)
        {
            texture?.Dispose();
        }
    }
}
