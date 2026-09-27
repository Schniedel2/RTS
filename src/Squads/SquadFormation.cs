using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public enum FormationType
{
    Line,
    Column,
    Wedge
}

public static class SquadFormation
{
    public const int MaximumMembers = 10;
    public const float AssembleRadiusInCells = 12.0f;

    public static IReadOnlyDictionary<Guid, Vector2> CreateAssignments(
        SquadLeader leader,
        IEnumerable<Soldier> members,
        Vector2 anchor,
        float? facingDegrees,
        float cellSize)
    {
        Vector2 forward = ResolveForward(leader, anchor, facingDegrees);
        Vector2 right = new(forward.Y, -forward.X);
        float spacing = Math.Max(1.25f, cellSize * 1.5f);
        Dictionary<Guid, Vector2> result = [];

        Soldier[] active = members
            .Where(member => member != leader && !member.IsDying && !member.IsEmbarked)
            .OrderBy(member => member.UnitId)
            .ToArray();
        AssignRow(active.OfType<Gunner>().Cast<Soldier>().ToArray(), spacing, anchor,
            forward, right, spacing, result);
        AssignRow(active.OfType<RakZero>().Cast<Soldier>().ToArray(), spacing, anchor,
            forward, right, 0.0f, result);
        AssignRow(active.Where(member => member is not Gunner and not RakZero).ToArray(), spacing,
            anchor, forward, right, -spacing, result);
        result[leader.UnitId] = anchor - forward * spacing * 2.0f;
        return result;
    }

    public static float ResolveFacingDegrees(SquadLeader leader, Vector2 anchor)
    {
        Vector2 direction = anchor - new Vector2(leader.Position.X, leader.Position.Z);
        if (direction.LengthSquared() <= 0.01f)
            direction = new Vector2(leader.Transform.Forward.X, leader.Transform.Forward.Z);
        if (direction.LengthSquared() <= 0.01f)
            direction = -Vector2.UnitY;
        direction.Normalize();
        return MathHelper.ToDegrees(MathF.Atan2(-direction.X, -direction.Y));
    }

    private static Vector2 ResolveForward(SquadLeader leader, Vector2 anchor, float? facingDegrees)
    {
        float degrees = facingDegrees ?? ResolveFacingDegrees(leader, anchor);
        float radians = MathHelper.ToRadians(degrees);
        return new Vector2(-MathF.Sin(radians), -MathF.Cos(radians));
    }

    private static void AssignRow(
        IReadOnlyList<Soldier> row,
        float spacing,
        Vector2 anchor,
        Vector2 forward,
        Vector2 right,
        float forwardOffset,
        IDictionary<Guid, Vector2> result)
    {
        float start = -(row.Count - 1) * spacing * 0.5f;
        for (int index = 0; index < row.Count; index++)
            result[row[index].UnitId] = anchor + forward * forwardOffset +
                right * (start + index * spacing);
    }
}
