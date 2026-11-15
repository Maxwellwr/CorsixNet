// Layout from the community format description (github.com/alexandergitter/theme-hospital-spec,
// "Savegames"), checked against real saves by comparing files; no game code was disassembled.

using System.Buffers.Binary;
using System.Text;

namespace CorsixNet.Formats;

/// <summary>One map tile of an original save.</summary>
/// <param name="Entity">Slot in <see cref="OriginalSaveGame.Entities"/> of an entity on the tile, or 0.</param>
/// <param name="Floor">Floor block code, as in <see cref="MapFileTile.Floor"/>.</param>
/// <param name="NorthWall">North wall block code, or 0.</param>
/// <param name="WestWall">West wall block code, or 0.</param>
/// <param name="Flags">Byte 5; in level files these are the passable/buildable flags.</param>
/// <param name="Extra">Byte 6: high nibble shadow, low nibble litter.</param>
/// <param name="Owner">Byte 7: bit 16 set on tiles outside the hospital, cleared when their parcel is bought (as in level files).</param>
public readonly record struct OriginalSaveTile(
    ushort Entity,
    byte Floor,
    byte NorthWall,
    byte WestWall,
    byte Flags,
    byte Extra,
    byte Owner);

/// <summary>
/// One 175-byte slot of the entity table (objects, staff, patients and other things placed on the map).
/// Only a few fields are known; <see cref="Raw"/> keeps the whole record.
/// </summary>
/// <param name="Slot">Index of the record in the table.</param>
/// <param name="Raw">The 175 stored bytes.</param>
public sealed record OriginalSaveEntity(int Slot, ReadOnlyMemory<byte> Raw)
{
    /// <summary>Slot of the next entity on the same tile, or 0; the tile's <see cref="OriginalSaveTile.Entity"/> is the first.</summary>
    public int NextOnTile => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span);

    /// <summary>Position along x in 1/256 tile (8.8 fixed point).</summary>
    public int X => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[4..]);

    /// <summary>Position along y in 1/256 tile (8.8 fixed point).</summary>
    public int Y => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[6..]);

    /// <summary>Tile column the entity stands on.</summary>
    public int TileX => X >> 8;

    /// <summary>Tile row the entity stands on.</summary>
    public int TileY => Y >> 8;

    /// <summary>Animation frame index.</summary>
    public int Frame => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[8..]);

    /// <summary>
    /// Byte 12, the kind of entity. Seen so far: 6 objects, 8 doors (with <see cref="Type"/> 3 or a swing door
    /// type), 4 staff and staff for hire, 2 patients, 20 not yet known.
    /// </summary>
    public byte Kind => Raw.Span[12];

    /// <summary>
    /// Byte 14: for objects the object type ("thob", as in level files); for staff 0 nurse, 1 doctor, 4 doctor dressed for surgery,
    /// 2 handyman, 3 receptionist.
    /// </summary>
    public byte Type => Raw.Span[14];

    /// <summary>
    /// Objects only: direction the object faces, encoded as <see cref="MapFileTile.ObjectDirection"/> in level files
    /// (a bench facing south-east on screen has 1).
    /// </summary>
    public byte Direction => Raw.Span[17];

    /// <summary>
    /// Patients only: the history drawn in the patient window's health graph, oldest first, from 0 to 100;
    /// values above 100 mark points not recorded yet (a patient who just arrived).
    /// </summary>
    public ReadOnlySpan<byte> HealthHistory => Raw.Span.Slice(158, 10);

    /// <summary>Patients only: happiness from 0 to 100, the first bar of the patient window.</summary>
    public byte Happiness => Raw.Span[81];

    /// <summary>Patients only: the thirst bar from 0 (very thirsty) to 100 (not thirsty).</summary>
    public byte Thirst => Raw.Span[134];

    /// <summary>
    /// Staff only: role bits, 1 receptionist, 2 handyman, 4 nurse, 8 junior, 16 doctor, 32 surgeon,
    /// 64 psychiatrist, 128 consultant, 256 researcher.
    /// </summary>
    public int StaffFlags => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[124..]);

    /// <summary>Staff only: skill from 0 to 1000.</summary>
    public int Skill => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[126..]);

    /// <summary>Staff only: monthly salary.</summary>
    public int Salary => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[108..]);

    /// <summary>Staff only: first letter of the name (the initial).</summary>
    public char Initial => (char)Raw.Span[132];

    /// <summary>Staff only: first part of the surname, an index into string section 8 of <c>LANG-*.DAT</c>.</summary>
    public byte NameStart => Raw.Span[133];

    /// <summary>Staff only: end of the surname, an index into string section 9.</summary>
    public byte NameEnd => Raw.Span[134];

    /// <summary>Staff only: the two description phrases, indices into string section 45 (misc) and then 46 or 47 (good or bad).</summary>
    public (byte Misc, byte Character) Description => (Raw.Span[138], Raw.Span[139]);
}

