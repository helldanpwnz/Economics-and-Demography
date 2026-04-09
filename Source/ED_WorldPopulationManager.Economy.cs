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
        try 
        {
            isCacheInitialized = false; 
            if (rawResourcesCache != null) rawResourcesCache.Clear();
            if (manufacturedCache != null) manufacturedCache.Clear();
            if (foodCache != null) foodCache.Clear();
            
            RecalculateGlobalPrices();
            UpdatePlayerAssetsCache();
            
            Log.Message("ED_Log_EconomyInit".Translate());
        }
        catch (Exception ex)
        {
            Log.Error("ED_Log_EconomyInitError".Translate() + ": " + ex.ToString());
        }
        finally
        {
            initializedSession = true; // Обязательно выходим из цикла даже при ошибках, чтобы не убить TPS
        }
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
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (IsSimulatedFaction(f)) GetStockpile(f).MaintainVariety(this);
            }
            ProcessDailyGrowth();
            RecalculateGlobalPrices();
            UpdatePlayerAssetsCache();
            ProcessDebtRepayment();
            
            // === РАСЧЕТ ИНФЛЯЦИИ (На душу населения) ===
            float totalSilverNow = CalculateTotalWorldSilver();
            
            // Считаем текущее население всего мира (Симулируемые фракции + Колонисты игрока)
            float totalWorldPop = 0;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (IsSimulatedFaction(f))
                {
                    totalWorldPop += GetTotalLiving(f);
                }
                else if (f.IsPlayer)
                {
                    foreach (var map in Find.Maps) totalWorldPop += map.mapPawns.FreeColonistsCount;
                }
            }
            if (totalWorldPop <= 0) totalWorldPop = 100f; // Дефолт

            var factions = Find.FactionManager.AllFactionsListForReading;

            // === 0. ДИНАМИЧЕСКИЙ ЛИМИТ (НОВЫЕ ФРАКЦИИ) ===
            var humanlikeFactions = factions.Where(f => IsSimulatedFaction(f)).ToList();
            foreach (var f in humanlikeFactions)
            {
                if (knownFactionIDs == null) knownFactionIDs = new List<int>();
                if (!knownFactionIDs.Contains(f.loadID))
                {
                    if (initialWorldSilver > 0) // Если расчет уже запущен
                    {
                        float startingSilver = GetStockpile(f).silver;
                        initialWorldSilver += startingSilver; 
                        
                        // Также индексируем базовое население при добавлении фракции
                        if (initialWorldPop > 0) initialWorldPop += GetTotalLiving(f);

                        Log.Message(string.Format((string)"ED_Log_NewFactionAdded".Translate(), f.Name, startingSilver.ToString("F0")));
                    }
                    knownFactionIDs.Add(f.loadID);
                }
            }            
            
            // Установка базового значения (самый первый расчет)
            if (initialWorldSilver < 0 && totalSilverNow > 100) 
            {
                initialWorldSilver = totalSilverNow;
                initialWorldPop = totalWorldPop;
                Log.Message("ED_Log_InflationBaselineSet".Translate(initialWorldSilver.ToString("F0"), initialWorldPop.ToString("F0")));
            }

            // ПЛАВНАЯ КАЛИБРОВКА (РОСТ ЭКОНОМИКИ)
            if (initialWorldSilver > 0 && !EconomicsDemographyMod.Settings.enableGoldStandard)
            {
                initialWorldSilver = Mathf.Lerp(initialWorldSilver, totalSilverNow, EconomicsDemographyMod.Settings.homeostasisEfficiency);
                if (initialWorldPop > 0) initialWorldPop = Mathf.Lerp(initialWorldPop, totalWorldPop, EconomicsDemographyMod.Settings.homeostasisEfficiency);
            }

            // РАСЧЕТ: Серебро на человека (текущее vs базовое)
            float currentPerCapita = totalSilverNow / totalWorldPop;
            float initialPerCapita = (initialWorldSilver > 0 && initialWorldPop > 0) ? (initialWorldSilver / initialWorldPop) : currentPerCapita;
            
            if (initialPerCapita <= 0) initialPerCapita = 10f;
            
            float rawRatio = currentPerCapita / initialPerCapita;
            float logScale = EconomicsDemographyMod.Settings.inflationLogScale;
            float targetInflation = Mathf.Clamp(Mathf.Pow(rawRatio, logScale), 0.1f, 20.0f); // Кап 20х теперь безопаснее

            float currentFactor = EconomicsDemographyMod.Settings.inflationUpdateFactor;
            currentInflation = Mathf.Lerp(currentInflation, targetInflation, currentFactor); 
            if (Mathf.Abs(currentInflation - targetInflation) < 0.001f) currentInflation = targetInflation;

            // Отладка для лога (синхронно с обновлением)
            if (Find.TickManager.TicksGame % Mathf.RoundToInt(EconomicsDemographyMod.Settings.updateIntervalHours * 2500f) == 0) 
                Log.Message(string.Format((string)"ED_Log_EconomyStatus".Translate(), currentInflation.ToString("P0"), totalSilverNow.ToString("F0")));
            
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (!IsSimulatedFaction(f)) continue;

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
                
                // АВТО-ФИКС: Исправление демографии для кастомных/монополых рас (включая HAR) для старых сохранений
                if (factionPopulation.ContainsKey(fid))
                {
                    float targetFR = GetFactionRealFemaleRatio(f);
                    if (targetFR >= 0f)
                    {
                        int pop = factionPopulation[fid];
                        int currentFemales = factionFemales.TryGetValue(fid, out int fem) ? fem : 0;
                        int targetFemales = Mathf.RoundToInt(pop * targetFR);
                        
                        if (Mathf.Abs(currentFemales - targetFemales) >= 1)
                        {
                            factionFemales[fid] = targetFemales;
                        }
                    }
                }
            }

            Patch_MarketValue_Dynamic.isDirty = true;
        }

        private void ProcessDebtRepayment()
        {
            if (factionRaidDebt == null) factionRaidDebt = new Dictionary<int, float>();

            var fidsWithDebt = factionRaidDebt.Keys.ToList();
            foreach (int fid in fidsWithDebt)
            {
                float debt = factionRaidDebt[fid];
                if (debt <= 0)
                {
                    factionRaidDebt.Remove(fid);
                    continue;
                }

                Faction f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.loadID == fid);
                if (f == null || f.defeated)
                {
                    factionRaidDebt.Remove(fid);
                    continue;
                }

                VirtualStockpile stock = GetStockpile(f);
                if (stock == null) continue;

                float currentWealth = stock.GetTotalWealth();
                float safeMargin = GetMaintenanceCost(f); // Неприкосновенный запас = 1 суточное содержание фракции
                
                if (currentWealth > safeMargin)
                {
                    float repaymentAmount = currentWealth - safeMargin;
                    // Максимум 20% от текущего долга за сутки или фиксированный минимум гарантированный от излишков
                    float paymentLimit = Mathf.Max(debt * 0.2f, 1000f);
                    repaymentAmount = Mathf.Min(repaymentAmount, paymentLimit);
                    repaymentAmount = Mathf.Min(repaymentAmount, debt); // Не платим больше, чем должны

                    if (repaymentAmount > 0)
                    {
                        if (stock.TryConsumeWealth(repaymentAmount, globalPriceModifiers))
                        {
                            factionRaidDebt[fid] -= repaymentAmount;
                            if (factionRaidDebt[fid] <= 0f) factionRaidDebt.Remove(fid);
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
            EnsureGoodsCached();
            
            foreach (Faction f in Find.FactionManager.AllFactions)
            {
                if (IsSimulatedFaction(f))
                {
                    GetStockpile(f); 
                }
            }

            // 1. Сбор динамических данных мира
            Dictionary<TechLevel, int> techPop = new Dictionary<TechLevel, int>();
            foreach (TechLevel tl in Enum.GetValues(typeof(TechLevel))) techPop[tl] = 0;
            foreach (var f in Find.FactionManager.AllFactionsListForReading) {
                if (IsSimulatedFaction(f)) techPop[f.def.techLevel] += GetTotalLiving(f);
            }
            int totalWorldPop = techPop.Values.Sum();
            if (totalWorldPop <= 0) return;

            float totalSilverNow = CalculateTotalWorldSilver();
            int activeFactionCountInt = Find.FactionManager.AllFactions.Count(fac => IsSimulatedFaction(fac));
            float activeFactionNum = (float)Mathf.Max(1, activeFactionCountInt);

            HashSet<ThingDef> allTradeableItems = new HashSet<ThingDef>();
            if (rawResourcesCache != null)
                foreach (var list in rawResourcesCache.Values) if (list != null) allTradeableItems.UnionWith(list);
            if (manufacturedCache != null)
                foreach (var list in manufacturedCache.Values) if (list != null) allTradeableItems.UnionWith(list);
            if (foodCache != null)
                foreach (var list in foodCache.Values) if (list != null) allTradeableItems.UnionWith(list);

            // 2. Предварительный расчет "Мировой емкости" (Общий вес всех хотелок)
            Dictionary<ThingDef, float> rawWeights = new Dictionary<ThingDef, float>();
            float totalGlobalWeight = 0f;

            foreach (ThingDef def in allTradeableItems) {
                if (def == null || def == ThingDefOf.Silver) continue;

                float priceBase = Mathf.Max(1.5f, def.BaseMarketValue);
                float importance = 1.0f / Mathf.Sqrt(priceBase);
                
                float catMult = 0.5f;
                if (def.IsIngestible) catMult = 2.0f;
                else if (def.IsStuff || def.IsMedicine) catMult = 1.0f;

                float techFactor = 0f;
                foreach (var kvp in techPop) {
                    if (kvp.Value <= 0) continue;
                    float w = (float)kvp.Value / totalWorldPop;
                    if (kvp.Key >= def.techLevel) techFactor += w;
                    else techFactor += w * ((int)def.techLevel - (int)kvp.Key == 1 ? 0.15f : 0.02f);
                }

                float finalW = importance * catMult * techFactor;
                rawWeights[def] = finalW;
                totalGlobalWeight += finalW;
            }
            if (totalGlobalWeight <= 0) totalGlobalWeight = 1f;

            Dictionary<ThingDef, float> newModifiers = new Dictionary<ThingDef, float>();
            Dictionary<string, int> totalWorldStock = new Dictionary<string, int>();

            foreach (var kvp in factionStockpiles)
            {
                if (kvp.Value == null || kvp.Value.inventory == null) continue;
                foreach (var item in kvp.Value.inventory)
                {
                    VirtualStockpile.ParseKey(item.Key, out string dName, out _);
                    if (!totalWorldStock.ContainsKey(dName)) totalWorldStock[dName] = 0;
                    totalWorldStock[dName] += item.Value;
                }
            }

            Dictionary<string, int> playerStuff = GetPlayerAssets();
            foreach (var item in playerStuff)
            {
                if (!totalWorldStock.ContainsKey(item.Key)) totalWorldStock[item.Key] = 0;
                totalWorldStock[item.Key] += item.Value;
            }



            float assetsToSilverRatio = 8.0f; 
            float targetGlobalAssetValue = totalSilverNow * assetsToSilverRatio;

            foreach (ThingDef def in allTradeableItems)
            {
                if (def == null) continue;
                if (!rawWeights.TryGetValue(def, out float weight)) continue;

                float priceBase = Mathf.Max(1.0f, def.BaseMarketValue);
                float itemTargetValue = targetGlobalAssetValue * (weight / totalGlobalWeight);
                float equilibriumQty = itemTargetValue / priceBase;
                
                if (def.stackLimit <= 1) equilibriumQty = Mathf.Max(equilibriumQty, activeFactionNum * 0.5f);
                else equilibriumQty = Mathf.Max(equilibriumQty, activeFactionNum * 15f);

                int currentAmount = totalWorldStock.ContainsKey(def.defName) ? totalWorldStock[def.defName] : 0;
                float targetPriceMult = 1.0f;

                float ratio = (float)currentAmount / equilibriumQty;
                if (ratio < 0.85f) { // Дефицит
                    targetPriceMult = 1.0f + (1.0f - ratio) * 2.0f;
                } else if (ratio > 1.3f) { // Избыток
                    targetPriceMult = 1.3f / ratio;
                }

                targetPriceMult *= Rand.Range(0.97f, 1.03f);
                targetPriceMult = Mathf.Clamp(targetPriceMult, 0.15f, 10.0f);

                float oldMult = 1.0f;
                if (this.globalPriceModifiers != null)
                    oldMult = this.globalPriceModifiers.TryGetValue(def, out float old) ? old : 1.0f;
                float finalSmoothedMult = Mathf.Lerp(oldMult, targetPriceMult, EconomicsDemographyMod.Settings.priceUpdateFactor);
                
                if (Mathf.Abs(finalSmoothedMult - 1.0f) < 0.005f) finalSmoothedMult = 1.0f;
                if (finalSmoothedMult != 1.0f) newModifiers[def] = finalSmoothedMult;
            }

            this.globalPriceModifiers = newModifiers;
            Log.Message(string.Format((string)"ED_Log_MarketUpdated".Translate(), totalWorldPop, totalSilverNow.ToString("F0")));
        }

        private void UpdatePlayerAssetsCache()
        {
            cachedPlayerAssets.Clear();

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                
                // Оптимизация: Используем счетчик ресурсов карты вместо перебора всех вещей
                var counts = map.resourceCounter.AllCountedAmounts;
                foreach (var kvp in counts)
                {
                    if (kvp.Key == null || kvp.Key.Minifiable) continue;
                    string name = kvp.Key.defName;
                    cachedPlayerAssets[name] = (cachedPlayerAssets.TryGetValue(name, out int v) ? v : 0) + kvp.Value;
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
                if (kvp.Value == null) continue;
                Faction f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.loadID == kvp.Key);
                if (!IsSimulatedFaction(f)) continue;
                total += kvp.Value.silver;
            }

            // 2. Серебро игрока на картах
            foreach (var map in Find.Maps)
            {
                if (map != null && map.resourceCounter != null) total += map.resourceCounter.Silver;
            }



            return total;
        }

        public float CalculateTotalWorldPopulation()
        {
            float total = 0;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (IsSimulatedFaction(f))
                {
                    total += GetTotalLiving(f);
                }
                else if (f.IsPlayer)
                {
                    foreach (var map in Find.Maps) total += map.mapPawns.FreeColonistsCount;
                }
            }
            return total;
        }

        public float CalculateTotalWorldWealth()
        {
            float total = 0f;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (IsSimulatedFaction(f))
                {
                    var stock = GetStockpile(f);
                    if (stock != null) total += stock.GetTotalWealth();
                }
                else if (f.IsPlayer)
                {
                    foreach (var map in Find.Maps) {
                        if (map != null && map.wealthWatcher != null) total += map.wealthWatcher.WealthTotal;
                    }
                }
            }
            return total;
        }
    }
}
