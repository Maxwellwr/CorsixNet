using System.Buffers.Binary;
using System.Text;

namespace CorsixNet.Formats.Tests;

public class OriginalSaveGameTests
{
    [Fact]
    public void ReadsHeaderDateTilesEntitiesAndHospitals()
    {
        var data = new byte[OriginalSaveGame.Length];
        data[0] = 3;
        data[2] = 4;
        Encoding.ASCII.GetBytes("FULL").CopyTo(data, 3);
        data[OriginalSaveGame.DateOffset] = 17;
        data[OriginalSaveGame.DateOffset + 1] = 2;

        var tile = OriginalSaveGame.TileOffset + (((5 * OriginalSaveGame.MapSize) + 7) * 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(tile), 9);
        data[tile + 2] = 0x10;
        data[tile + 3] = 0x20;
        data[tile + 4] = 0x21;
        data[tile + 6] = 0x31;
        data[tile + 7] = 2;

        var entity = OriginalSaveGame.EntityOffset + (9 * OriginalSaveGame.EntityLength);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 4), 0x0780);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 6), 0x0540);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 8), 0x019A);
        data[entity + 12] = 6;
        data[entity + 14] = 11;
        data[entity + 17] = 2;

        var hospital = OriginalSaveGame.HospitalOffset + OriginalSaveGame.HospitalLength;
        Encoding.ASCII.GetBytes("MAXWELL").CopyTo(data, hospital);
        data[hospital + 42] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 53), -17328);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 61), 35000);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 443), 87310);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 489), 131);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3228), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3232), 10);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 3236), 1);

        var save = OriginalSaveGame.Parse(data);

        Assert.Equal(4, save.Level);
        Assert.Equal("FULL", save.Difficulty);
        Assert.Equal(17, save.Day);
        Assert.Equal(2, save.Month);
        Assert.Equal(new OriginalSaveTile(9, 0x10, 0x20, 0x21, 0, 0x31, 2), save[7, 5]);

        var parsed = Assert.Single(save.Entities);
        Assert.Equal(9, parsed.Slot);
        Assert.Equal((7, 5), (parsed.TileX, parsed.TileY));
        Assert.Equal((0x0780, 0x0540), (parsed.X, parsed.Y));
        Assert.Equal(0x019A, parsed.Frame);
        Assert.Equal(6, parsed.Kind);
        Assert.Equal(11, parsed.Type);
        Assert.Equal(2, parsed.Direction);

        Assert.Equal(OriginalSaveGame.MaxPlayers, save.Hospitals.Count);
        Assert.Equal(0, save.Hospitals[0].Kind);
        var second = save.Hospitals[1];
        Assert.Equal("MAXWELL", second.Name);
        Assert.Equal(1, second.Kind);
        Assert.Equal(-17328, second.Money);
        Assert.Equal(35000, second.Loan);
        Assert.Equal(87310, second.Value);
        Assert.Equal(131, second.PeopleCured);
        Assert.Equal(20, second.Staff);
        Assert.Equal(10, second.Doctors);
        Assert.Equal(1, second.Receptionists);
    }

    [Theory]
    [InlineData(2, "header +2")]
    [InlineData(13 + (((5 * 128) + 7) * 8) + 2, "tile (7,5) byte 2")]
    [InlineData(131085 + (9 * 175) + 14, "entity 9 +14")]
    [InlineData(399030, "day")]
    [InlineData(399031, "month")]
    [InlineData(399968 + 4697 + 53, "hospital 1 +53")]
    [InlineData(399000, "unknown 0x61698")]
    public void DescribesOffsets(int offset, string expected)
    {
        Assert.Equal(expected, OriginalSaveGame.DescribeOffset(offset));
    }

    [Fact]
    public void ReadsStaffFields()
    {
        var data = new byte[OriginalSaveGame.Length];
        var entity = OriginalSaveGame.EntityOffset + (109 * OriginalSaveGame.EntityLength);
        data[entity + 12] = 4;
        data[entity + 14] = 1;
        data[entity + 124] = 16 + 32;
        data[entity + 125] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 126), 565);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 108), 161);
        data[entity + 132] = (byte)'T';
        data[entity + 133] = 16;
        data[entity + 134] = 15;
        data[entity + 138] = 23;
        data[entity + 139] = 13;

        var staff = Assert.Single(OriginalSaveGame.Parse(data).Entities);

        Assert.Equal(16 + 32 + 256, staff.StaffFlags);
        Assert.Equal(565, staff.Skill);
        Assert.Equal(161, staff.Salary);
        Assert.Equal('T', staff.Initial);
        Assert.Equal((16, 15), (staff.NameStart, staff.NameEnd));
        Assert.Equal(((byte)23, (byte)13), staff.Description);
    }

    [Fact]
    public void RejectsWrongSize()
    {
        Assert.Throws<InvalidDataException>(() => OriginalSaveGame.Parse(new byte[OriginalSaveGame.Length - 1]));
    }

    [Fact]
    public void RealSavesAreConsistent()
    {
        var install = RealGameData.Require();
        var folder = install.FindDirectory("SAVE");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder, "*.H*").Where(file => new FileInfo(file).Length == OriginalSaveGame.Length).ToList();
        if (files.Count == 0)
        {
            Assert.Skip("No original saves in the SAVE folder.");
        }

        foreach (var file in files)
        {
            var save = OriginalSaveGame.Parse(File.ReadAllBytes(file));

            Assert.InRange(save.Level, 1, 12);
            Assert.Contains(save.Difficulty, new[] { "EASY", "FULL", "HARD" });
            Assert.InRange(save.Day, 1, 31);
            Assert.InRange(save.Month, 0, 11);

            var local = save.Hospitals[0];
            Assert.Equal(1, local.Kind);
            Assert.NotEmpty(local.Name);
            Assert.Equal(local.Staff, local.Nurses + local.Doctors + local.Handymen + local.Receptionists);

            // Every staff member on the payroll has an entity, and tiles point at entities standing on them.
            Assert.Equal(local.Staff, save.Entities.Count(e => e.Kind == 4 && e.TileX != 0));
            var bySlot = save.Entities.ToDictionary(e => e.Slot);
            for (var y = 0; y < OriginalSaveGame.MapSize; y++)
            {
                for (var x = 0; x < OriginalSaveGame.MapSize; x++)
                {
                    if (save[x, y].Entity is var slot and not 0)
                    {
                        Assert.Equal((x, y), (bySlot[slot].TileX, bySlot[slot].TileY));
                    }
                }
            }
        }
    }
}
