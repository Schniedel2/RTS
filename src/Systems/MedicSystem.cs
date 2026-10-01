using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using RTS.Network;

namespace RTS;

public delegate void MedicMovementPlanner(Medic medic, Vector3 target, Func<bool> valid,
    Action<NetworkMessage?> completed, Action cancelled);

/// <summary>Host-owned healing and patient pursuit. All mutations and callbacks run on the game thread.</summary>
public sealed class MedicSystem
{
    private readonly GameWorld _world;
    private readonly ArmyHandler _armies;
    private readonly Guid _peerId;
    private readonly Func<NetworkMessage, Task> _publish;
    private readonly Func<NetworkMessage, NetworkMessage> _createStop;
    private readonly MedicMovementPlanner _planMovement;
    private readonly Func<long> _generation;
    private readonly Func<Guid, long> _version;
    private readonly Dictionary<Guid, double> _nextPatientHealTimes = [];
    private readonly Dictionary<Guid, MedicJob> _medicJobs = [];
    private readonly HashSet<Guid> _medicsHoldingPosition = [];
    private double _hostTime;
    public MedicSystem(GameWorld world, ArmyHandler armies, Guid peerId,
        Func<NetworkMessage, Task> publish, Func<NetworkMessage, NetworkMessage> createStop,
        MedicMovementPlanner planMovement, Func<long> generation, Func<Guid, long> version)
    {
        _world = world;
        _armies = armies;
        _peerId = peerId;
        _publish = publish;
        _createStop = createStop;
        _planMovement = planMovement;
        _generation = generation;
        _version = version;
    }
    public int ActiveJobCount => _medicJobs.Count;
    public void Reset()
    {
        _medicJobs.Clear();
        _medicsHoldingPosition.Clear();
        _nextPatientHealTimes.Clear();
    }
    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b) =>
        Vector2.DistanceSquared(new(a.X, a.Z), new(b.X, b.Z));

    private sealed class MedicJob(Guid patientId)
    {
        public Guid PatientId { get; } = patientId;
        public bool MoveIssued { get; set; }
        public bool Planning { get; set; }
    }

    public void HandleCommandOverride(NetworkMessage request, IEnumerable<Guid> expandedIds)
    {
        bool stop = request.Type == NetworkMessageType.StopRequest ||
            request.Type == NetworkMessageType.UnitActionRequest && request.UnitActionType == UnitActionType.Stop;
        bool overrideMovement = stop || request.Type is NetworkMessageType.GotoRequest or
            NetworkMessageType.FollowRequest or NetworkMessageType.AttackTargetRequest or
            NetworkMessageType.AttackGroundRequest or NetworkMessageType.MoveAwayRequest;
        if (!overrideMovement)
            return;

        IEnumerable<Guid> ids = expandedIds;
        if (request.Type == NetworkMessageType.MoveAwayRequest && request.UnitId is Guid singleId)
            ids = ids.Append(singleId);
        foreach (Guid id in ids.Distinct())
        {
            if (_world.Units.FindById(id) is not Medic medic ||
                !_armies.CanControl(request.SenderId, medic.ArmyId))
                continue;
            _medicJobs.Remove(id);
            if (stop)
                _medicsHoldingPosition.Add(id);
            else
                _medicsHoldingPosition.Remove(id);
        }
    }

    public async Task UpdateAsync(double hostTime)
    {
        _hostTime = hostTime;
        Medic[] medics = _world.Units.Units.OfType<Medic>()
            .Where(medic => !medic.IsDying && !medic.IsEmbarked)
            .ToArray();
        HashSet<Guid> activeMedicIds = medics.Select(medic => medic.UnitId).ToHashSet();
        foreach (Guid id in _medicJobs.Keys.Where(id => !activeMedicIds.Contains(id)).ToArray())
            _medicJobs.Remove(id);
        _medicsHoldingPosition.RemoveWhere(id => !activeMedicIds.Contains(id));

        Soldier[] soldiers = _world.Units.Units.OfType<Soldier>()
            .Where(patient => !patient.IsDying && !patient.IsEmbarked && patient.HitPoints > 0.0f)
            .ToArray();
        HashSet<Guid> patientIds = soldiers.Select(patient => patient.UnitId).ToHashSet();
        foreach (Guid id in _nextPatientHealTimes.Keys.Where(id => !patientIds.Contains(id)).ToArray())
            _nextPatientHealTimes.Remove(id);
        float healingRadius = Medic.HealingRadiusInCells * _world.GameGrid.CellSize;
        float healingRadiusSquared = healingRadius * healingRadius;
        float searchRadius = Medic.SearchRadiusInCells * _world.GameGrid.CellSize;
        float searchRadiusSquared = searchRadius * searchRadius;

        foreach (Medic medic in medics)
        {
            SquadLeader? leader = medic.SquadLeaderId is Guid leaderId &&
                _world.Units.FindById(leaderId) is SquadLeader candidateLeader &&
                !candidateLeader.IsDying && !candidateLeader.IsEmbarked && candidateLeader.ArmyId == medic.ArmyId
                    ? candidateLeader
                    : null;
            if (leader is not null)
            {
                if (_medicJobs.Remove(medic.UnitId, out MedicJob? independentJob) &&
                    independentJob.MoveIssued && medic.CurrentCommand is not null)
                    await _publish(_createStop(NetworkCommands.CreateStopRequest(
                        GetMedicCommandSender(medic), [medic.UnitId])));
                Soldier? squadPatient = SelectMedicPatient(medic, soldiers, healingRadiusSquared,
                    patient => patient.UnitId == leader.UnitId || patient.SquadLeaderId == leader.UnitId);
                if (squadPatient is not null)
                    await HealPatientAsync(medic, squadPatient);
                continue;
            }

            bool holdPosition = _medicsHoldingPosition.Contains(medic.UnitId);
            float acquisitionRadiusSquared = holdPosition ? healingRadiusSquared : searchRadiusSquared;
            MedicJob? job = _medicJobs.GetValueOrDefault(medic.UnitId);
            if (job?.Planning == true) continue;
            Soldier? patient = job is null ? null : soldiers.FirstOrDefault(candidate => candidate.UnitId == job.PatientId);
            if (!IsValidMedicPatient(medic, patient, acquisitionRadiusSquared))
            {
                bool cancelAutomaticMove = job?.MoveIssued == true && medic.CurrentCommand is not null;
                _medicJobs.Remove(medic.UnitId);
                job = null;
                patient = null;
                if (cancelAutomaticMove)
                {
                    NetworkMessage stop = _createStop(NetworkCommands.CreateStopRequest(
                        GetMedicCommandSender(medic), [medic.UnitId]));
                    await _publish(stop);
                    continue;
                }
            }

            if (job is null)
            {
                if (medic.CurrentCommand is not null)
                    continue;
                patient = SelectMedicPatient(medic, soldiers, acquisitionRadiusSquared, _ => true);
                if (patient is null)
                    continue;
                job = new MedicJob(patient.UnitId);
                _medicJobs[medic.UnitId] = job;
            }

            float distanceSquared = HorizontalDistanceSquared(medic.Position, patient!.Position);
            if (distanceSquared <= healingRadiusSquared)
            {
                if (job.MoveIssued && medic.CurrentCommand is not null)
                {
                    NetworkMessage stop = _createStop(NetworkCommands.CreateStopRequest(
                        GetMedicCommandSender(medic), [medic.UnitId]));
                    await _publish(stop);
                }
                job.MoveIssued = false;
                await HealPatientAsync(medic, patient);
                continue;
            }

            if (holdPosition)
            {
                _medicJobs.Remove(medic.UnitId);
                continue;
            }
            if (medic.CurrentCommand is not null && !job.MoveIssued)
            {
                _medicJobs.Remove(medic.UnitId);
                continue;
            }
            if (!job.MoveIssued || medic.CurrentCommand is null)
            {
                MedicJob pending = job;
                int pathRequest = medic._pathRequestId;
                long generation = _generation();
                long version = _version(medic.UnitId);
                Guid? squad = medic.SquadLeaderId;
                bool Valid() => generation == _generation() &&
                    _medicJobs.TryGetValue(medic.UnitId, out MedicJob? current) && ReferenceEquals(current, pending) &&
                    !medic.IsDying && !medic.IsEmbarked &&
                    ReferenceEquals(_world.Units.FindById(medic.UnitId), medic) &&
                    version == _version(medic.UnitId) && medic._pathRequestId == pathRequest &&
                    medic.SquadLeaderId == squad &&
                    _world.Units.FindById(pending.PatientId) is Soldier currentPatient &&
                    IsValidMedicPatient(medic, currentPatient, acquisitionRadiusSquared);
                job.Planning = true;
                _planMovement(medic, patient.Position, Valid, move =>
                {
                    if (!Valid()) return;
                    pending.Planning = false;
                    if (move is null || move.Routes?.All(route => route.Cells.Length == 0) == true)
                    { _medicJobs.Remove(medic.UnitId); return; }
                    _publish(move).GetAwaiter().GetResult();
                    pending.MoveIssued = true;
                }, () => pending.Planning = false);
            }
        }
    }

    private static bool IsValidMedicPatient(Medic medic, Soldier? patient, float radiusSquared) =>
        patient is not null && !patient.IsDying && !patient.IsEmbarked && patient.HitPoints > 0.0f &&
        patient.HitPoints < patient.MaxHitPoints && patient.ArmyId == medic.ArmyId &&
        HorizontalDistanceSquared(medic.Position, patient.Position) <= radiusSquared;

    private static Soldier? SelectMedicPatient(
        Medic medic, IEnumerable<Soldier> soldiers, float radiusSquared, Func<Soldier, bool> filter) =>
        soldiers.Where(patient => filter(patient) && IsValidMedicPatient(medic, patient, radiusSquared))
            .OrderBy(patient => patient.HitPoints / Math.Max(1.0f, patient.MaxHitPoints))
            .ThenBy(patient => HorizontalDistanceSquared(medic.Position, patient.Position))
            .ThenBy(patient => patient.UnitId)
            .FirstOrDefault();

    private Guid GetMedicCommandSender(Medic medic) => medic.ArmyId is Guid armyId &&
        _armies.Find(armyId) is Army army && army.OwnerPlayerIds.Count > 0
            ? army.OwnerPlayerIds.OrderBy(id => id).First()
            : _peerId;

    private async Task HealPatientAsync(Medic medic, Soldier patient)
    {
        if (_nextPatientHealTimes.GetValueOrDefault(patient.UnitId) > _hostTime)
            return;
        float healed = patient.Heal(Medic.HealAmountPerPulse);
        if (healed <= 0.0f)
            return;
        _nextPatientHealTimes[patient.UnitId] = _hostTime + Medic.HealPulseSeconds;
        await _publish(NetworkCommands.CreateUnitHitCommand(
            _peerId, patient.UnitId, medic.UnitId, patient.Position, -healed, patient.HitPoints));
    }

}
