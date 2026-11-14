// The hospital policy screen as the original draws it, ported from CorsixTH
// CorsixTH/Lua/dialogs/fullscreen/hospital_policy.lua (UIPolicy: its sliders, buttons, tooltips, draw, dragging, close).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The fullscreen hospital policy over Pol01V: sliders for sending home and guessing the cure (the first never past the
/// second), for when diagnosis stops and for how tired staff go to the staff room, dragged with the mouse; and whether
/// staff may leave their rooms. A slider's new value is sent when it is let go.
/// </summary>
internal sealed class PolicyScreen
{
    private const string Palette = "POL01V";

    private static readonly Slider[] Sliders =
    [
        // send_home is tested first, so that it can be moved when the big guess_cure slider lies on it.
        new(PolicyKind.SendHome, 1, 135, 82, 28, Left: 129, Range: 299, Base: 0, Label: 7, Fallback: "Send home"),
        new(PolicyKind.GuessCure, 2, 119, 82, 44, Left: 129, Range: 299, Base: 0, Label: 6, Fallback: "Guess cure"),
        new(PolicyKind.StopProcedure, 3, 210, 92, 28, Left: 124, Range: 299, Base: 1000, Label: 8, Fallback: "Stop procedure"),
        new(PolicyKind.GoToStaffRoom, 3, 285, 92, 28, Left: 149, Range: 250, Base: 0, Label: 9, Fallback: "Staff room"),
    ];

    private static readonly Rectangle Close = new(607, 447, 26, 26);
    private static readonly Rectangle Allow = new(348, 379, 48, 17);
    private static readonly Rectangle Prohibit = new(395, 379, 48, 17);

    private Slider? _dragging;
    private int _dragX;
    private int _grabOffset;
    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Picture("POL01V") is not null && graphics.Sheet("POL02V", Palette) is not null
        && graphics.Font("FONT74V", Palette) is not null && graphics.Font("FONT105V", Palette) is not null;

    /// <summary>Where a slider stands for a policy's value in thousandths (UIPolicy's slider positions).</summary>
    public static int SliderX(PolicyKind policy, int value)
    {
        var slider = Sliders.First(s => s.Policy == policy);
        return slider.Left + (int)Math.Floor((value - slider.Base) * slider.Range / 1000.0);
    }

