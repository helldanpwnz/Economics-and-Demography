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
            
            // Принудительно "забываем" торговца при заходе на карту, чтобы разрешить новую генерацию товаров из склада
            Patch_TradeSession_Setup.processedCaravans.Remove(traderID);

            List<Pawn> carriers = lord.ownedPawns.Where(p => p.inventory != null).ToList();
            if (carriers.Count == 0) return;

            List<Thing> vanillaGoods = new List<Thing>();
            foreach (var carrier in carriers)
            {
                vanillaGoods.AddRange(carrier.inventory.innerContainer.Where(t => t.def.category == ThingCategory.Item).ToList());
            }

            // 1. Пополняем склад из каравана (если фракция новая или ее склад пуст)
            if (vanillaGoods.Count > 0)
            {
                var currentStock = manager.GetStockpile(parms.faction);
                if (!manager.IsSimulatedFaction(parms.faction) || (currentStock != null && currentStock.inventory.Count == 0))
                {
                    manager.DepositGoods(parms.faction, vanillaGoods);
                }
            }

            // 2. Очищаем караван от ванили
            foreach (var carrier in carriers)
            {
                var inner = carrier.inventory.innerContainer;
                for (int i = inner.Count - 1; i >= 0; i--)
                {
                    if (inner[i].def.category == ThingCategory.Item && !carrier.RaceProps.Humanlike)
                    {
                        Thing t = inner[i];
                        inner.Remove(t);
                        t.Destroy();
                    }
                }
            }

            var stock = manager.GetStockpile(parms.faction);
            List<Thing> newThings = stock.GenerateRealThings(trader.TraderKind, false);

            // РАЗДАЧА: Серебро и Товары - Вьючным
            var packAnimalsOnly = carriers.Where(x => x.RaceProps.packAnimal).ToList();
            Pawn silverTarget = (packAnimalsOnly.Count > 0) ? packAnimalsOnly[0] : trader;
            int packIndex = 0;

            foreach (Thing t in newThings)
            {
                if (t == null || t.stackCount <= 0) continue;

                // ДЕНЬГИ - На вьючное (если есть)
                if (t.def == ThingDefOf.Silver)
                {
                    silverTarget.inventory.innerContainer.TryAdd(t);
                }
                else
                {
                    // ТОВАРЫ - На вьючное (или лидеру, если их нет)
                    Pawn target = (packAnimalsOnly.Count > 0) ? packAnimalsOnly[packIndex % packAnimalsOnly.Count] : trader;
                    target.inventory.innerContainer.TryAdd(t);
                    packIndex++;
                }
            }

            Patch_TradeSession_Setup.processedCaravans.Add(traderID);
            
            Log.Message(string.Format((string)"ED_Log_TraderArrivalReplacement".Translate(), parms.faction.Name));
        }
    }    
}
