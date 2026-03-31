using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using Verse.AI.Group;
using System.Text;

namespace EconomicsDemography
{
    // При удалении карты (например, поселения) возвращает оставшиеся на ней предметы в виртуальный склад.
    [HarmonyPatch(typeof(MapDeiniter), "Deinit")]
    public static class Patch_ReabsorbLootOnLeave
    {
        [HarmonyPrefix]
        static void Prefix(Map map)
        {
            if (map == null || map.IsPlayerHome) return;

            if (map.Parent == null || !(map.Parent is Settlement settlement)) return;

            Faction f = settlement.Faction;
            if (f == null || f.IsPlayer) return;

            WorldPopulationManager manager = null;
            try 
            {
                if (Find.World != null)
                    manager = Find.World.GetComponent<WorldPopulationManager>();
            }
            catch { return; }

            if (manager == null) return;

            VirtualStockpile stock = manager.GetStockpile(f);
            if (stock == null) return;

            List<Thing> itemsOnMap = map.listerThings.AllThings
                .Where(t => t.def.category == ThingCategory.Item && !t.def.Minifiable)
                .ToList();

            int recoveredCount = 0;
            int silverRecovered = 0;

            foreach (Thing t in itemsOnMap)
            {
                if (t.def.useHitPoints && t.HitPoints < t.MaxHitPoints * 0.5f) continue;
                
                if (t.ParentHolder is Pawn) continue;

                // Игнорируем обломки (каменные/шлаковые глыбы)
                if (t.def.thingCategories != null && t.def.thingCategories.Any(c => c.defName == "StoneChunks" || c.defName == "Chunks")) continue;
                if (t.def.defName.Contains("Chunk")) continue;

                if (t.def == ThingDefOf.Silver)
                {
                    stock.silver += t.stackCount;
                    silverRecovered += t.stackCount;
                }
                else
                {
                    int quality = -1;
                    if (t.TryGetComp<CompQuality>() is CompQuality qComp) quality = (int)qComp.Quality;
                    stock.AddItem(t.def, t.stackCount, quality);
                    recoveredCount++;
                }
            }

            // Считаем стоимость всего, что собрали
            float totalRecoveredValue = stock.GetTotalWealth();

            if (recoveredCount > 0 || silverRecovered > 0)
            {
                Log.Warning($"[ED-DEBUG-LOOT] ReabsorbLootOnLeave: {f.Name} получил {recoveredCount} предметов + {silverRecovered} серебра. Итого склад: {totalRecoveredValue:F0}");
            }
        }
    }
}
