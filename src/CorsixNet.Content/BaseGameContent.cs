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
        MultiUsage: UltrascanMultiUsage());

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
        UsageSouth: XRayUse());

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
        MultiUsage: InflatorMultiUsage());

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
        MultiUsage: BloodMachineMultiUsage());

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
        UsageSouth: ShowerUse());

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
        [HumanoidType.Doctor] = new(Entering: new(672, 10), Leaving: new(670, 12), EnteringSwing: new(4752, 6), LeavingSwing: new(4750, 5)),
        [HumanoidType.Nurse] = new(Entering: new(3266, 10), Leaving: new(3264, 12), EnteringSwing: new(3274, 6), LeavingSwing: new(3272, 7)),
        [HumanoidType.Handyman] = new(Entering: new(3288, 10), Leaving: new(3286, 12), EnteringSwing: new(3520, 6), LeavingSwing: new(3518, 9)),
    };

    public static GameContent Create() => new(
        [GpOffice, Pharmacy, GeneralDiagnosis, Toilets, Ultrascan, ResearchRoom, XRayRoom, Inflation, Psych, Ward, BloodMachineRoom, Decontamination],
        ExternalWalls,
        // Corridor objects in the order of CorsixTH's furnish corridor window (corridor_object).
        [Desk, Cabinet, Chair, PharmacyCabinet, Screen, CrashTrolley, ReceptionDesk, Bench, DrinksMachine, FireExtinguisher, Radiator, Bin, Plant, Loo, Sink, Ultrascanner, Computer, Analyser, Autopsy, XRay, RadiationShield, Inflator, Couch, ComfortableChair, Bookcase, Skeleton, Bed, BloodMachine, DoctorConsole, Shower],
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
        };
        return new() { [Direction.North] = north, [Direction.South] = north };
    }

    /// <summary>shower.lua's animations for the standard patients: stepping in, showering, stepping out.</summary>
    private static Dictionary<HumanoidType, UseAnimations> ShowerUse() => new()
    {
        [HumanoidType.StandardMalePatient] = new(new(2018, 21), [new(2150, 27)], new(2026, 14)),
        [HumanoidType.StandardFemalePatient] = new(new(2852, 27), [new(2856, 42)], new(2860, 15)),
    };

    /// <summary>x_ray.lua's animations for the standard patients: walking in, the radiation, walking out.</summary>
    private static Dictionary<HumanoidType, UseAnimations> XRayUse() => new()
    {
        [HumanoidType.StandardMalePatient] = new(new(1964, 27), [new(1960, 2)], new(1992, 16)),
        [HumanoidType.StandardFemalePatient] = new(new(2948, 27), [new(2952, 2)], new(2956, 16)),
    };

    /// <summary>radiation_shield.lua's doctor: sits down, pulls the handle, sits idle or pushes buttons, stands up.</summary>
    private static Dictionary<HumanoidType, UseAnimations> ShieldUse() => new()
    {
        [HumanoidType.Doctor] = new(new(798, 13), [new(810, 19), new(814, 20)], new(802, 8), Begin2: new(806, 20)),
    };

    /// <summary>loo.lua's animations for the standard patients: men pick one of two in-use animations.</summary>
    private static Dictionary<HumanoidType, UseAnimations> LooUse() => new()
    {
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
