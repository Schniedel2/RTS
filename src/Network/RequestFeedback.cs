using System;
using System.Collections.Generic;
using System.Linq;
namespace RTS.Network;

public sealed partial class NetworkHandler
{
    private readonly Dictionary<Guid, LocalRequestReceipt> _requestReceipts = [];
    private readonly Dictionary<(Guid Actor, Guid Id), NetworkMessage?> _hostRequestFeedback = [];
    private readonly Dictionary<Guid, double> _requestPollTimes = [];
    private readonly Dictionary<(Guid Actor, Guid Id), NetworkMessage> _acceptedRequests = [];
    internal IEnumerable<NetworkMessage> AcceptedRequests => _acceptedRequests.Values.ToArray();
    private long _requestSession = -1;
    private void EnsureRequestSession()
    {
        if (_requestSession == SessionGeneration) return;
        foreach (var receipt in _requestReceipts.Values)
        {
            if (receipt.Result.IsTerminal) continue;
            receipt.State = LocalRequestState.Abandoned;
            receipt.Result.Set(AIOrderStatus.Failed, AIOrderFailure.SessionChanged, "Session changed.");
        }
        _localRequests.Clear(); _pendingAIPurchases.Clear(); _localRejections.Clear();
        _requestReceipts.Clear(); _acceptedRequests.Clear(); _hostRequestFeedback.Clear(); _requestPollTimes.Clear();
        _requestSession = SessionGeneration;
    }
    // Retain deduplication tombstones for the entire session. At capacity reject new
    // work rather than evicting a purchase and risking a second execution.
    internal bool AdmitRequestId(NetworkMessage request)
    {
        EnsureRequestSession();
        if (request.RequestId is not Guid id) return true; // older internal callers
        var key = (request.SenderId, id);
        if (_hostRequestFeedback.ContainsKey(key)) { ReplayRequestFeedback(request); return false; }
        if (_hostRequestFeedback.Count >= 32768)
        { PublishRequestFeedback(request, LocalRequestState.Rejected, "Request history capacity reached.", AIOrderFailure.Validation); return false; }
        _hostRequestFeedback.Add(key, null);
        return true;
    }
    internal bool ReplayKnownRequest(NetworkMessage request)
    {
        EnsureRequestSession();
        if (request.RequestId is not Guid id || !_hostRequestFeedback.ContainsKey((request.SenderId, id))) return false;
        ReplayRequestFeedback(request);
        return true;
    }
    internal void ReplayRequestFeedback(NetworkMessage request)
    {
        EnsureRequestSession();
        if (request.RequestId is Guid id && _hostRequestFeedback.TryGetValue((request.SenderId, id), out var feedback) && feedback is not null)
            SendRequestFeedback(feedback);
    }
    private void SendRequestFeedback(NetworkMessage feedback)
    {
        if (_requestReceipts.ContainsKey(feedback.RequestFeedback!.RequestId)) ApplyRequestFeedback(feedback);
        _ = BroadcastAsync(feedback);
    }
    private void PublishRequestFeedback(NetworkMessage request, LocalRequestState state, string? reason, AIOrderFailure failure)
    {
        if (!IsHost || request.RequestId is not Guid id) return;
        EnsureRequestSession();
        var key = (request.SenderId, id);
        // An accepted/rejected decision is final; duplicates cannot change it.
        if (_hostRequestFeedback.TryGetValue(key, out var previous) && previous is not null) return;
        if (state == LocalRequestState.Accepted) _acceptedRequests[key] = request;
        var data = new RequestFeedback(id, request.AIControllerActorId ?? request.SenderId, request.RequestGeneration, state,
            state == LocalRequestState.Accepted ? AIOrderStatus.Accepted : AIOrderStatus.Rejected,
            state == LocalRequestState.Accepted ? AIOrderFailure.None : failure, reason,
            request.ProductionOrderId, request.Type == NetworkMessageType.BuildRequest ? request.UnitId : request.ConstructionSiteId);
        var message = new NetworkMessage(NetworkMessageType.RequestFeedbackCommand, LocalPeerId, TargetId: request.AIControllerActorId.HasValue ? request.ControllerPeerId : request.SenderId) { RequestFeedback = data };
        if (_hostRequestFeedback.ContainsKey(key) || _hostRequestFeedback.Count < 32768) _hostRequestFeedback[key] = message;
        SendRequestFeedback(message);
    }
    internal void ReportRequestExecution(RequestReceipt receipt, AIOrderStatus status, AIOrderFailure failure = AIOrderFailure.None, string? reason = null)
    {
        if (receipt.Generation != SessionGeneration) return;
        ReportRequestExecution(receipt.Request, status, failure, reason);
    }
    internal void ReportRequestExecution(NetworkMessage request, AIOrderStatus status, AIOrderFailure failure = AIOrderFailure.None, string? reason = null)
    {
        if (!IsHost || request.RequestId is not Guid id) return;
        var key = (request.SenderId, id);
        if (!_hostRequestFeedback.TryGetValue(key, out var previous) || previous?.RequestFeedback is not RequestFeedback data || data.State != LocalRequestState.Accepted) return;
        if (data.Status is AIOrderStatus.Completed or AIOrderStatus.Failed) return;
        if (data.Status == status && data.Failure == failure && data.Reason == reason) return;
        if (status is AIOrderStatus.Completed or AIOrderStatus.Failed) _acceptedRequests.Remove(key);
        var message = previous with { RequestFeedback = data with { Status = status, Failure = failure, Reason = reason } };
        _hostRequestFeedback[key] = message;
        SendRequestFeedback(message);
    }
    internal void ApplyRequestFeedback(NetworkMessage message)
    {
        EnsureRequestSession();
        if (message.RequestFeedback is not RequestFeedback feedback || !_requestReceipts.TryGetValue(feedback.RequestId, out var receipt) ||
            feedback.ActorId != receipt.Request.SenderId || feedback.Generation != receipt.Generation || receipt.Generation != SessionGeneration ||
            message.TargetId != (receipt.Request.AIControllerActorId.HasValue ? LocalPeerId : feedback.ActorId) || receipt.State == LocalRequestState.Abandoned) return;
        if (receipt.State == LocalRequestState.Rejected || receipt.Result.IsTerminal) return;
        if (receipt.State == LocalRequestState.Accepted && feedback.State != LocalRequestState.Accepted) return;
        receipt.State = feedback.State; receipt.Reason = feedback.Reason;
        // Acceptance never implies completion of a construction or production.
        if (receipt.Result.WasAccepted && feedback.Status == AIOrderStatus.Accepted) return;
        receipt.Result.Set(feedback.Status, feedback.Failure, feedback.Reason);
        if (feedback.State == LocalRequestState.Rejected) _localRejections[feedback.ActorId] = receipt;
        var key = LocalPurchaseKey(receipt.Request);
        if (_pendingAIPurchases.TryGetValue(key, out var pending) && ReferenceEquals(pending, receipt)) _pendingAIPurchases.Remove(key);
    }
    private void UpdateRequestFeedback()
    {
        EnsureRequestSession();
        if (_requestReceipts.Count > 1024)
            foreach (var receipt in _requestReceipts.Values.Where(r => r.Result.IsTerminal || r.State == LocalRequestState.Abandoned).Take(_requestReceipts.Count - 512).ToArray())
            { _requestReceipts.Remove(receipt.RequestId); _requestPollTimes.Remove(receipt.RequestId); }
        if (IsHost || !IsConnected) return;
        double now = _localClock.Elapsed.TotalSeconds;
        foreach (var receipt in _requestReceipts.Values.ToArray())
        {
            if (receipt.Result.IsTerminal || receipt.State == LocalRequestState.Abandoned) continue;
            if (now - _requestPollTimes.GetValueOrDefault(receipt.RequestId) < 5) continue;
            _requestPollTimes[receipt.RequestId] = now;
            // Re-send the SAME id. Host replay, or original execution if it never
            // arrived; never turn an uncertain timeout into a fresh purchase.
            _ = SendToHostAsync(receipt.Request);
        }
    }
}
