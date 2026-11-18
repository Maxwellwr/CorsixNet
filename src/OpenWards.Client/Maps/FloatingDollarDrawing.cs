// Ported from CorsixTH CorsixTH/Lua/world.lua (World:newFloatingDollarSign) and CorsixTH/Lua/graphics.lua
// (Graphics:getFloatingBanknoteSpriteTable).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenWards.Client.Rendering;
using OpenWards.Formats;
using OpenWards.Sim.Economy;

namespace OpenWards.Client.Maps;

/// <summary>
/// Draws the banknotes floating up from patients who have just paid: sprite 1 of <c>DATA/Money01V</c> with the amount's
/// digits (sprites 2 to 11) on it, green, or blue when an insurance company pays and the setting says so (CorsixTH's
/// <c>enable_insurance_blue_banknote</c>, Graphics:getFloatingBanknoteSpriteTable).
/// </summary>
internal sealed class FloatingDollarDrawing : IDisposable
{
    private const int NoteSprite = 1;
    private const int FirstDigitSprite = 2;

    private readonly SpriteAtlas _green;
    private readonly SpriteAtlas _blue;

    public FloatingDollarDrawing(GraphicsDevice device, ThemeHospitalInstall install, Palette palette, int scale)
    {
        var sheet = install.LoadSpriteSheet("DATA", "MONEY01V", complex: false);
        _green = new SpriteAtlas(device, sheet, palette, scale);
        _blue = new SpriteAtlas(device, sheet, Palette.Parse(BanknotePalette.MakeBlue(install.ReadDataFile("DATA", "MPALETTE.DAT"))), scale);
    }

    public void SetScale(int scale)
    {
        _green.SetScale(scale);
        _blue.SetScale(scale);
    }

    /// <summary>
    /// Draws <paramref name="sign"/> above the tile drawn at (<paramref name="anchorX"/>, <paramref name="anchorY"/>), blue
    /// for an insurance company's payment when <paramref name="insuranceBlue"/>.
    /// </summary>
    public void Draw(SpriteBatch batch, FloatingDollarSign sign, int anchorX, int anchorY, long tick, bool insuranceBlue = true)
    {
        var atlas = sign.Insurance && insuranceBlue ? _blue : _green;
        var x = anchorX + FloatingDollarSign.StartX;
        var y = anchorY + FloatingDollarSign.StartY - (int)Math.Clamp(sign.Age(tick), 0, FloatingDollarSign.Lifetime);
        atlas.Draw(batch, NoteSprite, x, y, Color.White);

        // The digits are centred on the note: xbase = floor(10.5 + (20 - 5 * len) / 2), five pixels apart.
        var digits = sign.Amount.ToString(CultureInfo.InvariantCulture);
        var xBase = (int)Math.Floor(10.5 + ((20 - (5.0 * digits.Length)) / 2));
        for (var i = 0; i < digits.Length; i++)
        {
            if (digits[i] is >= '0' and <= '9')
            {
                atlas.Draw(batch, FirstDigitSprite + (digits[i] - '0'), x + xBase + (5 * i), y + 5, Color.White);
            }
        }
    }

    public void Dispose()
    {
        _green.Dispose();
        _blue.Dispose();
    }
}
