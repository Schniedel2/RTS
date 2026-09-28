using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RTS;

public sealed record ProductionQueueOverviewEntry(
    Guid BuildingId,
    string BuildingName,
    string ActiveProductName,
    float Progress,
    IReadOnlyList<string> WaitingProducts);

/// <summary>Compact overview of every active production queue owned by the local army.</summary>
public sealed class ProductionQueuePanel
{
    private const int Padding = 10;
    private const int HeaderHeight = 28;
    private const int RowHeight = 59;

    public static IReadOnlyList<ProductionQueueOverviewEntry> GetEntries(
        GameWorld world,
        Guid armyId) =>
        world.Units.Units.OfType<Building>()
            .Where(building => building.ArmyId == armyId && !building.IsDying &&
                building.ProductionQueue.ActiveOrder is not null)
            .OrderBy(building => GetDisplayName(PurchasableType.Building, building.GameplayTypeId))
            .ThenBy(building => building.UnitId)
            .Select(building =>
            {
                ProductionOrder active = building.ProductionQueue.ActiveOrder!;
                return new ProductionQueueOverviewEntry(
                    building.UnitId,
                    GetDisplayName(PurchasableType.Building, building.GameplayTypeId),
                    GetProductDisplayName(active.UnitTypeId),
                    active.Progress,
                    building.ProductionQueue.Orders.Skip(1)
                        .Select(order => GetProductDisplayName(order.UnitTypeId))
                        .ToArray());
            })
            .ToArray();

    public void Draw(SpriteBatch spriteBatch, Rectangle availableBounds, Army army, GameWorld world)
    {
        if (availableBounds.Width <= 0 || availableBounds.Height < HeaderHeight)
            return;

        IReadOnlyList<ProductionQueueOverviewEntry> entries = GetEntries(world, army.Id);
        int visibleRows = Math.Min(entries.Count,
            Math.Max(0, (availableBounds.Height - HeaderHeight) / RowHeight));
        int height = HeaderHeight + Math.Max(1, visibleRows) * RowHeight;
        Rectangle bounds = new(availableBounds.X, availableBounds.Y,
            availableBounds.Width, Math.Min(availableBounds.Height, height));

        spriteBatch.Draw(Globals._whiteTexture, bounds, Color.Black * 0.78f);
        spriteBatch.DrawString(Globals._debugFont, "Production queues",
            new Vector2(bounds.X + Padding, bounds.Y + 6), new Color(150, 210, 255));
        spriteBatch.Draw(Globals._whiteTexture,
            new Rectangle(bounds.X, bounds.Y + HeaderHeight - 2, bounds.Width, 2),
            new Color(65, 105, 130));

        if (entries.Count == 0)
        {
            spriteBatch.DrawString(Globals._debugFont, "No active production",
                new Vector2(bounds.X + Padding, bounds.Y + HeaderHeight + 15), Color.Gray);
            return;
        }

        for (int index = 0; index < visibleRows; index++)
            DrawEntry(spriteBatch, bounds, entries[index], index);

        int hidden = entries.Count - visibleRows;
        if (hidden > 0)
            spriteBatch.DrawString(Globals._debugFont, $"+{hidden} more production queues",
                new Vector2(bounds.X + Padding, bounds.Bottom - 20), Color.LightGray);
    }

    private static void DrawEntry(SpriteBatch spriteBatch, Rectangle panel,
        ProductionQueueOverviewEntry entry, int index)
    {
        int top = panel.Y + HeaderHeight + index * RowHeight;
        string queued = entry.WaitingProducts.Count == 0
            ? string.Empty
            : $"  +{entry.WaitingProducts.Count} queued";
        spriteBatch.DrawString(Globals._debugFont, entry.BuildingName,
            new Vector2(panel.X + Padding, top + 4), Color.White);
        spriteBatch.DrawString(Globals._debugFont,
            $"{entry.ActiveProductName}  {entry.Progress * 100.0f:0}%{queued}",
            new Vector2(panel.X + Padding, top + 24), new Color(190, 220, 235));

        Rectangle track = new(panel.X + Padding, top + 45,
            panel.Width - Padding * 2, 7);
        spriteBatch.Draw(Globals._whiteTexture, track, new Color(45, 55, 62));
        spriteBatch.Draw(Globals._whiteTexture,
            new Rectangle(track.X, track.Y,
                (int)MathF.Round(track.Width * Math.Clamp(entry.Progress, 0.0f, 1.0f)), track.Height),
            new Color(70, 190, 235));
    }

    private static string GetProductDisplayName(string typeId) =>
        GameplayCatalog.Find(PurchasableType.Unit, typeId)?.DisplayName ??
        GameplayCatalog.Find(PurchasableType.Research, typeId)?.DisplayName ??
        typeId;

    private static string GetDisplayName(PurchasableType type, string typeId) =>
        GameplayCatalog.Find(type, typeId)?.DisplayName ?? typeId;
}
