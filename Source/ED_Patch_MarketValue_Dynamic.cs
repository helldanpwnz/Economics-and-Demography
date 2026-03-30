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
        private static Dictionary<ThingDef, float> multiplierCache = new Dictionary<ThingDef, float>();
        public static bool isDirty = true;

        [HarmonyPostfix]
        static void Postfix(StatRequest req, ref float __result)
        {
            // Самые дешевые проверки - первыми. WealthCalculationDepth - ThreadStatic, очень быстрый доступ.
            if (WorldPopulationManager.WealthCalculationDepth > 0 || Patch_MapGen_Tracker.IsGeneratingMap) return;

            ThingDef def = req.Def as ThingDef;
            if (def == null || def == ThingDefOf.Silver || def.category == ThingCategory.Pawn) return;

            if (isDirty)
            {
                multiplierCache.Clear();
                isDirty = false;
            }

            if (multiplierCache.TryGetValue(def, out float cachedMult))
            {
                if (cachedMult != 1f) __result *= cachedMult;
                return;
            }

            var manager = WorldPopulationManager.Instance;
            if (manager == null) return;

            float mult = 1f;
            if (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(def, out float globalMult))
            {
                mult *= globalMult;
            }
            
            if (EconomicsDemographyMod.Settings != null && EconomicsDemographyMod.Settings.enableGlobalInflation)
            {
                float inflation = manager.currentInflation;
                if (Math.Abs(inflation - 1.0f) > 0.0001f)
                    mult *= inflation;
            }

            multiplierCache[def] = mult;
            if (mult != 1f) __result *= mult;
        }
    }
}
