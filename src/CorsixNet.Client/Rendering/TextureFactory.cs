using CorsixNet.Formats;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Rendering;

/// <summary>Converts palette images into GPU textures.</summary>
internal static class TextureFactory
{
    /// <summary>
    /// Creates a texture with premultiplied alpha, as expected by <see cref="BlendState.AlphaBlend"/>.
    /// Returns null for empty images.
    /// </summary>
    public static Texture2D? Create(GraphicsDevice device, IndexedImage image, Palette palette)
    {
        if (image.IsEmpty)
        {
            return null;
        }

        var colors = new Color[image.Pixels.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            var c = palette[image.Pixels[i]];
            colors[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, (int)c.A);
        }

        var texture = new Texture2D(device, image.Width, image.Height);
        texture.SetData(colors);
        return texture;
    }
}
