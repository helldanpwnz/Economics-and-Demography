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
            Log.Message("ED_Log_TradeDialogClose_Called".Translate());
            
            if (TradeSession.trader == null || TradeSession.trader.Faction == null || TradeSession.trader.Faction.IsPlayer) 
            {
                Log.Message("ED_Log_TraderNullOrPlayer".Translate());
                return;
            }

            bool isCaravan = TradeSession.trader is Pawn p && p.Map != null;
            if (isCaravan)
            {
                Log.Message("ED_Log_TraderIsCaravan".Translate());
                return;
            }

            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null) 
            {
                Log.Error("ED_Log_ManagerNotFound".Translate());
                return;
            }
            
            var stock = manager.GetStockpile(TradeSession.trader.Faction);
            if (stock == null)
            {
                Log.Error("ED_Log_StockpileNotFound".Translate());
                return;
            }

            Log.Message("ED_Log_StockBeforeClose".Translate(stock.inventory?.Count ?? 0, stock.silver));

            ThingOwner owner = null;
            if (TradeSession.trader is Settlement sett && sett.trader != null)
            {
                owner = Traverse.Create(sett.trader).Field("stock").GetValue<ThingOwner>();
                Log.Message(string.Format((string)"ED_Log_SettlementOwnerFound".Translate(), owner != null, owner?.Count ?? 0));
            }
            else if (TradeSession.trader is TradeShip ship)
            {
                owner = ship.GetDirectlyHeldThings();
                Log.Message(string.Format((string)"ED_Log_ShipOwnerFound".Translate(), owner != null, owner?.Count ?? 0));
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
                
                Log.Message("ED_Log_ItemsToRecover".Translate(things.Count));

                foreach (Thing t in things)
                {
                    if (t == null || t.Destroyed) continue;
                    
                    if (t.def == ThingDefOf.Silver)
                    {
                        stock.silver += t.stackCount;
                        silverRecovered += t.stackCount;
                        Log.Message("ED_Log_SilverRecovered".Translate(t.stackCount));
                    }
                    else if (t.def.category == ThingCategory.Item)
                    {
                        int oldMax = stock.maxSlots;
                        stock.maxSlots = 9999;
                        
                        stock.AddItem(t.GetInnerIfMinified().def, t.stackCount);
                        
                        stock.maxSlots = oldMax;
                        itemsRecovered++;
                        Log.Message("ED_Log_ItemRecovered".Translate(t.def.defName, t.stackCount));
                    }
                }
                
                // Возвращаем капитал только за НОВЫХ рабов (которых купил ИИ у игрока)
                float newAssetsValue = 0;
                foreach (Thing t in owner)
                {
                    if (t is Pawn pawn && !Patch_TradeSession_Setup.existingPawnIds.Contains(pawn.thingIDNumber))
                        newAssetsValue += pawn.MarketValue;
                }
                stock.silver += Mathf.RoundToInt(newAssetsValue);

                // Вместо полной очистки удаляем только товары, сохраняя живых существ
                for (int i = owner.Count - 1; i >= 0; i--)
                {
                    if (!(owner[i] is Pawn))
                    {
                        Thing t = owner[i];
                        owner.Remove(t);
                        t.Destroy();
                    }
                }
                
                Log.Message("ED_Log_RecoverSuccess".Translate(itemsRecovered, silverRecovered, (stock.inventory?.Count ?? 0), stock.silver));
            }
            else
            {
                Log.Warning(string.Format((string)"ED_Log_OwnerNotFound".Translate(), owner == null));
            }
        }
    }
}
