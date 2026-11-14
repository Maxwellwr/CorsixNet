using System.Globalization;
using CorsixNet.Client.Maps;
using CorsixNet.Sim;
using CorsixNet.Sim.Commands;
using CorsixNet.Sim.Entities;
using CorsixNet.Sim.People;
using CorsixNet.Sim.Rooms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CorsixNet.Client.Ui.Windows;

/// <summary>
/// The patient window (CorsixTH's <c>dialogs/patient.lua</c>), opened by clicking a patient: disease and how far the
/// diagnosis is, health with its last days, warmth, thirst, need for the toilet and happiness, what they owe, where
/// they have been; sending them home (after a confirmation, as the original asks), guessing the cure, vaccinating them
/// in an epidemic's cover-up and showing their queue.
/// </summary>
internal sealed class PatientWindow(int patientId) : UiWindow
{
    private bool _confirmHome;

    public int PatientId { get; } = patientId;

    private PatientScreen? _screen;

    private PatientScreen Screen => _screen ??= new PatientScreen(PatientId);

    /// <summary>The original's look (<see cref="PatientScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => Patient(host) is not null && PatientScreen.IsAvailable(host);

    public override Point FullscreenSize => PatientScreen.Size;

    /// <summary>UIPatient:setDefaultPosition(-20, 30).</summary>
    public override Vector2? FullscreenPosition => new(-20, 30);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        if (Patient(host) is { Behaviour: PatientBehaviour patient } humanoid)
        {
            Screen.Draw(batch, host, origin, mouse, humanoid, patient);
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        if (Patient(host) is { Behaviour: PatientBehaviour patient } humanoid)
        {
            Screen.Mouse(host, this, local, mouse, previous, humanoid, patient);
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) =>
        Patient(host) is { Behaviour: PatientBehaviour patient } ? Screen.Tooltip(host, local, patient) : null;
    public override string Title => "Patient";

    public override bool IsSameAs(UiWindow other) => other is PatientWindow window && window.PatientId == PatientId;

    public override bool IsDone(IWindowHost host) => Patient(host) is null;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (Patient(host) is not { } humanoid || humanoid.Behaviour is not PatientBehaviour patient)
        {
            yield break;
        }

        var simulation = host.Simulation;
        var disease = patient.IsDiagnosed ? host.Texts?.DiseaseName(patient.DiseaseId) ?? patient.DiseaseId : "Not diagnosed yet";
        yield return WindowRow.Of("Disease", new WindowCell(disease, Highlight: patient.IsDiagnosed));
        yield return WindowRow.Of("Diagnosis", WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{Math.Min(100, patient.DiagnosisProgress / 10)} %")));
        yield return WindowRow.Of("Health", WindowCell.Number(StaffLabels.Percent(patient.Health)), HealthGraph(patient));
        yield return WindowRow.Of("Warmth", WindowCell.Number(StaffLabels.Percent(humanoid.Warmth)));
        yield return WindowRow.Of("Thirst", WindowCell.Number(StaffLabels.Percent(patient.Thirst)));
        yield return WindowRow.Of("Toilet", WindowCell.Number(StaffLabels.Percent(patient.ToiletNeed)));
        yield return WindowRow.Of("Happiness", WindowCell.Number(StaffLabels.Percent(humanoid.Happiness)), patient.IsSad ? "Sad" : string.Empty);
        if (patient.PayAmount > 0)
        {
            yield return WindowRow.Of("To pay", WindowCell.Number(MoneyText.Format(patient.PayAmount)));
        }

        if (patient.IsInfected)
        {
            yield return WindowRow.Of("Contagious", patient.IsVaccinated ? "Vaccinated" : patient.IsMarkedForVaccination ? "Waiting for a nurse" : "Not vaccinated");
        }

        if (patient.TreatmentHistory.Count > 0)
        {
            yield return WindowRow.Of("Been to", string.Join(", ", patient.TreatmentHistory.Select(entry => History(simulation, host, entry))));
        }

        if (patient.Question != PatientQuestion.None)
        {
            yield return WindowCell.Line(patient.Question == PatientQuestion.NoDiagnosisRoom ? "Asks what to do: no diagnosis room left" : "Asks what to do: no room to treat them", highlight: true);
        }

        yield return WindowRow.Gap;
        var hospital = simulation.Hospitals[host.Player];
        var canGuess = !patient.IsDiagnosed && patient.Disease is { } known && hospital.PeekCase(known, simulation.Rules.Diseases).Discovered;
        var coverUp = hospital.Epidemics.Current is { IsCoverUp: true, IsWatchRunning: true };
        yield return WindowRow.Of(
            (_confirmHome
                ? WindowCell.Button("Sure? Send home", () => host.Submit(new SendPatientHomeCommand(humanoid.Id)))
                : WindowCell.Button("Send home", () => _confirmHome = true)).WithTooltip(OriginalTooltips.Get(host, 94, "Send the patient home")),
            WindowCell.Button("Guess cure", () => host.Submit(new GuessPatientCureCommand(humanoid.Id)), canGuess).WithTooltip(OriginalTooltips.Get(host, 96, "Stop diagnosing and guess the cure")),
            WindowCell.Button("Vaccinate", () => host.Submit(new VaccinateCommand(humanoid.Id)), coverUp && patient.IsInfected && !patient.IsVaccinated && !patient.IsMarkedForVaccination).WithTooltip("Have a nurse vaccinate this patient during the epidemic"),
            WindowCell.Button("Show", () => host.CenterOn(humanoid.Tile)).WithTooltip(OriginalTooltips.Get(host, 95, "Show the patient in the hospital")),
            WindowCell.Button("Queue", () => host.Open(new QueueWindow(patient.TargetId)), QueueWindow.QueueOf(simulation, patient.TargetId) is not null).WithTooltip(OriginalTooltips.Get(host, 97, "Show the queue the patient is in")));
    }

    private static string History(Simulation simulation, IWindowHost host, TreatmentHistoryEntry entry) => entry.Kind switch
    {
        TreatmentHistoryKind.Room => simulation.Content.FindRoom(entry.Id)?.Name ?? entry.Id,
        TreatmentHistoryKind.Diagnosis => $"diagnosed {host.Texts?.DiseaseName(entry.Id) ?? entry.Id}",
        _ => "cured",
    };

    /// <summary>The health graph's last days, as a row of bars from low (.) to high (#).</summary>
    private static string HealthGraph(PatientBehaviour patient)
    {
        const string bars = ".:-=+*#";
        return new string(patient.HealthHistory.Select(health => bars[(int)Math.Clamp(health * (bars.Length - 1), 0m, bars.Length - 1)]).ToArray());
    }

    private Humanoid? Patient(IWindowHost host) =>
        host.Simulation.FindEntity(PatientId) is Humanoid { Behaviour: PatientBehaviour { Phase: not PatientPhase.GoingHome }, IsDespawning: false } humanoid && humanoid.HospitalPlayer == host.Player ? humanoid : null;
}

/// <summary>
/// The queue window (CorsixTH's <c>dialogs/queue_dialog.lua</c>), opened by clicking a room or a reception desk: who is
/// in the queue, moving a patient to the front or the back (<c>Queue:movePatient</c>), the queue's size, and sending a
/// patient to another room of the same kind.
/// </summary>
internal sealed class QueueWindow(int ownerId) : UiWindow
{
    public int OwnerId { get; } = ownerId;

