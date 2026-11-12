// Where a humanoid's mood icon hangs in an animation: the markers CorsixTH sets on animation frames
// (CorsixTH/Lua/graphics.lua's setPatientMarker/setStaffMarker, called from objects/*.lua, entities/humanoid.lua and
// humanoid_actions/sweep_floor.lua), and th_gfx.cpp's animation::draw_child and get_primary_marker.
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace CorsixNet.Client.Maps;

/// <summary>
/// One <c>set{Patient,Staff}Marker</c> call's positions, in pixels from the centre of the humanoid's tile at floor level
/// (graphics.lua's positionToXy: a tile position (x, y) is 32 (x - y), 16 (x + y) pixels).
/// </summary>
internal abstract record MoodMarker
{
    /// <summary>The same position on every frame.</summary>
    public static MoodMarker Static(int x, int y) => new StaticMarker((x, y));

    /// <summary>One position per frame, the last one held.</summary>
    public static MoodMarker PerFrame((int X, int Y)[] positions) => new PerFrameMarker(positions);

    /// <summary>From the first position on the first frame to the second on the last.</summary>
    public static MoodMarker Linear((int X, int Y) from, (int X, int Y) to) => new LinearMarker(from, to);

    /// <summary>From <paramref name="start"/> on frame 0 through each keyframe's position, the last one held.</summary>
    public static MoodMarker Keyframes((int X, int Y) start, (int Frame, int X, int Y)[] keys) => new KeyframeMarker(start, keys);

    /// <summary>The position on one frame of an animation of <paramref name="length"/> frames.</summary>
    public abstract (int X, int Y) At(int frame, int length);

    private static int Lerp(int from, int to, double n) => (int)Math.Floor(((to - from) * n) + from);

    private sealed record StaticMarker((int X, int Y) Position) : MoodMarker
    {
        public override (int X, int Y) At(int frame, int length) => Position;
    }

    private sealed record PerFrameMarker((int X, int Y)[] Positions) : MoodMarker
    {
        public override (int X, int Y) At(int frame, int length) => Positions[Math.Clamp(frame, 0, Positions.Length - 1)];
    }

    private sealed record LinearMarker((int X, int Y) From, (int X, int Y) To) : MoodMarker
    {
        public override (int X, int Y) At(int frame, int length)
        {
            var n = length <= 1 ? 0 : frame / (double)(length - 1);
            return (Lerp(From.X, To.X, n), Lerp(From.Y, To.Y, n));
        }
    }

    private sealed record KeyframeMarker((int X, int Y) Start, (int Frame, int X, int Y)[] Keys) : MoodMarker
    {
        public override (int X, int Y) At(int frame, int length)
        {
            // setMarkerRaw's keyframe variant: linear between keyframes, the last position after the last keyframe.
            var (f1, x1, y1) = (0, Start.X, Start.Y);
            foreach (var (f2, x2, y2) in Keys)
            {
                if (frame < f2)
                {
                    var n = (frame - f1) / (double)(f2 - f1);
                    return (Lerp(x1, x2, n), Lerp(y1, y2, n));
                }

                (f1, x1, y1) = (f2, x2, y2);
            }

            return (x1, y1);
        }
    }
}

/// <summary>
/// The animations' primary (patient) and secondary (staff) markers, which place a humanoid's mood icon while it plays
/// them; animations without one hang it over the humanoid's position.
/// </summary>
internal static class MoodMarkers
{
    private static readonly Dictionary<(bool Patient, int Animation), MoodMarker> Markers = Build();

    /// <summary>
    /// The icon's offset from the humanoid's drawing position on a frame of an animation (get_primary_marker: the marker,
    /// mirrored with the animation, plus 16 down to the tile's centre); a frame without one has the marker (0, 0).
    /// </summary>
    public static (int X, int Y) Offset(bool patient, int animation, int frame, int length, bool flip)
    {
        var (x, y) = Markers.TryGetValue((patient, animation), out var marker) ? marker.At(frame, length) : (0, 0);
        return (flip ? -x : x, y + 16);
    }

    private static void Set(Dictionary<(bool, int), MoodMarker> markers, bool patient, int[] animations, MoodMarker marker)
    {
        foreach (var animation in animations)
        {
            markers[(patient, animation)] = marker;
        }
    }

