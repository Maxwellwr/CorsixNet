using CorsixNet.Content.Saves;
using CorsixNet.Formats;
using CorsixNet.Formats.Tests;
using CorsixNet.Sim.Entities;

namespace CorsixNet.Content.Tests;

public sealed class OriginalSaveImporterTests(ITestOutputHelper output)
{
    [Fact]
    public void RebuildsTheRealSaves()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).Order().ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        foreach (var file in files)
        {
            var save = OriginalSaveGame.Parse(File.ReadAllBytes(file));
            if (save.Level is < 1 or > 12)
            {
                continue;
            }

            var (simulation, report) = OriginalSaveImporter.Start(install, save, seed: 1);
            var hospital = simulation.Hospitals[0];
            var rooms = simulation.Entities.OfType<Room>().Count(r => r.IsActive);
            var expectedRooms = save.Rooms.Count(r => r.Width > 1 && r.Height > 1);
            output.WriteLine($"{Path.GetFileName(file)}: level {save.Level}, {rooms}/{expectedRooms} rooms, {report.Problems.Count} problems, balance {hospital.Balance}, {simulation.Date}");
            foreach (var problem in report.Problems)
            {
                output.WriteLine("   " + problem);
            }

            Assert.Empty(report.Problems);
            Assert.Equal(save.Hospitals[0].Money, hospital.Balance);
            Assert.Equal(save.Hospitals[0].Loan, hospital.Loan);
            Assert.Equal(save.Month + 1, simulation.Date.Month);
            var staffInSave = save.Entities.Count(e => e.Kind == 4 && e.TileX != 0);
            var staff = simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == 0).ToList();
            output.WriteLine($"   staff {staff.Count}/{staffInSave}");
            Assert.Equal(staffInSave, staff.Count);
            Assert.All(staff, s => Assert.NotNull(s.Profile!.ImportedName));
            var patientsInSave = save.Entities.Count(e => e.Kind == 2 && e.TileX != 0 && e.Disease > 0);
            var patients = simulation.Entities.OfType<Humanoid>().Count(h => h.Behaviour is CorsixNet.Sim.People.PatientBehaviour);
            output.WriteLine($"   patients {patients}/{patientsInSave}");
            Assert.InRange(patients, patientsInSave * 9 / 10, patientsInSave);

            var saved = save.Hospitals[0];
            Assert.Equal(saved.Reputation, hospital.Reputation);
            Assert.Equal(saved.PeopleCured, hospital.Cured);
            output.WriteLine($"   statement {hospital.Transactions.Count}, months {hospital.Statistics.Count}, discovered {hospital.Casebook.Values.Count(c => c.Discovered)}");
            if (hospital.Transactions.Count > 0)
            {
                Assert.Equal(saved.Money, hospital.Transactions[0].Balance);
            }

            // The rebuilt hospital runs: a week of play goes by without an error.
            for (var tick = 0; tick < 7 * 50; tick++)
            {
                simulation.Step();
            }
        }
    }
}
