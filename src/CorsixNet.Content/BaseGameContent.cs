// Values from CorsixTH CorsixTH/Lua/rooms/*.lua, CorsixTH/Lua/walls/*.lua and CorsixTH/Lua/objects/*.lua.
// Animation lengths are the frame counts of those animations in the original's VSTART-1/VFRA-1.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim.Definitions;
using CorsixNet.Sim.Entities;
using static CorsixNet.Sim.Definitions.FootprintFlags;

namespace CorsixNet.Content;

/// <summary>
/// Content definitions of the original game. For now they are written in code; they will move
/// to data files that mods can extend.
/// </summary>
public static class BaseGameContent
{
    /// <summary>The "white" wall set (walls/white.lua).</summary>
    public static readonly WallSet WhiteWalls = new(InsideNorth: 82, InsideWest: 83, OutsideNorth: 90, OutsideWest: 91, WindowNorth: 128, WindowWest: 129);

    /// <summary>The "green" wall set (walls/green.lua).</summary>
    public static readonly WallSet GreenWalls = new(InsideNorth: 88, InsideWest: 89, OutsideNorth: 96, OutsideWest: 97, WindowNorth: 140, WindowWest: 141);

    /// <summary>The doctor's desk (objects/desk.lua). The doctor's chair is part of it.</summary>
    public static readonly ObjectDefinition Desk = new(
        "desk",
        "Desk",
        ObjectType: 1,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-2, -1, CompleteCell), new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, -1, NeedNorthSide), new(0, 0, UsePosition)], (0, -1)),
            [Direction.East] = new(
                [new(0, -2, NeedEastSide), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, UsePosition), new(0, 0, CompleteCell), new(-1, -2, UsePosition)],
                (0, -1),
                UsePosition: (-1, 0),
                FinishUsePosition: (-1, -2)),
            [Direction.South] = new(
                [new(0, -1, UsePosition), new(-2, 0, NeedSouthSide), new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell), new(-2, -1, UsePosition)],
                (-2, 0),
                UsePosition: (0, -1),
                FinishUsePosition: (-2, -1)),
            [Direction.West] = new([new(-1, -2, CompleteCell), new(0, 0, UsePosition), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, NeedWestSide)], (-1, 0)),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations>
        {
            // 72 (normal use) is meant to happen far more often than 718 (head scratching).
            [HumanoidType.Doctor] = new(new(56, 18), [.. Enumerable.Repeat(new AnimationRef(72, 4), 11), new(718, 17)], new(64, 13)),
        },
        UsageEast: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.Doctor] = new(new(58, 18), [new(74, 9)], new(66, 9)),
        });

    /// <summary>The filing cabinet (objects/cabinet.lua).</summary>
    public static readonly ObjectDefinition Cabinet = new(
        "cabinet",
        "Cabinet",
        ObjectType: 2,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition)]),
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition)]),
            [Direction.West] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(88, 10)], null) },
        UsageEast: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(90, 14)], null) });

    /// <summary>The patient's chair (objects/chair.lua).</summary>
    public static readonly ObjectDefinition Chair = new(
        "chair",
        "Chair",
        ObjectType: 6,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition | Shareable)]),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition | Shareable)]),
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition | Shareable)]),
            [Direction.West] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition | Shareable)]),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.StandardMalePatient] = new(new(678, 2), [new(702, 1)], new(694, 2)),
            [HumanoidType.StandardFemalePatient] = new(new(2824, 2), [new(2814, 1)], new(2832, 2)),
        },
        UsageEast: new Dictionary<HumanoidType, UseAnimations>
        {
            // Not talking, nodding and talking, weighted 16 : 3 : 1 as chair.lua's make_list does.
            [HumanoidType.StandardMalePatient] = new(new(680, 2), ChairTalk(new(704, 1), new(736, 2), new(744, 27)), new(696, 2)),
            [HumanoidType.StandardFemalePatient] = new(new(2826, 2), ChairTalk(new(2818, 1), new(3002, 2), new(2994, 13)), new(2834, 2)),
        },
        WalkInToUse: true);

    /// <summary>
    /// The reception desk (objects/reception_desk.lua). Patients queue at its use position; the
    /// receptionist comes from the secondary use position and stands on the desk's own tile.
    /// </summary>
    public static readonly ObjectDefinition ReceptionDesk = new(
        "reception_desk",
        "Reception Desk",
        ObjectType: 11,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(0, 0), new(0, -1, UsePosition), new(0, 1, UsePosition), new(1, 0, NeedNorthSide | NeedSouthSide), new(-1, 0, NeedNorthSide | NeedSouthSide)],
                UsePosition: (0, -1),
                SecondaryUsePosition: (0, 1)),
            [Direction.East] = new(
                [new(0, 0), new(0, -1, NeedWestSide | NeedEastSide), new(0, 1, NeedWestSide | NeedEastSide), new(1, 0, UsePosition), new(-1, 0, UsePosition)],
                UsePosition: (1, 0),
                SecondaryUsePosition: (-1, 0)),
            [Direction.South] = new(
                [new(0, 0), new(0, -1, UsePosition), new(0, 1, UsePosition), new(1, 0, NeedNorthSide | NeedSouthSide), new(-1, 0, NeedNorthSide | NeedSouthSide)],
                UsePosition: (0, 1),
                SecondaryUsePosition: (0, -1)),
            [Direction.West] = new(
                [new(0, 0), new(0, -1, NeedWestSide | NeedEastSide), new(0, 1, NeedWestSide | NeedEastSide), new(1, 0, UsePosition), new(-1, 0, UsePosition)],
                UsePosition: (-1, 0),
                SecondaryUsePosition: (1, 0)),
        },
        CorridorObject: true);

    /// <summary>
    /// The bench (objects/bench.lua): patients sit on it from the tile in front, which other objects may share.
    /// CorsixTH draws it split over three tiles facing north; here it is drawn from its own tile.
    /// </summary>
    public static readonly ObjectDefinition Bench = new(
        "bench",
        "Bench",
        ObjectType: 4,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition | Shareable)]),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition | Shareable)]),
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition | Shareable)]),
            [Direction.West] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition | Shareable)]),
        },
        CorridorObject: true,
        WalkInToUse: true);

    /// <summary>The drinks machine (objects/drinks_machine.lua), used from the tile in front.</summary>
    public static readonly ObjectDefinition DrinksMachine = new(
        "drinks_machine",
        "Drinks Machine",
        ObjectType: 7,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition)]),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.West] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition)]),
        },
        CorridorObject: true,
        UsageEast: DrinksMachineUse(196, 14, 256, 11),
        UsageSouth: DrinksMachineUse(190, 14, 250, 11),
        UsageWest: DrinksMachineUse(192, 11, 252, 10));

    /// <summary>The fire extinguisher (objects/fire_extinguisher.lua), a side object.</summary>
    public static readonly ObjectDefinition FireExtinguisher = new("extinguisher", "Fire Extinguisher", ObjectType: 43, SideOrientations(Direction.North, Direction.East, Direction.South, Direction.West), CorridorObject: true);

    /// <summary>The radiator (objects/radiator.lua), a side object. Heating is not ported yet.</summary>
    public static readonly ObjectDefinition Radiator = new("radiator", "Radiator", ObjectType: 44, SideOrientations(Direction.North, Direction.East, Direction.South, Direction.West), CorridorObject: true);

    /// <summary>The bin (objects/bin.lua), a side object that only faces north or east (which stands against the west side).</summary>
    public static readonly ObjectDefinition Bin = new("bin", "Bin", ObjectType: 50, SideOrientations(Direction.North, Direction.East), CorridorObject: true);

    /// <summary>The plant (objects/plant.lua). It droops over time (<c>PlantState</c>); watering is not ported yet.</summary>
    public static readonly ObjectDefinition Plant = new(
        "plant",
        "Plant",
        ObjectType: 45,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell)], AnimateFromUsePosition: true),
            [Direction.South] = new([new(0, 0, CompleteCell)], AnimateFromUsePosition: true),
            [Direction.West] = new([new(0, 0, CompleteCell)], AnimateFromUsePosition: true),
        },
        CorridorObject: true);

    /// <summary>
    /// The pharmacy cabinet (objects/pharmacy_cabinet.lua). The nurse stands at the use position and hands the patient,
    /// who stands at the secondary use position, a drink; only north and east facings exist, and the east one mirrors
    /// the north one's animations.
    /// </summary>
    public static readonly ObjectDefinition PharmacyCabinet = new(
        "pharmacy_cabinet",
        "Pharmacy Cabinet",
        ObjectType: 39,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(0, 0, CompleteCell), new(0, 1, UsePosition), new(0, 2, UsePosition), new(-1, 1, UsePosition)],
                UsePosition: (0, 1),
                SecondaryUsePosition: (-1, 1),
                AnimateFromUsePosition: true),
            [Direction.East] = new(
                [new(0, 0, CompleteCell), new(1, 0, UsePosition), new(2, 0, UsePosition), new(1, -1, UsePosition)],
                UsePosition: (1, 0),
                SecondaryUsePosition: (1, -1),
                AnimateFromUsePosition: true),
        },
        MultiUsage: PharmacyMultiUsage());

    /// <summary>
    /// The screen (objects/screen.lua): patients undress and dress behind it, its animation showing them. It only faces
    /// north. While a patient's clothes hang on it, it is drawn with them (see <see cref="RoomObject.HangingClothes"/>).
    /// </summary>
    public static readonly ObjectDefinition Screen = new(
        "screen",
        "Screen",
        ObjectType: 16,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-1, -1, UsePosition), new(-1, 0), new(0, -1), new(0, 0)], RenderAttach: (-1, 0)),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations>
        {
            // Undressing, then dressing again.
            [HumanoidType.StandardMalePatient] = new(null, [new(1048, 22)], null),
            [HumanoidType.StandardFemalePatient] = new(null, [new(2848, 31)], null),
            [HumanoidType.StrippedMalePatient] = new(null, [new(1052, 39)], null),
            [HumanoidType.StrippedFemalePatient] = new(null, [new(2844, 29)], null),
        });

    /// <summary>
    /// The crash trolley (objects/crash_trolley.lua). The patient lies on it from its use position while the doctor stands
    /// at the secondary one. CorsixTH draws it split over two tiles; here it is drawn from its own tile.
    /// </summary>
    public static readonly ObjectDefinition CrashTrolley = new(
        "crash_trolley",
        "Crash Trolley",
        ObjectType: 20,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(-1, 0, CompleteCell), new(-1, 1, UsePosition), new(0, 0, UsePosition), new(0, 1, UsePosition)],
                UsePosition: (-1, 1),
                SecondaryUsePosition: (0, 0)),
            [Direction.East] = new(
                [new(0, -1, CompleteCell), new(0, 0, UsePosition), new(1, -1, UsePosition), new(1, 0, UsePosition)],
                UsePosition: (1, -1),
                SecondaryUsePosition: (0, 0)),
        },
        MultiUsage: TrolleyMultiUsage());

    /// <summary>The extra objects the GP's office, the pharmacy and general diagnosis take (their <c>objects_additional</c>).</summary>
    private static readonly string[] AdditionalRoomObjects = ["extinguisher", "radiator", "plant", "bin"];

    /// <summary>The GP's office (rooms/gp.lua).</summary>
    public static readonly RoomDefinition GpOffice = new(
        "gp",
        "GP's Office",
        MinimumSize: 4,
        FloorSprite: 18,
        WhiteWalls,
        NeededObjects: new Dictionary<string, int> { ["desk"] = 1, ["cabinet"] = 1, ["chair"] = 1 },
        LevelConfigId: 7,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The pharmacy (rooms/pharmacy.lua).</summary>
    public static readonly RoomDefinition Pharmacy = new(
        "pharmacy",
        "Pharmacy",
        MinimumSize: 4,
        FloorSprite: 19,
        WhiteWalls,
        NeededObjects: new Dictionary<string, int> { ["pharmacy_cabinet"] = 1 },
        LevelConfigId: 11,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>General diagnosis (rooms/general_diag.lua).</summary>
    public static readonly RoomDefinition GeneralDiagnosis = new(
        "general_diag",
        "General Diagnosis",
        MinimumSize: 5,
        FloorSprite: 21,
        GreenWalls,
        NeededObjects: new Dictionary<string, int> { ["screen"] = 1, ["crash_trolley"] = 1 },
        LevelConfigId: 27,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>Sprites of the hospital's outer walls (walls/external.lua).</summary>
    private static readonly ushort[] ExternalWalls =
    [
        122, 124, 126, 144, 123, 127, 125, 145,
        114, 116, 118, 120, 142, 115, 119, 117, 121, 143,
    ];

    /// <summary>
    /// Door animations (DoorE, DoorL) of entities/humanoid.lua's <c>walk_anims</c> table. The receptionist and undressed
    /// patients have none there; they never go through a room door.
    /// </summary>
    public static readonly IReadOnlyDictionary<HumanoidType, DoorAnimations> DoorAnimations = new Dictionary<HumanoidType, DoorAnimations>
    {
        [HumanoidType.StandardMalePatient] = new(Entering: new(184, 10), Leaving: new(182, 12)),
        [HumanoidType.StandardFemalePatient] = new(Entering: new(260, 10), Leaving: new(258, 12)),
        [HumanoidType.Doctor] = new(Entering: new(672, 10), Leaving: new(670, 12)),
        [HumanoidType.Nurse] = new(Entering: new(3266, 10), Leaving: new(3264, 12)),
        [HumanoidType.Handyman] = new(Entering: new(3288, 10), Leaving: new(3286, 12)),
    };

    public static GameContent Create() => new(
        [GpOffice, Pharmacy, GeneralDiagnosis],
        ExternalWalls,
        // Corridor objects in the order of CorsixTH's furnish corridor window (corridor_object).
        [Desk, Cabinet, Chair, PharmacyCabinet, Screen, CrashTrolley, ReceptionDesk, Bench, DrinksMachine, FireExtinguisher, Radiator, Bin, Plant],
        DoorAnimations);

    /// <summary>
    /// pharmacy_cabinet.lua's <c>anim_set</c>: the nurse's own begin and finish animations around the ones showing her with
    /// the patient (<paramref name="begin"/>, <paramref name="finish"/>); the patient idles (<paramref name="idle"/>) while
    /// she gets the drink and drinks it (<paramref name="inUse"/>) while she waits. Defined for north and south.
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> PharmacyMultiUsage()
    {
        static MultiUseAnimations[] AnimSet(AnimationRef begin, AnimationRef finish, AnimationRef idle, AnimationRef inUse)
        {
            return
            [
                new MultiUseAnimations(
                    [new(new(1590, 22), idle), new(new(1594, 42), idle), new(begin)],
                    new(new(1666, 1), inUse),
                    [new(finish), new(new(1598, 9), idle)]),
            ];
        }

        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.Nurse, HumanoidType.StandardMalePatient)] = AnimSet(new(1630, 25), new(1638, 10), new(26, 1), new(1662, 26)),
            [(HumanoidType.Nurse, HumanoidType.StandardFemalePatient)] = AnimSet(new(3104, 25), new(3112, 10), new(10, 1), new(3108, 26)),
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>
    /// crash_trolley.lua's <c>multi_usage_animations</c>: the undressed patient lies down, is examined and gets up, the
    /// doctor drawn in the animations (no secondary ones, so the doctor is hidden). Each phase lists three animations; the
    /// use plays the same position of each list. Defined for north and south.
    /// </summary>
    /// <summary>
    /// The drinks machine's <c>in_use</c> animations for the standard male and female patients; it has them facing south,
    /// east and west, and north mirrors west. CorsixTH's one-pixel <c>added_animation_offset_while_in_use</c> facing north
    /// is left out.
    /// </summary>
    private static Dictionary<HumanoidType, UseAnimations> DrinksMachineUse(int male, int maleLength, int female, int femaleLength) => new()
    {
        [HumanoidType.StandardMalePatient] = new(null, [new(male, maleLength)], null),
        [HumanoidType.StandardFemalePatient] = new(null, [new(female, femaleLength)], null),
    };

    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> TrolleyMultiUsage()
    {
        static MultiUseAnimations[] Variants(AnimationRef[] begin, AnimationRef[] inUse, AnimationRef[] finish)
        {
            return [.. Enumerable.Range(0, begin.Length).Select(i => new MultiUseAnimations([new(begin[i])], new(inUse[i]), [new(finish[i])]))];
        }

        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.StrippedMalePatient, HumanoidType.Doctor)] = Variants(
                [new(3740, 14), new(3736, 14), new(3818, 11)],
                [new(540, 25), new(544, 41), new(552, 29)],
                [new(3802, 12), new(3806, 12), new(3826, 6)]),
            [(HumanoidType.StrippedFemalePatient, HumanoidType.Doctor)] = Variants(
                [new(3732, 14), new(3728, 14), new(3822, 11)],
                [new(524, 22), new(528, 35), new(536, 29)],
                [new(3810, 12), new(3814, 12), new(3830, 6)]),
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>Layouts of a side object (CorsixTH's <c>only_side</c> footprint) for the given facings.</summary>
    private static Dictionary<Direction, ObjectOrientation> SideOrientations(params Direction[] facings)
    {
        return facings.ToDictionary(facing => facing, _ => new ObjectOrientation([new(0, 0, OnlySide)]));
    }

    private static AnimationRef[] ChairTalk(AnimationRef quiet, AnimationRef nodding, AnimationRef talking)
    {
        return [.. Enumerable.Repeat(quiet, 16), .. Enumerable.Repeat(nodding, 3), talking];
    }
}
