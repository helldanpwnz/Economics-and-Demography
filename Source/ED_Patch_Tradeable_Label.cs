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
    // Добавляет к названию товара в окне торговли значок изменения цены.
    [HarmonyPatch(typeof(Tradeable), "get_Label")]
    public static class Patch_Tradeable_Label
    {
        [HarmonyPostfix]
        static void Postfix(Tradeable __instance, ref string __result)
        {
            if (__instance == null || !__instance.HasAnyThing) return;

            try 
            {
                ThingDef def = __instance.ThingDef;
                if (def == null || def == ThingDefOf.Silver) return;

                var manager = Find.World?.GetComponent<WorldPopulationManager>();
                if (manager == null || manager.globalPriceModifiers == null) return;

                if (manager.globalPriceModifiers.TryGetValue(def.defName, out float mult))
                {
                    if (Mathf.Abs(mult - 1f) < 0.05f) return;

                    float diff = (mult - 1f) * 100f;
                    string sign = diff > 0 ? "+" : "";
                    string color = mult > 1f ? "#ff6666" : "#66ff66";
                    string symbol = mult > 1f ? "▲" : "▼";

                    __result += $" <color={color}>({symbol}{sign}{diff:F0}%)</color>";
                }
            }
            catch
            { 
            }
        }
    }
}
