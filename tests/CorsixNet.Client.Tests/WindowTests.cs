using CorsixNet.Client.Ui;
using CorsixNet.Client.Ui.Windows;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Diseases;
using CorsixNet.Sim.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Tests;

/// <summary>The windows over the map: their layer, and the dialogs' buttons turning into the player's commands.</summary>
public class WindowTests
{
    private static readonly Rectangle Screen = new(0, 40, 1280, 680);

    [Fact]
    public void OpeningADialogTwiceKeepsOneAndItsHotkeyClosesIt()
    {
        var layer = new WindowLayer(text => text.Length * 6, 10);
        layer.Open(new PolicyWindow());
        layer.Open(new CasebookWindow());
        layer.Open(new PolicyWindow());

        Assert.Equal(2, layer.Windows.Count());
        Assert.IsType<PolicyWindow>(layer.Windows.Last());

        layer.Toggle(new PolicyWindow());
        Assert.IsType<CasebookWindow>(Assert.Single(layer.Windows));
        Assert.True(layer.CloseTop());
        Assert.False(layer.CloseTop());
    }

    [Fact]
    public void AClickOnAButtonRunsItAndTheMapDoesNotSeeIt()
    {
        var host = new WindowTestHost();
        var layer = new WindowLayer(text => text.Length * 6, 10) { Origin = new Point(8, 48) };
        var clicks = 0;
        layer.Open(new TestWindow(() => clicks++));
        var released = new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        // Lay the window out, then find its button: the second row, below the title bar.
        Assert.False(layer.Update(host, released, released, Screen));
        var x = 8 + 8 + 2;
        var y = 48 + 16 + 8 + 16 + 4;
        var over = new MouseState(x, y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        var press = new MouseState(x, y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        Assert.True(layer.Update(host, over, released, Screen));
        Assert.Equal(0, clicks);
        Assert.True(layer.Update(host, press, over, Screen));
        Assert.Equal(1, clicks);

        // Button:handleClick: the windows' default_button_sound.
        Assert.Equal(["selectx.wav"], host.Sounds);
    }

    [Fact]
    public void ThePolicyWindowMovesSlidersAndSwitches()
    {
        var host = new WindowTestHost();
        var window = new PolicyWindow();
        var policies = host.Simulation.Hospitals[0].Policies;
        var guess = policies.GuessCure;

        host.Click(window, "-10", skip: 1);
        Assert.Equal(guess - 100, host.Simulation.Hospitals[0].Policies.GuessCure);
        Assert.Equal(new SetPolicyCommand(PolicyKind.GuessCure, guess - 100), host.Submitted[^1]);

        host.Click(window, "Not allowed");
        Assert.False(host.Simulation.Hospitals[0].Policies.StaffAllowedToMove);
        Assert.Contains(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text == "Not allowed" && cell.Highlight && cell.OnClick is null);
    }

    [Fact]
    public void TheCasebookSetsPricesWithoutAddingToTheCasebookByLooking()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];
        var window = new CasebookWindow();
        var entries = hospital.Casebook.Count;

        var lines = host.Lines(window);
        Assert.Equal(entries, hospital.Casebook.Count);
        var known = DiseaseCatalog.All.Where(disease => hospital.PeekCase(disease, host.Simulation.Rules.Diseases).Discovered).ToList();
        Assert.NotEmpty(known);
        Assert.Equal(known.Count + 2, lines.Count);

        host.Click(window, "+5");
        var first = host.Submitted.OfType<SetTreatmentPriceCommand>().Single();
        Assert.Equal(DiseaseCase.StandardPrice + 5, first.PricePercent);
        Assert.Equal(DiseaseCase.StandardPrice + 5, hospital.Casebook[first.DiseaseId].PricePercent);
        Assert.Contains("105 %", host.Lines(window).SelectMany(line => line.Split('|')));
    }

