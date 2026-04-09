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
        // Инициализация кэша товаров
        private void EnsureGoodsCached()
        {
            if (isCacheInitialized) return;

            foreach (TechLevel level in Enum.GetValues(typeof(TechLevel)))
            {
                rawResourcesCache[level] = new List<ThingDef>();
                manufacturedCache[level] = new List<ThingDef>();
                foodCache[level] = new List<ThingDef>();
            }

            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (!IsSafeDef(d)) continue;
                
                if (!d.tradeability.TraderCanSell()) continue;
                
                if (d.tradeTags == null) d.tradeTags = new List<string>();
                if (d.thingCategories == null) d.thingCategories = new List<ThingCategoryDef>();

                TechLevel itemTech = d.techLevel;

                if (itemTech == TechLevel.Undefined)
                {
                    itemTech = TechLevel.Industrial; 

                    string dName = d.defName.ToLowerInvariant();
                    bool isBasic = dName == "steel" || dName == "silver" || dName == "gold" || dName == "jade" || dName == "woodlog" || dName == "pemmican";

                    if (!isBasic && d.thingCategories != null)
                    {
                        foreach (var cat in d.thingCategories)
                        {
                            if (cat == null) continue;
                            string cName = cat.defName.ToLowerInvariant();
                            
                            if (cName.Contains("meat") || cName.Contains("plant") || cName.Contains("leather") || 
                                cName.Contains("stone") || cName.Contains("textile") || cName.Contains("wood") || cName == "foodraw")
                            {
                                isBasic = true;
                                break;
                            }
                        }
                    }
                    if (isBasic) itemTech = TechLevel.Neolithic;
                }

                if (itemTech <= TechLevel.Medieval && d.BaseMarketValue >= 200f && !d.IsStuff && !d.IsIngestible)
                {
                    itemTech = TechLevel.Spacer;
                }

                if (d.IsStuff) 
                {
                    rawResourcesCache[itemTech].Add(d);
                }
                else if (d.IsIngestible) 
                {
                    foodCache[itemTech].Add(d);
                }
                else if (d.IsWeapon || d.IsApparel || d.IsMedicine || d.IsDrug || 
                        typeof(Book).IsAssignableFrom(d.thingClass) || 
                        (d.thingCategories != null && d.thingCategories.Any(c => c != null && c.defName.Contains("Manufactured"))))
                {
                    manufacturedCache[itemTech].Add(d);
                }
                else 
                {
                    rawResourcesCache[itemTech].Add(d); 
                }
            }
            isCacheInitialized = true;
        }

        // Безопасная проверка ThingDef
        private bool IsSafeDef(ThingDef d)
        {
            if (d == null || d.category != ThingCategory.Item || d.BaseMarketValue <= 0) return false;
            
            if (!d.destroyable) return false; 

            if (d.destroyOnDrop || d.isUnfinishedThing) return false;
            if (d.thingClass == typeof(MinifiedThing)) return false; 
            if (d.IsBlueprint || d.IsFrame) return false;

            if (typeof(IThingHolder).IsAssignableFrom(d.thingClass)) return false;
            
            // Исключаем Xenogerm и Genepack, так как они ломают CanStackTogether без инициализации генов
            if (d.defName == "Xenogerm" || d.defName == "Genepack") return false;

            Thing t = null;
            try
            {
                t = ThingMaker.MakeThing(d, GenStuff.DefaultStuffFor(d));
                if (t == null) return false;

                string testLabel = t.Label; 
                if (t is ThingWithComps twc)
                {
                    foreach (var comp in twc.AllComps) { if (comp != null) _ = comp.GetType(); }
                }

                // Ультимативный тест на "торгуемость": ловит NRE (как в Biotech) при попытке сравнения предметов
                _ = TransferableUtility.CanStackTogether(t, t);

                return true; 
            }
            catch { return false; }
            finally
            {
                if (t != null && !t.Destroyed) 
                {
                    try { t.Destroy(); } catch { }
                }
            }
        }

        // Вес предмета для архетипа
        private float GetWeightForDef(ThingDef d, string trait)
        {
            if (d == null || d == ThingDefOf.Silver) return 0f;
            string name = d.defName.ToLowerInvariant();

            // Базовый вес 10.0f для всех предметов, чтобы экономика была живой
            float baseWeight = 10.0f;
            float bonus = 0f;

            bool isMineral = d.stuffProps?.categories?.Contains(StuffCategoryDefOf.Metallic) == true || 
                             d.stuffProps?.categories?.Contains(StuffCategoryDefOf.Stony) == true;
            
            // Руды доступны только шахтерам и оптовикам
            if (isMineral && trait != "Miner" && trait != "Generalist" && trait != "Jeweler" && trait != "Wholesale") return 0.001f;

            switch (trait)
            {
                case "Miner":
                    bool isPrecious = name.Contains("gold") || name.Contains("jade");
                    if (isMineral && !isPrecious) bonus = 1000f;
                    if (name.Contains("component")) bonus = 1000f;
                    break;

                case "Farmer":
                    if (d.ingestible?.foodType.HasFlag(FoodTypeFlags.VegetableOrFruit) == true || 
                        d.ingestible?.foodType.HasFlag(FoodTypeFlags.Seed) == true) bonus = 1000f;
                    break;

                case "Lumberjack":
                    if (d == ThingDefOf.WoodLog || (d.stuffProps?.categories?.Any(c => c.defName == "Woody") == true)) bonus = 1000f;
                    break;

                case "Fisherman":
                    bool isFish = d.thingCategories?.Any(c => c.defName.Contains("Fish")) == true || name.Contains("fish");
                    if (isFish) bonus = 1000f;
                    if (d.IsMeat) return 0.001f;
                    break;

                case "Hunter":
                    if (name.Contains("human") || name.Contains("insect")) return 0.001f;
                    if (d.IsMeat || d.IsLeather || name.Contains("tusk") || name.Contains("horn")) bonus = 1000f;
                    break;

                case "Rancher":
                    if (name.Contains("human") || name.Contains("insect")) return 0.001f;
                    if (name.Contains("wool") || name.Contains("milk") || name.Contains("egg")) bonus = 1000f;
                    if (d.IsMeat) bonus = 300f;
                    break;

                case "Tailor":
                    if (name.Contains("human") || name.Contains("insect")) return 0.001f;
                    bool isTextile = d.stuffProps?.categories?.Contains(StuffCategoryDefOf.Fabric) == true || 
                                     d.stuffProps?.categories?.Contains(StuffCategoryDefOf.Leathery) == true;
                    if (isTextile || (d.IsApparel && !d.statBases.Any(s => s.stat == StatDefOf.ArmorRating_Sharp && s.value > 0.1f))) bonus = 1000f;
                    break;

                case "Technician":
                    if (name.Contains("component") || d.defName == "Neutroamine" || d.isTechHediff) bonus = 1000f;
                    if (d.techLevel >= TechLevel.Industrial) bonus = 300f;
                    break;

                case "Jeweler":
                    if (d.HasComp(typeof(CompArt)) || name.Contains("gold") || name.Contains("jade")) bonus = 1000f;
                    break;

                case "Medical":
                    if (d.IsMedicine || d.isTechHediff) bonus = 1000f;
                    break;

                case "Chemist":
                    if (d.IsDrug || name.Contains("chemfuel")) bonus = 1000f;
                    break;

                case "Warrior":
                    if (d.IsWeapon) bonus = 1000f;
                    if (d.IsApparel && d.statBases.Any(s => s.stat == StatDefOf.ArmorRating_Sharp && s.value > 0.1f)) bonus = 1000f;
                    break;
                case "Wholesale":
                    bonus = 50.0f;
                    break;
            }

            return baseWeight + bonus;
        }

        private bool IsProfile(ThingDef d, string trait)
        {
            return GetWeightForDef(d, trait) >= 50.0f;
        }    

        // Анализ тайла для определения архетипа
        private string AnalyzeTileForArchetype(int tileID, Faction f = null)
        {
            // ПРИНУДИТЕЛЬНЫЙ ВЫБОР ДЛЯ ТОРГОВОЙ ГИЛЬДИИ
            if (f != null && f.def.defName == "TradersGuild")
            {
                return new[] { "Wholesale", "Technician", "Warrior" }.RandomElement();
            }

            if (tileID < 0 || tileID >= Find.WorldGrid.TilesCount) return "Generalist";

            object tileObj = Find.WorldGrid[tileID];
            
            // Проверка на космос (с учетом 1.6 Odyssey)
            string tName = tileObj.GetType().Name;
            bool isSpace = tName.Contains("Space") || tName.Contains("Asteroid") || tName.Contains("Vacuum");
            
            if (isSpace) 
            {
                return new[] { "Technician", "Wholesale", "Warrior", "Medical", "Miner" }.RandomElement();
            }

            BiomeDef biome = Traverse.Create(tileObj).Field("biome").GetValue<BiomeDef>() ?? 
                             Traverse.Create(tileObj).Field("biomeDef").GetValue<BiomeDef>();

            if (biome == null) return "Generalist";

            Hilliness hilliness = Hilliness.Flat;
            try { hilliness = Traverse.Create(tileObj).Field("hilliness").GetValue<Hilliness>(); } catch {}

            float temperature = 20f;
            float rainfall = 0f;
            try
            {
                temperature = Traverse.Create(tileObj).Field("temperature").GetValue<float>();
                rainfall = Traverse.Create(tileObj).Field("rainfall").GetValue<float>();
            }
            catch {}

            float miner = 1f, farmer = 1f, medical = 1f, technician = 1f, tailor = 1f;
            float warrior = 1f, chemist = 1f, jeweler = 1f, hunter = 1f, lumberjack = 1f;
            float rancher = 1f, fisherman = 1f;

            if (hilliness == Hilliness.Mountainous) miner += 12f;
            else if (hilliness == Hilliness.LargeHills) miner += 6f;

            float plantDensity = biome.plantDensity; 
            
            if (plantDensity > 0.8f) farmer += 8f; 
            else if (plantDensity > 0.5f) farmer += 4f; 
            else if (plantDensity < 0.1f) farmer -= 5f; 

            if (plantDensity > 0.7f) lumberjack += 15f; 
            else if (plantDensity > 0.4f) lumberjack += 7f;

            if (plantDensity > 0.3f && plantDensity < 0.8f) rancher += 7f;

            if (rainfall > 1500f) medical += 6f; 
            if (rainfall > 800f) farmer += 3f;
            if (rainfall > 1000f && temperature > 20f) chemist += 10f; 

            if (temperature < -10f || temperature > 35f) { technician += 7f; warrior += 5f; }
            if (temperature > 10f && temperature < 25f && plantDensity > 0.4f) jeweler += 8f; 

            if (biome.animalDensity > 1.5f) tailor += 6f;
            if (biome.animalDensity > 1.2f) hunter += 12f;
            if (biome.animalDensity > 1.0f) rancher += 10f;

            if (plantDensity < 0.1f) warrior += 10f; 

            if (biome.defName.Contains("Techno") || biome.defName.Contains("Waste")) technician += 10f;
            
            string bName = biome.defName.ToLower();
            if (bName.Contains("ocean") || bName.Contains("sea") || bName.Contains("coast") || 
                bName.Contains("river") || bName.Contains("island") || bName.Contains("beach")) fisherman += 25f;

            if (f != null)
            {
                if (f.def.techLevel > TechLevel.Industrial) hunter = 0f;
                
                string fName = f.def.defName.ToLowerInvariant();
                if (fName.Contains("pirate") || fName.Contains("cannibal") || fName.Contains("waster") || fName.Contains("mercenary"))
                {
                    farmer = 0f;
                    rancher = 0f;
                    medical = 0f;
                    jeweler = 0f;
                    tailor = 0f;
                    warrior += 20f;
                    chemist += 5f;
                }
            }

            var scores = new Dictionary<string, float>
            {
                { "Miner", miner }, { "Farmer", farmer }, { "Medical", medical }, { "Technician", technician },
                { "Tailor", tailor }, { "Warrior", warrior }, { "Chemist", chemist }, { "Jeweler", jeweler },
                { "Hunter", hunter }, { "Lumberjack", lumberjack }, { "Rancher", rancher }, { "Fisherman", fisherman }
            };

            foreach (var role in scores.Keys.ToList())
            {
                int existingCount = factionTraits.Values.Count(v => v == role);
                // Бонус к разнообразию: Снижаем вес роли за каждое существующее вхождение
                if (existingCount > 0) scores[role] *= (1.0f / (existingCount + 1.0f));
            }

            return scores.RandomElementByWeight(kvp => Mathf.Max(0.01f, kvp.Value)).Key;
        }

        private List<ThingDef> GetPotentialGoodsFor(Faction f)
        {
            List<ThingDef> potential = new List<ThingDef>();
            void AddFromLevel(TechLevel level)
            {
                if (rawResourcesCache.ContainsKey(level)) potential.AddRange(rawResourcesCache[level]);
                if (manufacturedCache.ContainsKey(level)) potential.AddRange(manufacturedCache[level]);
                if (foodCache.ContainsKey(level)) potential.AddRange(foodCache[level]);
            }
            AddFromLevel(f.def.techLevel);
            if (potential.Count < 5 && f.def.techLevel > TechLevel.Neolithic) AddFromLevel(f.def.techLevel - 1);
            return potential;
        }

        // Основной метод производства
        private void ProduceGoodsCategorized(Faction f, int adults, float elders, float kids)
        {
            EnsureGoodsCached(); 
            
            float laborForce = (adults * 1.0f) + (kids * 0.5f) + (elders * 0.7f);
            if (laborForce <= 0) return;

            VirtualStockpile stock = GetStockpile(f);
            if (stock == null) return;

            // Оптимизация: Агрегируем инвентарь один раз на весь метод
            Dictionary<ThingDef, int> itemCountsAggregated = new Dictionary<ThingDef, int>();
            foreach (var kvp in stock.inventory)
            {
                VirtualStockpile.ParseKey(kvp.Key, out string dName, out _);
                ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(dName);
                if (d != null)
                {
                    itemCountsAggregated[d] = (itemCountsAggregated.TryGetValue(d, out int val) ? val : 0) + kvp.Value;
                }
            }

            float techMult = 1.0f;
            switch (f.def.techLevel)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic:   techMult = 1.0f; break;
                case TechLevel.Medieval:    techMult = 4.0f; break;
                case TechLevel.Industrial:  techMult = 15.0f; break;
                case TechLevel.Spacer:      techMult = 20.0f; break;
                case TechLevel.Ultra:       techMult = 30.0f; break;
                case TechLevel.Archotech:   techMult = 40.0f; break;
            }

            float economyOfScale = 1f + Mathf.Log10(Mathf.Max(1f, laborForce / 50f));
            float complexityPenalty = Mathf.Clamp(1.0f - (laborForce / 30000.0f), 0.1f, 1.0f);
            float efficiencyScale = economyOfScale * complexityPenalty;
            float techProdMult = 1f;
            var s = EconomicsDemographyMod.Settings;
            switch (f.def.techLevel)
            {
                case TechLevel.Animal:      techProdMult = s.prodAnimal; break;
                case TechLevel.Neolithic:   techProdMult = s.prodNeolithic; break;
                case TechLevel.Medieval:    techProdMult = s.prodMedieval; break;
                case TechLevel.Industrial:  techProdMult = s.prodIndustrial; break;
                case TechLevel.Spacer:      techProdMult = s.prodSpacer; break;
                case TechLevel.Ultra:       techProdMult = s.prodUltra; break;
                case TechLevel.Archotech:   techProdMult = s.prodArchotech; break;
            }

            float inflationBonus = (this.currentInflation > 1.0f) ? this.currentInflation : 1.0f;
            float baseProductionValue = laborForce * Rand.Range(0.60f, 1.00f) * techMult * efficiencyScale * techProdMult;

            // [EXCEPTION] Специальный бонус для Империи (DLC Royalty)
            // Увеличиваем производственную мощность в 3 раза, так как это технологически продвинутые колонизаторы
            if (f.def.defName == "Empire")
            {
                baseProductionValue *= 2.0f;
            }

            float marketProductionValue = baseProductionValue * inflationBonus;
                    
            float currentTotalWealth = stock.GetTotalWealth();
            float targetSilver = currentTotalWealth * 0.10f;

            string traitName = factionTraits.TryGetValue(f.loadID, out string tName) ? tName : "Generalist";
            if (f.def.defName == "TradersGuild" || traitName == "Miner" || traitName == "Jeweler")
            {
                float silverEffort = 0f;
                if (f.def.defName == "TradersGuild") silverEffort = 0.15f;
                else if (traitName == "Miner") silverEffort = 0.10f;
                else if (traitName == "Jeweler") silverEffort = 0.05f;
                else silverEffort = 0f; // Все остальные производят ТОЛЬКО товары

                float hillMult = 1.0f;
                if (traitName == "Miner" || traitName == "Jeweler")
                {
                    int hillsBonus = Find.WorldObjects.Settlements.Count(objs => objs.Faction == f && objs.Visitable && Find.WorldGrid[objs.Tile].hilliness >= Hilliness.LargeHills);
                    hillMult = 1.0f + (hillsBonus * 0.2f);
                }

                int producedSilver = Mathf.RoundToInt(baseProductionValue * silverEffort * hillMult);
                stock.silver += producedSilver;
                marketProductionValue -= producedSilver;
            }

            if (f.def.permanentEnemy) stock.silver -= Mathf.RoundToInt(stock.silver * 0.05f);
            
            float remainingProductionValue = marketProductionValue;
            List<ThingDef> potentialGoods = new List<ThingDef>();
            TechLevel minRestrictiveLevel = (f.def.techLevel > TechLevel.Medieval) ? f.def.techLevel - 1 : TechLevel.Neolithic;

            for (TechLevel l = TechLevel.Neolithic; l <= f.def.techLevel; l++)
            {
                if (rawResourcesCache.ContainsKey(l)) potentialGoods.AddRange(rawResourcesCache[l]);
                if (foodCache.ContainsKey(l)) potentialGoods.AddRange(foodCache[l]);

                if (manufacturedCache.ContainsKey(l))
                {
                    foreach (var d in manufacturedCache[l])
                    {
                        // Оружие, броня (Apparel) и Медицина — только свой уровень и один ниже
                        if (d.IsWeapon || d.IsApparel || d.IsMedicine)
                        {
                            if (l >= minRestrictiveLevel) potentialGoods.Add(d);
                        }
                        else // Всё остальное (наркотики, книги и т.д.) — без ограничений по тех-уровню (до макс. уровня фракции)
                        {
                            potentialGoods.Add(d);
                        }
                    }
                }
            }

            if (potentialGoods.Count > 0)
            {
                int fid = f.loadID;
                int totalLiving = GetTotalLiving(f);
                
                string GetGroup(ThingDef d)
                {
                    if (d.IsMeat) return "Meat";
                    if (d.IsLeather || (d.stuffProps?.categories?.Contains(StuffCategoryDefOf.Fabric) == true)) return "Textile";
                    if (d.IsMedicine || d.IsDrug) return "Meds";
                    if (d.IsWeapon) return "Weapon";
                    if (d.IsApparel) return "Apparel";
                    if (d.IsIngestible) return "Food";
                    return "Other";
                }

                bool CanAddByLimit(ThingDef def, List<ThingDef> currentLines)
                {
                    string group = GetGroup(def);
                    if (group == "Other") return true;
                    return currentLines.Count(x => GetGroup(x) == group) < 5;
                }

                bool IsSaturatedLocal(ThingDef d) => (itemCountsAggregated.TryGetValue(d, out int c) ? c : 0) >= (totalLiving * 15);

                bool needRefresh = !monthlyProductionPlans.ContainsKey(fid) || Find.TickManager.TicksGame % 60000 == 0;
                if (needRefresh)
                {
                    // Используем уже посчитанные агрегаты
                    Dictionary<string, int> catTotals = new Dictionary<string, int> { {"Meat", 0}, {"Textile", 0}, {"Meds", 0}, {"Weapon", 0}, {"Apparel", 0}, {"Food", 0}, {"Other", 0} };
                    foreach (var kvp in itemCountsAggregated)
                    {
                        catTotals[GetGroup(kvp.Key)] += kvp.Value;
                    }

                    string trait1 = factionTraits.TryGetValue(fid, out string t1) ? t1 : "Generalist";
                    var weightedItems = potentialGoods.Select(d => new { Def = d, Weight = GetWeightForDef(d, trait1) }).ToList();
                    var staples = weightedItems.Where(x => x.Weight >= 800f).InRandomOrder().Select(x => x.Def.defName).ToList();
                    var specialized = weightedItems.Where(x => x.Weight >= 200f && x.Weight < 800f).InRandomOrder().Select(x => x.Def.defName).ToList();
                    if (!specialized.Any() && !staples.Any()) specialized = weightedItems.InRandomOrder().Take(10).Select(x => x.Def.defName).ToList();

                    List<string> mixedPlan = new List<string>();
                    Dictionary<string, int> groupCountsInPlan = new Dictionary<string, int>();
                    int sIdx = 0, pIdx = 0;

                    while (mixedPlan.Count < 15)
                    {
                        bool added = false;
                        if (sIdx < staples.Count)
                        {
                            string dName = staples[sIdx++];
                            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(dName);
                            string grp = GetGroup(def);
                            if (grp == "Other" || (groupCountsInPlan.TryGetValue(grp, out int c) ? c : 0) < 7)
                            {
                                mixedPlan.Add(dName);
                                groupCountsInPlan[grp] = (groupCountsInPlan.TryGetValue(grp, out int val) ? val : 0) + 1;
                                added = true;
                            }
                        }
                        if (mixedPlan.Count < 15 && pIdx < specialized.Count)
                        {
                            string dName = specialized[pIdx++];
                            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(dName);
                            string grp = GetGroup(def);
                            if (grp == "Other" || (groupCountsInPlan.TryGetValue(grp, out int c) ? c : 0) < 7)
                            {
                                mixedPlan.Add(dName);
                                groupCountsInPlan[grp] = (groupCountsInPlan.TryGetValue(grp, out int val) ? val : 0) + 1;
                                added = true;
                            }
                        }
                        if (!added) break;
                    }
                    monthlyProductionPlans[fid] = mixedPlan;
                }

                int varietyLimit = Mathf.Clamp(Mathf.CeilToInt(adults / 10f), 6, 25);
                List<ThingDef> activeLines = new List<ThingDef>();
                if (monthlyProductionPlans.TryGetValue(fid, out List<string> plan))
                {
                    foreach (string dName in plan)
                    {
                        if (activeLines.Count >= varietyLimit * 0.4f) break;
                        ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(dName);
                        if (d != null && !activeLines.Contains(d) && CanAddByLimit(d, activeLines)) activeLines.Add(d);
                    }
                }

                var profitable = potentialGoods.Where(def => !activeLines.Contains(def) && !IsSaturatedLocal(def) && CanAddByLimit(def, activeLines))
                    .OrderByDescending(def => def.BaseMarketValue * (globalPriceModifiers.TryGetValue(def, out float m1) ? m1 : 1.0f))
                    .Take(varietyLimit - activeLines.Count);
                foreach (var p in profitable) activeLines.Add(p);

                if (activeLines.Count < varietyLimit)
                {
                    foreach (var r in potentialGoods.Where(def => !activeLines.Contains(def) && !IsSaturatedLocal(def) && CanAddByLimit(def, activeLines)).InRandomOrder().Take(varietyLimit - activeLines.Count))
                        activeLines.Add(r);
                }

                if (activeLines.Count > 0)
                {
                    float baseBudgetPerSlot = remainingProductionValue / activeLines.Count;
                    if (!productionProgress.ContainsKey(fid)) productionProgress[fid] = new FactionProductionProgress();
                    var myProgress = productionProgress[fid].progress; 

                    // Используем уже посчитанные агрегаты
                    Dictionary<string, int> catTotals = new Dictionary<string, int> { {"Meat", 0}, {"Textile", 0}, {"Meds", 0}, {"Weapon", 0}, {"Apparel", 0}, {"Food", 0}, {"Other", 0} };
                    int totalInvItems = 0;
                    foreach (var kvp in itemCountsAggregated)
                    {
                        catTotals[GetGroup(kvp.Key)] += kvp.Value;
                        totalInvItems += kvp.Value;
                    }

                    foreach (ThingDef good in activeLines)
                    {
                        if (remainingProductionValue <= 0) break;
                        float slotBudget = baseBudgetPerSlot * Rand.Range(0.8f, 1.2f);
                        string trait2 = factionTraits.TryGetValue(fid, out string t2) ? t2 : "Generalist";
                        if (GetWeightForDef(good, trait2) >= 1000f) slotBudget *= 2.0f;

                        float globalModifier = globalPriceModifiers.TryGetValue(good, out float gm) ? gm : 1.0f;
                        float actualMarketPrice = Mathf.Max(good.BaseMarketValue * globalModifier, 0.1f);
                        
                        if (globalModifier > 1.1f) slotBudget *= globalModifier;
                        else if (globalModifier < 0.9f) slotBudget *= globalModifier;

                        if (totalInvItems > 50) 
                        {
                            string myCat = GetGroup(good);
                            int catTotal = catTotals[myCat];

                            float catRatio = (float)catTotal / totalInvItems;
                            if (globalModifier <= 1.2f)
                            {
                                if (catRatio >= 0.50f) slotBudget /= 10.0f;
                                else if (catRatio >= 0.40f) slotBudget /= 3.0f;
                                else if (catRatio >= 0.30f) slotBudget /= 2.0f;
                                else if (catRatio >= 0.15f) slotBudget /= 1.2f;
                            }
                        }
                        
                        if (!myProgress.ContainsKey(good.defName)) myProgress[good.defName] = 0f;
                        myProgress[good.defName] += slotBudget;

                        if (myProgress[good.defName] >= actualMarketPrice)
                        {
                            int countToMake = Mathf.FloorToInt(myProgress[good.defName] / actualMarketPrice);
                            
                            // ПРАВИЛО 5 СТАКОВ: Производим только крупными партиями, чтобы не спамить единичками.
                            // Если склад маленький, ждем заполнения всего оставшегося места.
                            int hasCount = itemCountsAggregated.ContainsKey(good) ? itemCountsAggregated[good] : 0;
                            float storageLimitMult = Mathf.Clamp(globalModifier, 1.0f, 10.0f);
                            int roomLeft = Mathf.RoundToInt(totalLiving * 3.0f * storageLimitMult) - hasCount;
                            
                            int minBatch = (good.stackLimit > 1) ? Mathf.Min(good.stackLimit * 5, roomLeft) : 1;
                            if (countToMake < minBatch || countToMake <= 0) continue;
                            
                            if (roomLeft > 0)
                            {
                                int finalCount = Mathf.Min(countToMake, roomLeft);
                                int q = -1;
                                if (good.HasComp(typeof(CompQuality))) q = (int)stock.GenerateRandomQuality(good);
                                stock.AddItem(good, finalCount, q);

                                // Логируем производство в историю
                                float batchVal = good.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * finalCount;
                                TradingHistoryManager.AddLog(prodLogs, f.loadID, new TradingLogEntry(Find.TickManager.TicksGame, good.LabelCap, finalCount, batchVal));
                                
                                // Обновляем локальный кэш
                                itemCountsAggregated[good] = hasCount + finalCount;
                                catTotals[GetGroup(good)] += finalCount;
                                totalInvItems += finalCount;

                                myProgress[good.defName] -= (finalCount * actualMarketPrice);
                                remainingProductionValue -= (finalCount * actualMarketPrice);
                            }
                        }
                        else remainingProductionValue -= slotBudget;
                    }
                }
            }
        }
    }
}
