using System.Globalization;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Research;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The research policy window (CorsixTH's <c>dialogs/fullscreen/research_policy.lua</c>, F6): what each area works on,
/// how far it has got and its share of the research points, moved by 1, 5 or 20 percent as the window's arrows do
/// alone, with Shift or with Ctrl (<c>UIResearch:adjustResearch</c>).
/// </summary>
internal sealed class ResearchWindow : UiWindow
{
    public override string Title => "Research";

    public override string? Hotkey => "F6";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var research = simulation.Hospitals[host.Player].Research;
        yield return WindowRow.Of("Area", "Working on", WindowCell.Number("Done"), WindowCell.Number("Share"));
        foreach (var category in Enum.GetValues<ResearchCategory>())
        {
            var area = research.Area(category);
            var working = area.Current.Kind switch
            {
                ResearchTargetKind.None => "Finished",
                ResearchTargetKind.Drain => "Nothing yet",
                ResearchTargetKind.Object => simulation.Content.FindObject(area.Current.Id)?.Name ?? area.Current.Id,
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
        yield return WindowRow.Of("Total", string.Empty, string.Empty, WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{research.Total} %")));
        if (research.Concentrated.Length > 0)
        {
            yield return WindowCell.Line($"Concentrating on {host.Texts?.DiseaseName(research.Concentrated) ?? research.Concentrated} (the casebook stops it)", highlight: true);
        }
    }

    private static string Name(ResearchCategory category) => category switch
    {
        ResearchCategory.Cure => "Cure equipment",
        ResearchCategory.Diagnosis => "Diagnosis equipment",
        ResearchCategory.Drugs => "Drug research",
        ResearchCategory.Improvements => "Improvements",
        _ => "Specialised",
    };

    /// <summary>How far the area's work is, as the window's progress bar.</summary>
    private static string Progress(CorsixNet.Sim.Simulation simulation, ResearchDepartment research, ResearchTarget target)
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
            host.PlaySound("selectx.wav");
        }, enabled);
        var area = category switch
        {
            ResearchCategory.Cure => "Cure",
            ResearchCategory.Diagnosis => "Diagnosis",
            ResearchCategory.Drugs => "Drugs",
            ResearchCategory.Improvements => "Improvements",
            _ => "Specialisation",
        };
        return button.WithTooltip($"{(change < 0 ? "Decrease" : "Increase")} {area} research percentage");
    }
}