/// <summary>One 35-byte record of the room table. Only the first ten bytes are understood.</summary>
/// <param name="Index">Index of the record from the start of the table.</param>
/// <param name="Raw">The 35 stored bytes.</param>
public sealed record OriginalSaveRoom(int Index, ReadOnlyMemory<byte> Raw)
{
    /// <summary>
    /// Room type, numbered as <c>#rooms[n]</c> in the level <c>.SAM</c> files (7 GP's office, 9 ward, 12 cardiogram...).
    /// Every save starts with four type-1 records of size 0, presumably one per hospital.
    /// </summary>
    public byte Type => Raw.Span[0];

    /// <summary>Width along x, in tiles.</summary>
    public int Width => Raw.Span[1];

    /// <summary>Height along y, in tiles.</summary>
    public int Height => Raw.Span[2];

    /// <summary>Corner tile with the smallest x and y.</summary>
    public (int X, int Y) Origin => ReadTile(8);

    /// <summary>Tile of the door, inside the room.</summary>
    public (int X, int Y) Door => ReadTile(6);

    /// <summary>Tile just outside the door.</summary>
    public (int X, int Y) Outside => ReadTile(4);

    private (int X, int Y) ReadTile(int offset)
    {
        int index = BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[offset..]);
        return (index % OriginalSaveGame.MapSize, index / OriginalSaveGame.MapSize);
    }
}

/// <summary>Per-player hospital data of an original save (4697 bytes each).</summary>
/// <param name="Name">Hospital (player) name.</param>
/// <param name="Kind">1 local player, 2 computer player, 0 unused.</param>
/// <param name="Money">Bank balance.</param>
/// <param name="Loan">Outstanding loan.</param>
/// <param name="Value">Hospital value.</param>
/// <param name="PeopleCured">Patients cured.</param>
/// <param name="Deaths">Patients who died.</param>
/// <param name="Visitors">Visitors so far.</param>
/// <param name="Size">Hospital size in tiles.</param>
/// <param name="Staff">Staff count in total.</param>
/// <param name="Nurses">Nurses employed.</param>
/// <param name="Doctors">Doctors employed.</param>
/// <param name="Handymen">Handymen employed.</param>
/// <param name="Receptionists">Receptionists employed.</param>
/// <param name="Raw">The whole stored block.</param>
public sealed record OriginalSaveHospital(
    string Name,
    byte Kind,
    int Money,
    int Loan,
    int Value,
    int PeopleCured,
    int Deaths,
    int Visitors,
    int Size,
    int Staff,
    int Nurses,
    int Doctors,
    int Handymen,
    int Receptionists,
    ReadOnlyMemory<byte> Raw)
{
    /// <summary>Offset of the disease (expertise) table in the block.</summary>
    public const int DiseaseOffset = 544;

    /// <summary>Size of one disease record.</summary>
    public const int DiseaseLength = 38;

    /// <summary>Number of disease records, indexed like <c>#expertise[n]</c> in the <c>.SAM</c> files (1 general practice).</summary>
    public const int DiseaseCount = 47;

    /// <summary>Reputation as shown in the casebook.</summary>
    public int Reputation => BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[3198..]);

    /// <summary>Policy: stop diagnosis at this share of the normal procedure, in tenths of a percent (1000 = 100%).</summary>
    public int StopProcedure => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[535..]);

    /// <summary>Policy: guess at cure above this diagnosis certainty, in percent.</summary>
    public int GuessCurePercent => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[537..]);

    /// <summary>Policy: send patients home below this diagnosis certainty, in percent.</summary>
    public int SendHomePercent => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[539..]);

    /// <summary>Policy: send staff to the staff room at this tiredness, in tenths of a percent.</summary>
    public int StaffRestLevel => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[541..]);

    /// <summary>Policy: whether staff may leave their rooms.</summary>
    public bool StaffLeaveRooms => Raw.Span[543] != 0;

    /// <summary>
    /// Research distribution in percent: cure equipment, diagnosis equipment, drugs, improvements,
    /// specialisation; -1 when a category is finished.
    /// </summary>
    public IReadOnlyList<int> ResearchDistribution =>
        [.. Enumerable.Range(0, 5).Select(i => (int)BinaryPrimitives.ReadInt16LittleEndian(Raw.Span[(433 + (2 * i))..]))];

    /// <summary>The disease (expertise) record with an index.</summary>
    public OriginalSaveDisease Disease(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, DiseaseCount);
        return new OriginalSaveDisease(index, Raw.Slice(DiseaseOffset + (index * DiseaseLength), DiseaseLength));
    }
}

