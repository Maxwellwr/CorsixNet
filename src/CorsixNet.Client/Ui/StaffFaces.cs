// Staff portraits, ported from CorsixTH CorsixTH/Lua/staff_profile.lua (StaffProfile:drawFace) and dialogs/hire_staff.lua
// (the Face01V parts bitmap).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Formats;
using CorsixNet.Sim.Staff;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CorsixNet.Client.Ui;

/// <summary>
/// The parts staff portraits are made of: <c>QData/Face01V.dat</c>, a 65 by 1350 raw bitmap in the main palette with the
/// hair (29 pixels high each), faces (24 high, from 522) and chins (22 high, from 954) one under the other.
/// </summary>
internal sealed class StaffFaces
{
    /// <summary>A portrait's width.</summary>
    public const int Width = 65;

    /// <summary>A portrait's height: hair, face and chin.</summary>
    public const int Height = 29 + 24 + 22;

    private const int BitmapHeight = 1350;

    private readonly Texture2D _parts;

    private StaffFaces(Texture2D parts) => _parts = parts;

    /// <summary>The faces of the game being played, once loaded; null without the original's data.</summary>
    public static StaffFaces? Current { get; set; }

    /// <summary>Loads the parts bitmap, or null when the install lacks it.</summary>
    public static StaffFaces? Load(GraphicsDevice device, ThemeHospitalInstall install, Palette palette)
    {
        try
        {
            var image = install.LoadRawBitmap("QDATA", "FACE01V", Width, BitmapHeight);
            return TextureFactory.Create(device, image, palette) is { } texture ? new StaffFaces(texture) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where <c>drawFace</c> takes each part from and where it goes, relative to the portrait's top left: the hair, then the
    /// face 29 pixels down, then the chin 53 down.
    /// </summary>
    public static IReadOnlyList<(Rectangle Source, Point Offset)> Parts(StaffProfile profile) =>
    [
        (new Rectangle(0, profile.HairIndex * 29, Width, 29), Point.Zero),
        (new Rectangle(0, 522 + (profile.FaceIndex * 24), Width, 24), new Point(0, 29)),
        (new Rectangle(0, 954 + (profile.ChinIndex * 22), Width, 22), new Point(0, 53)),
    ];

    /// <summary>Draws the portrait with its top left at <paramref name="at"/>, <paramref name="scale"/> times its size.</summary>
    public void Draw(SpriteBatch batch, StaffProfile profile, Point at, int scale = 1)
    {
        foreach (var (source, offset) in Parts(profile))
        {
            if (source.Bottom > BitmapHeight)
            {
                continue;
            }

            var destination = new Rectangle(at.X + (offset.X * scale), at.Y + (offset.Y * scale), source.Width * scale, source.Height * scale);
            batch.Draw(_parts, destination, source, Color.White);
        }
    }
}