    private static Dictionary<(bool, int), MoodMarker> Build()
    {
        var markers = new Dictionary<(bool, int), MoodMarker>();
        // Generated from CorsixTH's calls, in their order (a later call for the same animation wins, as it does there).
        Set(markers, true, [2146], MoodMarker.Static(0, 0)); // autopsy.lua
        Set(markers, true, [4686, 4710], MoodMarker.Keyframes((64, 0), [(4, 44, -10), (12, 44, -10), (18, 22, -21)])); // bed.lua
        Set(markers, true, [4694, 4718], MoodMarker.Static(22, -21)); // bed.lua
        Set(markers, true, [4702, 4726], MoodMarker.Keyframes((22, -21), [(6, 22, -21), (12, 44, -10), (14, 64, 0)])); // bed.lua
        Set(markers, false, [90], MoodMarker.Static(0, 0)); // cabinet.lua
        Set(markers, false, [88], MoodMarker.Static(0, 0)); // cabinet.lua
        Set(markers, false, [2516], MoodMarker.Keyframes((0, -2), [(1, 2, -6), (2, 6, -11), (3, 10, -9), (4, 16, -10)])); // comfortable_chair.lua
        Set(markers, false, [2512], MoodMarker.Keyframes((16, -10), [(1, 17, -8), (2, 26, -12), (3, 34, -12)])); // comfortable_chair.lua
        Set(markers, false, [2544], MoodMarker.Static(34, -12)); // comfortable_chair.lua
        Set(markers, false, [2520], MoodMarker.Keyframes((34, -12), [(1, 28, -12), (2, 15, -11), (3, 11, -7), (4, 7, -4)])); // comfortable_chair.lua
        Set(markers, false, [524], MoodMarker.Static(-39, 11)); // crash_trolley.lua
        Set(markers, true, [524], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [528], MoodMarker.Static(-34, 7)); // crash_trolley.lua
        Set(markers, true, [528], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [536, 552], MoodMarker.Keyframes((-35, 11), [(1, -25, 7), (2, -22, 8), (17, -22, 8), (18, -29, 10), (19, -31, 11), (20, -36, 10)])); // crash_trolley.lua
        Set(markers, true, [536, 552], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [540], MoodMarker.Static(-36, 10)); // crash_trolley.lua
        Set(markers, true, [540], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [544], MoodMarker.Keyframes((-38, 13), [(36, -38, 13), (37, -34, 10), (38, -31, 14), (40, -33, 11)])); // crash_trolley.lua
        Set(markers, true, [544], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [3728, 3732, 3736, 3740], MoodMarker.Keyframes((0, 0), [(4, 0, 0), (5, -1, 0), (6, -9, 2), (7, -10, 4), (8, -12, 7), (9, -17, 5), (10, -22, 10), (11, -25, 11), (12, -31, 15)])); // crash_trolley.lua
        Set(markers, true, [3728, 3732, 3736, 3740], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [3802, 3806, 3810, 3814], MoodMarker.Keyframes((-30, 17), [(1, -22, 12), (2, -17, 11), (3, -13, 9), (4, -8, 9), (5, -1, 8), (6, 3, 4), (7, 0, 0)])); // crash_trolley.lua
        Set(markers, true, [3802, 3806, 3810, 3814], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [3818, 3822], MoodMarker.Keyframes((0, 0), [(1, 0, 0), (2, -1, 2), (3, -10, 4), (4, -10, 7), (5, -12, 6), (6, -17, 8), (7, -22, 8), (8, -25, 10), (9, -31, 17), (10, -31, 15)])); // crash_trolley.lua
        Set(markers, true, [3818, 3822], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [3826, 3830], MoodMarker.Keyframes((-30, 17), [(1, -22, 12), (2, -17, 11), (3, -13, 9), (4, -8, 9), (5, -1, 8)])); // crash_trolley.lua
        Set(markers, true, [3826, 3830], MoodMarker.Static(-63, -2)); // crash_trolley.lua
        Set(markers, false, [56], MoodMarker.Keyframes((0, 0), [(10, -23, -21), (13, -26, -16)])); // desk.lua
        Set(markers, false, [3240], MoodMarker.Keyframes((0, 0), [(10, -23, -21), (13, -26, -16)])); // desk.lua
        Set(markers, false, [72], MoodMarker.Static(-26, -16)); // desk.lua
        Set(markers, false, [718], MoodMarker.Static(-26, -16)); // desk.lua
        Set(markers, false, [3256], MoodMarker.Static(-26, -16)); // desk.lua
        Set(markers, false, [64], MoodMarker.Keyframes((-26, -16), [(3, -23, -21), (12, 0, 0)])); // desk.lua
        Set(markers, false, [3248], MoodMarker.Keyframes((-26, -16), [(3, -23, -21), (12, 0, 0)])); // desk.lua
        Set(markers, false, [58], MoodMarker.Keyframes((-32, -16), [(9, 3, -34), (11, 3, -28)])); // desk.lua
        Set(markers, false, [3242], MoodMarker.Keyframes((-32, -16), [(9, 3, -34), (11, 3, -28)])); // desk.lua
        Set(markers, false, [74], MoodMarker.Static(9, -28)); // desk.lua
        Set(markers, false, [3258], MoodMarker.Static(9, -28)); // desk.lua
        Set(markers, false, [66], MoodMarker.Keyframes((3, -28), [(3, 6, -29), (8, 32, -48)])); // desk.lua
        Set(markers, false, [3250], MoodMarker.Keyframes((3, -28), [(3, 6, -29), (8, 32, -48)])); // desk.lua
        Set(markers, true, [4560], MoodMarker.Keyframes((0, 0), [])); // gates_to_hell.lua
        Set(markers, true, [4560], MoodMarker.Keyframes((0, 0), [])); // gates_to_hell.lua
        Set(markers, false, [2632], MoodMarker.Keyframes((0, 0), [(10, -29, -18)])); // lecture_chair.lua
        Set(markers, false, [2622], MoodMarker.Static(-29, -18)); // lecture_chair.lua
        Set(markers, false, [2636], MoodMarker.Keyframes((-29, -18), [(11, -29, -18), (18, 0, 0)])); // lecture_chair.lua
        Set(markers, true, [950, 1712, 1712, 3132, 3132, 3132, 4158, 4256, 4292, 4388, 4432, 4520], MoodMarker.Keyframes((0, 0), [(1, 0, 0), (6, 25, -16)])); // loo.lua
        Set(markers, true, [954, 1716, 1716, 3136, 3136, 3136, 4162, 4260, 4296, 4392, 4436, 4524], MoodMarker.Static(25, -16)); // loo.lua
        Set(markers, true, [962, 1728, 1728, 1732, 1732, 3144, 3144, 3144, 4170, 4272, 4308, 4400, 4440, 4528], MoodMarker.Static(25, -16)); // loo.lua
        Set(markers, true, [966, 1740, 1740, 3152, 3152, 3152, 4174, 4280, 4316, 4404, 4444, 4536], MoodMarker.Static(25, -16)); // loo.lua
        Set(markers, true, [1158, 1744, 1744, 3156, 3156, 3156, 4284, 4320, 4408, 4448, 4540, 4740], MoodMarker.Keyframes((25, -16), [(1, 25, -16), (6, 0, 0)])); // loo.lua
        Set(markers, false, [1972], MoodMarker.Keyframes((-1, 1), [(12, -1, 1), (13, 4, -1)])); // plant.lua
        Set(markers, false, [1974], MoodMarker.Keyframes((-1, 1), [(19, -1, 1), (20, 2, -1), (21, 6, 0)])); // plant.lua
        Set(markers, false, [2590], MoodMarker.Static(-29, 1)); // projector.lua
        Set(markers, false, [2598], MoodMarker.Static(-29, 1)); // projector.lua
        Set(markers, false, [2602], MoodMarker.Static(-29, 1)); // projector.lua
        Set(markers, false, [2606], MoodMarker.Static(-29, 1)); // projector.lua
        Set(markers, false, [2610], MoodMarker.Keyframes((-29, 1), [(11, -29, 1), (16, 0, 0)])); // projector.lua
        Set(markers, true, [1022, 1204], MoodMarker.Static(0, -32)); // screen.lua
        Set(markers, false, [1022, 1204], MoodMarker.Static(0, -32)); // screen.lua
        Set(markers, true, [946], MoodMarker.Keyframes((0, -32), [(6, 0, -32), (7, -4, -28), (11, -4, -28), (12, 0, -32), (24, 0, -32), (28, 0, -20), (32, 0, -20), (36, 0, -32), (43, 0, -32)])); // screen.lua
        Set(markers, true, [1048], MoodMarker.Keyframes((0, -32), [(7, 0, -32), (8, -4, -28), (12, -4, -28), (13, 0, -32)])); // screen.lua
        Set(markers, true, [1052], MoodMarker.Keyframes((0, -32), [(10, 0, -32), (11, -4, -28), (21, -4, -28), (22, 0, -32)])); // screen.lua
        Set(markers, true, [2848], MoodMarker.Keyframes((0, -32), [(2, -4, -28), (3, -4, -28), (9, -4, -28), (11, 0, -32), (17, 0, -32), (18, -4, -28), (25, -4, -28), (26, 0, -32)])); // screen.lua
        Set(markers, true, [2844], MoodMarker.Keyframes((0, -32), [(2, 0, -32), (3, -4, -28), (12, -4, -28), (13, 0, -32)])); // screen.lua
        Set(markers, true, [1776, 3160], MoodMarker.Keyframes((0, 0), [(4, 22, -12)])); // sink.lua
        Set(markers, true, [1780, 3164], MoodMarker.Static(22, -12)); // sink.lua
        Set(markers, true, [1784, 3168], MoodMarker.Keyframes((22, -12), [(34, 22, -12), (39, 0, 0)])); // sink.lua
        Set(markers, false, [2178, 3570, 3878], MoodMarker.Keyframes((0, -32), [(2, -13, -26)])); // sofa.lua
        Set(markers, false, [2186, 3578, 3886], MoodMarker.Keyframes((-13, -26), [(2, -29, -5)])); // sofa.lua
        Set(markers, false, [2208, 4772, 4780], MoodMarker.Static(-29, -5)); // sofa.lua
        Set(markers, false, [2194, 3586, 3894], MoodMarker.Keyframes((-29, -5), [(2, -13, -26), (5, 0, -32)])); // sofa.lua
        Set(markers, false, [2180, 3572, 3880], MoodMarker.Keyframes((64, 0), [(2, 51, -7)])); // sofa.lua
        Set(markers, false, [2188, 3580, 3888], MoodMarker.Keyframes((51, -7), [(3, 28, -5)])); // sofa.lua
        Set(markers, false, [2210, 4774, 4782], MoodMarker.Static(28, -5)); // sofa.lua
        Set(markers, false, [2196, 3588, 3896], MoodMarker.Keyframes((28, -5), [(1, 28, -5), (3, 51, -7), (5, 64, 0)])); // sofa.lua
        Set(markers, true, [2772, 2774, 2776], MoodMarker.Static(-3, -38)); // surgeon_screen.lua
        Set(markers, false, [2772, 2774, 2776], MoodMarker.Static(-3, -38)); // surgeon_screen.lua
        Set(markers, false, [2780], MoodMarker.Keyframes((-3, -38), [(2, -3, -38), (7, -4, -21), (22, -4, -21), (25, -3, -38), (26, -3, -38)])); // surgeon_screen.lua
        Set(markers, false, [2782], MoodMarker.Keyframes((-3, -38), [(3, -3, -38), (6, -4, -28), (8, -4, -28), (9, -4, -21), (14, -4, -21), (18, -3, -38), (22, -3, -38), (27, -4, -21), (34, -4, -21), (37, -3, -38)])); // surgeon_screen.lua
        Set(markers, false, [2784], MoodMarker.Keyframes((-3, -38), [(3, -3, -38), (6, -4, -21), (14, -4, -21), (18, -3, -38), (23, -3, -38), (28, -4, -21), (33, -4, -21), (36, -3, -38)])); // surgeon_screen.lua
        Set(markers, false, [2790], MoodMarker.Keyframes((-3, -38), [(4, -4, -21), (12, -4, -21), (17, -3, -38), (24, -3, -38), (28, -4, -21), (35, -4, -21), (39, -3, -38)])); // surgeon_screen.lua
        Set(markers, false, [2792], MoodMarker.Keyframes((-3, -38), [(4, -3, -38), (8, -4, -21), (16, -4, -21), (21, -3, -38), (31, -3, -38), (34, -4, -21), (42, -4, -21), (46, -3, -38)])); // surgeon_screen.lua
        Set(markers, false, [2794], MoodMarker.Keyframes((-3, -38), [(5, -4, -21), (15, -4, -21), (21, -3, -38)])); // surgeon_screen.lua
        Set(markers, false, [2796], MoodMarker.Keyframes((-3, -38), [(4, -4, -21), (17, -4, -21), (21, -3, -38)])); // surgeon_screen.lua
        Set(markers, true, [4760], MoodMarker.Keyframes((0, -32), [(4, 0, -20), (16, 0, -20), (19, 0, -32)])); // surgeon_screen.lua
        Set(markers, true, [4768], MoodMarker.Keyframes((0, -32), [(2, 0, -32), (5, 0, -20), (11, 0, -20), (15, 0, -32)])); // surgeon_screen.lua
        Set(markers, true, [4762], MoodMarker.Keyframes((0, -32), [(5, 0, -20), (16, 0, -20), (19, 0, -32)])); // surgeon_screen.lua
        Set(markers, true, [4770], MoodMarker.Keyframes((0, -32), [(3, 0, -32), (10, 0, -20), (15, 0, -32)])); // surgeon_screen.lua
        Set(markers, true, [2228], MoodMarker.Static(-23, -37)); // blood_machine.lua
        Set(markers, false, [890], MoodMarker.Keyframes((-10, -7), [(1, -13, -11), (2, -17, -11), (3, -21, -13), (4, -27, -14), (5, -31, -16), (6, -37, -17)])); // cardio.lua
        Set(markers, false, [894], MoodMarker.Keyframes((-37, -17), [(5, -37, -17), (6, -29, -15), (7, -25, -15), (8, -20, -13), (9, -19, -13), (10, -18, -13), (11, -12, -10)])); // cardio.lua
        Set(markers, true, [648], MoodMarker.Static(0, -29)); // cardio.lua
        Set(markers, false, [3514], MoodMarker.Static(-1, -38)); // cast_remover.lua
        Set(markers, true, [2720, 4658], MoodMarker.Keyframes((-10, -5), [(1, -11, -7), (2, -15, -12), (3, -20, -14), (4, -26, -18), (5, -31, -17), (6, -35, -17), (7, -35, -17)])); // cast_remover.lua
        Set(markers, false, [2720, 4658], MoodMarker.Static(29, -18)); // cast_remover.lua
        Set(markers, true, [2696, 4666], MoodMarker.Static(-35, -17)); // cast_remover.lua
        Set(markers, false, [2696, 4666], MoodMarker.Static(29, -18)); // cast_remover.lua
        Set(markers, true, [2692, 4670], MoodMarker.Static(-35, -17)); // cast_remover.lua
        Set(markers, false, [2692, 4670], MoodMarker.Keyframes((31, -19), [(1, 22, -20), (2, 16, -23), (3, 11, -22), (6, 11, -22), (7, 16, -20), (8, 19, -20), (9, 11, -18), (10, 5, -13), (11, 5, -13), (12, -4, -4), (15, -4, -4), (16, 5, -4), (17, 14, -8), (18, 19, -10), (19, 27, -17), (20, 29, -17)])); // cast_remover.lua
        Set(markers, true, [2688, 4676], MoodMarker.Static(-35, -17)); // cast_remover.lua
        Set(markers, false, [2688, 4674], MoodMarker.Static(29, -18)); // cast_remover.lua
        Set(markers, true, [3662, 5112], MoodMarker.Static(-35, -17)); // cast_remover.lua
        Set(markers, false, [3662, 5112], MoodMarker.Keyframes((30, -17), [(1, 27, -14), (2, 23, -15), (3, 20, -11), (4, 15, -9), (5, 10, -7), (6, -2, -5), (9, -2, -5), (10, 3, -7), (11, 8, -10), (12, 14, -13), (13, 22, -14), (14, 27, -16), (15, 31, -14), (16, 27, -20), (17, 24, -20), (18, 17, -23), (20, 11, -21), (23, 11, -21), (24, 15, -19), (25, 19, -21), (26, 24, -20), (27, 27, -19), (28, 31, -17), (29, 38, -16), (30, 42, -13), (31, 49, -8), (32, 52, -8), (33, 55, -7), (34, 59, -4)])); // cast_remover.lua
        Set(markers, true, [3658, 4682], MoodMarker.Keyframes((-35, -17), [(11, -35, -17), (12, -26, -16), (13, -17, -11), (14, -9, -10), (15, -7, -9)])); // cast_remover.lua
        Set(markers, false, [3658, 4682], MoodMarker.Static(62, 1)); // cast_remover.lua
        Set(markers, true, [2492], MoodMarker.Static(-26, -39)); // cast_remover.lua
        Set(markers, true, [1262], MoodMarker.Static(-4, -40)); // electrolyser.lua
        Set(markers, true, [2070], MoodMarker.Static(3, -34)); // hair_restorer.lua
        Set(markers, false, [568], MoodMarker.Static(61, -36)); // hair_restorer.lua
        Set(markers, true, [464], MoodMarker.Keyframes((-32, 16), [(5, 0, 0)])); // inflator.lua
        Set(markers, true, [576], MoodMarker.Keyframes((0, 0), [(1, 0, 0), (11, 32, 16)])); // inflator.lua
        Set(markers, true, [572], MoodMarker.Static(3, -31)); // inflator.lua
        Set(markers, false, [3480], MoodMarker.Static(59, -3)); // inflator.lua
        Set(markers, false, [3480], MoodMarker.Static(59, -3)); // inflator.lua
        Set(markers, false, [2306, 2342], MoodMarker.Static(0, 0)); // operating_table_b.lua
        Set(markers, false, [2330], MoodMarker.Linear((0, 0), (-10, -18))); // operating_table_b.lua
        Set(markers, false, [2326, 4890], MoodMarker.Static(-10, -18)); // operating_table_b.lua
        Set(markers, false, [2334], MoodMarker.Linear((-10, -18), (0, 0))); // operating_table_b.lua
        Set(markers, true, [2314], MoodMarker.Static(26, -64)); // operating_table.lua
        Set(markers, false, [2238, 2318, 2318, 2322, 2338, 2346, 2348, 2932, 2936, 2938], MoodMarker.Static(32, -48)); // operating_table.lua
        Set(markers, false, [660], MoodMarker.Static(29, -52)); // operating_table.lua
        Set(markers, true, [1398], MoodMarker.Static(0, -36)); // scanner.lua
        Set(markers, false, [564], MoodMarker.Static(-3, -3)); // scanner.lua
        Set(markers, true, [1386], MoodMarker.Static(-13, -32)); // slicer.lua
        Set(markers, false, [3490], MoodMarker.Static(28, -20)); // slicer.lua
        Set(markers, true, [1556], MoodMarker.Static(0, -29)); // ultrascanner.lua
        Set(markers, false, [664], MoodMarker.Static(60, -4)); // ultrascanner.lua
        Set(markers, true, [1988], MoodMarker.Static(0, -64)); // x_ray.lua
        Set(markers, false, [3558], MoodMarker.Keyframes((59, -4), [(1, 55, -8), (2, 51, -8), (3, 47, -11), (4, 43, -12), (5, 43, -13), (6, 39, -15), (7, 34, -18), (8, 31, -20), (9, 29, -21), (10, 24, -23), (11, 18, -27), (12, 15, -33), (13, 14, -34), (14, 6, -36)])); // x_ray.lua
        Set(markers, false, [3562], MoodMarker.Static(6, -36)); // x_ray.lua
        Set(markers, true, [1526], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [184], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [260], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [3528], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [1798], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [1106], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [260], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [260], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [3054], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [184], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [3636], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [2750], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, true, [1524], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [182], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [258], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [3526], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [1796], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [1104], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [258], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [258], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [3052], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [182], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [3634], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [2748], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, true, [2040], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, true, [2864], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, true, [2864], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, true, [3626], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, true, [3626], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, true, [2042], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, true, [2866], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, true, [2866], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, true, [3628], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, true, [3628], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, false, [3288], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, false, [3266], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, false, [672], MoodMarker.PerFrame([(-19, -15), (-26, -17), (-26, -17), (-26, -17), (-26, -15), (-22, -13), (-19, -9), (-13, -5), (-8, -4), (-5, 0)])); // humanoid.lua
        Set(markers, false, [3286], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, false, [3264], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, false, [670], MoodMarker.PerFrame([(-7, 0), (-14, 6), (-14, 6), (-12, 5), (-11, 5), (-7, 3), (-4, 0), (2, -1), (2, -1), (10, -3), (14, -7), (20, -11)])); // humanoid.lua
        Set(markers, false, [3518], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, false, [3272], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, false, [4750], MoodMarker.Linear((3, 4), (32, -14))); // humanoid.lua
        Set(markers, false, [3520], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, false, [3274], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, false, [4752], MoodMarker.Linear((-32, -10), (0, -2))); // humanoid.lua
        Set(markers, true, [392], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4476], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [1034], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4452], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [2056], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4138], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4204], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4384], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [3184], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4324], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4864], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [368], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [2960], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4464], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [360], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4060], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [2964], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4468], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [364], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4472], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4744], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4852], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [2244], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4744], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4178], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4208], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4848], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [970], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, true, [4328], MoodMarker.Static(0, 0)); // humanoid.lua
        Set(markers, false, [1874, 1878], MoodMarker.Static(-1, 0)); // sweep_floor.lua
        return markers;
    }
}
