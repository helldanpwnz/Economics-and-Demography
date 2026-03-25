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
    // При закрытии окна торговли возвращает нераспроданные товары обратно в виртуальный склад.
    [HarmonyPatch(typeof(RimWorld.Dialog_Trade), "Close")]
    public static class Patch_Dialog_Trade_Close
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            Log.Message("[E&D] >>> Patch_Dialog_Trade_Close.Postfix вызван! <<<");
            
            if (TradeSession.trader == null || TradeSession.trader.Faction == null || TradeSession.trader.Faction.IsPlayer) 
            {
                Log.Message("[E&D] Торговец null или игрок, пропускаем");
                return;
            }

            bool isCaravan = TradeSession.trader is Pawn p && p.Map != null;
            if (isCaravan)
            {
                Log.Message("[E&D] Это караван, пропускаем (они унесут вещи)");
                return;
            }

            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null) 
            {
                Log.Error("[E&D] WorldPopulationManager не найден!");
                return;
            }
            
            var stock = manager.GetStockpile(TradeSession.trader.Faction);
            if (stock == null)
            {
                Log.Error("[E&D] Stockpile не найден!");
                return;
            }

            Log.Message($"[E&D] Склад до закрытия: {stock.inventory?.Count ?? 0} поз., Серебро: {stock.silver}");

            ThingOwner owner = null;
            if (TradeSession.trader is Settlement sett && sett.trader != null)
            {
                owner = Traverse.Create(sett.trader).Field("stock").GetValue<ThingOwner>();
                Log.Message($"[E&D] Это поселение, owner найден: {owner != null}, Count: {owner?.Count ?? 0}");
            }
            else if (TradeSession.trader is TradeShip ship)
            {
                owner = ship.GetDirectlyHeldThings();
                Log.Message($"[E&D] Это корабль, owner найден: {owner != null}, Count: {owner?.Count ?? 0}");
            }

            if (owner != null && owner.Count > 0)
            {
                int itemsRecovered = 0;
                int silverRecovered = 0;
                
                List<Thing> things = new List<Thing>();
                foreach (Thing t in owner)
                {
                    things.Add(t);
                }
                
                Log.Message($"[E&D] Найдено {things.Count} предметов для возврата");

                foreach (Thing t in things)
                {
                    if (t == null || t.Destroyed) continue;
                    
                    if (t.def == ThingDefOf.Silver)
                    {
                        stock.silver += t.stackCount;
                        silverRecovered += t.stackCount;
                        Log.Message($"[E&D] Возвращено серебра: {t.stackCount}");
                    }
                    else if (t.def.category == ThingCategory.Item)
                    {
                        int oldMax = stock.maxSlots;
                        stock.maxSlots = 9999;
                        
                        stock.AddItem(t.GetInnerIfMinified().def, t.stackCount);
                        
                        stock.maxSlots = oldMax;
                        itemsRecovered++;
                        Log.Message($"[E&D] Возвращен предмет: {t.def.defName}, стек: {t.stackCount}");
                    }
                }
                
                owner.ClearAndDestroyContents();
                
                Log.Message($"[E&D] Успех! Возвращено: {itemsRecovered} типов, {silverRecovered} серебра. Склад после: {stock.inventory?.Count ?? 0}, Серебро: {stock.silver}");
            }
            else
            {
                Log.Warning($"[E&D] owner пуст или не найден! owner == null: {owner == null}");
            }
        }
    }
}
