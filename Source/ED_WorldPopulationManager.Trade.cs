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

                VirtualStockpile actorStock = GetStockpile(actor);
                float silverRatio = actorStock.silver / Mathf.Max(1f, actorStock.GetTotalWealth());
                bool needsMoney = silverRatio < 0.30f; // Приоритетная задача до достижения 30% капитала в серебре

                // Если денег меньше 30% — торгуем принудительно (игнорируя 2%), иначе — по шансу
                if (!needsMoney && Rand.Value > actionChance) continue;

                if (actor.def.permanentEnemy)
                {
                    // (Рейды - без изменений)
                    var victims = factions.Where(v => v != actor && !v.IsPlayer && !v.defeated && !v.def.hidden && !v.def.permanentEnemy && v.def.techLevel > TechLevel.Neolithic).ToList();
                    if (victims.Count == 0) continue;
                    Faction victim = victims.RandomElement();
                    VirtualStockpile victimStock = GetStockpile(victim);
                    if (victimStock.inventory.Count == 0) continue;
                    float stealPercent = 0f;
                    switch (victim.def.techLevel) {
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
                    VirtualStockpile.ParseKey(targetKey, out string defName, out int q1);
                    ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (itemDef != null) {
                        victimStock.inventory[targetKey] -= amountToSteal;
                        if (victimStock.inventory[targetKey] <= 0) victimStock.inventory.Remove(targetKey);
                        actorStock.AddItem(itemDef, amountToSteal, q1);
                        Log.Message("ED_Log_Raid".Translate(actor.Name, amountToSteal, itemDef.label, victim.Name));
                        
                        // Логируем воровство в историю обеих сторон (теперь в stealLogs)
                        float theftVal = itemDef.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q1) * amountToSteal;
                        TradingHistoryManager.AddLog(stealLogs, actor.loadID, new TradingLogEntry(Find.TickManager.TicksGame, itemDef.LabelCap, amountToSteal, theftVal, victim.loadID));
                        TradingHistoryManager.AddLog(stealLogs, victim.loadID, new TradingLogEntry(Find.TickManager.TicksGame, itemDef.LabelCap, -amountToSteal, -theftVal, actor.loadID));
                    }
                }
                else
                {
                    var partners = factions.Where(p => 
                        p != actor && !p.IsPlayer && !p.defeated && !p.def.hidden
                    ).ToList();

                    if (partners.Count == 0) continue;

                    // Если денег не хватает, целенаправленно ищем богатых партнеров
                    Faction partner = needsMoney ? partners.RandomElementByWeight(p => Mathf.Max(1f, GetStockpile(p).silver)) : partners.RandomElement();
                    VirtualStockpile partnerStock = GetStockpile(partner);

                    string keyA = actorStock.inventory
                        .Where(kvp => kvp.Value > 0 && kvp.Key != "Silver")
                        .OrderByDescending(kvp => {
                            VirtualStockpile.ParseKey(kvp.Key, out string rawDefA, out _);
                            ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(rawDefA);
                            if (d == null) return 0f;
                            float m = globalPriceModifiers.TryGetValue(d, out float val) ? val : 1.0f;
                            float priority = kvp.Value * (2.0f - m);
                            
                            if (needsMoney) {
                                priority *= d.BaseMarketValue / 5f; 
                            }
                            return priority;
                        })
                        .Select(kvp => kvp.Key)
                        .FirstOrDefault();

                    if (keyA == null) continue;
                    VirtualStockpile.ParseKey(keyA, out string defNameA, out int qA);
                    ThingDef defA = DefDatabase<ThingDef>.GetNamedSilentFail(defNameA);
                    if (defA == null) continue;

                    bool isMilitary = defA.IsWeapon || defA.IsApparel;
                    if (isMilitary && actor.def.techLevel != partner.def.techLevel) continue;

                    float marketMult = globalPriceModifiers.TryGetValue(defA, out float mm) ? mm : 1.0f;
                    float baseValue = defA.BaseMarketValue * marketMult;

                    float sellPrice = actor.def.permanentEnemy ? (baseValue * 0.5f) : baseValue;
                    float buyPrice = partner.def.permanentEnemy ? (baseValue * 2.0f) : baseValue;
                    float finalUnitPrice = (sellPrice + buyPrice) / 2f;

                    int currentHasA = actorStock.inventory[keyA];
                    int amountA = Mathf.CeilToInt(currentHasA * Rand.Range(0.1f, 0.3f));
                    
                    if (defA.stackLimit > 1)
                    {
                        // Если это "хлам" (< 5 стаков), продаем весь остаток целиком одним лотом
                        if (currentHasA < defA.stackLimit * 5) amountA = currentHasA;
                        // Иначе продаем минимум 5 полных стаков (Крупный ОПТ)
                        else amountA = Mathf.Max(amountA, defA.stackLimit * 5);
                    }
                    amountA = Mathf.Min(amountA, currentHasA);
                    
                    float totalDealValue = amountA * finalUnitPrice;

                    bool actorIsLower = actor.def.techLevel < partner.def.techLevel;
                    bool partnerIsLower = partner.def.techLevel < actor.def.techLevel;

                    // Строго запрещаем сброс высоких технологий дикарям
                    if (partnerIsLower) continue; 

                    if (partnerStock.silver >= totalDealValue)
                    {
                        if (IsSaturated(defA, partnerStock, GetTotalLiving(partner)) && !actor.def.permanentEnemy) continue;
                        
                        actorStock.inventory[keyA] -= amountA;
                        if (actorStock.inventory[keyA] <= 0) actorStock.inventory.Remove(keyA);
                        partnerStock.AddItem(defA, amountA, qA);
                        partnerStock.silver -= Mathf.RoundToInt(totalDealValue);
                        actorStock.silver += Mathf.RoundToInt(totalDealValue);
                        
                        Log.Message("ED_Log_Export".Translate(actor.Name, actor.def.techLevel.ToString(), partner.Name, totalDealValue.ToString("F0")));
                        
                        // Логируем экспорт и покупку
                        TradingHistoryManager.AddLog(saleLogs, actor.loadID, new TradingLogEntry(Find.TickManager.TicksGame, defA.LabelCap, amountA, totalDealValue, partner.loadID));
                        TradingHistoryManager.AddLog(buyLogs, partner.loadID, new TradingLogEntry(Find.TickManager.TicksGame, defA.LabelCap, amountA, totalDealValue, actor.loadID));
                    }
                    else if (!actorIsLower && !needsMoney) // БАРТЕР ЗАПРЕЩЕН, ПРИ НЕХВАТКЕ СЕРЕБРА (нужно накопление)
                    {
                        if (IsSaturated(defA, partnerStock, GetTotalLiving(partner)) && !actor.def.permanentEnemy) continue;

                        string keyB = partnerStock.inventory
                            .Where(kvp => kvp.Value > 0 && kvp.Key != "Silver" && kvp.Key != keyA)
                            .OrderByDescending(kvp => {
                                VirtualStockpile.ParseKey(kvp.Key, out string rawDefB, out _);
                                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(rawDefB);
                                if (d == null) return 0f;
                                float m = globalPriceModifiers.TryGetValue(d, out float v) ? v : 1.0f;
                                return kvp.Value * (2.0f - m);
                            })
                            .Select(kvp => kvp.Key)
                            .FirstOrDefault(k => {
                                VirtualStockpile.ParseKey(k, out string rawDefB, out _);
                                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(rawDefB);
                                return d != null && !IsSaturated(d, actorStock, GetTotalLiving(actor));
                            });

                        if (keyB == null) continue;
                        VirtualStockpile.ParseKey(keyB, out string defNameB, out int qB);
                        ThingDef defB = DefDatabase<ThingDef>.GetNamedSilentFail(defNameB);
                        
                        if (defB == null || (isMilitary && defB.IsWeapon)) continue;

                        int amountB = Mathf.RoundToInt(totalDealValue / Mathf.Max(0.1f, defB.BaseMarketValue));
                        int currentHasB = partnerStock.inventory[keyB];

                        if (defB.stackLimit > 1)
                        {
                            // Если у партнера этого мало, он отдает всё. Иначе - минимум 5 стаков.
                            if (currentHasB < defB.stackLimit * 5) amountB = currentHasB;
                            else amountB = Mathf.Max(amountB, defB.stackLimit * 5);
                        }
                        amountB = Mathf.Min(amountB, currentHasB);

                        if (currentHasB >= amountB && amountB > 0)
                        {
                            actorStock.inventory[keyA] -= amountA;
                            if (actorStock.inventory[keyA] <= 0) actorStock.inventory.Remove(keyA);

                            partnerStock.inventory[keyB] -= amountB;
                            if (partnerStock.inventory[keyB] <= 0) partnerStock.inventory.Remove(keyB);

                            partnerStock.AddItem(defA, amountA, qA);
                            actorStock.AddItem(defB, amountB, qB);
                            Log.Message("ED_Log_Barter".Translate(actor.Name, partner.Name, defA.label, defB.label));

                            // Логируем бартерную сделку (Обе стороны: продажа одного и покупка другого)
                            float valB = amountB * defB.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(qB);
                            TradingHistoryManager.AddLog(saleLogs, actor.loadID, new TradingLogEntry(Find.TickManager.TicksGame, defA.LabelCap, amountA, totalDealValue, partner.loadID));
                            TradingHistoryManager.AddLog(buyLogs, partner.loadID, new TradingLogEntry(Find.TickManager.TicksGame, defA.LabelCap, amountA, totalDealValue, actor.loadID));
                             TradingHistoryManager.AddLog(buyLogs, actor.loadID, new TradingLogEntry(Find.TickManager.TicksGame, defB.LabelCap, amountB, valB, partner.loadID));
                             TradingHistoryManager.AddLog(saleLogs, partner.loadID, new TradingLogEntry(Find.TickManager.TicksGame, defB.LabelCap, amountB, valB, actor.loadID));
                        }
                    }
                }
            }
        }

        // Вспомогательный метод для проверки насыщения склада
        private bool IsSaturated(ThingDef d, VirtualStockpile s, int pop)
        {
            if (d == null || s == null) return false;

            int current = s.GetCount(d);

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
                
                VirtualStockpile.ParseKey(key, out string rDef, out _);
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(rDef);
                if (d != null && d.category == ThingCategory.Item && d != ThingDefOf.Silver) return key;
            }
            return null;
        }
    }
}
