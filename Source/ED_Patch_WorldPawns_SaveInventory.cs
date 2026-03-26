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
    // При уходе пешки с карты сохраняет её инвентарь в виртуальный склад фракции.
    [HarmonyPatch(typeof(WorldPawns), "PassToWorld")]
    public static class Patch_WorldPawns_SaveInventory
    {
        [HarmonyPrefix]
        static void Prefix(Pawn pawn)
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (pawn == null || pawn.Faction == null || pawn.Faction.IsPlayer || pawn.Dead) return;

            if (pawn.inventory != null && pawn.inventory.innerContainer != null && pawn.inventory.innerContainer.Count > 0)
            {
                var manager = Find.World.GetComponent<WorldPopulationManager>();
                if (manager != null)
                {
                    var stock = manager.GetStockpile(pawn.Faction);
                    var items = pawn.inventory.innerContainer.Where(t => t.def.category == ThingCategory.Item).ToList();
                    
                    if (items.Count > 0)
                    {
                        foreach (Thing item in items)
                        {
                            if (item is MinifiedThing || item.def.Minifiable) continue;

                            if (item.def == ThingDefOf.Silver) stock.silver += item.stackCount;
                            else stock.AddItem(item.GetInnerIfMinified().def, item.stackCount);
                        }
                        
                        pawn.inventory.innerContainer.ClearAndDestroyContents();
                        Log.Message("ED_Log_PawnDepartedMap".Translate(pawn.Faction.Name));
                    }
                }
            }
        }
    }
}
