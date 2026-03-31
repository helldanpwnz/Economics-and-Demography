using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace EconomicsDemography
{
    // Снимает долю капитала фракции при разрушении поселения, если она еще не была снята при генерации карты.
    // Используем базовый тип WorldObject, так как Settlement может не переопределять Destroy напрямую.
    [HarmonyPatch(typeof(WorldObject), "Destroy")]
    public static class Patch_Settlement_Destroy
    {
        [HarmonyPrefix]
        static void Prefix(WorldObject __instance)
        {
            // Проверяем, что это поселение, и оно не принадлежит игроку
            if (!(__instance is Settlement settlement) || settlement == null || settlement.Faction == null || settlement.Faction.IsPlayer) return;

            var manager = Find.World?.GetComponent<WorldPopulationManager>();
            if (manager == null) return;

            // Если богатство уже было изъято при генерации карты (нападение) — ничего не делаем
            if (manager.processedSettlements.Contains(settlement.ID))
            {
                manager.processedSettlements.Remove(settlement.ID);
                return;
            }

            VirtualStockpile stock = manager.GetStockpile(settlement.Faction);
            if (stock == null) return;

            // Считаем все поселения фракции (включая это, так как это Prefix)
            int basesCount = Find.WorldObjects.Settlements.Count(s => s.Faction == settlement.Faction);
            if (basesCount <= 0) return;

            // Списываем пропорциональную долю серебра
            if (stock.silver > 100)
            {
                int silverToLose = stock.silver / basesCount;
                stock.silver -= silverToLose;
            }

            // Списываем пропорциональную долю товаров
            List<string> keys = stock.inventory.Keys.ToList();
            foreach (var key in keys)
            {
                int amount = stock.inventory[key];
                if (amount > 0)
                {
                    int toLose = amount / basesCount;
                    if (toLose > 0)
                    {
                        stock.inventory[key] -= toLose;
                        if (stock.inventory[key] <= 0) stock.inventory.Remove(key);
                    }
                    else if (Rand.Value < (1f / basesCount)) // Шанс потерять 1 штуку, если их мало
                    {
                        stock.inventory[key]--;
                        if (stock.inventory[key] <= 0) stock.inventory.Remove(key);
                    }
                }
            }
            
            Log.Message(string.Format((string)"ED_Log_SettlementDestroyedReduction".Translate(), settlement.Name, settlement.Faction.Name, basesCount));
        }
    }
}
