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
    // Подменяет товары торговцев на виртуальные при открытии окна торговли.
    [HarmonyPatch(typeof(TradeSession), "SetupWith")]
    public static class Patch_TradeSession_Setup
    {
        public static HashSet<int> processedCaravans = new HashSet<int>();
        public static HashSet<int> existingPawnIds = new HashSet<int>();

        [HarmonyPrefix]
        static void Prefix(ITrader newTrader, Pawn newPlayerNegotiator)
        {
            existingPawnIds.Clear();
            try
            {
                if (newTrader == null || newTrader.Faction == null || newTrader.Faction.IsPlayer) return;

                var manager = Find.World?.GetComponent<WorldPopulationManager>();
                if (manager == null) return;
                
                var virtStock = manager.GetStockpile(newTrader.Faction);

                if (newTrader is Settlement sett && sett.trader != null)
                {
                    var trackerData = Traverse.Create(sett.trader);

                    ThingOwner owner = trackerData.Field("stock").GetValue<ThingOwner>();

                    // Если инвентарь пуст или устарел — принудительно генерируем ванильный запас (с рабами)
                    if (owner == null || owner.Count == 0 || Find.TickManager.TicksGame - trackerData.Field("lastStockGenerationTick").GetValue<int>() > 600000)
                    {
                        trackerData.Method("RegenerateStock").GetValue();
                        owner = trackerData.Field("stock").GetValue<ThingOwner>();
                    }
                    
                    if (owner == null)
                    {
                        owner = new ThingOwner<Thing>(sett.trader);
                        trackerData.Field("stock").SetValue(owner);
                    }

                    // Запоминаем ID существ, которые уже были у торговца (чтобы не начислять за них деньги дважды)
                    foreach (Thing t in owner)
                    {
                        if (t is Pawn pawn) existingPawnIds.Add(pawn.thingIDNumber);
                    }

                    // Удаляем только предметы, сохраняя рабов и животных
                    for (int i = owner.Count - 1; i >= 0; i--)
                    {
                        if (!(owner[i] is Pawn))
                        {
                            Thing t = owner[i];
                            owner.Remove(t);
                            t.Destroy();
                        }
                    }

                    trackerData.Field("lastStockGenerationTick").SetValue(Find.TickManager.TicksGame);

                    if (virtStock.inventory.Count == 0 && virtStock.silver <= 1000)
                    {
                        // manager.GenerateStartingStock(newTrader.Faction, virtStock); 
                    }

                    List<Thing> realThings = virtStock.GenerateRealThings(newTrader.TraderKind, true);
                    foreach (Thing t in realThings)
                    {
                        if (t != null) owner.TryAdd(t);
                    }

                    return;
                }

                bool isCaravan = newTrader is Pawn p && p.Map != null;
                if (isCaravan)
                {
                    int traderID = ((Pawn)newTrader).thingIDNumber;
                    if (processedCaravans.Contains(traderID)) return;
                    
                    Pawn traderPawn = (Pawn)newTrader;
                    var lord = traderPawn.GetLord();
                    var carriers = lord?.ownedPawns.Where(x => x.inventory != null && x.RaceProps.packAnimal).ToList() ?? new List<Pawn> { traderPawn };

                    foreach (var carrier in carriers)
                    {
                        var inner = carrier.inventory.innerContainer;
                        
                        for (int i = inner.Count - 1; i >= 0; i--)
                        {
                            Thing t = inner[i];
                            if (t != null && (t is MinifiedThing || t.def.Minifiable || t.TryGetComp<CompQuality>() != null))
                            {
                                inner.RemoveAt(i);
                                t.Destroy();
                            }
                        }

                        List<Thing> tempItems = inner.ToList();
                        inner.Clear(); 

                        foreach (var t in tempItems)
                        {
                            if (t == null) continue;
                            if (t.def == ThingDefOf.Silver) virtStock.silver += t.stackCount;
                            else virtStock.AddItem(t.def, t.stackCount);
                            t.Destroy();
                        }
                    }

                    float carrierCapacity = carriers.Sum(p => MassUtility.Capacity(p));
                    List<Thing> newThings = virtStock.GenerateRealThings(newTrader.TraderKind, false, carrierCapacity);
                    int carrierIndex = 0;
                    
                    var packAnimalsOnly = carriers.Where(x => x.RaceProps.packAnimal).ToList();
                    Pawn silverCarrier = (packAnimalsOnly.Count > 0) ? packAnimalsOnly[0] : traderPawn;

                    foreach (Thing t in newThings)
                    {
                        if (t == null || t.stackCount <= 0) continue; 
                        
                        try {
                            if (t.def == ThingDefOf.Silver)
                            {
                                silverCarrier.inventory.innerContainer.TryAdd(t);
                            }
                            else
                            {
                                Pawn target = (packAnimalsOnly.Count > 0) ? packAnimalsOnly[carrierIndex % packAnimalsOnly.Count] : traderPawn;
                                target.inventory.innerContainer.TryAdd(t);
                                carrierIndex++;
                            }
                        } catch { }
                    }
                    
                    processedCaravans.Add(traderID);
                }
            }
            catch (Exception ex) 
            { 
                Log.Error("ED_Log_SetupWithTradeError".Translate(ex.ToString())); 
            }
        }
    }
}