/// <summary>One 38-byte disease (expertise) record of a hospital: casebook figures and treatment price.</summary>
/// <param name="Index">Index as in <c>#expertise[n]</c> of the <c>.SAM</c> files.</param>
/// <param name="Raw">The stored bytes.</param>
public sealed record OriginalSaveDisease(int Index, ReadOnlyMemory<byte> Raw)
{
    /// <summary>Treatment charge.</summary>
    public int Charge => BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[12..]);

    /// <summary>Charge relative to the standard price, in hundredths of a percent (10000 = 100%).</summary>
    public int ChargePercent => BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[16..]);

    /// <summary>Money earned from it.</summary>
    public int MoneyEarned => BinaryPrimitives.ReadInt32LittleEndian(Raw.Span[20..]);

    /// <summary>Whether it is known (shown in the casebook).</summary>
    public bool Known => Raw.Span[28] != 0;

    /// <summary>Patients cured.</summary>
    public int Recoveries => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[30..]);

    /// <summary>Patients who died of it.</summary>
    public int Fatalities => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[32..]);

    /// <summary>Patients turned away.</summary>
    public int TurnedAway => BinaryPrimitives.ReadUInt16LittleEndian(Raw.Span[36..]);
}

/// <summary>
/// A save of the original Theme Hospital (<c>SAVE/&lt;name&gt;.H&lt;n&gt;</c>): a fixed-size, uncompressed
/// dump of the game state. Only the parts that are understood are decoded.
/// </summary>
/// <remarks>
/// Layout: 13-byte header (level at offset 2, difficulty name from offset 3); 128×128 tiles of 8 bytes
/// from offset 13; 1500 entity records of 175 bytes from 131085; day and month at 399030; four
/// 4697-byte hospital blocks from 399968.
/// </remarks>
public sealed class OriginalSaveGame
{
    /// <summary>Size of every save file.</summary>
    public const int Length = 420314;

    /// <summary>Width and height of the map, in tiles.</summary>
    public const int MapSize = 128;

    /// <summary>Number of entity slots.</summary>
    public const int EntityCount = 1500;

    /// <summary>Size of one entity record.</summary>
    public const int EntityLength = 175;

    /// <summary>Offset of the map.</summary>
    public const int TileOffset = 13;

    /// <summary>Offset of the entity table.</summary>
    public const int EntityOffset = TileOffset + (MapSize * MapSize * TileLength);

    /// <summary>Offset of the room table.</summary>
    public const int RoomOffset = 0x60194;

