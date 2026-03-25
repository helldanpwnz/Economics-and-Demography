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
    // Проверяет наличие товаров у фракции перед отправкой каравана.
    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), "TryExecuteWorker")]
    public static class Patch_TraderArrival_CheckStock
    {
        [HarmonyPrefix]
        static bool Prefix(IncidentParms parms, ref bool __result)
        {
            if (parms.faction == null || parms.faction.IsPlayer) return true;

            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null) return true;

            var stock = manager.GetStockpile(parms.faction);
            if (stock == null) return true;

            if (parms.traderKind == null)
            {
                if (!parms.faction.def.caravanTraderKinds.TryRandomElementByWeight(t => t.commonality, out var selected))
                {
                    __result = false;
                    return false; 
                }
                parms.traderKind = selected;
            }

            if (!stock.HasThingsFor(parms.traderKind))
            {
                __result = false;
                return false;
            }

            return true; 
        }
    }
}
