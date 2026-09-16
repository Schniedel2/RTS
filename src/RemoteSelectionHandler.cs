using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>Client-side, non-authoritative selection presence of other players.</summary>
public sealed class RemoteSelectionHandler
{
    private readonly Dictionary<Guid, HashSet<Guid>> _unitIdsByPlayer = [];
    public IReadOnlyDictionary<Guid, HashSet<Guid>> UnitIdsByPlayer => _unitIdsByPlayer;

    public void SetSelection(Guid playerId, IEnumerable<Guid> unitIds)
    {
        _unitIdsByPlayer[playerId] = [.. unitIds];
    }

    public void RemovePlayer(Guid playerId) => _unitIdsByPlayer.Remove(playerId);
}