    /// <summary>Size of one room record.</summary>
    public const int RoomLength = 35;

    /// <summary>Number of room records read; the real limit is not known, but no data follows this many.</summary>
    public const int RoomCount = 100;

    /// <summary>Offset of the plot owner table: one byte per land parcel of the level map.</summary>
    public const int PlotOffset = 0x6661C;

    /// <summary>Number of plot bytes read; levels use far fewer parcels.</summary>
    public const int MaxPlots = 32;

    /// <summary>Offset of the day of the month (1-based); the month follows.</summary>
    public const int DateOffset = 399030;

    /// <summary>Offset of the first hospital block.</summary>
    public const int HospitalOffset = 399968;

    /// <summary>Size of one hospital block.</summary>
    public const int HospitalLength = 4697;

    /// <summary>Number of hospital blocks (players).</summary>
    public const int MaxPlayers = 4;

    private const int TileLength = 8;

    private readonly OriginalSaveTile[] _tiles;

    private OriginalSaveGame(byte[] data, OriginalSaveTile[] tiles, IReadOnlyList<OriginalSaveEntity> entities, IReadOnlyList<OriginalSaveRoom> rooms, IReadOnlyList<OriginalSaveHospital> hospitals)
    {
        Data = data;
        _tiles = tiles;
        Entities = entities;
        Rooms = rooms;
        Hospitals = hospitals;
    }

    /// <summary>The whole file.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Level number (1–12 in the campaign).</summary>
    public int Level => Data.Span[2];

    /// <summary>Difficulty name: <c>EASY</c>, <c>FULL</c> or <c>HARD</c>, the prefix of the level's <c>.SAM</c> files.</summary>
    public string Difficulty => ReadString(Data.Span[3..TileOffset]);

    /// <summary>Day of the month, starting at 1.</summary>
    public int Day => Data.Span[DateOffset];

    /// <summary>Month of the game, starting at 0.</summary>
    public int Month => Data.Span[DateOffset + 1];

    /// <summary>
    /// Owner of a land parcel (numbered as in the level's <c>LEVEL.L*</c> map): 0 for sale, 1 bought by the
    /// player, 16 for parcel 0 (outside the hospital). Tiles of bought parcels lose bit 16 of
    /// <see cref="OriginalSaveTile.Owner"/>.
    /// </summary>
    public byte PlotOwner(int parcel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(parcel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(parcel, MaxPlots);
        return Data.Span[PlotOffset + parcel];
    }

    /// <summary>The map tile at a position.</summary>
    public OriginalSaveTile this[int x, int y] => _tiles[(y * MapSize) + x];

    /// <summary>Entity slots in use (any non-zero byte), in slot order.</summary>
    public IReadOnlyList<OriginalSaveEntity> Entities { get; }

    /// <summary>The four hospital blocks; unused players have <see cref="OriginalSaveHospital.Kind"/> 0.</summary>
    public IReadOnlyList<OriginalSaveHospital> Hospitals { get; }

    /// <summary>Room records in use (non-zero type), in table order.</summary>
    public IReadOnlyList<OriginalSaveRoom> Rooms { get; }

    /// <summary>Parses a save file.</summary>
    /// <exception cref="InvalidDataException">The data does not have the size of a save.</exception>
    public static OriginalSaveGame Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != Length)
        {
            throw new InvalidDataException($"Save has {data.Length} bytes; {Length} expected.");
        }

        var tiles = new OriginalSaveTile[MapSize * MapSize];
        for (var i = 0; i < tiles.Length; i++)
        {
            var raw = data.AsSpan(TileOffset + (i * TileLength), TileLength);
            tiles[i] = new OriginalSaveTile(BinaryPrimitives.ReadUInt16LittleEndian(raw), raw[2], raw[3], raw[4], raw[5], raw[6], raw[7]);
        }

        var entities = new List<OriginalSaveEntity>();
        for (var slot = 0; slot < EntityCount; slot++)
        {
            var record = data.AsMemory(EntityOffset + (slot * EntityLength), EntityLength);
            if (record.Span.ContainsAnyExcept((byte)0))
            {
                entities.Add(new OriginalSaveEntity(slot, record));
            }
        }

