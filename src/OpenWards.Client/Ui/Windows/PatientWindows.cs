using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Maps;
using OpenWards.Sim;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Entities;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;

namespace OpenWards.Client.Ui.Windows;

/// <summary>
/// The patient window (CorsixTH's <c>dialogs/patient.lua</c>), opened by clicking a patient: disease and how far the
/// diagnosis is, health with its last days, warmth, thirst, need for the toilet and happiness, what they owe, where
/// they have been; sending them home (after a confirmation, as the original asks), guessing the cure, vaccinating them
/// in an epidemic's cover-up and showing their queue.
/// </summary>
internal sealed class PatientWindow(int patientId) : UiWindow
{
    private bool _confirmHome;

    /// <summary>The hospital the referral button sends the patient to (the network game's Patient Redirection Panel).</summary>
    private int _referTo = -1;

    /// <summary>Whether the window shows the referral button under the original's panel: a game of several hospitals.</summary>
    private bool _referrals;

    private const int ReferHeight = 26;

    private readonly ResizablePanels _referPanels = new();

    /// <summary>The next other hospital after <paramref name="from"/>, going round; -1 with none.</summary>
    private static int NextHospital(IWindowHost host, int from, int step)
    {
        var count = host.Simulation.Hospitals.Count;
        for (var i = 1; i <= count; i++)
        {
            var next = (((from < 0 ? host.Player : from) + (step * i)) % count + count) % count;
            if (next != host.Player)
            {
                return next;
            }
        }

        return -1;
    }

    /// <summary>The referral button, CorsixTH style, under the original's panel: left sends, right picks another hospital.</summary>
    private List<DialogPanel> ReferPanels(IWindowHost host, Humanoid humanoid)
    {
        if (_referTo < 0 || _referTo >= host.Simulation.Hospitals.Count || _referTo == host.Player)
        {
            _referTo = NextHospital(host, -1, 1);
        }

        var label = UiText.Format("openwards.referral.refer", "Refer to %1%", ProgressReportScreen.HospitalName(host, _referTo));
        return
        [
            DialogPanel.Button(new Rectangle(0, PatientScreen.Size.Y + 2, PatientScreen.Size.X, ReferHeight - 4), label, step =>
            {
                if (step < 0)
                {
                    _referTo = NextHospital(host, _referTo, 1);
                }
                else
                {
                    host.Submit(new ReferPatientCommand(humanoid.Id, _referTo));
                }
            }, UiText.Get("openwards.referral.tooltip", "Send the patient to another player's hospital; right click picks another hospital")),
        ];
    }

    public int PatientId { get; } = patientId;

    private PatientScreen? _screen;

    private PatientScreen Screen => _screen ??= new PatientScreen(PatientId);

    /// <summary>The original's look (<see cref="PatientScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host)
    {
        _referrals = host.Simulation.Hospitals.Count > 1 && ResizablePanels.IsAvailable(host);
        return Patient(host) is not null && PatientScreen.IsAvailable(host);
    }

    public override Point FullscreenSize => _referrals ? new Point(PatientScreen.Size.X, PatientScreen.Size.Y + ReferHeight) : PatientScreen.Size;

