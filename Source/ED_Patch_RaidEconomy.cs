using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using Verse.AI.Group;

namespace EconomicsDemography
{
    /// <summary>
    /// Содержит все исправления для экономики рейдов в одном файле.
    /// Отвечает за:
    /// 1. Подсчет стоимости нападающей армии.
    /// 2. Списание капитала (с банкротством при нехватке).
    /// 3. Фикс "инфляционного скачка" (игнор временного серебра на картах).
    /// </summary>
    public static class ED_Patch_RaidEconomy
    {
        // Временное хранилище стоимости текущего рейда
        [ThreadStatic]
        public static float currentRaidWealth = 0f;

        // ПАТЧ 1: Подсчитываем реальную стоимость каждой пешки, генерируемой для рейда
        [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
        public static class Patch_Tracking
        {
            [HarmonyPostfix]
            static void Postfix(Pawn __result)
            {
                if (__result == null || __result.Faction == null || WorldPopulationManager.IsManuallyAdding) return;
                
                var manager = Find.World?.GetComponent<WorldPopulationManager>();
                if (manager != null && manager.IsSimulatedFaction(__result.Faction))
                {
                    // Считаем ТОЛЬКО снаряжение и вещи (без стоимости "тушки" самого человека)
            float gearValue = 0;
            if (__result.apparel != null)
            {
                    foreach (var a in __result.apparel.WornApparel)
                    {
                        int q = a.TryGetComp<CompQuality>() != null ? (int)a.TryGetComp<CompQuality>().Quality : 2;
                        float pMult = (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(a.def, out float m)) ? m : 1.0f;
                        gearValue += a.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * pMult;
                    }
            }
            if (__result.equipment != null)
            {
                    foreach (var e in __result.equipment.AllEquipmentListForReading)
                    {
                        int q = e.TryGetComp<CompQuality>() != null ? (int)e.TryGetComp<CompQuality>().Quality : 2;
                        float pMult = (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(e.def, out float m)) ? m : 1.0f;
                        gearValue += e.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * pMult;
                    }
            }
            if (__result.inventory != null)
            {
                    foreach (var i in __result.inventory.innerContainer)
                    {
                        int q = i.TryGetComp<CompQuality>() != null ? (int)i.TryGetComp<CompQuality>().Quality : 2;
                        float pMult = (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(i.def, out float m)) ? m : 1.0f;
                        gearValue += (i.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * i.stackCount * pMult);
                    }
            }

            currentRaidWealth += gearValue;
                    Log.Warning($"[ED-DEBUG-BILL] Генерация: {__result.LabelShort} ({__result.Faction.Name}) экип={gearValue:F0} (итого рейд={currentRaidWealth:F0})");
                }
            }
        }

        // ПАТЧ 2: Списываем капитал при генерации ЛЮБЫХ ивентов (Рейды, Подкрепления, Торговцы, Гости)
        [HarmonyPatch(typeof(IncidentWorker), "TryExecute")]
        public static class Patch_RaidCosts
        {
            [HarmonyPrefix]
            static void Prefix(IncidentParms parms)
            {
                currentRaidWealth = 0f;
            }

            [HarmonyPostfix]
            static void Postfix(IncidentParms parms, bool __result)
            {
                if (__result && parms.faction != null)
                {
                    var manager = Find.World?.GetComponent<WorldPopulationManager>();
                    var stock = manager?.GetStockpile(parms.faction);
                    
                    if (stock != null && manager != null)
                    {
                        // Считаем РЕАЛЬНУЮ стоимость снаряжения после полной сборки рейда
                        // (PawnGroupKindWorker модифицирует экипировку ПОСЛЕ PawnGenerator)
                        Map map = parms.target as Map;
                        float costToDeduct = 0f;
                        if (map != null)
                        {
                            foreach (Pawn p in map.mapPawns.SpawnedPawnsInFaction(parms.faction))
                            {
                                if (p.apparel != null)
                                    foreach (var a in p.apparel.WornApparel)
                                    {
                                        int q = a.TryGetComp<CompQuality>() != null ? (int)a.TryGetComp<CompQuality>().Quality : 2;
                                        float pMult = (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(a.def, out float m)) ? m : 1.0f;
                                        costToDeduct += a.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * pMult;
                                    }
                                if (p.equipment != null)
                                    foreach (var e in p.equipment.AllEquipmentListForReading)
                                    {
                                        int q = e.TryGetComp<CompQuality>() != null ? (int)e.TryGetComp<CompQuality>().Quality : 2;
                                        float pMult = (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(e.def, out float m)) ? m : 1.0f;
                                        costToDeduct += e.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * pMult;
                                    }
                                
                                bool isTrader = p.GetLord()?.LordJob is RimWorld.LordJob_TradeWithColony;
                                if (p.inventory != null && !isTrader)
                                    foreach (var i in p.inventory.innerContainer)
                                    {
                                        int q = i.TryGetComp<CompQuality>() != null ? (int)i.TryGetComp<CompQuality>().Quality : 2;
                                        float pMult = (manager.globalPriceModifiers != null && manager.globalPriceModifiers.TryGetValue(i.def, out float m)) ? m : 1.0f;
                                        costToDeduct += (i.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * i.stackCount * pMult);
                                    }
                            }
                        }
                        
                        if (costToDeduct <= 0f) costToDeduct = currentRaidWealth;
                        if (costToDeduct <= 0f) { currentRaidWealth = 0f; return; }

                        // Пытаемся списать по-хорошему (как военные расходы: сначала пушки, броня, лекарства)
                        if (!stock.TryConsumeWealth(costToDeduct, manager.globalPriceModifiers, true))
                        {
                            // Записываем долг = (стоимость рейда - то, что фракция реально имела)
                            float wealthBefore = stock.GetTotalWealth();
                            float unpaid = costToDeduct - wealthBefore;
                            if (unpaid > 0)
                            {
                                int fid = parms.faction.loadID;
                                if (!manager.factionRaidDebt.ContainsKey(fid)) manager.factionRaidDebt[fid] = 0f;
                                manager.factionRaidDebt[fid] += unpaid;
                            }
                            
                            stock.silver = 0;
                            stock.inventory.Clear();
                            Log.Message(string.Format((string)"ED_Log_RaidBankruptApplied".Translate(), parms.faction.Name));
                        }
                        else
                        {
                            Log.Message(string.Format((string)"ED_Log_RaidCostApplied".Translate(), parms.faction.Name, costToDeduct.ToString("F0")));
                        }
                    }
                }
                currentRaidWealth = 0f;
            }
        }

        // ПАТЧ 3: Исключаем временное/вражеское серебро из расчета инфляции, 
        // чтобы капитал в UI не "прыгал" в 2 раза при входе рейда на карту.
        [HarmonyPatch(typeof(WorldPopulationManager), nameof(WorldPopulationManager.CalculateTotalWorldSilver))]
        public static class Patch_InflationFix
        {
            [HarmonyPrefix]
            static bool Prefix(WorldPopulationManager __instance, ref float __result)
            {
                float total = 0f;

                // 1. Серебро фракций
                foreach (var fid in __instance.factionStockpiles.Keys)
                {
                    var stock = __instance.factionStockpiles[fid];
                    if (stock == null) continue;
                    
                    Faction f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.loadID == fid);
                    if (f != null && __instance.IsSimulatedFaction(f))
                    {
                        total += stock.silver;
                    }
                }

                // 2. Серебро игрока на картах (ТОЛЬКО Haulable и НЕ запрещенное)
                foreach (var map in Find.Maps)
                {
                    if (map == null) continue;
                    
                    // Считаем вручную, чтобы отсечь серебро в инвентарях рейдеров и "forbidden" дроп
                    var silverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
                    foreach (var thing in silverThings)
                    {
                        if (!thing.IsForbidden(Faction.OfPlayer))
                            total += thing.stackCount;
                    }
                }

                // 3. Серебро в караванах игрока
                foreach (var caravan in Find.WorldObjects.AllWorldObjects.OfType<Caravan>())
                {
                    if (caravan != null && caravan.IsPlayerControlled)
                    {
                        total += caravan.Goods.Where(t => t.def == ThingDefOf.Silver).Sum(t => (float)t.stackCount);
                    }
                }

                __result = total;
                return false; // Заменяем оригинальный метод
            }
        }
    }
}