        var rooms = new List<OriginalSaveRoom>();
        for (var index = 0; index < RoomCount; index++)
        {
            var record = data.AsMemory(RoomOffset + (index * RoomLength), RoomLength);
            if (record.Span[0] != 0)
            {
                rooms.Add(new OriginalSaveRoom(index, record));
            }
        }

        var hospitals = new OriginalSaveHospital[MaxPlayers];
        for (var player = 0; player < MaxPlayers; player++)
        {
            hospitals[player] = ParseHospital(data.AsMemory(HospitalOffset + (player * HospitalLength), HospitalLength));
        }

        return new OriginalSaveGame(data, tiles, entities, rooms, hospitals);
    }

    /// <summary>
    /// Finds the area an offset falls in, the index of the tile, entity, room or hospital record there,
    /// and the offset inside that record (or in the file, for <see cref="OriginalSaveArea.File"/>).
    /// </summary>
    public static (OriginalSaveArea Area, int Relative) Locate(int offset) => Locate(offset, out _);

    /// <inheritdoc cref="Locate(int)"/>
    public static (OriginalSaveArea Area, int Relative) Locate(int offset, out int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);
        (OriginalSaveArea, int)? InTable(OriginalSaveArea area, int start, int length, int count, out int record)
        {
            record = (offset - start) / length;
            return offset >= start && offset < start + (length * count) ? (area, (offset - start) % length) : null;
        }

        var found = InTable(OriginalSaveArea.Tile, TileOffset, TileLength, MapSize * MapSize, out index)
            ?? InTable(OriginalSaveArea.Entity, EntityOffset, EntityLength, EntityCount, out index)
            ?? InTable(OriginalSaveArea.Room, RoomOffset, RoomLength, RoomCount, out index)
            ?? InTable(OriginalSaveArea.Hospital, HospitalOffset, HospitalLength, MaxPlayers, out index);
        if (found is { } inTable)
        {
            return inTable;
        }

        index = 0;
        return (OriginalSaveArea.File, offset);
    }

    /// <summary>
    /// Names the part of the file an offset falls in, such as <c>tile (7,5) byte 2 floor</c>,
    /// <c>entity 9 +14 type</c> or <c>hospital 1 +53 money</c>; for comparing saves.
    /// </summary>
    public static string DescribeOffset(int offset)
    {
        var (area, relative) = Locate(offset, out var index);
        var place = area switch
        {
            OriginalSaveArea.Tile => $"tile ({index % MapSize},{index / MapSize}) byte {relative}",
            OriginalSaveArea.Entity => $"entity {index} +{relative}",
            OriginalSaveArea.Room => $"room {index} +{relative}",
            OriginalSaveArea.Hospital => $"hospital {index} +{relative}",
            _ when offset < TileOffset => $"header +{offset}",
            _ => $"0x{offset:X5}",
        };
        return OriginalSaveFields.Find(area, relative) is { } field ? $"{place} {field.Name}" : place;
    }

    private static OriginalSaveHospital ParseHospital(ReadOnlyMemory<byte> block)
    {
        var b = block.Span;
        int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(block.Span[offset..]);
        int U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(block.Span[offset..]);
        return new OriginalSaveHospital(
            Name: ReadString(b[..42]),
            Kind: b[42],
            Money: I32(53),
            Loan: I32(61),
            Value: I32(443),
            PeopleCured: U16(489),
            Deaths: U16(491),
            Visitors: U16(493),
            Size: U16(497),
            Staff: U16(3228),
            Nurses: U16(3230),
            Doctors: U16(3232),
            Handymen: U16(3234),
            Receptionists: U16(3236),
            Raw: block);
    }

    private static string ReadString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        var text = end < 0 ? bytes : bytes[..end];
        var builder = new StringBuilder(text.Length);
        foreach (var code in text)
        {
            builder.Append((char)Cp437.ToUnicode(code));
        }

        return builder.ToString();
    }
}
