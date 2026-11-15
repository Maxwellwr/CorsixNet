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

    [Fact]
    public void TheFaxesNameTheRoomToBuildInTheChosenTranslation()
    {
        // World:getRoomNameAndRequiredStaffName: the faxes name the room by its rooms_long name, not the English one.
        var languages = CorsixNet.Content.Languages.CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages"));
        var russian = languages.Load("Russian", _ => null);
        var host = new WindowTestHost { Texts = new CorsixNet.Content.OriginalTexts(CorsixNet.Formats.StringTable.Parse(new byte[] { 0, 0 }), russian) };
        var pharmacy = russian.Get("rooms_long.pharmacy")!;
        Assert.Equal("Аптека", pharmacy);

        var patient = new CorsixNet.Sim.People.PatientBehaviour();
        patient.SetDisease(CorsixNet.Sim.Diseases.DiseaseCatalog.Find("the_squits")!, 0, host.Simulation.Random);
        var missing = FaxWindow.NoTreatmentRoomLines(host, patient).ToList()[2];
        Assert.Contains(pharmacy, missing, StringComparison.Ordinal);
        Assert.DoesNotContain("Pharmacy", missing, StringComparison.Ordinal);

        var emergency = FaxWindow.EmergencyStartLines(host, new CorsixNet.Sim.Disasters.Emergency("the_squits", 5, 300, 75)).ToList();
        Assert.Contains(emergency, line => line.Contains(pharmacy, StringComparison.Ordinal));
        Assert.DoesNotContain(emergency, line => line.Contains("Pharmacy", StringComparison.Ordinal));
    }

    [Fact]
    public void TheEmergencyAndEpidemicFaxesSayWhatTheyAreAbout()
    {
        var host = new WindowTestHost();
        var emergency = FaxWindow.EmergencyStartLines(host, new CorsixNet.Sim.Disasters.Emergency("bloaty_head", 5, 300, 75)).ToList();
        Assert.Equal(4, emergency.Count);
        Assert.Contains("5 people with bloaty_head", emergency[1], StringComparison.Ordinal);
        Assert.StartsWith("You need to build a ", emergency[2], StringComparison.Ordinal);
        Assert.Contains("1500", emergency[3], StringComparison.Ordinal);

        var epidemic = FaxWindow.EpidemicStartLines(host, new CorsixNet.Sim.Disasters.Epidemic("broken_wind") { DeclareFine = 2000 }).ToList();
        Assert.Equal(4, epidemic.Count);
        Assert.Contains("2000", epidemic[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheWinningLetterWithoutTheGamesTextsSaysTheLevelIsWon()
    {
        var host = new WindowTestHost();
        Assert.DoesNotContain(FaxWindow.Letters(null)[0], line => line.Length > 0);
        var lines = FaxWindow.WinLetterLines(host).ToList();
        Assert.NotEmpty(lines);
        Assert.Equal("winletter:", Assert.Single(FaxWindow.Pending(host.Simulation, 0, winLetter: true).Take(1)));
    }
}
