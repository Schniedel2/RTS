using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public class Silo : Building
{
    public override string GameplayTypeId => "silo";
    public override IReadOnlyList<UnitAction> Actions => GetUnitActions();

    public Silo(
        Vector3 position,
        Guid unitId,
        string meshName = "silo-1",
        int purchasePrice = 0
        ) : base(
            position,
            unitId, purchasePrice)
    {
        SetMesh(meshName, deriveDimensions: true);

        ApplyCatalogMetadata();
    }

    public override void Draw(Effect effect)
    {
        base.Draw(effect);
    }

    public override void Draw2D(SpriteBatch spriteBatch, Camera camera, Viewport viewport)
    {
        base.Draw2D(spriteBatch, camera, viewport);
        if (IsCompleted)
            ResourceBarRenderer.Draw(spriteBatch, camera, viewport, this,
                StoredResources, ResourceCapacity, Color.LimeGreen);
    }

    public IReadOnlyList<UnitAction> GetUnitActions()
    {
        IReadOnlyList<UnitAction> actions = Array.Empty<UnitAction>();
    
        if (IsCompleted)
        {
            actions = 
            [
                new(UnitActionType.LeaveContainer, "Leave", 5, 1),
            ];
        }
        
        return WithSellAction(actions);
    }

}
