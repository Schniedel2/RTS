using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

using RTS;

public class SelectionGroup
{
    public List<Unit> Units { get; }

    public SelectionGroup(IReadOnlyList<Unit> units)
    {
        Units = new List<Unit>();
        foreach (var unit in units)
            Units.Add(unit);
    }
}
