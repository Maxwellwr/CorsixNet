using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The drug casebook (CorsixTH's <c>dialogs/fullscreen/drug_casebook.lua</c>, F5): every disease and diagnosis the
/// hospital knows, with its reputation, charge, earnings, recoveries, fatalities, patients turned away and cure rating.
/// The price buttons change it by 1 or 5 percent (<c>increasePay</c>, <c>decreasePay</c> with and without Shift), and
/// the research button concentrates research on the disease or stops.
/// </summary>
/// <remarks>
/// The original shows one disease at a time with a list to pick from; this stand-in shows all of them as a table.
/// </remarks>
internal sealed class CasebookWindow : UiWindow
{
    private bool _showPercent;

    public override string Title => "Drug casebook";

    public override string? Hotkey => "F5";

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        var hospital = simulation.Hospitals[host.Player];
        var rules = simulation.Rules.Diseases;
        yield return WindowCell.Button(_showPercent ? "Show charges" : "Show percent", () => _showPercent = !_showPercent).WithTooltip(OriginalTooltips.Get(host, 140, "Show the charge as money or as a percentage"));
        yield return WindowRow.Of("Disease", WindowCell.Number("Rep"), WindowCell.Number("Charge"), "", "", "", "", WindowCell.Number("Earned"), WindowCell.Number("Cured"), WindowCell.Number("Died"), WindowCell.Number("Away"), WindowCell.Number("Cure"), "Research");
        var known = DiseaseCatalog.All
            .Where(disease => hospital.PeekCase(disease, rules).Discovered)
            .Select(disease => (Disease: disease, Name: host.Texts?.DiseaseName(disease.Id) ?? disease.Id))
            .OrderBy(entry => entry.Disease.IsPseudo)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (disease, name) in known)
        {
            var entry = hospital.PeekCase(disease, rules);
            var reputation = Math.Clamp(entry.Reputation ?? hospital.Reputation, Hospital.MinReputation, Hospital.MaxReputation);
            var charge = _showPercent
                ? string.Create(CultureInfo.InvariantCulture, $"{entry.PricePercent} %")
                : MoneyText.Format(hospital.TreatmentPrice(disease, rules));
            var concentrated = string.Equals(hospital.Research.Concentrated, disease.Id, StringComparison.Ordinal);
            yield return WindowRow.Of(
                new WindowCell(name, Highlight: concentrated),
                WindowCell.Number(reputation.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(charge),
                Price(host, "-5", disease, entry.PricePercent - 5),
                Price(host, "-1", disease, entry.PricePercent - 1),
                Price(host, "+1", disease, entry.PricePercent + 1),
                Price(host, "+5", disease, entry.PricePercent + 5),
                WindowCell.Number(MoneyText.Format(entry.MoneyEarned)),
                WindowCell.Number(entry.Recoveries.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(entry.Fatalities.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(entry.TurnedAway.ToString(CultureInfo.InvariantCulture)),
                WindowCell.Number(disease.IsPseudo ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{entry.CureEffectiveness} %")),
                disease.IsPseudo
                    ? string.Empty
                    : WindowCell.Button(concentrated ? "Stop" : "Concentrate", () => host.Submit(new ConcentrateResearchCommand(disease.Id)), highlight: concentrated).WithTooltip(OriginalTooltips.Get(host, 147, "Concentrate research on treating this disease")));
        }
    }

    private WindowCell Price(IWindowHost host, string text, DiseaseDefinition disease, int percent) =>
        WindowCell.Button(text, () =>
        {
            // UICasebook:increasePay and decreasePay: the wrong-choice buzz at the end of the range.
            if (percent is < DiseaseCase.MinPrice or > DiseaseCase.MaxPrice)
            {
                host.PlaySound("Wrong2.wav");
            }

            host.Submit(new SetTreatmentPriceCommand(disease.Id, percent));
            _showPercent = true;
        }).WithTooltip(text.StartsWith('-') ? OriginalTooltips.Get(host, 145, "Lower the charge") : OriginalTooltips.Get(host, 146, "Raise the charge"));
}
