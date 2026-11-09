namespace CorsixNet.Formats.Tests;

public class BanknotePaletteTests
{
    [Fact]
    public void TurnsClearlyGreenEntriesBlueAndLeavesTheRest()
    {
        var data = new byte[768];
        // 0: bright green, 1: grey, 2: green leading by too little, 3: red.
        data[0] = 10;
        data[1] = 60;
        data[2] = 20;
        data[3] = 30;
        data[4] = 30;
        data[5] = 30;
        data[6] = 20;
        data[7] = 23;
        data[8] = 10;
        data[9] = 50;
        data[10] = 10;
        data[11] = 10;

        var blue = BanknotePalette.MakeBlue(data);

        Assert.Equal(new byte[] { 21, 33, 60 }, blue[0..3]);
        Assert.Equal(data[3..12], blue[3..12]);
        Assert.Equal(10, data[0]);
    }

    [Fact]
    public void RejectsDataOfTheWrongLength()
    {
        Assert.Throws<InvalidDataException>(() => BanknotePalette.MakeBlue(new byte[1024]));
    }

    [Fact]
    public void TheBanknoteSheetHasTheNoteAndTenDigits()
    {
        var install = RealGameData.Require();

        var sheet = install.LoadSpriteSheet("DATA", "MONEY01V", complex: false);
        var blue = BanknotePalette.MakeBlue(install.ReadDataFile("DATA", "MPALETTE.DAT"));

        Assert.True(sheet.Count >= 12, $"Only {sheet.Count} sprites.");
        Assert.True(sheet[1].Width > 0);
        Assert.NotEqual(install.ReadDataFile("DATA", "MPALETTE.DAT"), blue);
    }
}
