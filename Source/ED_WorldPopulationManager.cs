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
    // Главный компонент мира, управляющий демографией, экономикой и производством фракций.
    // Разбит на несколько partial-файлов для удобства навигации.
    public enum PopulationPool { Random, Adult, Child, Elder }

    public partial class WorldPopulationManager : WorldComponent
    {
        // === ОСНОВНЫЕ ДАННЫЕ ===
        private Dictionary<string, int> cachedPlayerAssets = new Dictionary<string, int>();
        public Dictionary<int, int> factionPopulation = new Dictionary<int, int>(); // Взрослые (боеспособные)
        public Dictionary<int, VirtualStockpile> factionStockpiles = new Dictionary<int, VirtualStockpile>();
        public Dictionary<int, int> lastRestockTick = new Dictionary<int, int>();
        public Dictionary<int, int> lastBaseCount = new Dictionary<int, int>();
        
        // История торговли (новое)
        public Dictionary<int, List<TradingLogEntry>> prodLogs = new Dictionary<int, List<TradingLogEntry>>();
        public Dictionary<int, List<TradingLogEntry>> saleLogs = new Dictionary<int, List<TradingLogEntry>>();
        public Dictionary<int, List<TradingLogEntry>> buyLogs = new Dictionary<int, List<TradingLogEntry>>();
        public Dictionary<int, List<TradingLogEntry>> raidLogs = new Dictionary<int, List<TradingLogEntry>>();
        public Dictionary<int, List<TradingLogEntry>> stealLogs = new Dictionary<int, List<TradingLogEntry>>();
        public Dictionary<int, List<TradingLogEntry>> consumeLogs = new Dictionary<int, List<TradingLogEntry>>();

        public Dictionary<ThingDef, float> globalPriceModifiers = new Dictionary<ThingDef, float>();
        public Dictionary<int, List<string>> monthlyProductionPlans = new Dictionary<int, List<string>>();
        public float FactionWealth = 0f;
        public Dictionary<int, float> factionLimitModifiers = new Dictionary<int, float>();

        // === ИНФЛЯЦИЯ ===
        public float initialWorldSilver = -1f;
        public float currentInflation = 1f;
        public float initialWorldPop = -1f;
        
        // Счетчик глубины вызовов: скрывать инфляцию от Рассказчика и статистики богатства
        [ThreadStatic]
        public static int WealthCalculationDepth = 0;
        public static bool IsCalculatingWealth => WealthCalculationDepth > 0;
        
        // 1. КЭШ (Разбитый по категориям)
        public static Dictionary<TechLevel, List<ThingDef>> rawResourcesCache = new Dictionary<TechLevel, List<ThingDef>>();
        public static Dictionary<TechLevel, List<ThingDef>> manufacturedCache = new Dictionary<TechLevel, List<ThingDef>>();
        public static Dictionary<TechLevel, List<ThingDef>> foodCache = new Dictionary<TechLevel, List<ThingDef>>();
        public Dictionary<int, FactionProductionProgress> productionProgress = new Dictionary<int, FactionProductionProgress>();
        public static WorldPopulationManager Instance;
        private static bool isCacheInitialized = false;
        
        // === ДЕМОГРАФИЯ ===
        public Dictionary<int, float> factionChildren = new Dictionary<int, float>(); // Дети (0-14)
        public Dictionary<int, float> factionElders = new Dictionary<int, float>();   // Старики (60+) - абстрактный пул
        public Dictionary<int, int> factionFemales = new Dictionary<int, int>(); // Женщины
        
        // Буферы перетока
        public Dictionary<int, float> maturationBuffer = new Dictionary<int, float>(); // Накопитель: Дети/Мигранты -> Взрослые
        public Dictionary<int, float> agingBuffer = new Dictionary<int, float>();      // Накопитель: Взрослые -> Старики

        private List<int> initialized = new List<int>();
        public Dictionary<int, int> ruinsExpiration = new Dictionary<int, int>();
        public Dictionary<int, int> orbitalBases = new Dictionary<int, int>();
        public Dictionary<int, float> factionRaidDebt = new Dictionary<int, float>();
        private List<int> vagrantWarningsSent = new List<int>();
        private List<int> knownFactionIDs = new List<int>();

        // Ограничитель спавна лута (чтобы не спавнить дважды)
        public HashSet<int> processedSettlements = new HashSet<int>();
        public static bool IsManuallyAdding = false; 
        private bool initializedSession = false;
        
        public Dictionary<int, string> factionTraits = new Dictionary<int, string>();

        private readonly List<string> economicArchetypes = new List<string> 
        { 
            "Miner", "Farmer", "Medical", "Technician", "Tailor", "Generalist",
            "Warrior", "Chemist", "Jeweler", "Hunter", "Lumberjack", "Rancher", "Fisherman", "Wholesale"
        };

        public WorldPopulationManager(World world) : base(world) { Instance = this; }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref factionTraits, "factionTraits", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionPopulation, "factionPopulation", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastBaseCount, "lastBaseCount", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionFemales, "factionFemales", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref initialized, "initialized", LookMode.Value);
            Scribe_Collections.Look(ref ruinsExpiration, "ruinsExpiration", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref orbitalBases, "orbitalBases", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref vagrantWarningsSent, "vagrantWarningsSent", LookMode.Value);
            Scribe_Collections.Look(ref knownFactionIDs, "knownFactionIDs", LookMode.Value);
            Scribe_Collections.Look(ref factionStockpiles, "factionStockpiles", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref lastRestockTick, "lastRestockTick", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref FactionWealth, "FactionWealth", 0f);
            Scribe_Collections.Look(ref productionProgress, "productionProgress", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref factionLimitModifiers, "factionLimitModifiers", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref initialWorldSilver, "initialWorldSilver", -1f);
            Scribe_Values.Look(ref currentInflation, "currentInflation", 1f);
            Scribe_Values.Look(ref initialWorldPop, "initialWorldPop", -1f);
            Scribe_Collections.Look(ref factionRaidDebt, "factionRaidDebt", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref processedSettlements, "processedSettlements", LookMode.Value);
            Scribe_Collections.Look(ref globalPriceModifiers, "globalPriceModifiers", LookMode.Def, LookMode.Value);
            
            // Сохранение истории (новое)
            Scribe_Collections.Look(ref prodLogs, "prodLogs", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref saleLogs, "saleLogs", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref buyLogs, "buyLogs", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref raidLogs, "raidLogs", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref stealLogs, "stealLogs", LookMode.Value, LookMode.Deep);
            Scribe_Collections.Look(ref consumeLogs, "consumeLogs", LookMode.Value, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && processedSettlements == null)
                processedSettlements = new HashSet<int>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit && factionLimitModifiers == null) 
                factionLimitModifiers = new Dictionary<int, float>();

            if (Scribe.mode == LoadSaveMode.LoadingVars && productionProgress == null)
                productionProgress = new Dictionary<int, FactionProductionProgress>();
                    
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (factionTraits == null) factionTraits = new Dictionary<int, string>();
                
                var keys = factionTraits.Keys.ToList();
                foreach (var k in keys)
                {
                    if (factionTraits[k] == "Alchemist") factionTraits[k] = "Chemist";
                }

                if (factionStockpiles != null)
                {
                    foreach (var kvp in factionStockpiles)
                    {
                        if (kvp.Value != null && factionTraits.TryGetValue(kvp.Key, out string trait))
                        {
                            kvp.Value.isWarrior = (trait == "Warrior");
                            
                            // Очистка старых сохранений от мусорного качества
                            List<string> keysToMigrate = new List<string>();
                            foreach (string key in kvp.Value.inventory.Keys.ToList())
                            {
                                if (key.EndsWith("_0") || key.EndsWith("_1")) 
                                    keysToMigrate.Add(key);
                            }
                            foreach (string badKey in keysToMigrate)
                            {
                                int amount = kvp.Value.inventory[badKey];
                                kvp.Value.inventory.Remove(badKey);
                                
                                string baseName = badKey.Substring(0, badKey.Length - 2);
                                string goodKey = baseName + "_2"; // Подтягиваем до Normal
                                
                                if (!kvp.Value.inventory.ContainsKey(goodKey)) kvp.Value.inventory[goodKey] = 0;
                                kvp.Value.inventory[goodKey] += amount;
                            }
                        }
                    }
                }
            }
            
            if (Scribe.mode == LoadSaveMode.Saving || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref monthlyProductionPlans, "monthlyProductionPlans", LookMode.Value, LookMode.Value);
            }

            if (monthlyProductionPlans == null) 
                monthlyProductionPlans = new Dictionary<int, List<string>>();

            if (lastRestockTick == null) lastRestockTick = new Dictionary<int, int>();
            
            // Демография
            Scribe_Collections.Look(ref factionChildren, "factionChildren", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref factionElders, "factionElders", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref maturationBuffer, "maturationBuffer", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref agingBuffer, "agingBuffer", LookMode.Value, LookMode.Value);

            if (factionPopulation == null) factionPopulation = new Dictionary<int, int>();
            if (lastBaseCount == null) lastBaseCount = new Dictionary<int, int>();
            if (initialized == null) initialized = new List<int>();
            if (ruinsExpiration == null) ruinsExpiration = new Dictionary<int, int>();
            if (orbitalBases == null) orbitalBases = new Dictionary<int, int>();
            if (vagrantWarningsSent == null) vagrantWarningsSent = new List<int>();
            
            if (factionChildren == null) factionChildren = new Dictionary<int, float>();
            if (factionElders == null) factionElders = new Dictionary<int, float>();
            if (maturationBuffer == null) maturationBuffer = new Dictionary<int, float>();
            if (agingBuffer == null) agingBuffer = new Dictionary<int, float>();
            if (factionStockpiles == null) factionStockpiles = new Dictionary<int, VirtualStockpile>();
            if (factionRaidDebt == null) factionRaidDebt = new Dictionary<int, float>();

            if (prodLogs == null) prodLogs = new Dictionary<int, List<TradingLogEntry>>();
            if (saleLogs == null) saleLogs = new Dictionary<int, List<TradingLogEntry>>();
            if (buyLogs == null) buyLogs = new Dictionary<int, List<TradingLogEntry>>();
            if (raidLogs == null) raidLogs = new Dictionary<int, List<TradingLogEntry>>();
            if (stealLogs == null) stealLogs = new Dictionary<int, List<TradingLogEntry>>();
            if (consumeLogs == null) consumeLogs = new Dictionary<int, List<TradingLogEntry>>();
        }

        public bool IsInitialized(Faction f)
        {
            if (f == null || f.loadID < 0) return false;
            return initialized != null && initialized.Contains(f.loadID);
        }

        public bool IsSimulatedFaction(Faction f)
        {
            if (f == null || f.loadID < 0 || f.IsPlayer || f.def == null || f.defeated) return false;
            // Игнорируем скрытые и неписи-монстры (не-люди)
            if (f.def.hidden || (f.hidden == true) || !f.def.humanlikeFaction) return false;
            // Игнорируем временные и квестовые фракции
            if (f.temporary) return false;
            
            string dn = f.def.defName;
            if (dn.Contains("Ancient") || dn.Contains("Refugee") || dn.Contains("Beggar") || 
                dn.Contains("Worksite") || dn.Contains("Quest")) return false;
                
            return true;
        }

        public int GetPopulation(Faction f)
        {
            if (!IsSimulatedFaction(f)) return 0;
            int fid = f.loadID;

            // 1. ПЕРВИЧНАЯ ИНИЦИАЛИЗАЦИЯ (Для новых фракций)
            if (!initialized.Contains(fid))
            {
                int settlements = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
                int multiplier = (settlements > 0) ? settlements : 1;
                
                // === ИЗМЕНЕНИЕ: Динамический старт ===
                // Узнаем норму для этой фракции (например, 25 для Империи, 50 для Племени)
                int capPerBase = GetBaseCapacity(f);
                
                // Генерируем от 80% до 100% от нормы, чтобы не было штрафа сразу
                int minStart = Mathf.RoundToInt(capPerBase * 0.8f);
                int maxStart = Mathf.RoundToInt(capPerBase * 1.0f);
                int baseAdults = multiplier * Rand.Range(minStart, maxStart);
                
                // =====================================
                
                factionPopulation[fid] = baseAdults;
                
                // Устанавливаем множители в зависимости от тех-уровня
                float childMulti = 0.25f; 
                float elderMulti = 0.15f;
                if (f.def.techLevel <= TechLevel.Neolithic) 
                { 
                    childMulti = 0.40f; 
                    elderMulti = 0.10f; 
                }
                else if (f.def.techLevel >= TechLevel.Spacer) 
                { 
                    childMulti = 0.15f; 
                    elderMulti = 0.30f; 
                }

                float totalPeople = baseAdults + (baseAdults * childMulti) + (baseAdults * elderMulti);
                factionChildren[fid] = baseAdults * childMulti;
                factionElders[fid] = baseAdults * elderMulti;
                factionPopulation[fid] = baseAdults;

                // === ГЛОБАЛЬНОЕ РАСПРЕДЕЛЕНИЕ РОЛЕЙ ===
                // Если это первая инициализация фракции в этой сессии (или новом мире), 
                // проводим массовое распределение ролей для ВСЕХ симулируемых фракций сразу.
                // Это гарантирует разнообразие (чтобы не было 20 лесорубов) и покрывает все необходимые роли.
                if (factionTraits == null || factionTraits.Count == 0)
                {
                    BulkAssignTraits();
                }

                // НАЗНАЧАЕМ СПЕЦИАЛИЗАЦИЮ ДО создания склада, если она всё еще отсутствует
                if (!factionTraits.ContainsKey(fid))
                {
                    int homeTile = -1;
                    var settlement = Find.WorldObjects.Settlements.FirstOrDefault(s => s.Faction == f);
                    if (settlement != null) homeTile = settlement.Tile;

                    factionTraits[fid] = AnalyzeTileForArchetype(homeTile, f);
                    Log.Message(string.Format((string)"ED_Log_EconomyPathChosen".Translate(), f.Name, factionTraits[fid]));
                }

                // Стартовое серебро теперь генерируется внутри GenerateStartingStock и масштабируется от поселений, а не от голов населения.
                // isWarrior будет выставлен внутри GetStockpile -> GenerateStartingStock благодаря коду ниже
                var initStock = GetStockpile(f); 
                if (initStock != null) initStock.isWarrior = (factionTraits[fid] == "Warrior");
                
                float targetFR = GetFactionRealFemaleRatio(f);
                float femaleRatio = targetFR >= 0f ? targetFR : Rand.Range(0.40f, 0.60f); 

                factionFemales[fid] = Mathf.RoundToInt(baseAdults * femaleRatio);
                
                maturationBuffer[fid] = 0f;
                agingBuffer[fid] = 0f;
                
                initialized.Add(fid);
            }

            // 2. ПРОВЕРКА ЦЕЛОСТНОСТИ
            if (!factionChildren.ContainsKey(fid)) factionChildren[fid] = factionPopulation[fid] * 0.25f;
            if (!factionElders.ContainsKey(fid)) factionElders[fid] = factionPopulation[fid] * 0.15f;
            if (!maturationBuffer.ContainsKey(fid)) maturationBuffer[fid] = 0f;
            if (!agingBuffer.ContainsKey(fid)) agingBuffer[fid] = 0f;

            return factionPopulation.TryGetValue(fid, out int val) ? val : 0;
        }
        
        private void EnsureFactionDataExists(Faction f)
        {
            if (f == null || f.loadID < 0) return;
            int fid = f.loadID;

            if (!factionPopulation.ContainsKey(fid)) factionPopulation[fid] = 0;
            if (!factionFemales.ContainsKey(fid))    factionFemales[fid] = 0;
            if (!factionChildren.ContainsKey(fid))   factionChildren[fid] = 0f;
            if (!factionElders.ContainsKey(fid))     factionElders[fid] = 0f;
            if (!maturationBuffer.ContainsKey(fid))  maturationBuffer[fid] = 0f;
            if (!agingBuffer.ContainsKey(fid))       agingBuffer[fid] = 0f;
            if (!lastBaseCount.ContainsKey(fid))     lastBaseCount[fid] = 0;

            // Санация данных (фикс багов при потере лидеров и перекосах пола)
            if (factionPopulation[fid] < 0) factionPopulation[fid] = 0;
            if (factionFemales[fid] < 0) factionFemales[fid] = 0;
            if (factionFemales[fid] > factionPopulation[fid]) factionFemales[fid] = factionPopulation[fid];
            if (factionChildren[fid] < 0) factionChildren[fid] = 0f;
            if (factionElders[fid] < 0) factionElders[fid] = 0f;
        }

        public void SilentRestorePopulation(Faction f, int targetPop)
        {
            if (f == null) return;
            factionPopulation[f.loadID] = targetPop;
        }

        public int GetTotalLiving(Faction f)
        {
            if (f == null) return 0;
            int fid = f.loadID;

            int adults = GetPopulation(f); 

            if (!factionChildren.ContainsKey(fid)) factionChildren[fid] = adults * 0.25f;
            if (!factionElders.ContainsKey(fid)) factionElders[fid] = adults * 0.15f;

            int kids = Mathf.CeilToInt(factionChildren[fid]);
            int elders = Mathf.CeilToInt(factionElders[fid]);

            return adults + kids + elders;
        }

        public void ModifyPopulation(Faction f, int amount, Gender? gender = null, Pawn contextPawn = null, PopulationPool pool = PopulationPool.Random)
        {
            if (!IsSimulatedFaction(f)) return;
            int fid = f.loadID;

            if (!factionPopulation.ContainsKey(fid)) factionPopulation[fid] = 0;
            if (!factionFemales.ContainsKey(fid)) factionFemales[fid] = 0;
            if (!factionChildren.ContainsKey(fid)) factionChildren[fid] = 0f;
            if (!factionElders.ContainsKey(fid)) factionElders[fid] = 0f;

            if (amount < 0) // УБЫЛЬ
            {
                int toKill = Mathf.Abs(amount);
                for (int i = 0; i < toKill; i++)
                {
                    int adults = factionPopulation[fid];
                    int eldersCount = Mathf.CeilToInt(factionElders[fid]);
                    int kidsCount = Mathf.CeilToInt(factionChildren[fid]);
                    int total = adults + eldersCount + kidsCount;

                    if (total <= 0) break;

                    bool handled = false;
                    
                    // 1. Приоритет явному пулу
                    if (pool != PopulationPool.Random)
                    {
                        if (pool == PopulationPool.Child && factionChildren[fid] > 0)
                        {
                            if (factionChildren[fid] <= 1.0f) factionChildren[fid] = 0f;
                            else factionChildren[fid] -= 1.0f;
                            handled = true;
                        }
                        else if (pool == PopulationPool.Elder && factionElders[fid] > 0)
                        {
                            if (factionElders[fid] <= 1.0f) factionElders[fid] = 0f;
                            else factionElders[fid] -= 1.0f;
                            handled = true;
                        }
                        else if (pool == PopulationPool.Adult && factionPopulation[fid] > 0)
                        {
                            if (gender == Gender.Female && factionFemales[fid] > 0) factionFemales[fid]--;
                            else if (gender == null)
                            {
                                float femaleChance = (float)factionFemales[fid] / Mathf.Max(1f, (float)factionPopulation[fid]);
                                if (Rand.Value < femaleChance && factionFemales[fid] > 0) factionFemales[fid]--;
                            }
                            factionPopulation[fid]--;
                            if (factionFemales[fid] > factionPopulation[fid]) factionFemales[fid] = factionPopulation[fid];
                            handled = true;
                        }
                    }

                    // 2. Вторичный приоритет контексту пешки
                    if (!handled && contextPawn != null && i == 0) 
                    {
                        if (contextPawn.ageTracker.AgeBiologicalYears < 14 && factionChildren[fid] > 0)
                        {
                            if (factionChildren[fid] <= 1.0f) factionChildren[fid] = 0f;
                            else factionChildren[fid] -= 1.0f;
                            handled = true;
                        }
                        else if (contextPawn.ageTracker.AgeBiologicalYears >= 60 && factionElders[fid] > 0)
                        {
                            if (factionElders[fid] <= 1.0f) factionElders[fid] = 0f;
                            else factionElders[fid] -= 1.0f;
                            handled = true;
                        }
                        else if (factionPopulation[fid] > 0)
                        {
                            if (gender.HasValue)
                            {
                                if (gender == Gender.Female && factionFemales[fid] > 0) factionFemales[fid]--;
                            }
                            else
                            {
                                float femaleChance = (float)factionFemales[fid] / Mathf.Max(1f, (float)factionPopulation[fid]);
                                if (Rand.Value < femaleChance && factionFemales[fid] > 0) factionFemales[fid]--;
                            }
                            factionPopulation[fid]--;
                            if (factionFemales[fid] > factionPopulation[fid]) factionFemales[fid] = factionPopulation[fid];
                            handled = true;
                        }

                        // Убыль ребенка при потере беременности
                        if (handled && amount < 0 && IsPregnant(contextPawn))
                        {
                            if (factionChildren[fid] >= 1.0f) factionChildren[fid] -= 1.0f;
                            else factionChildren[fid] = 0f;
                            Log.Message(string.Format((string)"ED_Log_PawnDeathPregnancy".Translate(), f.Name, contextPawn.Name));
                        }
                    }

                    // 3. Случайный выбор
                    if (!handled)
                    {
                        float roll = Rand.Value * total;

                        if (roll < adults) // Смерть взрослого
                        {
                            if (gender.HasValue)
                            {
                                if (gender == Gender.Female && factionFemales[fid] > 0) factionFemales[fid]--;
                            }
                            else
                            {
                                float femaleChance = (float)factionFemales[fid] / Mathf.Max(1f, (float)factionPopulation[fid]);
                                if (Rand.Value < femaleChance && factionFemales[fid] > 0) factionFemales[fid]--;
                            }
                            factionPopulation[fid]--;
                            if (factionFemales[fid] > factionPopulation[fid]) factionFemales[fid] = factionPopulation[fid];
                        }
                        else if (roll < adults + eldersCount) // Смерть старика
                        {
                            if (factionElders[fid] <= 1.0f) factionElders[fid] = 0f;
                            else factionElders[fid] -= 1.0f;
                        }
                        else // Смерть ребенка
                        {
                            if (factionChildren[fid] <= 1.0f) factionChildren[fid] = 0f;
                            else factionChildren[fid] -= 1.0f;
                        }
                    }
                }
            }
            else // РОСТ
            {
                for (int i = 0; i < amount; i++)
                {
                    bool handled = false;
                    
                    if (pool != PopulationPool.Random)
                    {
                        if (pool == PopulationPool.Child) { factionChildren[fid]++; handled = true; }
                        else if (pool == PopulationPool.Elder) { factionElders[fid]++; handled = true; }
                    }

                    if (!handled && contextPawn != null && i == 0)
                    {
                        if (contextPawn.ageTracker.AgeBiologicalYears < 14)
                        {
                            factionChildren[fid]++;
                            handled = true;
                        }
                        else if (contextPawn.ageTracker.AgeBiologicalYears >= 60)
                        {
                            factionElders[fid]++;
                            handled = true;
                        }
                    }

                    if (!handled)
                    {
                        factionPopulation[fid]++;
                        float chance = 0.51f;
                        if (gender == Gender.Female) chance = 1.0f;
                        else if (gender == Gender.Male) chance = 0.0f;
                        else
                        {
                            float realR = GetFactionRealFemaleRatio(f);
                            chance = realR >= 0f ? realR : ((float)factionFemales[fid] / Mathf.Max(1f, (float)(factionPopulation[fid] - 1)));
                            chance = Mathf.Clamp01(chance);
                        }
                        if (Rand.Value < chance) factionFemales[fid]++;
                    }
                }
            }

            int totalLiving = GetTotalLiving(f);

            if (totalLiving <= 0)
            {
                if (!IsSimulatedFaction(f)) 
                {
                    return; 
                }
                
                List<Settlement> settlements = Find.WorldObjects.Settlements.Where(s => s.Faction == f).ToList();
                
                if (!settlements.Any())
                {
                    if (!f.defeated) DefeatFaction(f);
                }
                else
                {
                    foreach (var s in settlements)
                    {
                        int t = s.Tile;
                        s.Destroy();
                        CreateRuinsWithTimer(t, f);
                    }
                    if (!f.defeated) DefeatFaction(f);
                    
                    if (EconomicsDemographyMod.Settings.enableNotifications)
                    {
                        Find.LetterStack.ReceiveLetter("ED_FactionExtinctionTitle".Translate(f.Name), 
                            "ED_FactionExtinctionText".Translate(f.Name), 
                            LetterDefOf.NeutralEvent);
                    }
                }
            }
            else
            {
                CheckSettlementBalance(f, totalLiving);
            }
        }

        private void CheckSettlementBalance(Faction f, int totalLiving)
        {
            int groundBases = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
            int spaceBases = orbitalBases.TryGetValue(f.loadID, out int b) ? b : 0;

            if (groundBases + spaceBases == 0 && totalLiving > 0)
            {
                if (!f.defeated && !vagrantWarningsSent.Contains(f.loadID))
                {
                    int adults = factionPopulation.TryGetValue(f.loadID, out int a) ? a : 0;
                    string status = (adults <= 0) ? "ED_VagrantsStatusChildren".Translate() : "ED_VagrantsStatusRemnants".Translate();

                    if (EconomicsDemographyMod.Settings.enableNotifications)
                    {
                        Messages.Message("ED_VagrantsMessage".Translate(status, f.Name, totalLiving), MessageTypeDefOf.NeutralEvent);
                    }
                    vagrantWarningsSent.Add(f.loadID);
                }
            }
        }

        private void DefeatFaction(Faction f)
        {
            f.defeated = true;
            f.hidden = true; 
            factionPopulation[f.loadID] = 0;
            factionFemales[f.loadID] = 0;
            factionChildren[f.loadID] = 0f;
            factionElders[f.loadID] = 0f;
            maturationBuffer[f.loadID] = 0f;
            agingBuffer[f.loadID] = 0f;
            if (EconomicsDemographyMod.Settings.enableNotifications)
            {
                Find.LetterStack.ReceiveLetter("ED_FactionDefeatTitle".Translate(f.Name), "ED_FactionDefeatText".Translate(f.Name), LetterDefOf.NeutralEvent, null, f);
            }
        }

        private void CreateRuinsWithTimer(int tile, Faction faction)
        {
            WorldObject ruins = WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.DestroyedSettlement);
            ruins.Tile = tile;
            ruins.SetFaction(faction);
            Find.WorldObjects.Add(ruins);
            int days = Rand.Range(30, 61);
            ruinsExpiration[ruins.ID] = Find.TickManager.TicksGame + (days * 60000);
        }

        private void CheckRuinsExpiration()
        {
            if (ruinsExpiration.Count == 0) return;
            List<int> toRemove = new List<int>();
            int currentTick = Find.TickManager.TicksGame;
            foreach (var kvp in ruinsExpiration)
            {
                if (currentTick >= kvp.Value)
                {
                    WorldObject ruin = Find.WorldObjects.AllWorldObjects.FirstOrDefault(o => o.ID == kvp.Key);
                    if (ruin != null) ruin.Destroy();
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove) ruinsExpiration.Remove(id);
        }
        
        public VirtualStockpile GetStockpile(Faction f)
        {
            if (f == null || f.loadID < 0) return null;
            int fid = f.loadID;
            if (!factionStockpiles.ContainsKey(fid))
            {
                factionStockpiles[fid] = new VirtualStockpile();
                GenerateStartingStock(f, factionStockpiles[fid]);
            }
            
            // Гарантируем привязку ID для логов
            factionStockpiles[fid].factionID = fid;
            
            return factionStockpiles[fid];
        }

        private void GenerateStartingStock(Faction f, VirtualStockpile stock)
        {
            if (f == null || f.IsPlayer) return;
            int fid = f.loadID;
            string trait = factionTraits.TryGetValue(fid, out string t) ? t : "Generalist";
            stock.isWarrior = (trait == "Warrior");
            TechLevel tech = f.def.techLevel;

            // 1. Базовый набор выживания для всех (масштабируется от кол-ва баз и тех-уровня)
            int settlements = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
            int mult = Mathf.Max(1, settlements);

            if (tech <= TechLevel.Medieval)
            {
                stock.AddItem(ThingDefOf.Pemmican, Rand.Range(100, 200) * mult);
                stock.AddItem(ThingDefOf.MedicineHerbal, Rand.Range(10, 20) * mult);
            }
            else
            {
                stock.AddItem(ThingDefOf.MealSurvivalPack, Rand.Range(30, 60) * mult);
                stock.AddItem(ThingDefOf.MedicineIndustrial, Rand.Range(8, 15) * mult);
            }
            stock.silver += Rand.Range(3000, 8000) * mult;

            // 2. Специализированные товары по архетипу
            List<ThingDef> potential = GetPotentialGoodsFor(f);

            if (potential.Count > 0)
            {
                // Для воинов жестко добавляем оружие и броню, чтобы они не были безоружными
                if (trait == "Warrior")
                {
                    var weapons = potential.Where(x => x.IsWeapon).InRandomOrder().Take(5);
                    foreach (var w in weapons) stock.AddItem(w, Rand.Range(1, 3), (int)stock.GenerateRandomQuality(w));
                    
                    var armor = potential.Where(x => x.IsApparel && x.statBases.Any(s => s.stat == StatDefOf.ArmorRating_Sharp && s.value > 0.1f)).InRandomOrder().Take(3);
                    foreach (var a in armor) stock.AddItem(a, Rand.Range(1, 2), (int)stock.GenerateRandomQuality(a));
                }
                // Для животноводов жестко продукты животных
                else if (trait == "Rancher")
                {
                    var items = potential.Where(x => x.defName.Contains("Wool") || x.defName.Contains("Milk") || x.defName.Contains("Egg") || x.IsMeat).InRandomOrder().Take(4);
                    foreach (var i in items) stock.AddItem(i, Rand.Range(10, 30));
                }

                // Добавляем стаки товаров согласно весам нашего архетипа (масштабируем кол-во линий от размера фракции)
                int lineCount = 8 + mult; 
                for (int i = 0; i < lineCount; i++)
                {
                    ThingDef chosen = potential.RandomElementByWeight(d => GetWeightForDef(d, trait));
                    if (chosen != null)
                    {
                        // Количество растет от кол-ва поселений
                        int countBase = chosen.stackLimit > 1 ? Rand.Range(15, 60) : Rand.Range(1, 3);
                        int finalCount = countBase + (countBase * (mult / 4));
                        int q = -1;
                        if (chosen.HasComp(typeof(CompQuality))) q = (int)stock.GenerateRandomQuality(chosen);
                        stock.AddItem(chosen, finalCount, q);
                    }
                }
            }

            Log.Message(string.Format((string)"ED_Log_StartStockpile".Translate(), f.Name, trait, stock.GetTotalWealth().ToString("F1")));
        }

        public void AbsorbVanillaStock(Faction f, List<Thing> vanillaItems)
        {
            int fid = f.loadID;
            int currentTick = Find.TickManager.TicksGame;
            bool canRestock = !lastRestockTick.ContainsKey(fid) || (currentTick - lastRestockTick[fid] > 300000);

            if (canRestock)
            {
                var stock = GetStockpile(f);
                int absorbedCount = 0;

                foreach (Thing t in vanillaItems)
                {
                    if (t.def == ThingDefOf.Silver || t.def.category == ThingCategory.Pawn || t.def.Minifiable) continue;
                    
                    int quality = -1;
                    if (t.TryGetComp<CompQuality>() is CompQuality qualityComp) quality = (int)qualityComp.Quality;

                    stock.AddItem(t.def, t.stackCount, quality);
                    absorbedCount++;
                }
                
                lastRestockTick[fid] = currentTick;
                if (absorbedCount > 0) 
                    Log.Message(string.Format((string)"ED_Log_RestockAbsorbed".Translate(), f.Name, absorbedCount));
            }
        }

        // Определяем вместимость базы в зависимости от технологий
        private int GetBaseCapacity(Faction f)
        {
            var settings = EconomicsDemographyMod.Settings;
            switch (f.def.techLevel)
            {
                case TechLevel.Animal:      return Mathf.RoundToInt(settings.capAnimal);
                case TechLevel.Neolithic:   return Mathf.RoundToInt(settings.capNeolithic);
                case TechLevel.Medieval:    return Mathf.RoundToInt(settings.capMedieval); 
                case TechLevel.Industrial:  return Mathf.RoundToInt(settings.capIndustrial);
                case TechLevel.Spacer:      return Mathf.RoundToInt(settings.capSpacer);
                case TechLevel.Ultra:       return Mathf.RoundToInt(settings.capUltra); 
                case TechLevel.Archotech:   return Mathf.RoundToInt(settings.capArchotech);
                default:                    return 100;
            }
        }

        public void DepositGoods(Faction f, List<Thing> itemsToDeposit)
        {
            if (itemsToDeposit == null || itemsToDeposit.Count == 0) return;
            if (f == null) return;

            VirtualStockpile stock = GetStockpile(f);

            foreach (Thing item in itemsToDeposit)
            {
                if (item == null || item.Destroyed) continue;

                if (item.def == ThingDefOf.Silver)
                {
                    stock.silver += item.stackCount;
                }
                else
                {
                    Thing inner = (item is MinifiedThing m) ? m.InnerThing : item;
                    ThingDef def = inner.def;
                    int quality = -1;
                    if (inner.TryGetComp<CompQuality>() is CompQuality qualityComp)
                    {
                        quality = (int)qualityComp.Quality;
                    }
                    stock.AddItem(def, item.stackCount, quality);
                }

                if (!item.Destroyed) item.Destroy();
            }
        }

        private bool IsPregnant(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;
            return pawn.health.hediffSet.hediffs.Any(h => h.def.defName == "Pregnant" || h.def.defName == "PregnancyHuman" || h.def.defName == "Pregnancy");
        }

        // Массовое распределение ролей при старте мира или первом вызове
        private void BulkAssignTraits()
        {
            if (factionTraits == null) factionTraits = new Dictionary<int, string>();

            // 1. Получаем список всех симулируемых фракций, которым еще не назначена роль
            var allSimulated = Find.FactionManager.AllFactions
                .Where(f => IsSimulatedFaction(f) && !factionTraits.ContainsKey(f.loadID))
                .InRandomOrder()
                .ToList();

            if (!allSimulated.Any()) return;

            // 2. Создаем перемешанный пул ролей, чтобы гарантировать покрытие всех архетипов
            List<string> rolePool = new List<string>(economicArchetypes);
            rolePool.Remove("Generalist");
            rolePool.Shuffle();

            int roleIdx = 0;
            foreach (var f in allSimulated)
            {
                // Берём следующую роль из пула (зацикливаем, если фракций больше чем ролей)
                string candidate = rolePool[roleIdx % rolePool.Count];
                roleIdx++;

                int tile = -1;
                var settlement = Find.WorldObjects.Settlements.FirstOrDefault(s => s.Faction == f);
                if (settlement != null) tile = settlement.Tile;

                // 3. ПРОВЕРКА НА СОВМЕСТИМОСТЬ (Биомная логика)
                // Если роль критически не подходит (например, фермер в космосе), 
                // используем стандартный взвешенный выбор, который учитывает биом и уже занятые роли.
                if (tile != -1 && IsIncompatibleWithBiome(tile, f, candidate))
                {
                    factionTraits[f.loadID] = AnalyzeTileForArchetype(tile, f);
                }
                else
                {
                    factionTraits[f.loadID] = candidate;
                }
                
                Log.Message(string.Format((string)"ED_Log_EconomyPathChosenBulk".Translate(), f.Name, factionTraits[f.loadID]));
            }
        }

        private bool IsIncompatibleWithBiome(int tile, Faction f, string role)
        {
            if (f != null && f.def.defName == "TradersGuild" && role != "Wholesale" && role != "Technician" && role != "Warrior") return true;
            if (tile < 0 || tile >= Find.WorldGrid.TilesCount) return false;

            object tileObj = Find.WorldGrid[tile];
            string tName = tileObj.GetType().Name;
            bool isSpace = tName.Contains("Space") || tName.Contains("Asteroid") || tName.Contains("Vacuum");

            // Исключения для космоса (согласно правилам пользователя)
            if (isSpace)
            {
                if (role == "Fisherman" || role == "Farmer" || role == "Rancher" || role == "Hunter" || role == "Lumberjack") 
                    return true;
            }

            // Исключения для агрессивных фракций (пираты, каннибалы и т.д. не могут быть фермерами/медиками)
            if (f != null)
            {
                string fn = f.def.defName.ToLowerInvariant();
                if (fn.Contains("pirate") || fn.Contains("cannibal") || fn.Contains("waster") || fn.Contains("mercenary"))
                {
                    // Пираты не занимаются мирным трудом (фермерство/медицина). Остальное (лесоруб/охотник) допустимо.
                    if (role == "Farmer" || role == "Rancher" || role == "Medical" || role == "Jeweler" || role == "Tailor")
                        return true;
                }
            }

            return false;
        }

        public float GetFactionRealFemaleRatio(Faction f)
        {
            if (f?.def == null) return -1f;
            try
            {
                PawnKindDef kind = f.def.basicMemberKind;
                if (kind == null && f.def.pawnGroupMakers != null)
                {
                    var options = f.def.pawnGroupMakers.SelectMany(gm => gm.options ?? new List<PawnGenOption>()).ToList();
                    var specKind = options.FirstOrDefault(o => o.kind?.fixedGender.HasValue == true || o.kind?.race?.defName != "Human")?.kind;
                    kind = specKind ?? options.FirstOrDefault()?.kind;
                }

                if (kind != null)
                {
                    float baseRatio = 0.5f;
                    // 1. Проверяем фиксированный пол в Kind
                    if (kind.fixedGender.HasValue) baseRatio = kind.fixedGender.Value == Gender.Female ? 1f : 0f;
                    // 2. Проверяем Alien Races настройки
                    else if (kind.race?.GetType().Name.Contains("AlienRace") == true)
                    {
                        float maleProb = Traverse.Create(kind.race).Field("alienRace").Field("generalSettings").Field("maleGenderProbability").GetValue<float>();
                        baseRatio = Mathf.Clamp01(1f - maleProb);
                    }

                    // Если раса ОДНОПОЛАЯ (0 или 1) - возвращаем как есть, чтобы не было краша
                    if (baseRatio > 0.99f || baseRatio < 0.01f) return baseRatio;
                }
            }
            catch { }
            
            // Для всех остальных (где есть 2 пола) возвращаем -1, чтобы сработал твой разброс 40-60%
            return -1f; 
        }
    }
}
