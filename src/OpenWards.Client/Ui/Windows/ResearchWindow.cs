using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Research;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The research policy window (CorsixTH's <c>dialogs/fullscreen/research_policy.lua</c>, F6): what each area works on,
/// how far it has got and its share of the research points, moved by 1, 5 or 20 percent as the window's arrows do
/// alone, with Shift or with Ctrl (<c>UIResearch:adjustResearch</c>).
/// </summary>
internal sealed class ResearchWindow : UiWindow
{
    public override string Title => UiText.Get("openwards.menu.research", "Research");

    public override string? Hotkey => "F6";

    private readonly ResearchScreen _screen = new();

    /// <summary>The original's look (<see cref="ResearchScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => ResearchScreen.IsAvailable(host);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => _screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => _screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => ResearchScreen.Tooltip(host, local);
    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var research = simulation.Hospitals[host.Player].Research;
        yield return WindowRow.Of(UiText.Get("openwards.research.area", "Area"), UiText.Get("openwards.research.working_on", "Working on"), WindowCell.Number(UiText.Get("openwards.research.done", "Done")), WindowCell.Number(UiText.Get("openwards.research.share", "Share")));
        foreach (var category in Enum.GetValues<ResearchCategory>())
        {
            var area = research.Area(category);
            var working = area.Current.Kind switch
            {
                ResearchTargetKind.None => UiText.Get("openwards.research.finished", "Finished"),
                ResearchTargetKind.Drain => UiText.Get("openwards.research.nothing_yet", "Nothing yet"),
                ResearchTargetKind.Object => UiText.ObjectName(simulation.Content, area.Current.Id),
                _ => host.Texts?.DiseaseName(area.Current.Id) ?? area.Current.Id,
            };
            yield return WindowRow.Of(
                Name(category),
                new WindowCell(working, Dim: area.Current.IsNone),
                WindowCell.Number(Progress(simulation, research, area.Current)),
                WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{area.Share} %")),
                Adjust(host, "-20", category, -20, area.Share > 0),
                Adjust(host, "-5", category, -5, area.Share > 0),
                Adjust(host, "-1", category, -1, area.Share > 0),
                Adjust(host, "+1", category, 1, research.Total < 100 && !area.Current.IsNone),
                Adjust(host, "+5", category, 5, research.Total < 100 && !area.Current.IsNone),
                Adjust(host, "+20", category, 20, research.Total < 100 && !area.Current.IsNone));
        }

        yield return WindowRow.Gap;
        yield return WindowRow.Of(UiText.Get("openwards.research.total", "Total"), string.Empty, string.Empty, WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{research.Total} %")));
        if (research.Concentrated.Length > 0)
        {
            yield return WindowCell.Line(UiText.Format("openwards.research.concentrating", "Concentrating on %1% (the casebook stops it)", host.Texts?.DiseaseName(research.Concentrated) ?? research.Concentrated), highlight: true);
        }
    }

    private static string Name(ResearchCategory category) => category switch
    {
        ResearchCategory.Cure => UiText.Get("research.categories.cure", "Cure equipment"),
        ResearchCategory.Diagnosis => UiText.Get("research.categories.diagnosis", "Diagnosis equipment"),
        ResearchCategory.Drugs => UiText.Get("research.categories.drugs", "Drug research"),
        ResearchCategory.Improvements => UiText.Get("research.categories.improvements", "Improvements"),
        _ => UiText.Get("research.categories.specialisation", "Specialised"),
    };

    /// <summary>How far the area's work is, as the window's progress bar.</summary>
    private static string Progress(OpenWards.Sim.Simulation simulation, ResearchDepartment research, ResearchTarget target)
    {
        decimal? points = target.Kind switch
        {
            ResearchTargetKind.Object => research.Objects.TryGetValue(target.Id, out var obj) ? obj.Points : null,
            ResearchTargetKind.Drug => research.Drugs.TryGetValue(target.Id, out var drug) ? drug : null,
            _ => null,
        };
        if (points is not { } done || research.Required(simulation, target) is not { } required || required <= 0)
        {
            return string.Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Min(100, done * 100 / required)} %");
    }

    /// <summary>A button changing an area's share; its tooltip is CorsixTH's English <c>tooltip.research</c> text.</summary>
    private static WindowCell Adjust(IWindowHost host, string text, ResearchCategory category, int change, bool enabled)
    {
        var button = WindowCell.Button(text, () =>
        {
            host.Submit(new AdjustResearchCommand(category, change));
        }, enabled);
        var (area, english) = category switch
        {
            ResearchCategory.Cure => ("cure", "Cure"),
            ResearchCategory.Diagnosis => ("diagnosis", "Diagnosis"),
            ResearchCategory.Drugs => ("drugs", "Drugs"),
            ResearchCategory.Improvements => ("improvements", "Improvements"),
            _ => ("specialisation", "Specialisation"),
        };
        return button.WithTooltip(change < 0
            ? UiText.Get($"tooltip.research.{area}_dec", $"Decrease {english} research percentage")
            : UiText.Get($"tooltip.research.{area}_inc", $"Increase {english} research percentage"));
    }
}
