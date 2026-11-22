using System;
using System.Collections.Generic;
using System.Linq;
using TheEmptying.Modules.Inventory;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = TheEmptying.Core.Entities.EntityId;

namespace TheEmptying.Presentation.Buildings
{
    /// <summary>3.10b2: the crown stockpile's contents, shared by the dedicated Stockpile panel and the building panel's own view of the storehouse, so the two always agree.</summary>
    internal static class StockpileContentsFormatter
    {
        public static void AppendGoodsRows(VisualElement container, EntityId storehouseId, InventoryModule inventory)
        {
            List<KeyValuePair<string, int>> goods = inventory.GetAll(storehouseId).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();

            if (goods.Count == 0)
            {
                container.Add(new Label("Empty.") { style = { color = Color.gray } });
                return;
            }

            foreach (KeyValuePair<string, int> good in goods)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 2 } };
                row.Add(new Label(good.Key) { style = { color = new Color(0.82f, 0.82f, 0.82f), fontSize = 12 } });
                row.Add(new Label(good.Value.ToString()) { style = { color = new Color(0.9f, 0.9f, 0.9f), fontSize = 12, unityFontStyleAndWeight = FontStyle.Bold } });
                container.Add(row);
            }
        }
    }
}
