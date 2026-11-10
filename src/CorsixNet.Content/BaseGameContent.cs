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
    public static readonly WallSet WhiteWalls = new(InsideNorth: 82, InsideWest: 83, OutsideNorth: 90, OutsideWest: 91, WindowNorth: 128, WindowWest: 129)
    {
        Swing = new SwingWalls(98, 99, 130, 131, 106, 107, 148, 149),
    };

    /// <summary>The "green" wall set (walls/green.lua).</summary>
    /// <summary>walls/yellow.lua.</summary>
    public static readonly WallSet YellowWalls = new(InsideNorth: 84, InsideWest: 85, OutsideNorth: 92, OutsideWest: 93, WindowNorth: 132, WindowWest: 133);

    /// <summary>walls/blue.lua.</summary>
    public static readonly WallSet BlueWalls = new(InsideNorth: 86, InsideWest: 87, OutsideNorth: 94, OutsideWest: 95, WindowNorth: 136, WindowWest: 137);

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
            [HumanoidType.Nurse] = new(new(3240, 16), [new(3256, 14)], new(3248, 11)),
        },
        UsageEast: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.Doctor] = new(new(58, 18), [new(74, 9)], new(66, 9)),
            [HumanoidType.Nurse] = new(new(3242, 18), [new(3258, 15)], new(3250, 9)),
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
            [HumanoidType.SlackMalePatient] = new(new(4580, 2), [new(4588, 1)], new(4596, 2)),
            [HumanoidType.ChewbaccaPatient] = new(new(3792, 2), [new(3784, 1)], new(3776, 2)),
            [HumanoidType.AlternateMalePatient] = new(new(4480, 2), [new(4488, 1)], new(4504, 2)),
        },
        UsageEast: new Dictionary<HumanoidType, UseAnimations>
        {
            // Not talking, nodding and talking, weighted 16 : 3 : 1 as chair.lua's make_list does.
            [HumanoidType.StandardMalePatient] = new(new(680, 2), ChairTalk(new(704, 1), new(736, 2), new(744, 27)), new(696, 2)),
            [HumanoidType.StandardFemalePatient] = new(new(2826, 2), ChairTalk(new(2818, 1), new(3002, 2), new(2994, 13)), new(2834, 2)),

            // make_list(1544, nil, 4606): quiet and talking, no nodding.
            [HumanoidType.SlackMalePatient] = new(new(1540, 2), [.. Enumerable.Repeat(new AnimationRef(1544, 1), 16), new(4606, 42)], new(1552, 2)),
            [HumanoidType.ChewbaccaPatient] = new(new(3794, 2), [.. Enumerable.Repeat(new AnimationRef(3786, 1), 16), new(4144, 40)], new(3778, 2)),
            [HumanoidType.AlternateMalePatient] = new(new(4482, 2), [.. Enumerable.Repeat(new AnimationRef(4490, 1), 16), new(4498, 27)], new(4506, 2)),
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
        UsageEast: With(WithChewbacca(WithSlackMale(DrinksMachineUse(196, 14, 256, 11), new(null, [new(1538, 14)], null)), new(null, [new(3774, 14)], null)), HumanoidType.AlternateMalePatient, new(null, [new(2762, 14)], null)),
        UsageSouth: With(WithChewbacca(WithSlackMale(DrinksMachineUse(190, 14, 250, 11), new(null, [new(1532, 14)], null)), new(null, [new(3768, 14)], null)), HumanoidType.AlternateMalePatient, new(null, [new(2756, 14)], null)),
        UsageWest: With(WithChewbacca(WithSlackMale(DrinksMachineUse(192, 11, 252, 10), new(null, [new(1534, 11)], null)), new(null, [new(3770, 11)], null)), HumanoidType.AlternateMalePatient, new(null, [new(2758, 11)], null)));

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
            [HumanoidType.StrippedMalePatient2] = new(null, [new(1052, 39)], null),
            [HumanoidType.StrippedMalePatient3] = new(null, [new(1052, 39)], null),
            [HumanoidType.StrippedFemalePatient] = new(null, [new(2844, 29)], null),
            [HumanoidType.StrippedFemalePatient2] = new(null, [new(2844, 29)], null),
            [HumanoidType.StrippedFemalePatient3] = new(null, [new(2844, 29)], null),
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

    /// <summary>The toilet (objects/loo.lua), used from the tile in front; facing south it uses the north animations.</summary>
    public static readonly ObjectDefinition Loo = new(
        "loo",
        "Toilet",
        ObjectType: 51,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: LooUse(),
        UsageSouth: LooUse());

    /// <summary>The toilet sink (objects/sink.lua); only the standard patients have animations for it.</summary>
    public static readonly ObjectDefinition Sink = new(
        "sink",
        "Toilet Sink",
        ObjectType: 32,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: SinkUse(),
        UsageSouth: SinkUse());

    /// <summary>The toilets (rooms/toilets.lua): no staff, as many patients at once as there are toilets.</summary>
    public static readonly RoomDefinition Toilets = new(
        "toilets",
        "Toilets",
        MinimumSize: 4,
        FloorSprite: 21,
        GreenWalls,
        NeededObjects: new Dictionary<string, int> { ["loo"] = 1, ["sink"] = 1 },
        LevelConfigId: 29,
        AdditionalObjects: ["extinguisher", "radiator", "plant", "bin", "loo", "sink"]);

    /// <summary>
    /// The ultrascanner (objects/machines/ultrascanner.lua): the patient lies on its bed from the west (facing north) while
    /// the doctor scans them from the other end; one animation shows both. Facing south it uses the north animations.
    /// CorsixTH draws it facing east from three tiles (<c>render_attach_position</c>); here from the first.
    /// </summary>
    public static readonly ObjectDefinition Ultrascanner = new(
        "ultrascanner",
        "Ultrascanner",
        ObjectType: 22,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-1, -1, NeedWestSide), new(0, -1, CompleteCell), new(1, -1, UsePosition | CompleteCell),
                    new(-1, 0, UsePosition | NeedWestSide), new(0, 0), new(1, 0, UsePosition),
                    new(-1, 1, NeedWestSide), new(0, 1),
                ],
                UsePosition: (-1, 0),
                SecondaryUsePosition: (1, -1)),
            [Direction.East] = new(
                [
                    new(-1, -1, CompleteCell), new(0, -1, UsePosition | NeedNorthSide), new(1, -1, NeedNorthSide),
                    new(-1, 0, CompleteCell), new(0, 0), new(1, 0),
                    new(-1, 1, UsePosition | CompleteCell), new(0, 1, UsePosition),
                ],
                UsePosition: (0, -1),
                SecondaryUsePosition: (-1, 1)),
        },
        MultiUsage: UltrascanMultiUsage(),
        Machine: new(new(3396, 1), new(3436, 4), new(null, [new(664, 41)], null), HandymanPositions: new Dictionary<Direction, IReadOnlyList<(int X, int Y)>> { [Direction.North] = [(2, 0)], [Direction.East] = [(0, 2)] }));

    /// <summary>The ultrascan (rooms/ultrascan.lua): a diagnosis room with a doctor.</summary>
    public static readonly RoomDefinition Ultrascan = new(
        "ultrascan",
        "Ultrascan",
        MinimumSize: 4,
        FloorSprite: 19,
        YellowWalls,
        NeededObjects: new Dictionary<string, int> { ["ultrascanner"] = 1 },
        LevelConfigId: 14,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The research computer (objects/computer.lua): a doctor works at it from the tile in front.</summary>
    public static readonly ObjectDefinition Computer = new(
        "computer",
        "Computer",
        ObjectType: 40,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(2098, 24)], null) },
        UsageSouth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(2098, 24)], null) });

    /// <summary>The atom analyser (objects/analyser.lua): the doctor stands inside its footprint; it stays drawn.</summary>
    public static readonly ObjectDefinition Analyser = new(
        "analyser",
        "Atom Analyser",
        ObjectType: 41,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(0, 0, UsePosition), new(1, 0)], UsePosition: (0, 0)),
            [Direction.East] = new([new(-1, -1), new(0, -1), new(-1, 0), new(0, 0, UsePosition), new(-1, 1), new(0, 1)], UsePosition: (0, 0)),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(4878, 24)], null) },
        UsageSouth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(4878, 24)], null) },
        VisibleWhileUsed: true);

    /// <summary>
    /// The auto autopsy (objects/autopsy.lua). Autopsies themselves, the doctor's use with a patient, are not ported yet.
    /// </summary>
    public static readonly ObjectDefinition Autopsy = new(
        "autopsy",
        "Auto Autopsy",
        ObjectType: 55,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-2, -1, CompleteCell), new(-2, 0, CompleteCell), new(-2, 1, UsePosition),
                    new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(-1, 1, UsePosition),
                    new(0, -1, CompleteCell), new(0, 0, CompleteCell), new(0, 1, UsePosition),
                ],
                UsePosition: (0, 1),
                SecondaryUsePosition: (-2, 1)),
            [Direction.East] = new(
                [
                    new(-1, -2, CompleteCell), new(-1, -1, CompleteCell), new(-1, 0, CompleteCell),
                    new(0, -2, CompleteCell), new(0, -1, CompleteCell), new(0, 0, CompleteCell),
                    new(1, -2, UsePosition), new(1, -1, UsePosition), new(1, 0, UsePosition),
                ],
                RenderAttach: (-1, 0),
                UsePosition: (1, 0),
                SecondaryUsePosition: (1, -2)),
        });

    /// <summary>
    /// The research department (rooms/research.lua): researchers work at its desks, cabinets, computers and atom
    /// analysers; as many researchers as it has desks.
    /// </summary>
    public static readonly RoomDefinition ResearchRoom = new(
        "research",
        "Research Department",
        MinimumSize: 5,
        FloorSprite: 21,
        GreenWalls,
        NeededObjects: new Dictionary<string, int> { ["desk"] = 1, ["cabinet"] = 1, ["autopsy"] = 1 },
        LevelConfigId: 28,
        AdditionalObjects: ["extinguisher", "radiator", "plant", "bin", "computer", "desk", "cabinet", "analyser"]);

    /// <summary>
    /// The X-ray (objects/machines/x_ray.lua): the patient walks into it, is X-rayed and walks out; facing south it uses
    /// the north animations.
    /// </summary>
    public static readonly ObjectDefinition XRay = new(
        "x_ray",
        "X-Ray",
        ObjectType: 27,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(-2, -2), new(-1, -2), new(-2, -1), new(-1, -1), new(0, -1), new(1, -1, UsePosition), new(-2, 0), new(-1, 0), new(0, 0)],
                RenderAttach: (-1, -1),
                UsePosition: (1, -1)),
            [Direction.East] = new(
                [new(-2, -2), new(-1, -2), new(0, -2), new(-2, -1), new(-1, -1), new(0, -1), new(-1, 0), new(0, 0), new(-1, 1, UsePosition)],
                RenderAttach: (-1, -1),
                UsePosition: (-1, 1)),
        },
        UsageNorth: XRayUse(),
        UsageSouth: XRayUse(),
        Machine: new(new(3384, 1), new(3440, 4), new(new(3558, 37), [new(3562, 40)], new(3562, 40))));

    /// <summary>
    /// The radiation shield (objects/radiation_shield.lua): the doctor sits at its console while the patient is X-rayed. Its
    /// front part (radiation_shield_b) is drawn by the client.
    /// </summary>
    public static readonly ObjectDefinition RadiationShield = new(
        "radiation_shield",
        "Radiation Shield",
        ObjectType: 28,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(-2, -1), new(-1, -1), new(0, -1), new(-2, 0), new(-1, 0), new(0, 0, UsePosition), new(-2, 1), new(-1, 1), new(0, 1)],
                RenderAttach: (0, -1),
                UsePosition: (0, 0)),
            [Direction.East] = new(
                [new(-1, -2), new(0, -2), new(1, -2), new(-1, -1), new(0, -1), new(1, -1), new(-1, 0), new(0, 0, UsePosition), new(1, 0)],
                RenderAttach: (0, -1),
                UsePosition: (0, 0)),
        },
        UsageNorth: ShieldUse(),
        UsageSouth: ShieldUse());

    /// <summary>The X-ray room (rooms/x_ray_room.lua): a diagnosis room with a doctor.</summary>
    public static readonly RoomDefinition XRayRoom = new(
        "x_ray",
        "X-Ray",
        MinimumSize: 6,
        FloorSprite: 19,
        YellowWalls,
        NeededObjects: new Dictionary<string, int> { ["x_ray"] = 1, ["radiation_shield"] = 1 },
        LevelConfigId: 16,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>
    /// The inflator (objects/machines/inflator.lua): the doctor pops the bloated head and pumps it up again while the
    /// patient stands on the machine; facing south it uses the north animations.
    /// </summary>
    public static readonly ObjectDefinition Inflator = new(
        "inflator",
        "Inflator",
        ObjectType: 9,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-1, -1), new(0, -1, NeedNorthSide), new(1, -1, UsePosition),
                    new(-1, 0), new(0, 0), new(1, 0, UsePosition),
                    new(-1, 1, UsePosition), new(0, 1, UsePosition), new(1, 1, UsePosition),
                ],
                UsePosition: (0, 1),
                SecondaryUsePosition: (-1, 1),
                FinishUsePosition: (1, 0)),
            [Direction.East] = new(
                [
                    new(-1, -1), new(0, -1), new(1, -1, UsePosition),
                    new(-1, 0, NeedWestSide), new(0, 0), new(1, 0, UsePosition),
                    new(-1, 1, UsePosition), new(0, 1, UsePosition), new(1, 1, UsePosition),
                ],
                UsePosition: (1, 0),
                SecondaryUsePosition: (1, -1),
                FinishUsePosition: (0, 1)),
        },
        MultiUsage: InflatorMultiUsage(),
        Machine: new(new(3362, 1), new(3424, 4), new(null, [new(3480, 41)], null), HandymanPositions: new Dictionary<Direction, IReadOnlyList<(int X, int Y)>> { [Direction.North] = [(1, -1)], [Direction.East] = [(-1, 1)] }));

    /// <summary>The inflation room (rooms/inflation.lua): a treatment room for bloaty heads with a doctor.</summary>
    public static readonly RoomDefinition Inflation = new(
        "inflation",
        "Inflation",
        MinimumSize: 4,
        FloorSprite: 17,
        BlueWalls,
        NeededObjects: new Dictionary<string, int> { ["inflator"] = 1 },
        LevelConfigId: 17,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The psychiatric couch (objects/couch.lua): the patient lies on it for the session.</summary>
    public static readonly ObjectDefinition Couch = new(
        "couch",
        "Couch",
        ObjectType: 18,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, -1, UsePosition), new(0, 0, UsePosition)], RenderAttach: (-1, 0)),
            [Direction.East] = new([new(-1, -1, CompleteCell), new(-1, 0, UsePosition), new(0, -1, CompleteCell), new(0, 0, UsePosition)]),
        },
        UsageNorth: CouchUse(),
        UsageSouth: CouchUse());

    /// <summary>The psychiatrist's comfortable chair (objects/comfortable_chair.lua).</summary>
    public static readonly ObjectDefinition ComfortableChair = new(
        "comfortable_chair",
        "Comfortable Chair",
        ObjectType: 61,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: ComfortableChairUse(),
        UsageSouth: ComfortableChairUse());

    /// <summary>The bookcase (objects/bookcase.lua), which the psychiatrist reads at.</summary>
    public static readonly ObjectDefinition Bookcase = new(
        "bookcase",
        "Bookcase",
        ObjectType: 56,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(2414, 43)], null) },
        UsageSouth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(2414, 43)], null) });

    /// <summary>The skeleton (objects/skeleton.lua), an extra object of the psychiatric room.</summary>
    public static readonly ObjectDefinition Skeleton = new(
        "skeleton",
        "Skeleton",
        ObjectType: 60,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(2614, 38)], null) },
        UsageSouth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Doctor] = new(null, [new(2614, 38)], null) });

    /// <summary>
    /// The psychiatric room (rooms/psych.lua): a diagnosis and treatment room for a psychiatrist, with a screen, a couch
    /// and a comfortable chair; bookcases and skeletons are its extra objects.
    /// </summary>
    public static readonly RoomDefinition Psych = new(
        "psych",
        "Psychiatric",
        MinimumSize: 5,
        FloorSprite: 18,
        WhiteWalls,
        NeededObjects: new Dictionary<string, int> { ["screen"] = 1, ["couch"] = 1, ["comfortable_chair"] = 1 },
        LevelConfigId: 8,
        AdditionalObjects: ["extinguisher", "radiator", "plant", "bin", "bookcase", "skeleton"]);

    /// <summary>The ward's bed (objects/bed.lua): the patient lies down, stays and gets up.</summary>
    public static readonly ObjectDefinition Bed = new(
        "bed",
        "Bed",
        ObjectType: 8,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(1, -1, UsePosition), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell)],
                UsePosition: (1, -1)),
            [Direction.East] = new(
                [new(0, 1, UsePosition), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell)],
                UsePosition: (0, 1)),
            [Direction.South] = new(
                [new(1, 0, UsePosition), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell)],
                UsePosition: (1, 0)),
            [Direction.West] = new(
                [new(-1, 1, UsePosition), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell)],
                UsePosition: (-1, 1)),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.StandardMalePatient] = new(new(4686, 29), [new(4694, 1)], new(4702, 14)),
            [HumanoidType.StandardFemalePatient] = new(new(4710, 29), [new(4718, 1)], new(4726, 16)),
        },
        UsageEast: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.StandardMalePatient] = new(new(4688, 23), [new(4696, 1)], new(4704, 15)),
            [HumanoidType.StandardFemalePatient] = new(new(4712, 23), [new(4720, 1)], new(4728, 15)),
        });

    /// <summary>
    /// The ward (rooms/ward.lua): a diagnosis and treatment room with beds and desks, as many patients as beds and as many
    /// nurses as desks.
    /// </summary>
    public static readonly RoomDefinition Ward = new(
        "ward",
        "Ward",
        MinimumSize: 6,
        FloorSprite: 21,
        WhiteWalls,
        NeededObjects: new Dictionary<string, int> { ["desk"] = 1, ["bed"] = 1 },
        LevelConfigId: 9,
        AdditionalObjects: ["extinguisher", "radiator", "plant", "desk", "bin", "bed"],
        SwingDoors: true);

    /// <summary>
    /// The blood machine (objects/machines/blood_machine.lua): its door opens, the chair comes out, the patient sits and
    /// goes in, the machine works and lets them out again; the doctor's animations show the patient too.
    /// </summary>
    public static readonly ObjectDefinition BloodMachine = new(
        "blood_machine",
        "Blood Machine",
        ObjectType: 42,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-1, -1, UsePosition | NeedEastSide | NeedWestSide), new(0, -1, UsePosition | NeedNorthSide),
                    new(-2, 0, NeedSouthSide | NeedNorthSide), new(-1, 0), new(0, 0, CompleteCell), new(1, 0, UsePosition),
                    new(-1, 1, NeedEastSide | NeedWestSide),
                ],
                UsePosition: (0, -1),
                SecondaryUsePosition: (1, 0)),
            [Direction.East] = new(
                [
                    new(0, -2, NeedEastSide | NeedWestSide), new(-1, -1, UsePosition | CompleteCell), new(0, -1),
                    new(1, -1, NeedSouthSide | NeedNorthSide), new(-1, 0, UsePosition | NeedWestSide),
                    new(0, 0, CompleteCell), new(0, 1, UsePosition),
                ],
                UsePosition: (-1, 0),
                SecondaryUsePosition: (0, 1)),
        },
        MultiUsage: BloodMachineMultiUsage(),
        Machine: new(new(3372, 1), new(3460, 4), new(null, [new(3498, 4), new(3484, 38)], null), HandymanPositions: new Dictionary<Direction, IReadOnlyList<(int X, int Y)>> { [Direction.North] = [(0, -1)], [Direction.East] = [(-1, 0)] }));

    /// <summary>The blood machine room (rooms/blood_machine_room.lua): a diagnosis room with a doctor.</summary>
    public static readonly RoomDefinition BloodMachineRoom = new(
        "blood_machine",
        "Blood Machine",
        MinimumSize: 4,
        FloorSprite: 19,
        YellowWalls,
        NeededObjects: new Dictionary<string, int> { ["blood_machine"] = 1 },
        LevelConfigId: 15,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The console (objects/console.lua): the doctor sits at it to work the scanner or the shower.</summary>
    public static readonly ObjectDefinition DoctorConsole = new(
        "console",
        "Console",
        ObjectType: 15,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, -1, CompleteCell), new(0, 0, CompleteCell | UsePosition)], RenderAttach: (0, -1)),
            [Direction.East] = new([new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(0, -1, CompleteCell), new(0, 0, CompleteCell | UsePosition)], RenderAttach: (-1, 0)),
        },
        UsageNorth: ShieldUse(),
        UsageSouth: ShieldUse());

    /// <summary>The decontamination shower (objects/machines/shower.lua): the patient showers alone.</summary>
    public static readonly ObjectDefinition Shower = new(
        "shower",
        "Decontamination Shower",
        ObjectType: 54,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-2, -2, CompleteCell), new(-1, -2, CompleteCell), new(0, -2),
                    new(-2, -1, CompleteCell), new(-1, -1, CompleteCell), new(0, -1, CompleteCell),
                    new(-2, 0, CompleteCell), new(-1, 0, CompleteCell), new(0, 0, CompleteCell),
                    new(-1, 1, UsePosition),
                ],
                RenderAttach: (-1, 0),
                UsePosition: (-1, 1)),
            [Direction.East] = new(
                [
                    new(-2, -2, CompleteCell), new(-1, -2, CompleteCell), new(0, -2, CompleteCell),
                    new(-2, -1, CompleteCell), new(-1, -1, CompleteCell), new(0, -1, CompleteCell), new(1, -1, UsePosition),
                    new(-2, 0), new(-1, 0, CompleteCell), new(0, 0, CompleteCell),
                ],
                RenderAttach: (-1, 0),
                UsePosition: (1, -1)),
        },
        UsageNorth: ShowerUse(),
        UsageSouth: ShowerUse(),
        Machine: new(new(3380, 1), new(3448, 4), new(new(3534, 36), [new(3538, 38)], new(3542, 14))));

    /// <summary>The decontamination room (rooms/decontamination.lua): a treatment room for serious radiation with a doctor.</summary>
    public static readonly RoomDefinition Decontamination = new(
        "decontamination",
        "Decontamination",
        MinimumSize: 5,
        FloorSprite: 19,
        BlueWalls,
        NeededObjects: new Dictionary<string, int> { ["shower"] = 1, ["console"] = 1 },
        LevelConfigId: 30,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>
    /// The cardiogram machine (objects/machines/cardio.lua): the stripped patient is wired up while the doctor watches the
    /// readout at its other end.
    /// </summary>
    public static readonly ObjectDefinition Cardio = new(
        "cardio",
        "Cardiogram",
        ObjectType: 13,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(-1, -1, CompleteCell), new(-1, 0, CompleteCell), new(1, -1, UsePosition), new(0, -1, NeedNorthSide), new(0, 0, UsePosition)],
                RenderAttach: (-1, 0),
                UsePosition: (1, -1),
                SecondaryUsePosition: (0, 0)),
            [Direction.East] = new(
                [new(-1, -1, CompleteCell), new(-1, 0), new(0, -1, CompleteCell), new(0, 0, UsePosition), new(-1, 1, UsePosition)],
                RenderAttach: (0, -1),
                UsePosition: (-1, 1),
                SecondaryUsePosition: (0, 0)),
        },
        MultiUsage: CardioMultiUsage(),
        Machine: new(new(3308, 1), new(3432, 4), new(null, [new(890, 41)], new(894, 12)), HandymanOffsets: new Dictionary<Direction, (int X, int Y)> { [Direction.North] = (1, -1), [Direction.East] = (-1, 1) }));

    /// <summary>The cardiogram room (rooms/cardiogram.lua): a diagnosis room with a screen, the machine and a doctor.</summary>
    public static readonly RoomDefinition Cardiogram = new(
        "cardiogram",
        "Cardiogram",
        MinimumSize: 4,
        FloorSprite: 19,
        YellowWalls,
        NeededObjects: new Dictionary<string, int> { ["cardio"] = 1, ["screen"] = 1 },
        LevelConfigId: 12,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>
    /// The scanner (objects/machines/scanner.lua): the stripped patient stands in it and it tips back and forth over its
    /// five begin and five finish animations; it has no in-use one.
    /// </summary>
    public static readonly ObjectDefinition Scanner = new(
        "scanner",
        "Scanner",
        ObjectType: 14,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(-2, -1), new(-1, -1), new(0, -1), new(-2, 0), new(-1, 0, NeedSouthSide), new(0, 0, CompleteCell | UsePosition)],
                RenderAttach: (0, -1),
                UsePosition: (0, 0)),
            [Direction.East] = new(
                [new(-1, -2), new(0, -2), new(-1, -1), new(0, -1, NeedEastSide), new(-1, 0), new(0, 0, CompleteCell | UsePosition)],
                RenderAttach: (-1, 0),
                UsePosition: (0, 0)),
        },
        UsageNorth: ScannerUse(),
        UsageSouth: ScannerUse(),
        Machine: new(new(3316, 1), new(3428, 4), new(null, [], null, Begin2: new(564, 35))));

    /// <summary>The scanner room (rooms/scanner_room.lua): a diagnosis room with the scanner, a console, a screen and a doctor.</summary>
    public static readonly RoomDefinition ScannerRoom = new(
        "scanner",
        "Scanner",
        MinimumSize: 5,
        FloorSprite: 19,
        YellowWalls,
        NeededObjects: new Dictionary<string, int> { ["scanner"] = 1, ["console"] = 1, ["screen"] = 1 },
        LevelConfigId: 13,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>
    /// The surgeon screen (objects/surgeon_screen.lua): patients change into a gown behind it; surgeons change into scrubs
    /// there too, with animations the operating theatre picks itself.
    /// </summary>
    public static readonly ObjectDefinition SurgeonScreen = new(
        "surgeon_screen",
        "Surgeon Screen",
        ObjectType: 35,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-1, -1, UsePosition), new(-1, 0), new(0, -1), new(0, 0)], RenderAttach: (-1, 0)),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations>
        {
            // Into the gown, then out of it again.
            [HumanoidType.StandardMalePatient] = new(null, [new(4760, 24)], null),
            [HumanoidType.StandardFemalePatient] = new(null, [new(4762, 24)], null),
            [HumanoidType.GownedMalePatient] = new(null, [new(4768, 23)], null),
            [HumanoidType.GownedFemalePatient] = new(null, [new(4770, 23)], null),
        });

    /// <summary>The surgeons' sink (objects/op_sink1.lua, with op_sink2 drawn beside it): a surgeon steps in and washes.</summary>
    public static readonly ObjectDefinition OperatingSink = new(
        "op_sink1",
        "Sink",
        ObjectType: 33,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, CompleteCell), new(1, 0, UsePosition)], UsePosition: (1, 0)),
            [Direction.East] = new([new(0, 0, CompleteCell), new(-1, 0, CompleteCell), new(0, 1, UsePosition)], UsePosition: (0, 1)),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Surgeon] = new(null, [new(2362, 33)], null) },
        UsageSouth: new Dictionary<HumanoidType, UseAnimations> { [HumanoidType.Surgeon] = new(null, [new(2362, 33)], null) },
        WalkInToUse: true);

    /// <summary>The X-ray viewer (objects/x_ray_viewer.lua) on the operating theatre's wall.</summary>
    public static readonly ObjectDefinition XRayViewer = new(
        "x_ray_viewer",
        "X-Ray Viewer",
        ObjectType: 29,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, NeedWestSide)]),
            [Direction.East] = new([new(0, 0, NeedNorthSide)]),
        });

    /// <summary>
    /// The operating table (objects/machines/operating_table.lua): the first surgeon operates with the gowned patient on
    /// it; operating_table_b, its far side, is where the second surgeon works (drawn beside it).
    /// </summary>
    public static readonly ObjectDefinition OperatingTable = new(
        "operating_table",
        "Operating Table",
        ObjectType: 30,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-2, -1, UsePosition), new(-1, -1, CompleteCell), new(-1, -2, UsePosition), new(0, -1, CompleteCell), new(0, -2, CompleteCell),
                    new(1, 0, CompleteCell), new(1, -2, CompleteCell), new(1, -1, UsePosition),
                ],
                RenderAttach: (0, -1),
                UsePosition: (-1, -2),
                SecondaryUsePosition: (-2, -1)),
            [Direction.East] = new(
                [
                    new(-1, -2, UsePosition), new(-1, -1, CompleteCell), new(-2, -1, UsePosition), new(-1, 0, CompleteCell), new(-2, 0, CompleteCell),
                    new(0, 1, CompleteCell), new(-2, 1, CompleteCell), new(-1, 1, UsePosition), new(0, 0),
                ],
                RenderAttach: (-1, 0),
                UsePosition: (-2, -1),
                SecondaryUsePosition: (-1, -2)),
        },
        MultiUsage: OperatingTableMultiUsage(),
        Machine: new(new(3392, 1), new(3472, 4), new(null, [new(660, 35)], null)));

    /// <summary>
    /// The operating theatre (rooms/operating_theatre.lua): two surgeons, a table, a surgeon screen, a sink and an X-ray
    /// viewer behind swing doors; the treatment after the ward for the surgical diseases.
    /// </summary>
    public static readonly RoomDefinition OperatingTheatre = new(
        "operating_theatre",
        "Operating Theatre",
        MinimumSize: 6,
        FloorSprite: 21,
        WhiteWalls,
        NeededObjects: new Dictionary<string, int> { ["operating_table"] = 1, ["surgeon_screen"] = 1, ["op_sink1"] = 1, ["x_ray_viewer"] = 1 },
        LevelConfigId: 10,
        AdditionalObjects: AdditionalRoomObjects,
        SwingDoors: true);

    /// <summary>The staff room's sofa (objects/sofa.lua): doctors, nurses and handymen sit down and rest on it.</summary>
    public static readonly ObjectDefinition Sofa = new(
        "sofa",
        "Sofa",
        ObjectType: 19,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(-1, 0, CompleteCell), new(0, 0, CompleteCell), new(-1, -1, UsePosition)], RenderAttach: (-1, 0)),
            [Direction.East] = new([new(0, -1, CompleteCell), new(0, 0, CompleteCell), new(1, -1, UsePosition)]),
            [Direction.South] = new([new(-1, 0, CompleteCell), new(0, 0, CompleteCell), new(-1, 1, UsePosition)]),
            [Direction.West] = new([new(0, -1, CompleteCell), new(0, 0, CompleteCell), new(-1, -1, UsePosition)]),
        },
        UsageNorth: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.Doctor] = new(new(2178, 5), [new(2208, 1)], new(2194, 7), Begin2: new(2186, 5)),
            [HumanoidType.Nurse] = new(new(3878, 5), [new(4780, 1)], new(3894, 7), Begin2: new(3886, 5)),
            [HumanoidType.Handyman] = new(new(3570, 5), [new(4772, 1)], new(3586, 6), Begin2: new(3578, 5)),
        },
        UsageEast: new Dictionary<HumanoidType, UseAnimations>
        {
            [HumanoidType.Doctor] = new(new(2180, 10), [new(2210, 1)], new(2196, 6), Begin2: new(2188, 15)),
            [HumanoidType.Nurse] = new(new(3880, 5), [new(4782, 1)], new(3896, 6), Begin2: new(3888, 15)),
            [HumanoidType.Handyman] = new(new(3572, 10), [new(4774, 1)], new(3588, 6), Begin2: new(3580, 15)),
        });

    /// <summary>The pool table (objects/pool_table.lua), which doctors and handymen play at.</summary>
    public static readonly ObjectDefinition PoolTable = new(
        "pool_table",
        "Pool Table",
        ObjectType: 10,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-1, -2, UsePosition), new(0, -2, NeedNorthSide), new(1, -2, UsePosition),
                    new(-1, -1, UsePosition), new(0, -1), new(1, -1, UsePosition),
                    new(-1, 0, UsePosition), new(0, 0, UsePosition | CompleteCell), new(1, 0, UsePosition),
                ],
                RenderAttach: (-1, 0),
                UsePosition: (0, 0)),
            [Direction.East] = new(
                [
                    new(-2, -1, UsePosition), new(-1, -1, UsePosition), new(0, -1, UsePosition),
                    new(-2, 0, CompleteCell), new(-1, 0), new(0, 0, UsePosition | CompleteCell),
                    new(-2, 1, UsePosition), new(-1, 1, UsePosition), new(0, 1, UsePosition),
                ],
                RenderAttach: (0, -1),
                UsePosition: (0, 0)),
        },
        UsageNorth: PoolTableUse(),
        UsageSouth: PoolTableUse());

    /// <summary>The video game (objects/video_game.lua), which doctors and nurses play.</summary>
    public static readonly ObjectDefinition VideoGame = new(
        "video_game",
        "Video Game",
        ObjectType: 57,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)], AnimateFromUsePosition: true),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)], AnimateFromUsePosition: true),
        },
        UsageNorth: VideoGameUse(),
        UsageSouth: VideoGameUse());

    /// <summary>The television (objects/tv.lua), for the staff room's looks.</summary>
    public static readonly ObjectDefinition Television = new(
        "tv",
        "TV",
        ObjectType: 21,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, 0, CompleteCell), new(0, -1, UsePosition)]),
            [Direction.East] = new([new(0, 0, CompleteCell), new(1, 0, UsePosition)]),
            [Direction.South] = new([new(0, 0, CompleteCell), new(0, 1, UsePosition)]),
            [Direction.West] = new([new(0, 0, CompleteCell), new(-1, 0, UsePosition)]),
        });

    /// <summary>The staff room (rooms/staff_room.lua): tired staff rest there; it needs a sofa.</summary>
    public static readonly RoomDefinition StaffRoom = new(
        "staff_room",
        "Staff Room",
        MinimumSize: 4,
        FloorSprite: 17,
        GreenWalls,
        NeededObjects: new Dictionary<string, int> { ["sofa"] = 1 },
        LevelConfigId: 25,
        AdditionalObjects: ["extinguisher", "radiator", "plant", "sofa", "pool_table", "tv", "video_game"]);

    /// <summary>The slicer (objects/machines/slicer.lua): the doctor draws the slack tongue out and cuts it.</summary>
    public static readonly ObjectDefinition Slicer = new(
        "slicer",
        "Slicer",
        ObjectType: 26,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [new(-1, -1, UsePosition), new(-1, 0), new(-1, 1), new(0, -1, UsePosition), new(0, 0, UsePosition | NeedEastSide), new(0, 1, UsePosition)],
                UsePosition: (0, 0),
                SecondaryUsePosition: (-1, -1)),
            [Direction.East] = new(
                [new(-1, -1, UsePosition), new(0, -1), new(1, -1), new(-1, 0, UsePosition), new(0, 0, UsePosition | NeedSouthSide), new(1, 0, UsePosition)],
                UsePosition: (0, 0),
                SecondaryUsePosition: (-1, -1)),
        },
        MultiUsage: SlicerMultiUsage(),
        Machine: new(new(3400, 1), new(3464, 4), new(null, [new(3490, 41)], null), HandymanPositions: new Dictionary<Direction, IReadOnlyList<(int X, int Y)>> { [Direction.North] = [(1, -1)], [Direction.East] = [(-1, 1)] }));

    /// <summary>The slack tongue room (rooms/slack_tongue.lua): a treatment room with the slicer and a doctor.</summary>
    public static readonly RoomDefinition SlackTongue = new(
        "slack_tongue",
        "Slack Tongue",
        MinimumSize: 4,
        FloorSprite: 17,
        BlueWalls,
        NeededObjects: new Dictionary<string, int> { ["slicer"] = 1 },
        LevelConfigId: 20,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The hair restorer (objects/machines/hair_restorer.lua): a bald man sits under its cap.</summary>
    public static readonly ObjectDefinition HairRestorer = new(
        "hair_restorer",
        "Hair Restorer",
        ObjectType: 25,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new([new(0, -1, UsePosition), new(0, 0, CompleteCell), new(0, 1, UsePosition)], UsePosition: (0, 1), FinishUsePosition: (0, 1), AnimateFromUsePosition: true),
            [Direction.East] = new([new(-1, 0, UsePosition), new(0, 0, CompleteCell), new(1, 0, UsePosition)], UsePosition: (1, 0), FinishUsePosition: (1, 0), AnimateFromUsePosition: true),
        },
        UsageNorth: HairRestorerUse(),
        UsageSouth: HairRestorerUse(),
        Machine: new(new(5116, 1), new(3460, 4), new(null, [new(568, 35)], null), HandymanPositions: new Dictionary<Direction, IReadOnlyList<(int X, int Y)>> { [Direction.North] = [(0, -1)], [Direction.East] = [(-1, 0)] }, HandymanOffsets: new Dictionary<Direction, (int X, int Y)> { [Direction.North] = (0, 2), [Direction.East] = (2, 0) }));

    /// <summary>The hair restoration room (rooms/hair_restoration.lua): a treatment room for baldness with a doctor.</summary>
    public static readonly RoomDefinition HairRestoration = new(
        "hair_restoration",
        "Hair Restoration",
        MinimumSize: 4,
        FloorSprite: 17,
        BlueWalls,
        NeededObjects: new Dictionary<string, int> { ["hair_restorer"] = 1, ["console"] = 1 },
        LevelConfigId: 19,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The electrolyser (objects/machines/electrolyser.lua): the hairy patient is electrocuted until the hair falls off.</summary>
    public static readonly ObjectDefinition Electrolyser = new(
        "electrolyser",
        "Electrolyser",
        ObjectType: 46,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-2, -2, NeedWestSide), new(-1, -2), new(0, -2, NeedEastSide),
                    new(-2, -1, CompleteCell), new(-1, -1), new(0, -1, CompleteCell),
                    new(-2, 0, CompleteCell), new(-1, 0), new(0, 0, CompleteCell),
                    new(-1, 1, UsePosition | NeedSouthSide),
                ],
                RenderAttach: (-1, -1)),
            [Direction.East] = new(
                [
                    new(-2, -2, NeedNorthSide), new(-1, -2), new(0, -2, CompleteCell),
                    new(-2, -1), new(-1, -1), new(0, -1, CompleteCell),
                    new(-2, 0, NeedSouthSide), new(-1, 0), new(0, 0, CompleteCell),
                    new(1, -1, UsePosition | NeedEastSide),
                ],
                RenderAttach: (-1, -1)),
        },
        UsageNorth: ElectrolyserUse(),
        UsageSouth: ElectrolyserUse(),
        Machine: new(new(3300, 1), new(3456, 4), new(new(3546, 30), [new(3550, 38)], new(3554, 17))));

    /// <summary>The electrolysis room (rooms/electrolysis.lua): a treatment room for hairyitis with a doctor.</summary>
    public static readonly RoomDefinition Electrolysis = new(
        "electrolysis",
        "Electrolysis",
        MinimumSize: 5,
        FloorSprite: 17,
        BlueWalls,
        NeededObjects: new Dictionary<string, int> { ["electrolyser"] = 1, ["console"] = 1 },
        LevelConfigId: 23,
        AdditionalObjects: AdditionalRoomObjects);

    /// <summary>The cast remover (objects/machines/cast_remover.lua): the nurse holds the bucket while it takes the casts off.</summary>
    public static readonly ObjectDefinition CastRemover = new(
        "cast_remover",
        "Cast Remover",
        ObjectType: 24,
        new Dictionary<Direction, ObjectOrientation>
        {
            [Direction.North] = new(
                [
                    new(-1, -1, CompleteCell), new(0, -1, UsePosition), new(1, -1, UsePosition),
                    new(-1, 0, CompleteCell), new(0, 0, UsePosition | CompleteCell), new(-1, 1, UsePosition | NeedWestSide),
                ],
                RenderAttach: (-1, 1),
                UsePosition: (0, 0),
                SecondaryUsePosition: (0, -1)),
            [Direction.East] = new(
                [
                    new(-1, -1), new(0, -1, CompleteCell), new(1, -1, UsePosition | NeedNorthSide),
                    new(-1, 0, UsePosition), new(0, 0, UsePosition | CompleteCell), new(-1, 1, UsePosition),
                ],
                UsePosition: (0, 0),
                SecondaryUsePosition: (-1, 0)),
        },
        MultiUsage: CastRemoverMultiUsage(),
        Machine: new(new(3388, 1), new(3468, 4), new(null, [new(3514, 20)], null), HandymanPositions: new Dictionary<Direction, IReadOnlyList<(int X, int Y)>> { [Direction.North] = [(0, -2), (-1, -1)], [Direction.East] = [(-1, -1), (-2, 0)] }));

    /// <summary>The fracture clinic (rooms/fracture_clinic.lua): a treatment room for fractured bones with a nurse.</summary>
    public static readonly RoomDefinition FractureClinic = new(
        "fracture_clinic",
        "Fracture Clinic",
        MinimumSize: 4,
        FloorSprite: 17,
        BlueWalls,
        NeededObjects: new Dictionary<string, int> { ["cast_remover"] = 1 },
        LevelConfigId: 21,
        AdditionalObjects: AdditionalRoomObjects);

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
        [HumanoidType.StandardMalePatient] = new(Entering: new(184, 10), Leaving: new(182, 12), EnteringSwing: new(2042, 6), LeavingSwing: new(2040, 9)),
        [HumanoidType.StandardFemalePatient] = new(Entering: new(260, 10), Leaving: new(258, 12), EnteringSwing: new(2866, 6), LeavingSwing: new(2864, 9)),
        [HumanoidType.SlackFemalePatient] = new(Entering: new(260, 10), Leaving: new(258, 12), EnteringSwing: new(2866, 6), LeavingSwing: new(2864, 9)),
        [HumanoidType.SlackMalePatient] = new(Entering: new(1526, 10), Leaving: new(1524, 12)),
        [HumanoidType.ChewbaccaPatient] = new(Entering: new(3528, 10), Leaving: new(3526, 12)),
        [HumanoidType.AlternateMalePatient] = new(Entering: new(2750, 10), Leaving: new(2748, 12)),
        [HumanoidType.Doctor] = new(Entering: new(672, 10), Leaving: new(670, 12), EnteringSwing: new(4752, 6), LeavingSwing: new(4750, 5)),
        [HumanoidType.Nurse] = new(Entering: new(3266, 10), Leaving: new(3264, 12), EnteringSwing: new(3274, 6), LeavingSwing: new(3272, 7)),
        [HumanoidType.Handyman] = new(Entering: new(3288, 10), Leaving: new(3286, 12), EnteringSwing: new(3520, 6), LeavingSwing: new(3518, 9)),
    };

    public static GameContent Create() => new(
        [GpOffice, Pharmacy, GeneralDiagnosis, Toilets, Ultrascan, ResearchRoom, XRayRoom, Inflation, Psych, Ward, BloodMachineRoom, Decontamination, Cardiogram, ScannerRoom, OperatingTheatre, StaffRoom, SlackTongue, HairRestoration, Electrolysis, FractureClinic],
        ExternalWalls,
        // Corridor objects in the order of CorsixTH's furnish corridor window (corridor_object).
        [Desk, Cabinet, Chair, PharmacyCabinet, Screen, CrashTrolley, ReceptionDesk, Bench, DrinksMachine, FireExtinguisher, Radiator, Bin, Plant, Loo, Sink, Ultrascanner, Computer, Analyser, Autopsy, XRay, RadiationShield, Inflator, Couch, ComfortableChair, Bookcase, Skeleton, Bed, BloodMachine, DoctorConsole, Shower, Cardio, Scanner, SurgeonScreen, OperatingSink, XRayViewer, OperatingTable, Sofa, PoolTable, VideoGame, Television, Slicer, HairRestorer, Electrolyser, CastRemover],
        DoorAnimations,
        PatientAnimations)
    {
        // swing_door_right.lua: the left door swings in 2032 or out 2034, the right one in 2048 or out 2052.
        SwingDoor = new SwingDoorAnimations(SlaveIn: new(2032, 27), SlaveOut: new(2034, 22), MasterIn: new(2048, 19), MasterOut: new(2052, 17)),
    };

    /// <summary>humanoid.lua's <c>vomit_anim</c> and <c>pee_anim</c> for the standard patients.</summary>
    public static readonly IReadOnlyDictionary<HumanoidType, PatientAnimations> PatientAnimations = new Dictionary<HumanoidType, PatientAnimations>
    {
        [HumanoidType.StandardMalePatient] = new(Vomit: new(2056, 27), Pee: new(2244, 8)),
        [HumanoidType.StandardFemalePatient] = new(Vomit: new(3184, 9), Pee: new(4744, 8)),
        [HumanoidType.SlackFemalePatient] = new(Vomit: new(3184, 9), Pee: new(4744, 8)),
        [HumanoidType.SlackMalePatient] = new(Vomit: new(4324, 9), Pee: new(4328, 8)),
        [HumanoidType.ChewbaccaPatient] = new(Vomit: new(4138, 15), Pee: new(4178, 9)),
        [HumanoidType.AlternateMalePatient] = new(Vomit: new(4476, 27), Pee: new(4472, 8)),
    };

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
    /// ultrascanner.lua's <c>multi_usage_animations</c>: the patient climbs onto the bed, the doctor takes the instrument,
    /// examines them, puts it away, and the patient climbs off; each animation shows both, so the doctor is hidden.
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> UltrascanMultiUsage()
    {
        static MultiUseAnimations[] Use(AnimationRef begin, AnimationRef begin2, AnimationRef inUse, AnimationRef finish, AnimationRef finish2) =>
            [new MultiUseAnimations([new(begin), new(begin2)], new(inUse), [new(finish), new(finish2)])];

        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.StandardMalePatient, HumanoidType.Doctor)] = Use(new(1560, 7), new(1568, 20), new(1614, 28), new(1574, 32), new(1610, 11)),
            [(HumanoidType.StandardFemalePatient, HumanoidType.Doctor)] = Use(new(3084, 7), new(3092, 16), new(3096, 19), new(3100, 22), new(1618, 12)),
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>
    /// inflator.lua's <c>multi_usage_animations</c> for the standard male patient (bloaty heads are men): he is invited
    /// onto the machine, the doctor pops his head, walks round the machine and pumps it up again (played once), and he
    /// walks off; each animation shows both, so the doctor is hidden.
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> InflatorMultiUsage()
    {
        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.StandardMalePatient, HumanoidType.Doctor)] =
            [
                new MultiUseAnimations([new(new(464, 11)), new(new(478, 27)), new(new(482, 36))], new(new(496, 38)), [new(new(576, 14))]),
            ],
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>couch.lua's animations for the standard patients: lying down, lying there, getting up.</summary>
    private static Dictionary<HumanoidType, UseAnimations> CouchUse() => new()
    {
        [HumanoidType.StandardMalePatient] = new(new(2528, 15), [new(2536, 1)], new(2532, 8)),
        [HumanoidType.StandardFemalePatient] = new(new(3180, 12), [new(3330, 1)], new(3326, 7)),
    };

    /// <summary>comfortable_chair.lua's doctor: two animations to sit down, sitting, getting up.</summary>
    private static Dictionary<HumanoidType, UseAnimations> ComfortableChairUse() => new()
    {
        [HumanoidType.Doctor] = new(new(2516, 10), [new(2544, 1)], new(2520, 5), Begin2: new(2512, 9)),
    };

    /// <summary>
    /// blood_machine.lua's <c>multi_usage_animations</c> for the doctor with the standard patients, who are hidden: five
    /// begin phases, the in-use phase (looped) and five finish phases.
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> BloodMachineMultiUsage()
    {
        static MultiUseAnimations Use(AnimationRef[] begin, AnimationRef[] finish) =>
            new([.. begin.Select(a => new MultiUsePhase(a))], new(new(2566, 8)), [.. finish.Select(a => new MultiUsePhase(a))]);

        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.Doctor, HumanoidType.StandardMalePatient)] =
            [
                Use([new(2232, 13), new(2236, 31), new(2554, 19), new(2220, 38), new(2224, 23)], [new(2252, 17), new(2256, 19), new(2558, 14), new(2204, 38), new(2274, 23)]),
            ],
            [(HumanoidType.Doctor, HumanoidType.StandardFemalePatient)] =
            [
                Use([new(2232, 13), new(2236, 31), new(4614, 19), new(3232, 38), new(3236, 27)], [new(1172, 19), new(4738, 39), new(4618, 13), new(2204, 38), new(2274, 23)]),
            ],

            // Only for baldness.
            [(HumanoidType.Doctor, HumanoidType.SlackMalePatient)] =
            [
                Use([new(2232, 13), new(2236, 31), new(5146, 19), new(5150, 38), new(2224, 23)], [new(2252, 17), new(5154, 19), new(622, 14), new(2204, 38), new(2274, 23)]),
            ],
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>shower.lua's animations for the standard patients: stepping in, showering, stepping out.</summary>
    private static Dictionary<HumanoidType, UseAnimations> ShowerUse() => new()
    {
        [HumanoidType.StandardMalePatient] = new(new(2018, 21), [new(2150, 27)], new(2026, 14)),
        [HumanoidType.StandardFemalePatient] = new(new(2852, 27), [new(2856, 42)], new(2860, 15)),
    };

    /// <summary>
    /// cardio.lua's <c>multi_usage_animations</c>, the same for the three kinds of stripped patient: getting on, being
    /// monitored (looped) and getting off, with the doctor's own animations at the readout.
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> CardioMultiUsage()
    {
        static MultiUseAnimations Use(AnimationRef begin, AnimationRef inUse, AnimationRef finish) =>
            new([new(begin, new(1030, 1))], new(inUse, new(656, 6)), [new(finish, new(1030, 1))]);

        MultiUseAnimations[] male = [Use(new(1018, 16), new(652, 6), new(3282, 10))];
        MultiUseAnimations[] female = [Use(new(3008, 16), new(2840, 6), new(4556, 10))];
        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.StrippedMalePatient, HumanoidType.Doctor)] = male,
            [(HumanoidType.StrippedMalePatient2, HumanoidType.Doctor)] = male,
            [(HumanoidType.StrippedMalePatient3, HumanoidType.Doctor)] = male,
            [(HumanoidType.StrippedFemalePatient, HumanoidType.Doctor)] = female,
            [(HumanoidType.StrippedFemalePatient2, HumanoidType.Doctor)] = female,
            [(HumanoidType.StrippedFemalePatient3, HumanoidType.Doctor)] = female,
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>
    /// scanner.lua's animations for the three kinds of stripped patient: down, staying down, up, staying up and back,
    /// then the scan forwards and back and tipping forward again.
    /// </summary>
    private static Dictionary<HumanoidType, UseAnimations> ScannerUse() => new()
    {
        [HumanoidType.SlackMalePatient] = new(new(4974, 1), [], new(4974, 1), Begin2: new(4974, 1), Finish2: new(4978, 5))
        {
            More = new(Begin3: new(4978, 5), Begin4: new(4986, 1), Begin5: new(4982, 5), Finish3: new(4986, 1), Finish4: new(4982, 5), Finish5: new(4974, 1)),
        },
        [HumanoidType.AlternateMalePatient] = new(new(4958, 1), [], new(4958, 1), Begin2: new(4958, 1), Finish2: new(4962, 5))
        {
            More = new(Begin3: new(4962, 5), Begin4: new(4970, 1), Begin5: new(4966, 5), Finish3: new(4970, 1), Finish4: new(4966, 5), Finish5: new(4958, 1)),
        },
        [HumanoidType.ChewbaccaPatient] = new(new(1176, 1), [], new(1176, 1), Begin2: new(1176, 1), Finish2: new(1180, 5))
        {
            More = new(Begin3: new(1180, 5), Begin4: new(1188, 1), Begin5: new(1184, 5), Finish3: new(1188, 1), Finish4: new(1184, 5), Finish5: new(1176, 1)),
        },
        [HumanoidType.SlackFemalePatient] = new(new(5158, 1), [], new(5158, 1), Begin2: new(5158, 1), Finish2: new(5162, 5))
        {
            More = new(Begin3: new(5162, 5), Begin4: new(5166, 1), Begin5: new(5170, 5), Finish3: new(5166, 1), Finish4: new(5170, 5), Finish5: new(5158, 1)),
        },
        [HumanoidType.StrippedMalePatient] = new(new(1402, 1), [], new(1402, 1), Begin2: new(1402, 1), Finish2: new(1406, 5))
        {
            More = new(Begin3: new(1406, 5), Begin4: new(1410, 1), Begin5: new(1414, 5), Finish3: new(1410, 1), Finish4: new(1414, 5), Finish5: new(1402, 1)),
        },
        [HumanoidType.StrippedFemalePatient] = new(new(1442, 1), [], new(1442, 1), Begin2: new(1442, 1), Finish2: new(1446, 5))
        {
            More = new(Begin3: new(1446, 5), Begin4: new(1450, 1), Begin5: new(1454, 5), Finish3: new(1450, 1), Finish4: new(1454, 5), Finish5: new(1442, 1)),
        },
        [HumanoidType.StrippedMalePatient2] = new(new(1406, 5), [], new(1422, 1), Begin2: new(1410, 1), Finish2: new(1426, 6))
        {
            More = new(Begin3: new(1414, 5), Begin4: new(1402, 1), Begin5: new(1418, 6), Finish3: new(1430, 1), Finish4: new(1434, 6), Finish5: new(1438, 6)),
        },
        [HumanoidType.StrippedFemalePatient2] = new(new(1446, 5), [], new(1462, 1), Begin2: new(1450, 1), Finish2: new(1466, 6))
        {
            More = new(Begin3: new(1454, 5), Begin4: new(1442, 1), Begin5: new(1458, 6), Finish3: new(1470, 1), Finish4: new(1474, 6), Finish5: new(1478, 6)),
        },
        [HumanoidType.StrippedMalePatient3] = new(new(1402, 1), [], new(1430, 1), Begin2: new(1402, 1), Finish2: new(1434, 6))
        {
            More = new(Begin3: new(1418, 6), Begin4: new(1422, 1), Begin5: new(1426, 6), Finish3: new(1422, 1), Finish4: new(1422, 1), Finish5: new(1438, 6)),
        },
        [HumanoidType.StrippedFemalePatient3] = new(new(1442, 1), [], new(1470, 1), Begin2: new(1442, 1), Finish2: new(1474, 6))
        {
            More = new(Begin3: new(1458, 6), Begin4: new(1462, 1), Begin5: new(1466, 6), Finish3: new(1462, 1), Finish4: new(1462, 1), Finish5: new(1478, 6)),
        },
    };

    /// <summary>
    /// operating_table.lua's <c>multi_usage_animations</c>: the surgeon with the gowned patient, who stands idle at the
    /// table's side at the first and last phases and is in the surgeon's animations between; the one-frame in-use
    /// animation loops (CorsixTH sets its length to 1).
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> OperatingTableMultiUsage()
    {
        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.Surgeon, HumanoidType.GownedMalePatient)] =
            [
                new MultiUseAnimations([new(new(2318, 9), new(416, 1)), new(new(2322, 21))], new(new(2348, 1)), [new(new(2346, 14)), new(new(2338, 12), new(416, 1))]),
            ],
            [(HumanoidType.Surgeon, HumanoidType.GownedFemalePatient)] =
            [
                new MultiUseAnimations([new(new(2318, 9), new(2886, 1)), new(new(2932, 20))], new(new(2938, 1)), [new(new(2936, 14)), new(new(2338, 12), new(2886, 1))]),
            ],
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>pool_table.lua's doctor and handyman: three animations to start, playing (looped), putting the cue away.</summary>
    private static Dictionary<HumanoidType, UseAnimations> PoolTableUse() => new()
    {
        [HumanoidType.Doctor] = new(new(1244, 3), [new(1234, 18)], new(1248, 3), Begin2: new(1238, 21)) { More = new(Begin3: new(1230, 8)) },
        [HumanoidType.Handyman] = new(new(3936, 2), [new(3928, 18)], new(3940, 2), Begin2: new(3932, 21)) { More = new(Begin3: new(3924, 8)) },
    };

    /// <summary>video_game.lua's doctor and nurse playing.</summary>
    private static Dictionary<HumanoidType, UseAnimations> VideoGameUse() => new()
    {
        [HumanoidType.Doctor] = new(null, [new(3700, 40)], null),
        [HumanoidType.Nurse] = new(null, [new(4764, 40)], null),
    };

    /// <summary>slicer.lua's <c>multi_usage_animations</c>: the slack patient has the tongue drawn out, the doctor shown too.</summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> SlicerMultiUsage()
    {
        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.SlackMalePatient, HumanoidType.Doctor)] = [new MultiUseAnimations([], new(new(1390, 27)), [])],
            [(HumanoidType.SlackFemalePatient, HumanoidType.Doctor)] = [new MultiUseAnimations([], new(new(1394, 27)), [])],
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>hair_restorer.lua's slack man: sits down, the cap comes down, restoring, the cap goes up as he gets up.</summary>
    private static Dictionary<HumanoidType, UseAnimations> HairRestorerUse() => new()
    {
        [HumanoidType.SlackMalePatient] = new(new(2074, 14), [new(2082, 4)], new(2086, 10), Begin2: new(2078, 11)),
    };

    /// <summary>
    /// electrolyser.lua's standard patients (the hairy one becomes one as the use starts): on the machine, electrocuted
    /// (looped), sparking, the hair falling off, off the machine.
    /// </summary>
    private static Dictionary<HumanoidType, UseAnimations> ElectrolyserUse() => new()
    {
        [HumanoidType.StandardMalePatient] = new(new(1266, 24), [new(1274, 3)], new(1278, 10), Finish2: new(1286, 13)) { More = new(Finish3: new(1294, 16)) },
        [HumanoidType.StandardFemalePatient] = new(new(1266, 24), [new(1274, 3)], new(1278, 10), Finish2: new(2940, 13)) { More = new(Finish3: new(2944, 16)) },
    };

    /// <summary>
    /// cast_remover.lua's man in casts with the nurse: onto the machine, it closes, the nurse brings the bucket, the casts
    /// go in it, she takes it away, he gets up; the nurse is in the animations.
    /// </summary>
    private static Dictionary<Direction, IReadOnlyDictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>> CastRemoverMultiUsage()
    {
        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.AlternateMalePatient, HumanoidType.Nurse)] =
            [
                new MultiUseAnimations([new(new(2720, 8)), new(new(2696, 16)), new(new(2692, 31))], new(new(2688, 18)), [new(new(3662, 35)), new(new(3658, 16))]),
            ],
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>x_ray.lua's animations for the standard patients: walking in, the radiation, walking out.</summary>
    private static Dictionary<HumanoidType, UseAnimations> XRayUse() => new()
    {
        [HumanoidType.SlackMalePatient] = new(new(5178, 27), [new(5174, 2)], new(5182, 16)),
        [HumanoidType.ChewbaccaPatient] = new(new(5132, 27), [new(610, 2)], new(5136, 16)),
        [HumanoidType.StandardMalePatient] = new(new(1964, 27), [new(1960, 2)], new(1992, 16)),
        [HumanoidType.StandardFemalePatient] = new(new(2948, 27), [new(2952, 2)], new(2956, 16)),
    };

    /// <summary>radiation_shield.lua's doctor: sits down, pulls the handle, sits idle or pushes buttons, stands up.</summary>
    private static Dictionary<HumanoidType, UseAnimations> ShieldUse() => new()
    {
        [HumanoidType.Doctor] = new(new(798, 13), [new(810, 19), new(814, 20)], new(802, 8), Begin2: new(806, 20)),
    };

    /// <summary>A usage table with a slack man's animations added.</summary>
    private static Dictionary<HumanoidType, UseAnimations> WithSlackMale(Dictionary<HumanoidType, UseAnimations> table, UseAnimations slackMale)
    {
        table[HumanoidType.SlackMalePatient] = slackMale;
        return table;
    }

    /// <summary>A usage table with another kind of humanoid's animations added.</summary>
    private static Dictionary<HumanoidType, UseAnimations> With(Dictionary<HumanoidType, UseAnimations> table, HumanoidType type, UseAnimations animations)
    {
        table[type] = animations;
        return table;
    }

    /// <summary>A usage table with a hairy patient's animations added.</summary>
    private static Dictionary<HumanoidType, UseAnimations> WithChewbacca(Dictionary<HumanoidType, UseAnimations> table, UseAnimations chewbacca)
    {
        table[HumanoidType.ChewbaccaPatient] = chewbacca;
        return table;
    }

    /// <summary>loo.lua's animations for the standard patients: men pick one of two in-use animations.</summary>
    private static Dictionary<HumanoidType, UseAnimations> LooUse() => new()
    {
        [HumanoidType.SlackMalePatient] = new(new(4292, 15), [new(4308, 32)], new(4316, 27), Begin2: new(4296, 25), Finish2: new(4320, 18)),
        [HumanoidType.ChewbaccaPatient] = new(new(4158, 15), [new(4170, 39)], new(4174, 27), Begin2: new(4162, 25), Finish2: new(4740, 18)),
        [HumanoidType.AlternateMalePatient] = new(new(4520, 15), [new(4528, 45)], new(4536, 27), Begin2: new(4524, 25), Finish2: new(4540, 18)),
        [HumanoidType.StandardMalePatient] = new(new(1712, 15), [new(1728, 45), new(1732, 32)], new(1740, 27), Begin2: new(1716, 25), Finish2: new(1744, 18)),
        [HumanoidType.StandardFemalePatient] = new(new(3132, 18), [new(3144, 48)], new(3152, 26), Begin2: new(3136, 25), Finish2: new(3156, 18)),
    };

    /// <summary>sink.lua's animations for the standard patients.</summary>
    private static Dictionary<HumanoidType, UseAnimations> SinkUse() => new()
    {
        [HumanoidType.StandardMalePatient] = new(new(1776, 18), [new(1780, 4)], new(1784, 39)),
        [HumanoidType.StandardFemalePatient] = new(new(3160, 18), [new(3164, 5)], new(3168, 39)),
    };

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

        var male = Variants(
            [new(3740, 14), new(3736, 14), new(3818, 11)],
            [new(540, 25), new(544, 41), new(552, 29)],
            [new(3802, 12), new(3806, 12), new(3826, 6)]);
        var female = Variants(
            [new(3732, 14), new(3728, 14), new(3822, 11)],
            [new(524, 22), new(528, 35), new(536, 29)],
            [new(3810, 12), new(3814, 12), new(3830, 6)]);

        // The same for each of the three kinds of stripped patient.
        var north = new Dictionary<(HumanoidType, HumanoidType), IReadOnlyList<MultiUseAnimations>>
        {
            [(HumanoidType.StrippedMalePatient, HumanoidType.Doctor)] = male,
            [(HumanoidType.StrippedMalePatient2, HumanoidType.Doctor)] = male,
            [(HumanoidType.StrippedMalePatient3, HumanoidType.Doctor)] = male,
            [(HumanoidType.StrippedFemalePatient, HumanoidType.Doctor)] = female,
            [(HumanoidType.StrippedFemalePatient2, HumanoidType.Doctor)] = female,
            [(HumanoidType.StrippedFemalePatient3, HumanoidType.Doctor)] = female,
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
