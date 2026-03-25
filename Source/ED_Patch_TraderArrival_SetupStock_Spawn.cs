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
    // При спавне каравана на карте заменяет его товары на виртуальные.
    [HarmonyPatch(typeof(IncidentWorker_TraderCaravanArrival), "TryExecuteWorker")]
    public static class Patch_TraderArrival_SetupStock_Spawn
    {
        [HarmonyPostfix]
        static void Postfix(IncidentParms parms, ref bool __result)
        {
            if (!__result || parms.faction == null || parms.faction.IsPlayer) return;

            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null) return;

            Map map = (Map)parms.target;
            if (map == null) return;

            var lord = map.lordManager.lords.FirstOrDefault(l => l.faction == parms.faction && l.LordJob is LordJob_TradeWithColony);
            if (lord == null) return;

            Pawn trader = lord.ownedPawns.FirstOrDefault(p => p.TraderKind != null);
            if (trader == null) return;

            int traderID = trader.thingIDNumber;
            
            if (Patch_TradeSession_Setup.processedCaravans.Contains(traderID)) return;

            List<Pawn> carriers = lord.ownedPawns.Where(p => p.inventory != null && p.RaceProps.packAnimal).ToList();
            if (carriers.Count == 0) carriers = new List<Pawn> { trader };

            List<Thing> vanillaGoods = new List<Thing>();
            foreach (var carrier in carriers)
            {
                var inner = carrier.inventory.innerContainer;
                var items = inner.Where(t => t.def.category == ThingCategory.Item).ToList();
                vanillaGoods.AddRange(items);
            }

            if (vanillaGoods.Count > 0)
            {
                manager.DepositGoods(parms.faction, vanillaGoods);
            }

            var stock = manager.GetStockpile(parms.faction);
            List<Thing> newThings = stock.GenerateRealThings(trader.TraderKind, false);

            int carrierIndex = 0;
            foreach (Thing t in newThings)
            {
                carriers[carrierIndex].inventory.innerContainer.TryAdd(t);
                carrierIndex = (carrierIndex + 1) % carriers.Count;
            }

            Patch_TradeSession_Setup.processedCaravans.Add(traderID);
            
            Log.Message($"[E&D] Караван {parms.faction.Name} прибыл на карту. Товары успешно заменены в момент спавна.");
        }
    }    
}
