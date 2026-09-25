using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum PerkType
{
    DetailedHealth,
    Home
}

public enum PerkLifetime
{
    Permanent,
    WhileProviderExists,
    WhileProviderOperational
}

public enum PerkScope
{
    Global,
    Radius
}

public sealed record PerkGrant(
    PerkType Perk,
    PerkLifetime Lifetime,
    PerkScope Scope = PerkScope.Global,
    Vector3 Position = default,
    float Radius = 0.0f)
{
    public bool AppliesAt(Vector3 position) => Scope == PerkScope.Global ||
        Radius > 0.0f && Vector3.DistanceSquared(Position, position) <= Radius * Radius;
}

/// <summary>
/// Implemented by buildings or units which contribute temporary army perks.
/// Returning no grants immediately disables this provider, which also supports
/// crew-dependent and operational-state-dependent perks.
/// </summary>
public interface IPerkProvider
{
    IReadOnlyList<PerkGrant> GetProvidedPerks();
}

/// <summary>Source-aware active perks for one army.</summary>
public sealed class ArmyPerkState
{
    private readonly Dictionary<Guid, IReadOnlyList<PerkGrant>> _sources = [];

    public IReadOnlyCollection<PerkType> ActivePerks => _sources.Values
        .SelectMany(grants => grants)
        .Select(grant => grant.Perk)
        .Distinct()
        .ToArray();

    public void SetSource(Guid sourceId, IEnumerable<PerkGrant> grants)
    {
        PerkGrant[] active = grants.ToArray();
        if (active.Length == 0)
            RemoveSource(sourceId);
        else
            _sources[sourceId] = active;
    }

    public void GrantPermanent(PerkType perk, Guid researchId) =>
        SetSource(researchId, [new PerkGrant(perk, PerkLifetime.Permanent)]);

    public void RemoveSource(Guid sourceId, bool includePermanent = false)
    {
        if (!_sources.TryGetValue(sourceId, out IReadOnlyList<PerkGrant>? grants))
            return;
        if (includePermanent)
        {
            _sources.Remove(sourceId);
            return;
        }

        PerkGrant[] permanent = grants
            .Where(grant => grant.Lifetime == PerkLifetime.Permanent)
            .ToArray();
        if (permanent.Length == 0)
            _sources.Remove(sourceId);
        else
            _sources[sourceId] = permanent;
    }

    public bool Has(PerkType perk) =>
        _sources.Values.SelectMany(grants => grants).Any(grant => grant.Perk == perk);

    public bool HasAt(PerkType perk, Vector3 position) =>
        _sources.Values.SelectMany(grants => grants)
            .Any(grant => grant.Perk == perk && grant.AppliesAt(position));

    public bool TryGetNearestSourcePosition(PerkType perk, Vector3 referencePosition,
        out Vector3 position)
    {
        (Guid SourceId, PerkGrant Grant)? nearest = _sources
            .SelectMany(source => source.Value
                .Where(grant => grant.Perk == perk)
                .Select(grant => (SourceId: source.Key, Grant: grant)))
            .OrderBy(candidate => Vector3.DistanceSquared(
                candidate.Grant.Position, referencePosition))
            .ThenBy(candidate => candidate.SourceId)
            .Select(candidate => ((Guid SourceId, PerkGrant Grant)?)candidate)
            .FirstOrDefault();
        position = nearest?.Grant.Position ?? default;
        return nearest is not null;
    }

    public void Clear() => _sources.Clear();

    internal void CopyPermanentFrom(ArmyPerkState source)
    {
        foreach ((Guid sourceId, IReadOnlyList<PerkGrant> grants) in source._sources)
        {
            PerkGrant[] permanent = grants.Where(grant => grant.Lifetime == PerkLifetime.Permanent).ToArray();
            if (permanent.Length > 0)
                _sources[sourceId] = permanent;
        }
    }
}
