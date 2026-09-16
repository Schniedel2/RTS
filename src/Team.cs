using System;
using System.Collections.Generic;

namespace RTS;

/// <summary>Diplomatic team membership. Teams never imply army control.</summary>
public sealed class Team
{
    public Guid Id { get; }
    public string Name { get; set; }
    public HashSet<Guid> PlayerIds { get; } = [];

    public Team(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}

public sealed class TeamHandler
{
    private readonly Dictionary<int, Team> _teams = [];

    public Team Ensure(int teamId)
    {
        if (!_teams.TryGetValue(teamId, out Team? team))
        {
            // Existing network messages use int TeamId. Keep that compatible
            // while Team itself can still have a stable GUID identity later.
            team = new Team(GuidUtility.FromInt(teamId), $"Team {teamId}");
            _teams.Add(teamId, team);
        }
        return team;
    }

    public void UpdateMembership(Guid playerId, int previousTeamId, int teamId)
    {
        if (_teams.TryGetValue(previousTeamId, out Team? previous))
            previous.PlayerIds.Remove(playerId);
        Ensure(teamId).PlayerIds.Add(playerId);
    }

    public void RemovePlayer(Guid playerId)
    {
        foreach (Team team in _teams.Values)
            team.PlayerIds.Remove(playerId);
    }
}

internal static class GuidUtility
{
    public static Guid FromInt(int value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, value);
        return new Guid(bytes);
    }
}
