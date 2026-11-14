// The patient window as the original draws it, ported from CorsixTH CorsixTH/Lua/dialogs/patient.lua (UIPatient: its
// panels, buttons and blankers, tooltips, draw, the bars, the health graph, the treatment history, the view circle).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Client.Rendering;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// A patient's window of Req02V's panels, 191 by 310: the health graph (or, after a click on it, where they have been),
/// the happiness, thirst and warmth bars, buttons for their queue, the casebook, guessing the cure and sending them
/// home (each blanked out when it does not apply), and the view circle showing them.
/// </summary>
internal sealed class PatientScreen(int patientId)
{
    public static readonly Point Size = new(191, 310);

    private static readonly Color Blanker = new(113, 117, 170);
    private static readonly Rectangle History = new(36, 22, 99, 88);
    private static readonly Rectangle Close = new(147, 21, 24, 24);
    private static readonly Rectangle Queue = new(146, 80, 25, 31);
    private static readonly Rectangle Casebook = new(131, 168, 38, 38);
    private static readonly Rectangle Home = new(109, 219, 60, 60);
    private static readonly Rectangle Guess = new(131, 119, 38, 38);

    private bool _history;
    private string? _pressed;

    public static bool IsAvailable(IWindowHost host) =>
        host.Graphics is { } graphics && graphics.Sheet("REQ02V", OriginalGraphics.MainPalette) is not null
        && graphics.Font("FONT74V", OriginalGraphics.MainPalette) is not null;

    /// <summary>is_in_view_circle: within 39 pixels of (55, 254).</summary>
    public static bool InViewCircle(Point local) => ((local.X - 55) * (local.X - 55)) + ((local.Y - 254) * (local.Y - 254)) < 39 * 39;

    /// <summary>The health graph's points (drawHealthHistory): 76 pixels across from x 58, 70 high from y 27.</summary>
    public static List<Vector2> GraphPoints(IReadOnlyList<decimal> history, int size)
    {
        var points = new List<Vector2>();
        if (size <= 0)
        {
            return points;
        }

        var step = 76f / size;
        for (var i = 0; i < history.Count; i++)
        {
            points.Add(new Vector2(58 + ((size - history.Count + i) * step), 27 + ((1 - (float)history[i]) * 70)));
        }

        return points;
    }

    public void Draw(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse, Humanoid humanoid, PatientBehaviour patient)
    {
        var graphics = host.Graphics!;
        var sheet = graphics.Sheet("REQ02V", OriginalGraphics.MainPalette)!;
        var font = graphics.Font("FONT74V", OriginalGraphics.MainPalette)!;
        var local = new Point(mouse.X, mouse.Y) - origin;
        var held = mouse.LeftButton == ButtonState.Pressed;
        void Panel(int sprite, int x, int y) => sheet.Draw(batch, sprite, origin.X + x, origin.Y + y, Color.White);
        void Colour(Rectangle area, Color colour) => batch.Draw(graphics.Pixel, new Rectangle(origin + area.Location, area.Size), colour);
        void Button(string key, Rectangle area, int sprite, bool shown, Rectangle blanker)
        {
            if (shown)
            {
                Panel(held && _pressed == key && area.Contains(local) ? sprite + 1 : sprite, area.X, area.Y);
            }
            else
            {
                Colour(blanker, Blanker);
            }
        }

        if (host.CloseUp(patientId) is { } closeUp)
        {
            batch.Draw(closeUp, new Rectangle(origin.X + 17, origin.Y + 216, 75, 76), Color.White);
        }

        Panel(320, 15, 0);
        Panel(321, 15, 61);
        if (_history)
        {
            Colour(History, new Color(223, 223, 223));
        }

        Panel(322, 15, 126);
        Panel(323, 0, 201);
        Panel(324, 0, 254);
        Panel(held && _pressed == "close" && Close.Contains(local) ? 326 : 325, Close.X, Close.Y);
        Panel(held && _pressed == "queue" && Queue.Contains(local) ? 412 : 411, Queue.X, Queue.Y);
        var (casebook, home, guess) = Shown(host, patient);
        Button("casebook", Casebook, 329, casebook, new Rectangle(129, 166, 45, 45));
        Button("home", Home, 331, home, new Rectangle(107, 217, 67, 67));
        Button("guess", Guess, 413, guess, new Rectangle(129, 117, 45, 45));

        foreach (var (sprite, share, y) in new[] { (348, humanoid.Happiness, 126), (351, 1 - patient.Thirst, 154), (349, ProgressReportScreen.NormaliseWarmth(humanoid.Warmth), 183) })
        {
            for (var dx = 0; dx < StaffScreen.Bar(share); dx++)
            {
                Panel(sprite, 58 + dx, y);
            }
        }

        if (_history)
        {
            // drawTreatmentHistory: the rooms they have been to, in capitals.
            var y = 25;
            foreach (var entry in patient.TreatmentHistory)
            {
                var text = (entry.Kind == TreatmentHistoryKind.Room ? host.Simulation.Content.FindRoom(entry.Id)?.Name ?? entry.Id : host.Texts?.DiseaseName(entry.Id) ?? entry.Id).ToUpperInvariant();
                foreach (var line in Maps.MapViewScreen.WrapToWidth(text, 95, t => font.Measure(t).Width))
                {
                    font.Draw(batch, line, new Vector2(origin.X + 40, origin.Y + y));
                    y += font.LineHeight;
                }
            }

            return;
        }

        // drawHealthHistory: a red line of their health over the last days.
        var points = GraphPoints(patient.HealthHistory, PatientBehaviour.HealthHistorySize);
        for (var i = 1; i < points.Count; i++)
        {
            var from = origin.ToVector2() + points[i - 1];
            var delta = origin.ToVector2() + points[i] - from;
            batch.Draw(graphics.Pixel, from, null, new Color(200, 55, 30), MathF.Atan2(delta.Y, delta.X), new Vector2(0, 0.5f), new Vector2(delta.Length(), 2), SpriteEffects.None, 0);
        }
    }