    public override string Title => "Queue";

    private QueueScreen? _screen;

    private QueueScreen Screen => _screen ??= new QueueScreen(OwnerId);

    /// <summary>The original's look (<see cref="QueueScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => QueueScreen.IsAvailable(host);

    public override Point FullscreenSize => QueueScreen.Size;

    public override bool CapturesMouse => _screen is { IsDragging: true };

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => Screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => Screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => QueueScreen.Tooltip(host, local);
    public override bool IsSameAs(UiWindow other) => other is QueueWindow window && window.OwnerId == OwnerId;

    public override bool IsDone(IWindowHost host) => QueueOf(host.Simulation, OwnerId) is null;

    /// <summary>The queue of a room's door or a reception desk, or null for anything else.</summary>
    public static HumanoidQueue? QueueOf(Simulation simulation, int ownerId) => simulation.FindEntity(ownerId) switch
    {
        Room room => room.DoorQueue,
        RoomObject { Desk: { } desk } => desk.Queue,
        _ => null,
    };

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        var simulation = host.Simulation;
        if (QueueOf(simulation, OwnerId) is not { } queue)
        {
            yield break;
        }

        var owner = simulation.FindEntity(OwnerId);
        yield return WindowCell.Line(owner is Room room ? simulation.Content.FindRoom(room.RoomType)?.Name ?? room.RoomType : "Reception desk", highlight: true);
        yield return WindowRow.Of(
            "Queue size",
            WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{queue.Count} of {queue.MaxSize}")),
            WindowCell.Button("-5", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, -5)), queue.MaxSize > 0).WithTooltip(OriginalTooltips.Get(host, 75, "Make the queue shorter")),
            WindowCell.Button("-1", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, -1)), queue.MaxSize > 0).WithTooltip(OriginalTooltips.Get(host, 75, "Make the queue shorter")),
            WindowCell.Button("+1", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, 1)), queue.MaxSize < HumanoidQueue.MaxMaxSize).WithTooltip(OriginalTooltips.Get(host, 76, "Make the queue longer")),
            WindowCell.Button("+5", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, 5)), queue.MaxSize < HumanoidQueue.MaxMaxSize).WithTooltip(OriginalTooltips.Get(host, 76, "Make the queue longer")));
        if (owner is Room withStaff && simulation.FindEntity(withStaff.StaffId) is Humanoid { Profile: not null } staff)
        {
            yield return WindowRow.Of("Staff", StaffLabels.Name(host, staff));
        }

        yield return WindowRow.Gap;
        if (queue.Count == 0)
        {
            yield return WindowCell.Line("Nobody is queueing.", dim: true);
            yield break;
        }

        var others = owner is Room same
            ? simulation.Entities.OfType<Room>().Where(other => other.Id != same.Id && other.IsActive && other.RoomType == same.RoomType && RoomBuilder.IsOwnedBy(simulation.Map, other, host.Player)).ToList()
            : [];
        for (var i = 0; i < queue.Count; i++)
        {
            var id = queue.Members[i];
            if (simulation.FindEntity(id) is not Humanoid { Behaviour: PatientBehaviour patient } member)
            {
                continue;
            }

            var position = i;
            var cells = new List<WindowCell>
            {
                WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{i + 1}.")),
                WindowCell.Button(patient.IsDiagnosed ? host.Texts?.DiseaseName(patient.DiseaseId) ?? patient.DiseaseId : string.Create(CultureInfo.InvariantCulture, $"Patient {member.Id}"), () => host.Open(new PatientWindow(member.Id))).WithTooltip(OriginalTooltips.Get(host, 80, "Open the patient's window")),
                WindowCell.Number(StaffLabels.Percent(patient.Health)),
                WindowCell.Button("Front", () => host.Submit(new MoveInQueueCommand(id, 0)), position > 0).WithTooltip(OriginalTooltips.Get(host, 77, "Move the patient to the front of the queue")),
                WindowCell.Button("Back", () => host.Submit(new MoveInQueueCommand(id, queue.Count - 1)), position < queue.Count - 1).WithTooltip(OriginalTooltips.Get(host, 78, "Move the patient to the end of the queue")),
            };
            foreach (var other in others.Take(2))
            {
                cells.Add(WindowCell.Button(string.Create(CultureInfo.InvariantCulture, $"To room {other.Id}"), () => host.Submit(new SendPatientToRoomCommand(id, other.Id))));
            }

            yield return new WindowRow(cells);
        }
    }
}
