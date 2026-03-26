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
    // Динамически изменяет рыночную стоимость предметов в зависимости от глобальных цен.
    [HarmonyPatch(typeof(StatWorker_MarketValue), "GetValueUnfinalized")]
    public static class Patch_MarketValue_Dynamic
    {
        [HarmonyPostfix]
        static void Postfix(StatRequest req, ref float __result)
        {
            if (Patch_MapGen_Tracker.IsGeneratingMap) return;

            // Если сейчас считается богатство (для Рассказчика или статистики) - инфляцию ИГНОРИРУЕМ
            if (WorldPopulationManager.IsCalculatingWealth) return;

            if (req.Def is ThingDef def) 
            {
                if (def == ThingDefOf.Silver || def.category == ThingCategory.Pawn) return;

                var manager = WorldPopulationManager.Instance;
                if (manager != null)
                {
                    if (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(def, out float mult))
                    {
                        __result *= mult;
                    }
                    
                    // Инфляция (если это не серебро)
                    if (def != ThingDefOf.Silver && EconomicsDemographyMod.Settings.enableGlobalInflation)
                    {
                        __result *= manager.currentInflation;
                    }
                }
            }
        }
    }
}