    public void Mouse(IWindowHost host, UiWindow window, Point local, MouseState mouse, MouseState previous, Humanoid humanoid, PatientBehaviour patient)
    {
        if (mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released && InViewCircle(local))
        {
            // onMouseUp's right button: the next of the hospital's patients.
            var patients = host.Simulation.Entities.OfType<Humanoid>()
                .Where(h => h.Behaviour is PatientBehaviour { Phase: not PatientPhase.GoingHome } && h.HospitalPlayer == host.Player && !h.IsDespawning)
                .OrderBy(h => h.Id)
                .ToList();
            var next = patients.FirstOrDefault(h => h.Id > humanoid.Id) ?? patients.FirstOrDefault();
            if (next is not null && next.Id != humanoid.Id)
            {
                host.Close(window);
                host.Open(new PatientWindow(next.Id));
            }

            return;
        }

        var under = ButtonAt(host, local, patient);
        if (mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released)
        {
            _pressed = under;
            return;
        }

        if (mouse.LeftButton == ButtonState.Pressed)
        {
            if (_pressed == "circle" && InViewCircle(local))
            {
                host.CenterOn(humanoid.Tile);
            }

            return;
        }

        if (previous.LeftButton != ButtonState.Pressed)
        {
            return;
        }

        var pressed = _pressed;
        _pressed = null;
        if (pressed is null || pressed != under)
        {
            return;
        }

        switch (pressed)
        {
            case "close":
                host.PlaySound("no4.wav");
                host.Close(window);
                break;
            case "history":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                _history = !_history;
                break;
            case "queue":
                // viewQueue: the queue they are in, or Wrong2.wav.
                if (QueueWindow.QueueOf(host.Simulation, patient.TargetId) is not null)
                {
                    host.PlaySound(WindowLayer.DefaultButtonSound);
                    host.Open(new QueueWindow(patient.TargetId));
                }
                else
                {
                    host.PlaySound("wrong2.wav");
                }

                break;
            case "casebook":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                CasebookScreen.Remember(patient.DiseaseId);
                host.Open(new CasebookWindow());
                break;
            case "home":
                // goHome: sack.wav, and the window closes.
                host.PlaySound("sack.wav");
                host.Submit(new SendPatientHomeCommand(humanoid.Id));
                host.Close(window);
                break;
            case "guess":
                host.PlaySound(WindowLayer.DefaultButtonSound);
                host.Submit(new GuessPatientCureCommand(humanoid.Id));
                break;
        }
    }

    public string? Tooltip(IWindowHost host, Point local, PatientBehaviour patient)
    {
        string Tip(int index, string fallback) => OriginalTooltips.Get(host, index, fallback);
        return ButtonAt(host, local, patient) switch
        {
            "close" => Tip(88, "Close this window"),
            "history" => Tip(89, "Click to show the health graph or the treatment history"),
            "queue" => Tip(97, "Show the queue the patient is in"),
            "casebook" => Tip(93, "Show the casebook entry for this disease"),
            "home" => Tip(94, "Send the patient home"),
            "guess" => Tip(96, "Stop diagnosing and guess the cure"),
            "circle" => Tip(95, "Left click to follow the patient, right click to go on to the next"),
            _ => new Rectangle(33, 117, 91, 24).Contains(local) ? Tip(90, "Happiness")
                : new Rectangle(33, 141, 91, 28).Contains(local) ? Tip(91, "Thirst")
                : new Rectangle(33, 169, 91, 34).Contains(local) ? Tip(92, "Warmth")
                : null,
        };
    }

    /// <summary>updateInformation: which of the casebook, send home and guess cure buttons apply.</summary>
    private static (bool Casebook, bool Home, bool Guess) Shown(IWindowHost host, PatientBehaviour patient)
    {
        var hospital = host.Simulation.Hospitals[host.Player];
        var discovered = patient.Disease is { } disease && hospital.PeekCase(disease, host.Simulation.Rules.Diseases).Discovered;
        var leaving = patient.Phase == PatientPhase.GoingHome || patient.DyingStage != DyingStage.None;
        return (patient.IsDiagnosed && discovered, !leaving, !leaving && !patient.IsDiagnosed && patient.DiagnosisProgress > 0);
    }

    private string? ButtonAt(IWindowHost host, Point local, PatientBehaviour patient)
    {
        var (casebook, home, guess) = Shown(host, patient);
        return Close.Contains(local) ? "close"
            : Queue.Contains(local) ? "queue"
            : casebook && Casebook.Contains(local) ? "casebook"
            : home && Home.Contains(local) ? "home"
            : guess && Guess.Contains(local) ? "guess"
            : History.Contains(local) ? "history"
            : InViewCircle(local) ? "circle"
            : null;
    }
}
