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

            Assert.Equal(save.Hospitals[0].Money, hospital.Balance);
            Assert.Equal(save.Hospitals[0].Loan, hospital.Loan);
            Assert.Equal(save.Month + 1, simulation.Date.Month);
        }
    }
}
