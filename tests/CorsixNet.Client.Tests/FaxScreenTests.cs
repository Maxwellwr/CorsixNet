using CorsixNet.Client.Ui.Windows;
using Microsoft.Xna.Framework;
using Xunit;

namespace CorsixNet.Client.Tests;

public sealed class FaxScreenTests
{
    [Fact]
    public void ChoicesFillTheThreePlacesFromTheBottom()
    {
        Assert.Equal(new Rectangle(492, 175 + (2 * 48), 43, 43), FaxScreen.ChoiceButton(0, 1));
        Assert.Equal(new Rectangle(492, 175 + 48, 43, 43), FaxScreen.ChoiceButton(0, 2));
        Assert.Equal(new Rectangle(492, 175, 43, 43), FaxScreen.ChoiceButton(0, 3));
    }

    [Fact]
    public void AReportFaxIsOneMessageWithItsCloseChoice()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];
        CorsixNet.Sim.Economy.VipVisits.OnVipLeft(host.Simulation, hospital, new CorsixNet.Sim.People.VipBehaviour(3, 2));
        var fax = new FaxWindow();
        var message = Assert.Single(fax.Messages(host));
        var choice = Assert.Single(message.Choices);
        choice.Choose();
        Assert.Null(hospital.Vips.Report);
        Assert.False(fax.IsFullscreen(host));
    }

    [Fact]
    public void TheEpidemicResultSaysHowTheCoverUpEnded()
    {
        var host = new WindowTestHost();
        var worked = FaxWindow.EpidemicReportLines(host, new CorsixNet.Sim.Disasters.EpidemicReport("broken_wind", CorsixNet.Sim.Disasters.EpidemicOutcome.CoveredUp, 2500, 0)).ToList();
        Assert.Equal(3, worked.Count);
        Assert.Contains("2500", worked[2], StringComparison.Ordinal);
        var evacuated = FaxWindow.EpidemicReportLines(host, new CorsixNet.Sim.Disasters.EpidemicReport("broken_wind", CorsixNet.Sim.Disasters.EpidemicOutcome.Evacuated, 9000, 0)).ToList();
        Assert.Equal("Your hospital is being evacuated.", evacuated[2]);
    }

    [Fact]
    public void ADiscoveredDiseaseIsAFaxOfItsOwn()
    {
        var host = new WindowTestHost();
        var lines = FaxWindow.DiseaseReportLines(host, new CorsixNet.Sim.Economy.DiseaseReport("broken_wind", 0)).ToList();
        Assert.Contains("broken_wind", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void APatientsFaxSaysWhatIsMissingToCureThem()
    {
        var host = new WindowTestHost();
        var patient = new CorsixNet.Sim.People.PatientBehaviour();
        patient.SetDisease(CorsixNet.Sim.Diseases.DiseaseCatalog.Find("bloaty_head")!, 0, host.Simulation.Random);
        var lines = FaxWindow.NoTreatmentRoomLines(host, patient).ToList();
        Assert.Equal(4, lines.Count);
        Assert.StartsWith("If you build a ", lines[2], StringComparison.Ordinal);
        Assert.EndsWith("Doctor you can cure it.", lines[2], StringComparison.Ordinal);

        patient.DiagnosisProgress = 420;
        var diagnosis = FaxWindow.NoDiagnosisRoomLines(host, patient, known: true).ToList();
        Assert.Contains("42%", diagnosis[2], StringComparison.Ordinal);
    }
}
