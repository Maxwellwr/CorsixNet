// The fax as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/fullscreen/fax.lua (UIFax: the message,
// its choices, the keypad, cancel, correct and validate, the close button).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Maps;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>One fax: its lines and its choices, each with whether it can be taken and what it does.</summary>
internal sealed record FaxMessage(IReadOnlyList<string> Lines, IReadOnlyList<(string Text, bool Enabled, Action Choose)> Choices);

/// <summary>
/// The fullscreen fax over Fax01V: the first fax waiting (centred lines in Font51V), its choices with their buttons
/// (greyed out when they cannot be taken), and the keypad for codes; close. With no fax waiting it shows only the keypad.
/// </summary>
internal sealed class FaxScreen
{
    private const string Palette = "FAX01V";

    private static readonly Rectangle Close = new(598, 440, 26, 26);
    private static readonly Rectangle CancelCode = new(471, 349, 87, 20);
    private static readonly Rectangle Validate = new(474, 372, 91, 27);
    private static readonly Rectangle Correct = new(168, 348, 43, 10);

    /// <summary>The keypad's keys: what each types, where it is and its pressed sprite (fax.lua's buttons).</summary>
    private static readonly (string Key, Rectangle Area, int Sprite)[] Keys =
    [
        ("1", new(220, 348, 43, 10), 2), ("2", new(272, 348, 44, 10), 3), ("3", new(327, 348, 43, 10), 4),
        ("4", new(219, 358, 44, 10), 5), ("5", new(272, 358, 43, 10), 6), ("6", new(326, 358, 44, 10), 7),
        ("7", new(218, 370, 44, 11), 8), ("8", new(271, 370, 44, 11), 9), ("9", new(326, 370, 44, 11), 10),
        ("*", new(217, 382, 45, 12), 11), ("0", new(271, 382, 44, 11), 12), ("#", new(326, 382, 44, 11), 13),
    ];

    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("FAX01V") is not null && graphics.Sheet("FAX02V", Palette) is not null
        && graphics.Font("FONT51V", Palette) is not null;

    /// <summary>A choice's button for <paramref name="index"/> of <paramref name="count"/> (they fill the three places from the bottom).</summary>
    public static Rectangle ChoiceButton(int index, int count) => new(492, 175 + ((index + 3 - count) * 48), 43, 43);

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, FaxMessage? message)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("FAX02V", Palette)!;
        var font = graphics.Font("FONT51V", Palette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        batch.Draw(graphics.Picture("FAX01V")!, origin.ToVector2(), Color.White);
        void Pressed(string key, Rectangle area, int sprite)
        {
            if (held && _pressed == key && area.Contains(local))
            {
                sheet.Draw(batch, sprite, origin.X + area.X, origin.Y + area.Y, Color.White);
            }
        }

        Pressed("close", Close, 16);
        Pressed("cancel", CancelCode, 14);
        Pressed("validate", Validate, 15);
        Pressed("correct", Correct, 1);
        foreach (var (key, area, sprite) in Keys)
        {
            Pressed("key" + key, area, sprite);
        }

        if (message is null)
        {
            return;
        }

        var y = 40;
        foreach (var text in message.Lines)
        {
            foreach (var line in MapViewScreen.WrapToWidth(text, 360, t => font.Measure(t).Width))
            {
                font.Draw(batch, line, new Vector2(origin.X + 190 + ((360 - font.Measure(line).Width) / 2), origin.Y + y));
                y += font.LineHeight;
            }
        }

        for (var i = 0; i < message.Choices.Count; i++)
        {
            var (text, enabled, _) = message.Choices[i];
            var button = ChoiceButton(i, message.Choices.Count);
            sheet.Draw(batch, !enabled ? 19 : held && _pressed == "choice" + i && button.Contains(local) ? 18 : 17, origin.X + button.X, origin.Y + button.Y, Color.White);
            var lineY = button.Y + 15;
            foreach (var line in MapViewScreen.WrapToWidth(text, 300, t => font.Measure(t).Width))
            {
                font.Draw(batch, line, new Vector2(origin.X + 190, origin.Y + lineY));
                lineY += font.LineHeight;
            }
        }
    }

    /// <summary>The control let go over: "close", "cancel", "validate", "correct", "key" + what it types, or "choice" + its number.</summary>
    public string? Clicked(Point local, MouseState mouse, MouseState previous, FaxMessage? message)
    {
        var under = ButtonAt(local, message);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under;
            return null;
        }

        if (mouse.LeftButton != ButtonState.Released || previous.LeftButton != ButtonState.Pressed)
        {
            return null;
        }

        var pressed = _pressed;
        _pressed = null;
        return pressed is not null && pressed == under ? pressed : null;
    }

    private static string? ButtonAt(Point local, FaxMessage? message)
    {
        if (Close.Contains(local))
        {
            return "close";
        }

        if (CancelCode.Contains(local))
        {
            return "cancel";
        }

        if (Validate.Contains(local))
        {
            return "validate";
        }

        if (Correct.Contains(local))
        {
            return "correct";
        }

        foreach (var (key, area, _) in Keys)
        {
            if (area.Contains(local))
            {
                return "key" + key;
            }
        }

        for (var i = 0; message is not null && i < message.Choices.Count; i++)
        {
            if (message.Choices[i].Enabled && ChoiceButton(i, message.Choices.Count).Contains(local))
            {
                return "choice" + i;
            }
        }

        return null;
    }
}