    /// <summary>The value, in thousandths, of a slider standing at <paramref name="x"/> (<c>UIPolicy:close</c>).</summary>
    public static int SliderValue(PolicyKind policy, int x)
    {
        var slider = Sliders.First(s => s.Policy == policy);
        return slider.Base + (int)Math.Round((x - slider.Left) * 1000.0 / slider.Range);
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        var graphics = host.Graphics!;
        var policies = host.Simulation.Hospitals[host.Player].Policies;
        batch.Draw(graphics.Picture("POL01V")!, origin.ToVector2(), Color.White);
        var sheet = graphics.Sheet("POL02V", Palette)!;
        var label = graphics.Font("FONT74V", Palette)!;
        var text = graphics.Font("FONT105V", Palette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        if (held && _pressed == "close" && Close.Contains(local))
        {
            sheet.Draw(batch, 6, origin.X + Close.X, origin.Y + Close.Y, Color.White);
        }

        // The toggle buttons show the policy in force.
        if (policies.StaffAllowedToMove)
        {
            sheet.Draw(batch, 4, origin.X + Allow.X, origin.Y + Allow.Y, Color.White);
        }
        else
        {
            sheet.Draw(batch, 5, origin.X + Prohibit.X, origin.Y + Prohibit.Y, Color.White);
        }

        foreach (var slider in Sliders)
        {
            var x = slider == _dragging ? _dragX : SliderX(slider.Policy, Value(policies, slider.Policy));
            sheet.Draw(batch, slider.Sprite, origin.X + x, origin.Y + slider.Y, Color.White);
            var name = (host.Texts?.String(18, slider.Label) ?? slider.Fallback).ToUpperInvariant();
            label.Draw(batch, name, new Vector2(origin.X + x + ((slider.Width - label.Measure(name).Width) / 2), origin.Y + slider.Y + 2));
        }

        void At(string value, int x, int y) => text.Draw(batch, value, new Vector2(origin.X + x, origin.Y + y));
        var header = host.Texts?.String(18, 1) ?? "Hospital policy";
        At(header, 160 + ((300 - text.Measure(header).Width) / 2), 78);
        At(host.Texts?.String(18, 2) ?? "Diagnosis procedure", 161, 100);
        At(host.Texts?.String(18, 3) ?? "Diagnosis termination", 161, 181);
        At(host.Texts?.String(18, 4) ?? "Staff rest", 161, 262);
        At(host.Texts?.String(18, 5) ?? "Staff leave rooms", 161, 374);
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous)
    {
        var policies = host.Simulation.Hospitals[host.Player].Policies;
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            // UIPolicy:panelHit, then onMouseDown: the slider follows the mouse from where it was taken.
            _dragging = Sliders.FirstOrDefault(slider =>
            {
                var x = SliderX(slider.Policy, Value(policies, slider.Policy));
                return local.X > x && local.Y > slider.Y && local.X < x + slider.Width && local.Y < slider.Y + slider.Height;
            });
            if (_dragging is { } taken)
            {
                _dragX = SliderX(taken.Policy, Value(policies, taken.Policy));
                _grabOffset = local.X - _dragX;
                return;
            }

            _pressed = Close.Contains(local) ? "close" : Allow.Contains(local) ? "allow" : Prohibit.Contains(local) ? "prohibit" : null;
            return;
        }

        if (_dragging is { } slider)
        {
            var (low, high) = Limits(policies, slider);
            _dragX = Math.Clamp(local.X - _grabOffset, low, high);
            if (mouse.LeftButton == ButtonState.Released)
            {
                host.Submit(new SetPolicyCommand(slider.Policy, SliderValue(slider.Policy, _dragX)));
                _dragging = null;
            }

            return;
        }

        if (mouse.LeftButton == ButtonState.Released && previous.LeftButton == ButtonState.Pressed)
        {
            var pressed = _pressed;
            _pressed = null;
            if (pressed == "close" && Close.Contains(local))
            {
                host.PlaySound("no4.wav");
                host.Close(window);
            }
            else if ((pressed == "allow" && Allow.Contains(local)) || (pressed == "prohibit" && Prohibit.Contains(local)))
            {
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Submit(new SetPolicyCommand(PolicyKind.StaffAllowedToMove, pressed == "allow" ? 1 : 0));
            }
        }
    }

    public static string? Tooltip(IWindowHost host, Point local)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return Close.Contains(local) ? Tip(230, "Close the policy screen")
            : Allow.Contains(local) ? Tip(231, "Staff may leave their rooms to work where they are needed")
            : Prohibit.Contains(local) ? Tip(232, "Staff stay in the rooms you put them in")
            : Corners(161, 119, 479, 174).Contains(local) ? Tip(233, "When patients are sent home or their cure is guessed")
            : Corners(161, 210, 479, 249).Contains(local) ? Tip(234, "Diagnosis goes on until the doctors are this certain")
            : Corners(161, 285, 479, 324).Contains(local) ? Tip(235, "How tired staff are before they rest in the staff room")
            : null;
    }

    /// <summary>A slider's range: send home stops at guess cure and guess cure at send home, as their min_x and max_x.</summary>
    private static (int Low, int High) Limits(HospitalPolicies policies, Slider slider)
    {
        var low = slider.Left;
        var high = slider.Left + slider.Range;
        if (slider.Policy == PolicyKind.SendHome)
        {
            high = SliderX(PolicyKind.GuessCure, policies.GuessCure);
        }
        else if (slider.Policy == PolicyKind.GuessCure)
        {
            low = SliderX(PolicyKind.SendHome, policies.SendHome);
        }

        return (low, high);
    }

    private static int Value(HospitalPolicies policies, PolicyKind policy) => policy switch
    {
        PolicyKind.SendHome => policies.SendHome,
        PolicyKind.GuessCure => policies.GuessCure,
        PolicyKind.StopProcedure => policies.StopProcedure,
        _ => policies.GoToStaffRoom,
    };

    private static Rectangle Corners(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    private sealed record Slider(PolicyKind Policy, int Sprite, int Y, int Width, int Height, int Left, int Range, int Base, int Label, string Fallback);
}