    /// <summary>UIPatient:setDefaultPosition(-20, 30).</summary>
    public override Vector2? FullscreenPosition => new(-20, 30);

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse)
    {
        if (Patient(host) is { Behaviour: PatientBehaviour patient } humanoid)
        {
            Screen.Draw(batch, host, origin, mouse, humanoid, patient);
            if (_referrals)
            {
                var look = host.Graphics!.Resizable!;
                var font = ResizablePanels.Font(host);
                foreach (var panel in ReferPanels(host, humanoid))
                {
                    var area = new Rectangle(origin + panel.Area.Location, panel.Area.Size);
                    var held = mouse.LeftButton == ButtonState.Pressed && area.Contains(mouse.X, mouse.Y);
                    look.DrawBevel(batch, area, panel.Colour, held, panel.Label, font);
                }
            }
        }
    }

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous)
    {
        if (Patient(host) is { Behaviour: PatientBehaviour patient } humanoid)
        {
            if (_referrals && local.Y >= PatientScreen.Size.Y)
            {
                _referPanels.Mouse(host, ReferPanels(host, humanoid), local, mouse, previous);
                return;
            }

            Screen.Mouse(host, this, local, mouse, previous, humanoid, patient);
        }
    }

    public override string? FullscreenTooltip(IWindowHost host, Point local) =>
        Patient(host) is { Behaviour: PatientBehaviour patient } humanoid
            ? (_referrals && local.Y >= PatientScreen.Size.Y ? ResizablePanels.Tooltip(ReferPanels(host, humanoid), local) : Screen.Tooltip(host, local, patient))
            : null;

    /// <summary>ingame_patient_gohome (H): sends the patient home, as the button does.</summary>
    public override bool TakesKey(string action) => action == "ingame_patient_gohome";

    public override void FullscreenKey(IWindowHost host, string action)
    {
        if (Patient(host) is { } humanoid)
        {
            PatientScreen.GoHome(host, this, humanoid);
        }
    }
    public override string Title => UiText.Get("openwards.patient.caption", "Patient");

    public override bool IsSameAs(UiWindow other) => other is PatientWindow window && window.PatientId == PatientId;

    public override bool IsDone(IWindowHost host) => Patient(host) is null;

    public override IEnumerable<WindowRow> Rows(IWindowHost host)
    {
        if (Patient(host) is not { } humanoid || humanoid.Behaviour is not PatientBehaviour patient)
        {
            yield break;
        }

        var simulation = host.Simulation;
        var disease = patient.IsDiagnosed ? host.Texts?.DiseaseName(patient.DiseaseId) ?? patient.DiseaseId : UiText.Get("openwards.patient.not_diagnosed", "Not diagnosed yet");
        yield return WindowRow.Of(UiText.Get("openwards.casebook.disease", "Disease"), new WindowCell(disease, Highlight: patient.IsDiagnosed));
        yield return WindowRow.Of(UiText.Get("openwards.policy.diagnosis", "Diagnosis"), WindowCell.Number(string.Create(CultureInfo.InvariantCulture, $"{Math.Min(100, patient.DiagnosisProgress / 10)} %")));
        yield return WindowRow.Of(UiText.Get("openwards.patient.health", "Health"), WindowCell.Number(StaffLabels.Percent(patient.Health)), HealthGraph(patient));
        yield return WindowRow.Of(UiText.Get("openwards.patient.warmth", "Warmth"), WindowCell.Number(StaffLabels.Percent(humanoid.Warmth)));
        yield return WindowRow.Of(UiText.Get("openwards.patient.thirst", "Thirst"), WindowCell.Number(StaffLabels.Percent(patient.Thirst)));
        yield return WindowRow.Of(UiText.Get("openwards.patient.toilet", "Toilet"), WindowCell.Number(StaffLabels.Percent(patient.ToiletNeed)));
        yield return WindowRow.Of(UiText.Get("tooltip.handyman_window.happiness", "Happiness"), WindowCell.Number(StaffLabels.Percent(humanoid.Happiness)), patient.IsSad ? UiText.Get("openwards.patient.sad", "Sad") : string.Empty);
        if (patient.PayAmount > 0)
        {
            yield return WindowRow.Of(UiText.Get("openwards.patient.to_pay", "To pay"), WindowCell.Number(MoneyText.Format(patient.PayAmount)));
        }

        if (patient.IsInfected)
        {
            yield return WindowRow.Of(UiText.Get("openwards.patient.contagious", "Contagious"), patient.IsVaccinated ? UiText.Get("openwards.patient.vaccinated", "Vaccinated") : patient.IsMarkedForVaccination ? UiText.Get("openwards.patient.waiting_for_nurse", "Waiting for a nurse") : UiText.Get("openwards.patient.not_vaccinated", "Not vaccinated"));
        }

        if (patient.TreatmentHistory.Count > 0)
        {
            yield return WindowRow.Of(UiText.Get("openwards.patient.been_to", "Been to"), string.Join(", ", patient.TreatmentHistory.Select(entry => History(simulation, host, entry))));
        }

        if (patient.Question != PatientQuestion.None)
        {
            yield return WindowCell.Line(patient.Question == PatientQuestion.NoDiagnosisRoom ? UiText.Get("openwards.patient.asks_no_diagnosis", "Asks what to do: no diagnosis room left") : UiText.Get("openwards.patient.asks_no_treatment", "Asks what to do: no room to treat them"), highlight: true);
        }

        yield return WindowRow.Gap;
        var hospital = simulation.Hospitals[host.Player];
        var canGuess = !patient.IsDiagnosed && patient.Disease is { } known && hospital.PeekCase(known, simulation.Rules.Diseases).Discovered;
        var coverUp = hospital.Epidemics.Current is { IsCoverUp: true, IsWatchRunning: true };
        yield return WindowRow.Of(
            (_confirmHome
                ? WindowCell.Button(UiText.Get("openwards.patient.confirm_send_home", "Sure? Send home"), () => host.Submit(new SendPatientHomeCommand(humanoid.Id)))
                : WindowCell.Button(UiText.Get("openwards.patient.send_home", "Send home"), () => _confirmHome = true)).WithTooltip(OriginalTooltips.Get(host, 94, "Send the patient home")),
            WindowCell.Button(UiText.Get("openwards.patient.guess_cure", "Guess cure"), () => host.Submit(new GuessPatientCureCommand(humanoid.Id)), canGuess).WithTooltip(OriginalTooltips.Get(host, 96, "Stop diagnosing and guess the cure")),
            WindowCell.Button(UiText.Get("openwards.patient.vaccinate", "Vaccinate"), () => host.Submit(new VaccinateCommand(humanoid.Id)), coverUp && patient.IsInfected && !patient.IsVaccinated && !patient.IsMarkedForVaccination).WithTooltip(UiText.Get("openwards.patient.vaccinate_tooltip", "Have a nurse vaccinate this patient during the epidemic")),
            WindowCell.Button(UiText.Get("openwards.staff.show", "Show"), () => host.CenterOn(humanoid.Tile)).WithTooltip(OriginalTooltips.Get(host, 95, "Show the patient in the hospital")),
            WindowCell.Button(UiText.Get("openwards.queue.caption", "Queue"), () => host.Open(new QueueWindow(patient.TargetId)), QueueWindow.QueueOf(simulation, patient.TargetId) is not null).WithTooltip(OriginalTooltips.Get(host, 97, "Show the queue the patient is in")));
        if (simulation.Hospitals.Count > 1)
        {
            // The network game's Patient Redirection Panel: a button for each other hospital.
            yield return WindowRow.Of([.. Enumerable.Range(0, simulation.Hospitals.Count).Where(other => other != host.Player).Select(other =>
                WindowCell.Button(UiText.Format("openwards.referral.refer", "Refer to %1%", ProgressReportScreen.HospitalName(host, other)), () => host.Submit(new ReferPatientCommand(humanoid.Id, other)))
                    .WithTooltip(UiText.Get("openwards.referral.tooltip", "Send the patient to another player's hospital; right click picks another hospital")))]);
        }
    }

    private static string History(Simulation simulation, IWindowHost host, TreatmentHistoryEntry entry) => entry.Kind switch
    {
        TreatmentHistoryKind.Room => UiText.RoomName(simulation.Content, entry.Id),
        TreatmentHistoryKind.Diagnosis => UiText.Format("openwards.patient.diagnosed", "diagnosed %1%", host.Texts?.DiseaseName(entry.Id) ?? entry.Id),
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

    public override string Title => UiText.Get("openwards.queue.caption", "Queue");

    private QueueScreen? _screen;

    private QueueScreen Screen => _screen ??= new QueueScreen(OwnerId);

    /// <summary>The original's look (<see cref="QueueScreen"/>) where its graphics are there; else the table below.</summary>
    public override bool IsFullscreen(IWindowHost host) => QueueScreen.IsAvailable(host);

    public override Point FullscreenSize => QueueScreen.Size;

    public override bool CapturesMouse => _screen is { IsDragging: true };

    public override void DrawFullscreen(SpriteBatch batch, IWindowHost host, Point origin, MouseState mouse) => Screen.Draw(batch, host, origin, mouse);

    public override void FullscreenMouse(IWindowHost host, Point local, MouseState mouse, MouseState previous) => Screen.Mouse(host, this, local, mouse, previous);

    public override string? FullscreenTooltip(IWindowHost host, Point local) => QueueScreen.Tooltip(host, local);

    /// <summary>
    /// <c>UIQueue:onMouseMove</c>: while a patient is dragged, the default cursor over the dialog's 605 by 120 pixels and
    /// the queue's drag cursor outside them.
    /// </summary>
    public override GameCursor? FullscreenCursor(IWindowHost host, Point local) =>
        _screen is { IsDragging: true } && !(local.X > 0 && local.X < 605 && local.Y > 0 && local.Y < 120) ? GameCursor.QueueDrag : null;

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
        yield return WindowCell.Line(owner is Room room ? UiText.RoomName(simulation.Content, room.RoomType) : UiText.Get("object.reception_desk", "Reception desk"), highlight: true);
        yield return WindowRow.Of(
            UiText.Get("queue_window.num_in_queue", "Queue size"),
            WindowCell.Number(UiText.Format("openwards.machines.uses_of", "%1% of %2%", queue.Count, queue.MaxSize)),
            WindowCell.Button("-5", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, -5)), queue.MaxSize > 0).WithTooltip(OriginalTooltips.Get(host, 75, "Make the queue shorter")),
            WindowCell.Button("-1", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, -1)), queue.MaxSize > 0).WithTooltip(OriginalTooltips.Get(host, 75, "Make the queue shorter")),
            WindowCell.Button("+1", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, 1)), queue.MaxSize < HumanoidQueue.MaxMaxSize).WithTooltip(OriginalTooltips.Get(host, 76, "Make the queue longer")),
            WindowCell.Button("+5", () => host.Submit(new ChangeQueueSizeCommand(OwnerId, 5)), queue.MaxSize < HumanoidQueue.MaxMaxSize).WithTooltip(OriginalTooltips.Get(host, 76, "Make the queue longer")));
        if (owner is Room withStaff && simulation.FindEntity(withStaff.StaffId) is Humanoid { Profile: not null } staff)
        {
            yield return WindowRow.Of(UiText.Get("openwards.menu.staff", "Staff"), StaffLabels.Name(host, staff));
        }

        yield return WindowRow.Gap;
        if (queue.Count == 0)
        {
            yield return WindowCell.Line(UiText.Get("openwards.patient.nobody_queueing", "Nobody is queueing."), dim: true);
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
                WindowCell.Button(patient.IsDiagnosed ? host.Texts?.DiseaseName(patient.DiseaseId) ?? patient.DiseaseId : UiText.Format("openwards.patient.number", "Patient %1%", member.Id), () => host.Open(new PatientWindow(member.Id))).WithTooltip(OriginalTooltips.Get(host, 80, "Open the patient's window")),
                WindowCell.Number(StaffLabels.Percent(patient.Health)),
                WindowCell.Button(UiText.Get("openwards.patient.front", "Front"), () => host.Submit(new MoveInQueueCommand(id, 0)), position > 0).WithTooltip(OriginalTooltips.Get(host, 77, "Move the patient to the front of the queue")),
                WindowCell.Button(UiText.Get("openwards.patient.back", "Back"), () => host.Submit(new MoveInQueueCommand(id, queue.Count - 1)), position < queue.Count - 1).WithTooltip(OriginalTooltips.Get(host, 78, "Move the patient to the end of the queue")),
            };
            foreach (var other in others.Take(2))
            {
                cells.Add(WindowCell.Button(UiText.Format("openwards.patient.to_room", "To room %1%", other.Id), () => host.Submit(new SendPatientToRoomCommand(id, other.Id))));
            }

            yield return new WindowRow(cells);
        }
    }
}
