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
    public partial class WorldPopulationManager
    {
        // Виртуальная торговля между фракциями
        private void ProcessVirtualTrade(List<Faction> factions)
        {
            float actionChance = 0.02f; 

            foreach (var actor in factions)
            {
                if (actor.IsPlayer || actor.defeated || actor.def.hidden || (actor.leader == null && actor.def.techLevel > TechLevel.Animal)) continue;
                if (Rand.Value > actionChance) continue;

                VirtualStockpile actorStock = GetStockpile(actor);

                if (actor.def.permanentEnemy)
                {
                    var victims = factions.Where(v => 
                        v != actor && !v.IsPlayer && !v.defeated && !v.def.hidden &&
                        !v.def.permanentEnemy && 
                        v.def.techLevel > TechLevel.Neolithic 
                    ).ToList();

                    if (victims.Count == 0) continue;

                    Faction victim = victims.RandomElement();
                    VirtualStockpile victimStock = GetStockpile(victim);

                    if (victimStock.inventory.Count == 0) continue;

                    float stealPercent = 0f;
                    switch (victim.def.techLevel)
                    {
                        case TechLevel.Medieval:   stealPercent = 0.05f; break; 
                        case TechLevel.Industrial: stealPercent = 0.03f; break; 
                        case TechLevel.Spacer:     stealPercent = 0.02f; break; 
                        case TechLevel.Ultra:       
                        case TechLevel.Archotech:  stealPercent = 0.01f; break; 
                        default: continue; 
                    }

                    string targetKey = GetRandomValidItemKey(victimStock);
                    if (targetKey == null) continue;

                    int totalVictimHas = victimStock.inventory[targetKey];
                    int amountToSteal = Mathf.Clamp(Mathf.CeilToInt(totalVictimHas * stealPercent), 1, totalVictimHas);
                    
                    ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(targetKey);
                    if (itemDef != null)
                    {
                        victimStock.inventory[targetKey] -= amountToSteal;
                        if (victimStock.inventory[targetKey] <= 0) victimStock.inventory.Remove(targetKey);

                        actorStock.AddItem(itemDef, amountToSteal);
                        Log.Message("ED_Log_Raid".Translate(actor.Name, amountToSteal, itemDef.label, victim.Name));
                    }
                }
                else
                {
                    var partners = factions.Where(p => 
                        p != actor && !p.IsPlayer && !p.defeated && !p.def.hidden
                    ).ToList();

                    if (partners.Count == 0) continue;

                    Faction partner = partners.RandomElement();
                    VirtualStockpile partnerStock = GetStockpile(partner);

                    float silverRatio = actorStock.silver / Mathf.Max(1f, actorStock.GetTotalWealth());
                    bool needsMoney = silverRatio < 0.10f;

                    string keyA = actorStock.inventory
                        .Where(kvp => kvp.Value > 0 && kvp.Key != "Silver")
                        .OrderByDescending(kvp => {
                            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
                            if (d == null) return 0f;
                            float m = globalPriceModifiers.TryGetValue(d, out float val) ? val : 1.0f;
                            float priority = kvp.Value * (2.0f - m);
                            
                            if (needsMoney) {
                                priority *= d.BaseMarketValue / 10f; 
                            }
                            return priority;
                        })
                        .Select(kvp => kvp.Key)
                        .FirstOrDefault();

                    if (keyA == null) continue;
                    ThingDef defA = DefDatabase<ThingDef>.GetNamedSilentFail(keyA);
                    if (defA == null) continue;

                    bool isMilitary = defA.IsWeapon || defA.IsApparel;
                    if (isMilitary && actor.def.techLevel != partner.def.techLevel) continue;

                    float marketMult = globalPriceModifiers.TryGetValue(defA, out float mm) ? mm : 1.0f;
                    float baseValue = defA.BaseMarketValue * marketMult;

                    float sellPrice = actor.def.permanentEnemy ? (baseValue * 0.5f) : baseValue;
                    float buyPrice = partner.def.permanentEnemy ? (baseValue * 2.0f) : baseValue;
                    float finalUnitPrice = (sellPrice + buyPrice) / 2f;

                    int amountA = Mathf.Max(1, Mathf.CeilToInt(actorStock.inventory[keyA] * Rand.Range(0.1f, 0.3f)));
                    float totalDealValue = amountA * finalUnitPrice;

                    bool actorIsLower = actor.def.techLevel < partner.def.techLevel;
                    bool partnerIsLower = partner.def.techLevel < actor.def.techLevel;

                    // Строго запрещаем сброс высоких технологий дикарям
                    if (partnerIsLower) continue; 

                    if (partnerStock.silver >= totalDealValue)
                    {
                        if (IsSaturated(defA, partnerStock, GetTotalLiving(partner)) && !actor.def.permanentEnemy) continue;
                        
                        actorStock.AddItem(defA, -amountA);
                        partnerStock.AddItem(defA, amountA);
                        partnerStock.silver -= Mathf.RoundToInt(totalDealValue);
                        actorStock.silver += Mathf.RoundToInt(totalDealValue);
                        
                        Log.Message("ED_Log_Export".Translate(actor.Name, actor.def.techLevel.ToString(), partner.Name, totalDealValue.ToString("F0")));
                    }
                    else if (!actorIsLower) // Денег нет. Если это сделка "между своими", они переходят на бартер
                    {
                        if (IsSaturated(defA, partnerStock, GetTotalLiving(partner)) && !actor.def.permanentEnemy) continue;

                        string keyB = partnerStock.inventory
                            .Where(kvp => kvp.Value > 0 && kvp.Key != "Silver" && kvp.Key != keyA)
                            .OrderByDescending(kvp => {
                                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
                                if (d == null) return 0f;
                                float m = globalPriceModifiers.TryGetValue(d, out float v) ? v : 1.0f;
                                return kvp.Value * (2.0f - m);
                            })
                            .Select(kvp => kvp.Key)
                            .FirstOrDefault(k => {
                                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(k);
                                return d != null && !IsSaturated(d, actorStock, GetTotalLiving(actor));
                            });

                        if (keyB == null) continue;
                        ThingDef defB = DefDatabase<ThingDef>.GetNamedSilentFail(keyB);
                        
                        if (defB == null || (isMilitary && defB.IsWeapon)) continue;

                        int amountB = Mathf.Max(1, Mathf.RoundToInt(totalDealValue / defB.BaseMarketValue));

                        if (partnerStock.inventory.TryGetValue(keyB, out int pHas) && pHas >= amountB)
                        {
                            actorStock.AddItem(defA, -amountA);
                            partnerStock.AddItem(defA, amountA);
                            partnerStock.AddItem(defB, -amountB);
                            actorStock.AddItem(defB, amountB);
                            Log.Message("ED_Log_Barter".Translate(actor.Name, partner.Name, defA.label, defB.label));
                        }
                    }
                }
            }
        }

        // Вспомогательный метод для проверки насыщения склада
        private bool IsSaturated(ThingDef d, VirtualStockpile s, int pop)
        {
            if (d == null || s == null) return false;

            int current = s.inventory.TryGetValue(d.defName, out int val) ? val : 0;

            float threshold = pop * 3.0f;
            if (d.stackLimit > 1) threshold = pop * 6f;

            return current >= threshold;
        }

        private string GetRandomValidItemKey(VirtualStockpile stock)
        {
            if (stock.inventory.Count == 0) return null;
            
            for (int i = 0; i < 5; i++)
            {
                string key = stock.inventory.Keys.RandomElement();
                if (stock.inventory[key] <= 0) continue;
                
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(key);
                if (d != null && d.category == ThingCategory.Item && d != ThingDefOf.Silver) return key;
            }
            return null;
        }
    }
}
