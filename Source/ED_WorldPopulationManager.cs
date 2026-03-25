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
        public Dictionary<string, float> globalPriceModifiers = new Dictionary<string, float>();
        public Dictionary<int, List<string>> monthlyProductionPlans = new Dictionary<int, List<string>>();
        public float FactionWealth = 0f;
        public Dictionary<int, float> factionLimitModifiers = new Dictionary<int, float>();

        // === ИНФЛЯЦИЯ ===
        public float initialWorldSilver = -1f;
        public float currentInflation = 1f;
        
        // Счетчик глубины вызовов: скрывать инфляцию от Рассказчика и статистики богатства
        [ThreadStatic]
        public static int WealthCalculationDepth = 0;
        public static bool IsCalculatingWealth => WealthCalculationDepth > 0;
        
        // 1. КЭШ (Разбитый по категориям)
        private static Dictionary<TechLevel, List<ThingDef>> rawResourcesCache = new Dictionary<TechLevel, List<ThingDef>>();
        private static Dictionary<TechLevel, List<ThingDef>> manufacturedCache = new Dictionary<TechLevel, List<ThingDef>>();
        private static Dictionary<TechLevel, List<ThingDef>> foodCache = new Dictionary<TechLevel, List<ThingDef>>();
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
        private List<int> vagrantWarningsSent = new List<int>();
        private List<int> knownFactionIDs = new List<int>();
        public static bool IsManuallyAdding = false; 
        private bool initializedSession = false;
        
        public Dictionary<int, string> factionTraits = new Dictionary<int, string>();

        private readonly List<string> economicArchetypes = new List<string> 
        { 
            "Miner", "Farmer", "Medical", "Technician", "Tailor", "Generalist",
            "Warrior", "Alchemist", "Jeweler", "Hunter", "Lumberjack", "Rancher", "Fisherman", "Wholesale"
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

            if (Scribe.mode == LoadSaveMode.PostLoadInit && factionLimitModifiers == null) 
                factionLimitModifiers = new Dictionary<int, float>();

            if (Scribe.mode == LoadSaveMode.LoadingVars && productionProgress == null)
                productionProgress = new Dictionary<int, FactionProductionProgress>();
                    
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (factionTraits == null) factionTraits = new Dictionary<int, string>();
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
        }

        // === ИНИЦИАЛИЗАЦИЯ И СТАРТОВАЯ ГЕНЕРАЦИЯ ===
        public int GetPopulation(Faction f)
        {
            if (f == null || f.IsPlayer || f.def.hidden || !f.def.humanlikeFaction) return -1;
            if (f.leader == null && f.def.techLevel > TechLevel.Animal) return -1;
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

                // Стартовое серебро теперь генерируется внутри GenerateStartingStock и масштабируется от поселений, а не от голов населения.
                GetStockpile(f); 
                
                float femaleRatio = Rand.Range(0.45f, 0.55f); 
                factionFemales[fid] = Mathf.RoundToInt(baseAdults * femaleRatio);
                
                maturationBuffer[fid] = 0f;
                agingBuffer[fid] = 0f;
                
                // НОВОЕ: Назначаем специализацию
                if (!factionTraits.ContainsKey(fid))
                {
                    int homeTile = -1;
                    var settlement = Find.WorldObjects.Settlements.FirstOrDefault(s => s.Faction == f);
                    if (settlement != null) homeTile = settlement.Tile;

                    factionTraits[fid] = AnalyzeTileForArchetype(homeTile, f);

                    Log.Message($"<color=#77ffff>[E&D Экономика]</color> Фракция <b>{f.Name}</b> выбрала путь: <color=#ffee00>{factionTraits[fid]}</color>");
                }
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
            if (f == null || f.IsPlayer || f.loadID < 0 || f.def.hidden || !f.def.humanlikeFaction) return;
            if (f.leader == null && f.def.techLevel > TechLevel.Animal) return;
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
                            handled = true;
                        }

                        // Убыль ребенка при потере беременности
                        if (handled && amount < 0 && IsPregnant(contextPawn))
                        {
                            if (factionChildren[fid] >= 1.0f) factionChildren[fid] -= 1.0f;
                            else factionChildren[fid] = 0f;
                            Log.Message($"[E&D] Фракция {f.Name} потеряла ребенка из-за гибели/потери беременной пешки ({contextPawn.Name}).");
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
                        if (gender == Gender.Female || (gender == null && Rand.Value < 0.51f)) 
                            factionFemales[fid]++;
                    }
                }
            }

            int totalLiving = GetTotalLiving(f);

            if (totalLiving <= 0)
            {
                if (f.def.hidden || !f.def.humanlikeFaction || f.IsPlayer || (f.leader == null && f.def.techLevel > TechLevel.Animal)) 
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
                    
                    Find.LetterStack.ReceiveLetter("Вымирание: " + f.Name, 
                        $"Последний житель фракции {f.Name} погиб. Их поселения опустели.", 
                        LetterDefOf.NeutralEvent);
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
                    string status = (adults <= 0) ? "Выжившие дети и старики" : "Остатки фракции";

                    Messages.Message($"{status} {f.Name} ({totalLiving} чел.) теперь скитаются по миру без дома.", MessageTypeDefOf.NeutralEvent);
                    vagrantWarningsSent.Add(f.loadID);
                }
            }
        }

        private void DefeatFaction(Faction f)
        {
            f.defeated = true;
            f.hidden = true; 
            factionPopulation[f.loadID] = 0;
            Find.LetterStack.ReceiveLetter("Гибель фракции: " + f.Name, $"История народа {f.Name} завершена. Последние выжившие исчезли.", LetterDefOf.NeutralEvent, null, f);
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
            if (!factionStockpiles.ContainsKey(f.loadID))
            {
                factionStockpiles[f.loadID] = new VirtualStockpile();
                GenerateStartingStock(f, factionStockpiles[f.loadID]);
            }
            return factionStockpiles[f.loadID];
        }

        private void GenerateStartingStock(Faction f, VirtualStockpile stock)
        {
            if (f == null || f.IsPlayer) return;
            int fid = f.loadID;
            string trait = factionTraits.TryGetValue(fid, out string t) ? t : "Generalist";
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
            stock.silver += Rand.Range(500, 1500) * mult;

            // 2. Специализированные товары по архетипу
            List<ThingDef> potential = GetPotentialGoodsFor(f);

            if (potential.Count > 0)
            {
                // Для воинов жестко добавляем оружие и броню, чтобы они не были безоружными
                if (trait == "Warrior")
                {
                    var weapons = potential.Where(x => x.IsWeapon).InRandomOrder().Take(5);
                    foreach (var w in weapons) stock.AddItem(w, Rand.Range(1, 3));
                    
                    var armor = potential.Where(x => x.IsApparel && x.statBases.Any(s => s.stat == StatDefOf.ArmorRating_Sharp && s.value > 0.1f)).InRandomOrder().Take(3);
                    foreach (var a in armor) stock.AddItem(a, Rand.Range(1, 2));
                }
                // Для животноводов жестко продукты животных
                else if (trait == "Rancher")
                {
                    var items = potential.Where(x => x.defName.Contains("Wool") || x.defName.Contains("Milk") || x.defName.Contains("Egg") || x.IsMeat).InRandomOrder().Take(4);
                    foreach (var i in items) stock.AddItem(i, Rand.Range(10, 30));
                }

                // Добавляем стаки товаров согласно весам нашего архетипа (масштабируем кол-во линий от размера фракции)
                int lineCount = 15 + (mult * 3); 
                for (int i = 0; i < lineCount; i++)
                {
                    ThingDef chosen = potential.RandomElementByWeight(d => GetWeightForDef(d, trait));
                    if (chosen != null)
                    {
                        // Количество растет от кол-ва поселений
                        int countBase = chosen.stackLimit > 1 ? Rand.Range(15, 60) : Rand.Range(1, 3);
                        int finalCount = countBase + (countBase * (mult / 4));
                        stock.AddItem(chosen, finalCount);
                    }
                }
            }

            Log.Message($"[E&D] Стартовый запас {f.Name} ({trait}) готов. Капитал: {stock.GetTotalWealth():F1}$");
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
                    
                    stock.AddItem(t.def, t.stackCount);
                    absorbedCount++;
                }
                
                lastRestockTick[fid] = currentTick;
                if (absorbedCount > 0) 
                    Log.Message($"[E&D] Ресток: Фракция {f.Name} поглотила {absorbedCount} простых товаров.");
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
                    ThingDef def = (item is MinifiedThing m) ? m.InnerThing.def : item.def;
                    stock.AddItem(def, item.stackCount);
                }

                if (!item.Destroyed) item.Destroy();
            }
        }

        private bool IsPregnant(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;
            return pawn.health.hediffSet.hediffs.Any(h => h.def.defName == "Pregnant" || h.def.defName == "PregnancyHuman" || h.def.defName == "Pregnancy");
        }
    }
}
