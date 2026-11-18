// The debug menu's make debug patient window, ported from CorsixTH
// CorsixTH/Lua/dialogs/resizables/menu_list_dialogs/make_debug_patient.lua (UIMakeDebugPatient: a list of the level's
// diseases, each making a diagnosed patient with it near the view's middle).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using OpenWards.Sim.Diseases;

namespace OpenWards.Client.Ui.Windows;

/// <summary><c>UIMakeDebugPatient</c>: a button for each of the level's diseases; a click makes a patient with it.</summary>
/// <param name="make">Makes a patient with the disease (sends the command).</param>
internal sealed class DebugPatientWindow(Action<DiseaseDefinition> make) : UiWindow
{
    public override string Title => UiText.Get("debug_patient_window.caption", "Debug Patient");

    public override bool UsesResizableLook => true;

    public override bool IsSameAs(UiWindow other) => other is DebugPatientWindow;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (host.Simulation is not { } simulation)
        {
            yield break;
        }

        foreach (var disease in simulation.Rules.Diseases.AvailableDiseases())
        {
            var name = host.Texts?.DiseaseName(disease.Id) ?? disease.Id;
            yield return WindowRow.Of(WindowCell.Button(name, () => make(disease)) with
            {
                Tooltip = UiText.Format("tooltip.debug_patient_window.item", "Debug patient with %s", name),
            });
        }
    }
}
