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

public override void WorldComponentTick()
{
    base.WorldComponentTick();
    
    // Сработает ровно 1 раз при первом снятии с паузы
    if (!initializedSession)
    {
        isCacheInitialized = false; 
        rawResourcesCache.Clear();
        manufacturedCache.Clear();
        foodCache.Clear();
        
        RunDailyTasks(); 
        
        initializedSession = true;
        Log.Message("ED_Log_EconomyInit".Translate());
    }

    int currentTick = Find.TickManager.TicksGame;
    int interval = Mathf.RoundToInt(EconomicsDemographyMod.Settings.updateIntervalHours * 2500f);

    // Выполняем согласно настройкам (интервал в часах -> тики)
    if (currentTick > 0 && currentTick % interval == 0)
    {
        RunDailyTasks();
    }
    
    // Выполняем раз в 15 дней
    if (currentTick > 0 && currentTick % 900000 == 0) // 900000
    {
        RunMonthlyCleanup();
    }
}

        private void RunDailyTasks()
        {
            CheckRuinsExpiration();
            ProcessDailyGrowth();
            RecalculateGlobalPrices();
            UpdatePlayerAssetsCache();
            
            // === РАСЧЕТ ИНФЛЯЦИИ (Серебро к серебру) ===
            float totalSilverNow = CalculateTotalWorldSilver();
            var factions = Find.FactionManager.AllFactionsListForReading;

            // === 0. ДИНАМИЧЕСКИЙ ЛИМИТ (НОВЫЕ ФРАКЦИИ) ===
            var humanlikeFactions = factions.Where(f => f != null && !f.IsPlayer && !f.def.hidden && f.def.humanlikeFaction).ToList();
            foreach (var f in humanlikeFactions)
            {
                if (knownFactionIDs == null) knownFactionIDs = new List<int>();
                if (!knownFactionIDs.Contains(f.loadID))
                {
                    if (initialWorldSilver > 0) // Если расчет уже запущен
                    {
                        float startingSilver = GetStockpile(f).silver;
                        initialWorldSilver += startingSilver; 
                        Log.Message(string.Format((string)"ED_Log_NewFactionAdded".Translate(), f.Name, startingSilver.ToString("F0")));
                    }
                    knownFactionIDs.Add(f.loadID);
                }
            }            
            // Авто-сброс, если точка отсчета "сломана" (например, осталась от тестов с Капиталом)
            if (initialWorldSilver > totalSilverNow * 50f) 
            {
                initialWorldSilver = -1f;
                Log.Message("ED_Log_InflationReset".Translate());
            }

            // ПЛАВНАЯ КАЛИБРОВКА (РОСТ ЭКОНОМИКИ)
            // Добавлена проверка на Золотой стандарт (без индексации)
            if (initialWorldSilver > 0 && !EconomicsDemographyMod.Settings.enableGoldStandard)
            {
                initialWorldSilver = Mathf.Lerp(initialWorldSilver, totalSilverNow, EconomicsDemographyMod.Settings.homeostasisEfficiency);
            }

            // Установка базового значения (самый первый расчет)
            if (initialWorldSilver < 0 && totalSilverNow > 500) 
            {
                initialWorldSilver = totalSilverNow;
                Log.Message("ED_Log_InflationBaselineSet".Translate(initialWorldSilver.ToString("F0")));
            }

            float baseline = initialWorldSilver > 0 ? initialWorldSilver : totalSilverNow;
            if (baseline <= 0) baseline = 10000f; // Дефолт
            float targetInflation = Mathf.Clamp(totalSilverNow / baseline, 0.1f, 10000.0f);

            float currentFactor = EconomicsDemographyMod.Settings.inflationUpdateFactor;
            currentInflation = Mathf.Lerp(currentInflation, targetInflation, currentFactor); 
            if (Mathf.Abs(currentInflation - targetInflation) < 0.001f) currentInflation = targetInflation;

            // Отладка для лога (синхронно с обновлением)
            if (Find.TickManager.TicksGame % Mathf.RoundToInt(EconomicsDemographyMod.Settings.updateIntervalHours * 2500f) == 0) 
                Log.Message(string.Format((string)"ED_Log_EconomyStatus".Translate(), currentInflation.ToString("P0"), totalSilverNow.ToString("F0")));
            
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f.def.hidden || f.IsPlayer || (f.leader == null && f.def.techLevel > TechLevel.Animal)) continue;

                int fid = f.loadID;
                float current = factionLimitModifiers.TryGetValue(fid, out float m) ? m : 1f;

                float drift = current * Verse.Rand.Range(0.99f, 1.01f);
                factionLimitModifiers[fid] = Mathf.Clamp(drift, 0.8f, 1.2f);

                // АВТО-ФИКС: Проверка на соответствие роли и места обитания (для фикса старых сохранений)
                if (factionTraits.TryGetValue(fid, out string trait))
                {
                    var settlement = Find.WorldObjects.Settlements.FirstOrDefault(s => s.Faction == f);
                    if (settlement != null)
                    {
                        var b = settlement.Biome;
                        bool inSpace = (b != null && (b.defName.Contains("Space") || b.defName.Contains("Asteroid") || b.defName.Contains("Vacuum")));
                        
                        if (inSpace && (trait == "Fisherman" || trait == "Farmer" || trait == "Rancher" || trait == "Hunter" || trait == "Lumberjack"))
                        {
                            factionTraits[fid] = AnalyzeTileForArchetype(settlement.Tile, f);
                            Log.Message(string.Format((string)"ED_Log_RoleReset".Translate(), f.Name, trait, factionTraits[fid].ToString()));
                        }
                    }
                }
            }
        }

        private void RunMonthlyCleanup()
        {
            Log.Message("ED_Log_AbandonedCheck".Translate());

            int currentTick = Find.TickManager.TicksGame;
            int oneYearTicks = 3600000;

            if (productionProgress != null)
            {
                var activeFactionIDs = Find.FactionManager.AllFactions.Select(f => f.loadID).ToList();
                var idsToRemove = new List<int>();

                foreach (var kvp in productionProgress)
                {
                    int fid = kvp.Key;
                    if (!activeFactionIDs.Contains(fid)) { idsToRemove.Add(fid); continue; }

                    var progWrapper = kvp.Value;
                    
                    var itemsToRemove = progWrapper.progress.Keys.Where(itemName => 
                    {
                        bool isAbandoned = progWrapper.lastUpdateTick.TryGetValue(itemName, out int lastTick) 
                                           && (currentTick - lastTick) > oneYearTicks;
                        
                        bool isDust = progWrapper.progress[itemName] < 0.1f;

                        return isAbandoned || isDust;
                    }).ToList();

                    foreach (var item in itemsToRemove)
                    {
                        progWrapper.progress.Remove(item);
                        progWrapper.lastUpdateTick.Remove(item);
                    }
                }
                foreach (var id in idsToRemove) productionProgress.Remove(id);
            }
            
            monthlyProductionPlans.Clear();
        }

        public void RecalculateGlobalPrices()
        {
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                if (f != null && !f.IsPlayer && !f.def.hidden && !f.defeated && f.def.humanlikeFaction && (f.leader != null || f.def.techLevel <= TechLevel.Animal))
                {
                    GetStockpile(f); 
                }
            }

            int totalWorldPop = Find.FactionManager.AllFactions
                .Where(f => !f.def.hidden && f.def.humanlikeFaction)
                .Sum(f => this.GetTotalLiving(f));

            int activeFactionCountInt = Find.FactionManager.AllFactions.Count(fac => fac != null && !fac.def.hidden);
            if (activeFactionCountInt <= 0) activeFactionCountInt = 5; 
            float activeFactionCount = (float)activeFactionCountInt;

            float demandMult = Mathf.Max(0.5f, totalWorldPop / (activeFactionCount * 25f)); 

            Dictionary<ThingDef, float> newModifiers = new Dictionary<ThingDef, float>();
            Dictionary<string, int> totalWorldStock = new Dictionary<string, int>();

            foreach (var kvp in factionStockpiles)
            {
                if (kvp.Value == null || kvp.Value.inventory == null) continue;
                foreach (var item in kvp.Value.inventory)
                {
                    if (!totalWorldStock.ContainsKey(item.Key)) totalWorldStock[item.Key] = 0;
                    totalWorldStock[item.Key] += item.Value;
                }
            }

            Dictionary<string, int> playerStuff = GetPlayerAssets();
            foreach (var item in playerStuff)
            {
                if (!totalWorldStock.ContainsKey(item.Key)) totalWorldStock[item.Key] = 0;
                totalWorldStock[item.Key] += item.Value;
            }

            HashSet<ThingDef> allTradeableItems = new HashSet<ThingDef>();
            foreach (var list in rawResourcesCache.Values) if (list != null) allTradeableItems.UnionWith(list);
            foreach (var list in manufacturedCache.Values) if (list != null) allTradeableItems.UnionWith(list);
            foreach (var list in foodCache.Values) if (list != null) allTradeableItems.UnionWith(list);

            foreach (ThingDef def in allTradeableItems)
            {
                if (def == null || def == ThingDefOf.Silver) continue;

                int currentAmount = totalWorldStock.ContainsKey(def.defName) ? totalWorldStock[def.defName] : 0;

                float breakEvenPoint = activeFactionCount * def.stackLimit * 0.10f * demandMult * 0.2f; 
                float surplusPoint = activeFactionCount * def.stackLimit * 0.8f * demandMult * 0.2f;

                if (def.stackLimit > 1) {
                    breakEvenPoint = Mathf.Max(breakEvenPoint, 8.0f * demandMult);
                    surplusPoint = Mathf.Max(surplusPoint, 30.0f * demandMult);
                } else {
                    breakEvenPoint = Mathf.Max(breakEvenPoint, 1.0f);
                    surplusPoint = Mathf.Max(surplusPoint, 3.0f);
                }

                float elasticity = 1.0f; 
                if (def.IsIngestible || def.IsMedicine) elasticity = 0.4f;
                else if (def.IsWeapon || def.IsApparel) elasticity = 0.8f;
                else if (def.BaseMarketValue > 500f) elasticity = 1.5f;

                float targetPriceMult = 1.0f;
                
                if (currentAmount < breakEvenPoint)
                {
                    float deficitFactor = 1f - ((float)currentAmount / breakEvenPoint);
                    targetPriceMult = 1.0f + (deficitFactor * (0.6f / elasticity)); 
                }
                else if (currentAmount > surplusPoint)
                {
                    float surplusFactor = surplusPoint / (float)currentAmount;
                    targetPriceMult = Mathf.Lerp(1.0f, surplusFactor, 0.5f / elasticity); 
                }

                targetPriceMult *= Rand.Range(0.95f, 1.05f); 
                targetPriceMult = Mathf.Clamp(targetPriceMult, 0.2f, 5.0f);

                float oldMult = this.globalPriceModifiers.TryGetValue(def, out float old) ? old : 1.0f;
                
                float finalSmoothedMult = Mathf.Lerp(oldMult, targetPriceMult, EconomicsDemographyMod.Settings.priceUpdateFactor);

                if (Mathf.Abs(finalSmoothedMult - 1.0f) < 0.01f) finalSmoothedMult = 1.0f;

                if (Mathf.Abs(finalSmoothedMult - 1.0f) > 0.001f)
                {
                    newModifiers[def] = finalSmoothedMult;
                }
            }
            
            this.globalPriceModifiers = newModifiers;
            Log.Message(string.Format((string)"ED_Log_MarketUpdated".Translate(), totalWorldPop, demandMult.ToString("F2")));
        }

        private void UpdatePlayerAssetsCache()
        {
            cachedPlayerAssets.Clear();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                
                List<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableAlways);
                int count = items.Count;
                
                for (int j = 0; j < count; j++)
                {
                    Thing t = items[j];
                    if (t.def.category == ThingCategory.Item && !t.def.Minifiable)
                    {
                        string name = t.def.defName;
                        if (cachedPlayerAssets.TryGetValue(name, out int val))
                        {
                            cachedPlayerAssets[name] = val + t.stackCount;
                        }
                        else
                        {
                            cachedPlayerAssets[name] = t.stackCount;
                        }
                    }
                }
            }

            var caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                var car = caravans[i];
                if (car.IsPlayerControlled)
                {
                    foreach (Thing t in car.AllThings)
                    {
                        if (t.def.category == ThingCategory.Item && !t.def.Minifiable)
                        {
                            string name = t.def.defName;
                            if (cachedPlayerAssets.TryGetValue(name, out int val))
                            {
                                cachedPlayerAssets[name] = val + t.stackCount;
                            }
                            else
                            {
                                cachedPlayerAssets[name] = t.stackCount;
                            }
                        }
                    }
                }
            }
        }

        private Dictionary<string, int> GetPlayerAssets()
        {
            // Используем уже заполненный кэш
            return new Dictionary<string, int>(cachedPlayerAssets);
        }

        public float CalculateTotalWorldSilver()
        {
            float total = 0f;
            
            // 1. Серебро фракций
            foreach (var kvp in factionStockpiles)
            {
                Faction f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.loadID == kvp.Key);
                if (f == null || f.defeated || f.def.hidden) continue;
                total += kvp.Value.silver;
            }

            // 2. Серебро игрока на картах
            foreach (var map in Find.Maps)
            {
                if (map != null) total += map.resourceCounter.Silver;
            }

            // 3. Серебро в караванах
            foreach (var caravan in Find.WorldObjects.AllWorldObjects.OfType<Caravan>())
            {
                if (caravan != null && caravan.IsPlayerControlled)
                {
                    total += caravan.Goods.Where(t => t.def == ThingDefOf.Silver).Sum(t => (float)t.stackCount);
                }
            }

            return total;
        }
    }
}
