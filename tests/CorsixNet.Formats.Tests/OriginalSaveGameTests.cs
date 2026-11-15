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
    [InlineData(2, "header +2 level")]
    [InlineData(13 + (((5 * 128) + 7) * 8) + 2, "tile (7,5) byte 2 floor")]
    [InlineData(131085 + (9 * 175) + 14, "entity 9 +14 type")]
    [InlineData(0x60194 + (17 * 35) + 2, "room 17 +2 height")]
    [InlineData(0x6661C + 2, "0x6661E plot owners")]
    [InlineData(399030, "0x616B6 day")]
    [InlineData(399031, "0x616B7 month")]
    [InlineData(399968 + 4697 + 53, "hospital 1 +53 money")]
    [InlineData(0x61000, "0x61000")]
    public void DescribesOffsets(int offset, string expected)
    {
        Assert.Equal(expected, OriginalSaveGame.DescribeOffset(offset));
    }

    [Fact]
    public void ReadsRooms()
    {
        var data = new byte[OriginalSaveGame.Length];
        var room = OriginalSaveGame.RoomOffset + (17 * OriginalSaveGame.RoomLength);
        data[room] = 7;
        data[room + 1] = 4;
        data[room + 2] = 5;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 4), (81 * 128) + 82);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 6), (81 * 128) + 83);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(room + 8), (80 * 128) + 83);

        var parsed = Assert.Single(OriginalSaveGame.Parse(data).Rooms);

        Assert.Equal(17, parsed.Index);
        Assert.Equal(7, parsed.Type);
        Assert.Equal((4, 5), (parsed.Width, parsed.Height));
        Assert.Equal((83, 80), parsed.Origin);
        Assert.Equal((83, 81), parsed.Door);
        Assert.Equal((82, 81), parsed.Outside);
    }

    [Fact]
    public void ReadsPlotOwners()
    {
        var data = new byte[OriginalSaveGame.Length];
        data[OriginalSaveGame.PlotOffset] = 16;
        data[OriginalSaveGame.PlotOffset + 1] = 1;

        var save = OriginalSaveGame.Parse(data);

        Assert.Equal(16, save.PlotOwner(0));
        Assert.Equal(1, save.PlotOwner(1));
        Assert.Equal(0, save.PlotOwner(2));
    }

    [Fact]
    public void ReadsPolicyResearchReputationAndDiseases()
    {
        var data = new byte[OriginalSaveGame.Length];
        var hospital = OriginalSaveGame.HospitalOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 535), 1000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 537), 79);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 539), 70);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(hospital + 541), 600);
        data[hospital + 543] = 1;
        short[] distribution = [-1, -1, 20, 0, 20];
        for (var i = 0; i < distribution.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(hospital + 433 + (2 * i)), distribution[i]);
        }

        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(hospital + 3198), 844);
        var gp = hospital + 544 + 38;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 12), 168);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 16), 10000);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(gp + 20), 9180);
        data[gp + 28] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gp + 30), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gp + 32), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(gp + 36), 1);

        var parsed = OriginalSaveGame.Parse(data).Hospitals[0];

        Assert.Equal((1000, 79, 70, 600, true), (parsed.StopProcedure, parsed.GuessCurePercent, parsed.SendHomePercent, parsed.StaffRestLevel, parsed.StaffLeaveRooms));
        Assert.Equal([-1, -1, 20, 0, 20], parsed.ResearchDistribution);
        Assert.Equal(844, parsed.Reputation);
        var disease = parsed.Disease(1);
        Assert.Equal((168, 10000, 9180, true), (disease.Charge, disease.ChargePercent, disease.MoneyEarned, disease.Known));
        Assert.Equal((3, 2, 1), (disease.Recoveries, disease.Fatalities, disease.TurnedAway));
        Assert.False(parsed.Disease(2).Known);
    }

    [Fact]
    public void ReadsChartHistory()
    {
        var data = new byte[OriginalSaveGame.Length];
        var balance = OriginalSaveGame.ChartOffset + (3 * 3 * 12 * 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(balance), -5189);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(balance + (11 * 4)), 293);

        var chart = OriginalSaveGame.Parse(data).Chart(OriginalSaveChart.Balance);

        Assert.Equal(12, chart.Count);
        Assert.Equal(-5189, chart[0]);
        Assert.Equal(293, chart[11]);
    }

    [Fact]
    public void ReadsBankStatement()
    {
        var data = new byte[OriginalSaveGame.Length];
        var line = OriginalSaveGame.StatementOffset + 8;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(line), -2280);
        data[line + 4] = 4;
        data[line + 5] = 2;
        data[line + 6] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(line + 8), 923);
        data[line + 12] = 7;
        data[line + 13] = 28;
        data[line + 14] = 8;
        data[line + 15] = 2;

        var statement = OriginalSaveGame.Parse(data).Statement;

        Assert.Equal([new OriginalSaveStatementLine(-2280, 4, 2, 0, 0), new OriginalSaveStatementLine(923, 7, 28, 8, 2)], statement);
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
        data[entity + 81] = 94;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(entity + 120), 324);

        var staff = Assert.Single(OriginalSaveGame.Parse(data).Entities);

        Assert.Equal(16 + 32 + 256, staff.StaffFlags);
        Assert.Equal(565, staff.Skill);
        Assert.Equal(161, staff.Salary);
        Assert.Equal('T', staff.Initial);
        Assert.Equal((16, 15), (staff.NameStart, staff.NameEnd));
        Assert.Equal(((byte)23, (byte)13), staff.Description);
        Assert.Equal(94, staff.Morale);
        Assert.Equal(324, staff.Energy);
    }

    [Fact]
    public void ReadsPatientHealthHistory()
    {
        var data = new byte[OriginalSaveGame.Length];
        var entity = OriginalSaveGame.EntityOffset + (189 * OriginalSaveGame.EntityLength);
        data[entity + 12] = 2;
        byte[] history = [13, 13, 13, 13, 13, 13, 26, 21, 16, 11];
        history.CopyTo(data, entity + 158);
        data[entity + 81] = 68;
        data[entity + 134] = 42;
        data[entity + 83] = 31;

        var patient = Assert.Single(OriginalSaveGame.Parse(data).Entities);

        Assert.Equal(history, patient.HealthHistory.ToArray());
        Assert.Equal(68, patient.Happiness);
        Assert.Equal(42, patient.Thirst);
        Assert.Equal(31, patient.Warmth);
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

            var bySlotAll = save.Entities.ToDictionary(e => e.Slot);

            // The chart history is recorded month by month, so it never runs ahead of today's totals.
            Assert.True(save.Chart(OriginalSaveChart.Cures).Max() <= local.PeopleCured);

            // Following an object type's list from its first object visits objects of that type.
            var listed = 0;
            var wrongType = 0;
            for (var type = 1; type < OriginalSaveGame.ObjectTypes; type++)
            {
                var seen = new HashSet<int>();
                for (var objectSlot = save.FirstObjectOfType(type); objectSlot != 0 && seen.Add(objectSlot) && bySlotAll.TryGetValue(objectSlot, out var item); objectSlot = item.NextOfType)
                {
                    listed++;
                    wrongType += item.Type == type ? 0 : 1;
                }
            }

            // A fresh level carries a few stale entries over, so only judge saves with real furniture.
            Assert.True(listed < 20 || wrongType * 10 <= listed, $"{wrongType} of {listed} listed objects have another type");

            // Rooms lie on the map with their door on their edge and the outside tile next to it.
            foreach (var room in save.Rooms.Where(r => r.Width > 1))
            {
                Assert.InRange(room.Door.X, room.Origin.X, room.Origin.X + room.Width - 1);
                Assert.InRange(room.Door.Y, room.Origin.Y, room.Origin.Y + room.Height - 1);
                Assert.Equal(1, Math.Abs(room.Door.X - room.Outside.X) + Math.Abs(room.Door.Y - room.Outside.Y));
            }

            // Every staff member on the payroll has an entity (someone fired and still walking out has one too),
            // and tiles point at entities standing on them.
            Assert.True(save.Entities.Count(e => e.Kind == 4 && e.TileX != 0) >= local.Staff);
            var bySlot = save.Entities.ToDictionary(e => e.Slot);
            for (var y = 0; y < OriginalSaveGame.MapSize; y++)
            {
                for (var x = 0; x < OriginalSaveGame.MapSize; x++)
                {
                    if (save[x, y].Entity is var slot and not 0)
                    {
                        for (var entity = bySlot[slot]; ; entity = bySlot[entity.NextOnTile])
                        {
                            Assert.Equal((x, y), (entity.TileX, entity.TileY));
                            if (entity.NextOnTile == 0)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
}
