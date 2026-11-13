// Mood icons over humanoids, after CorsixTH CorsixTH/Lua/entities/humanoid.lua (moods, Humanoid:setMood) and
// entity.lua (Entity:setMoodInfo).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

using CorsixNet.Sim;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;

namespace CorsixNet.Client.Maps;

/// <summary>
/// The mood icon a humanoid shows: of the moods active for it, the one of the highest priority (<c>Humanoid:setMood</c>),
/// from humanoid.lua's table of icons and priorities. The moods are worked out from the simulation's state where CorsixTH
/// switches them on and off.
/// </summary>
internal static class HumanoidMoods
{
    /// <summary>How far above the humanoid the icon is drawn (<c>mood_info:setPosition(-1, -96)</c>).</summary>
    public const int OffsetX = -1;

    /// <inheritdoc cref="OffsetX"/>
    public const int OffsetY = -96;

    /// <summary>
    /// Too cold (below 11 °C, warmth 0.22) or too hot (above 18 °C, 0.36): <c>Humanoid:checkWarmth</c>'s cold and hot moods,
    /// which have no priority and show only while the cursor is over the humanoid (GameUI's hoverable moods).
    /// </summary>
    public static int? HoverIcon(Humanoid humanoid) =>
        humanoid.Behaviour is not PatientBehaviour && humanoid.Profile is null ? null
        : humanoid.Warmth < 0.22m ? 3994
        : humanoid.Warmth > 0.36m ? 3988
        : null;

    /// <summary>
    /// The animation of the humanoid's mood icon, or null for none. <paramref name="hovered"/>: the cursor is over the
    /// humanoid; <paramref name="queueRoomId"/>: the room the cursor is over, whose door queue shows the queue mood.
    /// </summary>
    public static int? Icon(Simulation simulation, Humanoid humanoid, bool hovered = false, int queueRoomId = 0)
    {
        // GameUI:onCursorWorldPositionChange: a hoverable mood replaces the shown one while the cursor is over them.
        if (hovered && HoverIcon(humanoid) is { } hover)
        {
            return hover;
        }

        var icon = 0;
        var priority = -1;

        void Offer(bool active, int animation, int moodPriority)
        {
            if (active && moodPriority > priority)
            {
                icon = animation;
                priority = moodPriority;
            }
        }

        if (humanoid.Behaviour is PatientBehaviour patient)
        {
            var dying = patient.Phase == PatientPhase.Dying;
            var home = patient.Phase == PatientPhase.GoingHome;
            Offer(dying, 4046, 60); // dead
            Offer(home && patient.IsCured, 4048, 60); // cured
            Offer(home && patient.HomeMood == PatientHomeMood.Exit, 4052, 60); // exit
            Offer(home && patient.HomeMood == PatientHomeMood.SadMoney, 4018, 55); // sad_money
            Offer(patient.EmergencyIndex != 0, 3914, 50); // emergency

            // GameUI: the cursor over a room or its door shows the queue mood over every patient queueing there.
            Offer(queueRoomId != 0 && simulation.FindEntity(queueRoomId) is Room { DoorQueue: { } queue } && queue.Contains(humanoid.Id), 4568, 70); // queue

            // Epidemic:startCoverUp shows the infected patients' status: infected, marked, chosen by a nurse, vaccinated.
            if (patient.IsInfected && humanoid.HospitalPlayer < simulation.Hospitals.Count
                && simulation.Hospitals[humanoid.HospitalPlayer].Epidemics.Current is { IsCoverUp: true })
            {
                var epidemic = patient.IsVaccinated ? 4566 : patient.VaccinatingNurseId != 0 ? 4572 : patient.IsMarkedForVaccination ? 4570 : 4574;
                Offer(true, epidemic, 55);
            }

            // Patient:_dailyHealthChecks's health_treshold_rangemap: dying1 to dying5 as health falls below 0.22.
            if (!patient.IsCured && !dying && patient.Health < 0.22m)
            {
                var stage = patient.Health < 0.06m ? 4 : patient.Health < 0.10m ? 3 : patient.Health < 0.14m ? 2 : patient.Health < 0.18m ? 1 : 0;
                Offer(true, 4000 + (2 * stage), 41 + stage);
            }

            Offer(patient.IsSad, 4578, 40); // sad2
            Offer(patient.Phase == PatientPhase.Waiting, 5006, 39); // patient_wait: no room to go to
            Offer(patient.IsNeedingToilet, 3996, 5); // poo
            Offer(patient.IsThirsty, 3986, 4); // thirsty
        }
        else if (humanoid.Profile is not null)
        {
            Offer(humanoid.IsFired, 4052, 60); // exit
            Offer(humanoid.RaiseRequest > 0, 4576, 40); // pay_rise
            Offer(humanoid.IsTiredMoodShown, 3990, 30); // tired

            var roomId = humanoid.Behaviour switch { DoctorBehaviour doctor => doctor.RoomId, NurseBehaviour nurse => nurse.RoomId, _ => 0 };
            if (roomId != 0 && simulation.FindEntity(roomId) is Room { PatientId: not 0 } room
                && simulation.FindEntity(room.PatientId) is Humanoid { Behaviour: PatientBehaviour visitor })
            {
                // Room:_staffWaitToggle: from the patient leaving the door queue until they come in (not in the toilets).
                Offer(visitor.Phase == PatientPhase.EnteringRoom && room.RoomType != "toilets", 4054, 20); // staff_wait

                // GPRoom:dealtWithPatient: the light bulb once the patient is diagnosed, uncertainty when they are sent on to
                // a diagnosis room; both go when the patient leaves (onHumanoidLeave).
                if (room.RoomType == "gp" && visitor.Phase == PatientPhase.LeavingRoom)
                {
                    Offer(visitor.IsDiagnosed, 4044, 12); // idea3
                    Offer(!visitor.IsDiagnosed && visitor.WillSeekRoom, 4020, 5); // reflexion
                }
            }
        }

        return priority < 0 ? null : icon;
    }
}