    [Fact]
    public void TheBankManagerBorrowsAndRepays()
    {
        var host = new WindowTestHost();
        var hospital = host.Simulation.Hospitals[0];
        var window = new BankManagerWindow();
        Assert.Equal(0, hospital.Loan);
        Assert.DoesNotContain(window.Rows(host).SelectMany(row => row.Cells), cell => cell.Text.StartsWith("Repay", StringComparison.Ordinal) && cell.OnClick is not null);

        host.Click(window, "Borrow $5,000");
        Assert.Equal(Hospital.LoanStep, hospital.Loan);
        host.Click(window, "Repay all you can");
        Assert.Equal(0, hospital.Loan);
        Assert.Equal(["cashreg.wav", "cashreg.wav"], host.Sounds);
    }

    [Fact]
    public void TheBankStatementListsTheLoan()
    {
        var host = new WindowTestHost();
        host.Submit(new BorrowCommand(false));

        var lines = host.Lines(new BankStatementWindow());

        Assert.Contains(lines, line => line.Contains("Bank loan", StringComparison.Ordinal) && line.Contains("$5,000", StringComparison.Ordinal));
    }

    [Fact]
    public void TheResearchWindowShowsEveryAreaAndMovesShares()
    {
        var host = new WindowTestHost();
        var window = new ResearchWindow();
        var lines = host.Lines(window);

        Assert.Equal(1 + Enum.GetValues<CorsixNet.Sim.Research.ResearchCategory>().Length + 2, lines.Count);
        var research = host.Simulation.Hospitals[0].Research;
        var area = Enum.GetValues<CorsixNet.Sim.Research.ResearchCategory>().First(category => research.Area(category).Share > 0);
        var share = research.Area(area).Share;
        host.Click(window, "-1");
        Assert.Equal(share - 1, research.Area(area).Share);
    }

    [Fact]
    public void ACellsTooltipShowsWhileTheMouseIsOverIt()
    {
        var host = new WindowTestHost();
        var layer = new WindowLayer(text => text.Length * 6, 10) { Origin = new Point(8, 48) };
        layer.Open(new TestWindow(() => { }));
        var released = new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        layer.Update(host, released, released, Screen);

        Assert.Equal("Does it", layer.TooltipAt(new Point(8 + 8 + 2, 48 + 16 + 8 + 16 + 4)));
        Assert.Null(layer.TooltipAt(new Point(8 + 8 + 2, 48 + 16 + 8 + 4)));
        Assert.Null(layer.TooltipAt(new Point(1000, 600)));
    }

    [Fact]
    public void TheBottomPanelsTooltipsNameTheHotkeys()
    {
        var host = new WindowTestHost();
        host.Hotkeys["ingame_panel_bankManager"] = "F1";
        var cells = new BottomPanel(_ => { }).Rows(host).First().Cells;

        Assert.Equal("Bank Manager (F1)", cells.Single(cell => cell.Text == "Bank").Tooltip);
        Assert.Equal("Furnish corridor", cells.Single(cell => cell.Text == "Corridor").Tooltip);
        Assert.Equal("Build rooms (B)", cells.Single(cell => cell.Text == "Build").Tooltip);
    }

    [Fact]
    public void DialogsOpenWhereCorsixThPutsThem()
    {
        var host = new WindowTestHost();
        var layer = new WindowLayer(text => text.Length * 6, 10) { Origin = new Point(8, 48) };
        layer.Open(new ConfirmWindow("Sure?", () => { }));
        layer.Open(new TipOfTheDayWindow(0));
        MouseState At(int x, int y) => new(x, y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

        // The tip at the bottom right, the confirmation in the middle; the cascade corner stays free.
        Assert.True(layer.Update(host, At(Screen.Right - 25, Screen.Bottom - 25), At(0, 0), Screen));
        Assert.False(layer.Update(host, At(Screen.Right - 15, Screen.Bottom - 15), At(0, 0), Screen));
        Assert.True(layer.Update(host, At(Screen.Center.X, Screen.Center.Y), At(0, 0), Screen));
        Assert.False(layer.Update(host, At(20, 60), At(0, 0), Screen));
    }

    private sealed class TestWindow(Action onClick) : UiWindow
    {
        public override string Title => "Test";

        public override IEnumerable<WindowRow> Rows(IWindowHost host)
        {
            yield return "First row";
            yield return WindowCell.Button("Click", onClick) with { Tooltip = "Does it" };
        }
    }
}
