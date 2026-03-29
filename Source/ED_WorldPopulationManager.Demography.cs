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
        // Демографические процессы: рождаемость, старение, переток населения.
        private void ProcessDailyGrowth()
        {
            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            
            // --- ШАГ 1: ПЕРЕТОК (Хищничество и Миграция) ---
            foreach (var aggressor in factions) 
            {
                if (aggressor == null || aggressor.loadID <= 0 || aggressor.def.hidden || aggressor.defeated || (aggressor.leader == null && aggressor.def.techLevel > TechLevel.Animal)) continue;
                if (aggressor.IsPlayer || aggressor.def.hidden || aggressor.defeated) continue;
                
                int aggId = aggressor.loadID;
                
                VirtualStockpile aggStock = GetStockpile(aggressor);
                float currentWealth = aggStock.GetTotalWealth();

                float maintenance = GetMaintenanceCost(aggressor);
                if (currentWealth < (maintenance * 20f)) continue;

                float kidnapCost = 10f; 

                if (currentWealth < kidnapCost) continue;

                if (GetPopulation(aggressor) <= 0) continue;

                float kidnapEfficiency = 1.0f; 

                if (aggressor.def.permanentEnemy)
                {
                    kidnapEfficiency = 1.5f;
                }
                else
                {
                    switch (aggressor.def.techLevel)
                    {
                        case TechLevel.Animal:
                        case TechLevel.Neolithic:  kidnapEfficiency = 0.1f; break;
                        case TechLevel.Medieval:   kidnapEfficiency = 0.2f; break;
                        case TechLevel.Industrial: kidnapEfficiency = 0.3f; break;
                        case TechLevel.Spacer:     kidnapEfficiency = 0.4f; break;
                        case TechLevel.Ultra:       
                        case TechLevel.Archotech:  kidnapEfficiency = 0.5f; break;
                    }
                }

                var victims = factions.Where(v => 
                    !v.IsPlayer && !v.def.hidden && !v.defeated && v != aggressor && 
                    GetPopulation(v) > 50 &&
                    !(v.def.techLevel <= TechLevel.Neolithic && v.def.permanentEnemy)
                ).ToList();
                
                if (victims.Any())
                {
                    Faction victim = victims.RandomElement();
                    
                    float successChance = 0.02f; 
                    switch (victim.def.techLevel)
                    {
                        case TechLevel.Neolithic:  successChance = 0.30f; break;
                        case TechLevel.Medieval:   successChance = 0.20f; break; 
                        case TechLevel.Industrial: successChance = 0.15f; break; 
                        case TechLevel.Spacer:     successChance = 0.05f; break; 
                        case TechLevel.Ultra:      successChance = 0.02f; break; 
                    }
                    
                    if (Rand.Value < successChance * EconomicsDemographyMod.Settings.eventPopMultiplier)
                    {
                        int baseAmount = Rand.RangeInclusive(1, 2); 
                        int aggCapacity = Find.WorldObjects.Settlements.Count(s => s.Faction == aggressor) * GetBaseCapacity(aggressor);
                        float aggLimitMult = (GetTotalLiving(aggressor) > aggCapacity) ? 0.1f : 1.0f;

                        int transferAmount = GenMath.RoundRandom((float)baseAmount * kidnapEfficiency * aggLimitMult);
                        
                        if (transferAmount <= 0) continue;

                        if (aggStock.TryConsumeWealth(kidnapCost, globalPriceModifiers))
                        {
                            int victimId = victim.loadID;

                            for (int i = 0; i < transferAmount; i++)
                            {
                                int vFem = factionFemales.TryGetValue(victimId, out int f) ? f : 0;
                                int vPop = factionPopulation.TryGetValue(victimId, out int p) ? p : 1;
                                
                                Gender transferGender = (Rand.Value < (float)vFem / vPop) ? Gender.Female : Gender.Male;

                                ModifyPopulation(victim, -1, transferGender);
                                ModifyPopulation(aggressor, 1, transferGender);
                            }

                            string actionKey = aggressor.def.permanentEnemy ? "ED_Kidnapped" : "ED_Lured";
                            Log.Message("ED_KidnapLog".Translate(aggressor.Name, actionKey.Translate(), transferAmount, victim.Name));
                        }
                    }
                }
            }

            // --- ШАГ 2: БИОЛОГИЧЕСКИЙ ЦИКЛ ---
            foreach (var f in factions)
            {
                if (f == null || f.loadID < 0 || f.IsPlayer || f.def.hidden || f.defeated || !f.def.humanlikeFaction || (f.leader == null && f.def.techLevel > TechLevel.Animal)) continue;

                EnsureFactionDataExists(f);
                
                if (f == null || f.loadID < 0) continue; 
                
                if (f.IsPlayer || f.def.hidden || f.defeated || !f.def.humanlikeFaction) continue;

                string defName = f.def.defName;
                if (defName.Contains("Ancient") || defName.Contains("Refugee") || defName.Contains("Beggar")) continue;

                int fid = f.loadID;

                int adults = GetPopulation(f); 
                int totalLiving = GetTotalLiving(f); 

                if (totalLiving <= 0) continue;

                int groundBases = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
                int spaceBasesCount = orbitalBases.TryGetValue(fid, out int sb) ? sb : 0;
                int totalBases = groundBases + spaceBasesCount;
                
                if (totalBases == 0 && totalLiving > 0 && EconomicsDemographyMod.Settings.enablePopulationLoss)
                {
                    int vagrantLoss = Mathf.CeilToInt(totalLiving * 0.01f);
                    ModifyPopulation(f, -vagrantLoss);
                    totalLiving -= vagrantLoss; 
                }
                
                int capacityPerBase = GetBaseCapacity(f); 
                int totalCapacity = totalBases * capacityPerBase;
                
                VirtualStockpile stock = GetStockpile(f);

                float currentKids = factionChildren.TryGetValue(fid, out float kVal) ? kVal : 0f;
                float currentElders = factionElders.TryGetValue(fid, out float eVal) ? eVal : 0f;

                // 4. ПРОИЗВОДСТВО ТОВАРОВ (вынесено в Production)
                if (stock != null)
                {
                    ProduceGoodsCategorized(f, adults, currentElders, currentKids);
                }

                // === 2. ЭКОНОМИКА (РЕАЛЬНОЕ СОДЕРЖАНИЕ) ===
                float costPerPerson = 0.2f; 

                switch (f.def.techLevel)
                {
                    case TechLevel.Animal:
                    case TechLevel.Neolithic:   costPerPerson = 0.05f; break;  
                    case TechLevel.Medieval:    costPerPerson = 0.10f; break;
                    case TechLevel.Industrial:  costPerPerson = 0.25f; break; 
                    case TechLevel.Spacer:      costPerPerson = 0.32f; break; 
                    case TechLevel.Ultra:       costPerPerson = 0.36f; break; 
                    case TechLevel.Archotech:   costPerPerson = 0.45f; break; 
                }

                float consumers = (adults * 1.0f) + (currentKids * 0.5f) + (currentElders * 0.7f);
                float adminOverhead = 1.0f + (consumers / 10000.0f);

                float techConsMult = 1f;
                var s = EconomicsDemographyMod.Settings;
                switch (f.def.techLevel)
                {
                    case TechLevel.Animal:      techConsMult = s.consAnimal; break;
                    case TechLevel.Neolithic:   techConsMult = s.consNeolithic; break;
                    case TechLevel.Medieval:    techConsMult = s.consMedieval; break;
                    case TechLevel.Industrial:  techConsMult = s.consIndustrial; break;
                    case TechLevel.Spacer:      techConsMult = s.consSpacer; break;
                    case TechLevel.Ultra:       techConsMult = s.consUltra; break;
                    case TechLevel.Archotech:   techConsMult = s.consArchotech; break;
                }
                
                float totalDailyCost = (consumers * costPerPerson * adminOverhead) * techConsMult;

                int dayOfYear = GenDate.DayOfYear(Find.TickManager.TicksGame, 0f);
                if (dayOfYear >= 45 || dayOfYear < 15) totalDailyCost *= 1.2f; 

                float wealthAvailable = stock.GetTotalWealth();
                float coverage = 1.0f;

                if (wealthAvailable >= totalDailyCost)
                {
                    stock.TryConsumeWealth(totalDailyCost, globalPriceModifiers);
                    coverage = 1.0f;
                }
                else
                {
                    stock.TryConsumeWealth(wealthAvailable, globalPriceModifiers);
                    if (totalDailyCost > 0)
                        coverage = wealthAvailable / totalDailyCost;
                    else
                        coverage = 1.0f;
                }
                
                float starvationFactor = Mathf.Clamp(coverage, 0.1f, 1.0f);

                if (coverage < 0.4f && EconomicsDemographyMod.Settings.enablePopulationLoss)
                {
                    float deficit = totalDailyCost - wealthAvailable;
                    
                    float deathResistance = (f.def.techLevel <= TechLevel.Neolithic) ? 400f : 250f; 
                    
                    int starvationDeaths = Mathf.CeilToInt(deficit / deathResistance);
                    
                    int maxDeaths = Mathf.Max(1, Mathf.RoundToInt(totalLiving * 0.02f));
                    
                    starvationDeaths = Mathf.Clamp(starvationDeaths, 0, maxDeaths);

                    if (starvationDeaths > 0)
                    {
                        ModifyPopulation(f, -starvationDeaths);
                    }
                }
                
                // === 4. ДЕМОГРАФИЯ (ПАРЫ И РОЖДАЕМОСТЬ) ===
                int females = factionFemales.TryGetValue(fid, out int fem) ? fem : 0;
                int males = Mathf.Max(0, adults - females);
                
                // Для монополых рас (партеногенез, клонирование и т.д.) все взрослые считаются «парами»
                float realFR = GetFactionRealFemaleRatio(f);
                bool isMonoGender = (realFR >= 0.95f || realFR <= 0.05f) && realFR >= 0f;
                int pairs = isMonoGender ? adults : Math.Min(males, females); 
                
                float maxChildRatio = 0.5f; 

                switch (f.def.techLevel)
                {
                    case TechLevel.Animal:
                    case TechLevel.Neolithic:   maxChildRatio = 1.2f; break;
                    case TechLevel.Medieval:    maxChildRatio = 1.0f; break;
                    case TechLevel.Industrial:  maxChildRatio = 0.8f; break;
                    case TechLevel.Spacer:      maxChildRatio = 0.6f; break;
                    case TechLevel.Ultra:       maxChildRatio = 0.5f; break;
                    case TechLevel.Archotech:   maxChildRatio = 0.3f; break;
                }

                float currentRatio = (adults > 0) ? (currentKids / (float)adults) : 10f; 
                float childDensityFactor = 1.0f - (currentRatio / maxChildRatio);
                childDensityFactor = Mathf.Clamp(childDensityFactor, 0f, 1.0f);
                
                float ratePerPair = 5.0f; 
                if (f.def.techLevel <= TechLevel.Neolithic) ratePerPair = 10.0f;
                else if (f.def.techLevel == TechLevel.Medieval) ratePerPair = 7.0f;
                else if (f.def.techLevel == TechLevel.Spacer) ratePerPair = 4.0f;
                else if (f.def.techLevel >= TechLevel.Ultra) ratePerPair = 3.0f;
                
                float hostileBirthPenalty = f.def.permanentEnemy ? 0.3f : 1.0f;

                float birthMultiplier = 1.0f;

                float techBirthMult = 1f;
                switch (f.def.techLevel)
                {
                    case TechLevel.Animal:      techBirthMult = s.birthAnimal; break;
                    case TechLevel.Neolithic:   techBirthMult = s.birthNeolithic; break;
                    case TechLevel.Medieval:    techBirthMult = s.birthMedieval; break;
                    case TechLevel.Industrial:  techBirthMult = s.birthIndustrial; break;
                    case TechLevel.Spacer:      techBirthMult = s.birthSpacer; break;
                    case TechLevel.Ultra:       techBirthMult = s.birthUltra; break;
                    case TechLevel.Archotech:   techBirthMult = s.birthArchotech; break;
                }

                float dailyBirths = (pairs * ratePerPair * birthMultiplier * hostileBirthPenalty * childDensityFactor * starvationFactor * techBirthMult) / 60f;

                // ЛОГИКА ДЕГРАДАЦИИ И УХОДА В БРОДЯГИ
                if (totalBases > 0 && Find.TickManager.TicksGame > 1800000)
                {
                    bool shouldCollapse = false;
                    int workingPop = adults + Mathf.CeilToInt(currentElders); // Дети не поддерживают базу
                    float ratio = (float)workingPop / totalCapacity;

                    float threshold = EconomicsDemographyMod.Settings.collapseThresholdFactor;

                    if (totalBases > 1 && ratio < threshold) 
                    {
                        shouldCollapse = true;
                    }
                    else if (totalBases == 1 && ratio < threshold) // Если осталась 1 база и население ниже порога — уходим в бродяги
                    {
                        shouldCollapse = true;
                    }

                    if (shouldCollapse && Rand.Value < 0.05f)
                    {
                        Settlement settlementToCollapse = Find.WorldObjects.Settlements
                            .Where(s => s.Faction == f)
                            .RandomElementWithFallback();

                        if (settlementToCollapse != null)
                        {
                            int tile = settlementToCollapse.Tile;
                            string settName = settlementToCollapse.Name;

                            settlementToCollapse.Destroy();
                            CreateRuinsWithTimer(tile, f);
                            
                            lastBaseCount[fid] = Find.WorldObjects.Settlements.Count(s => s.Faction == f);

                            if (totalBases == 1)
                            {
                                if (EconomicsDemographyMod.Settings.enableNotifications)
                                {
                                    Find.LetterStack.ReceiveLetter("ED_FactionRuinedTitle".Translate(f.Name),
                                        "ED_FactionRuinedText".Translate(f.Name, settName, totalLiving),
                                        LetterDefOf.NegativeEvent, new GlobalTargetInfo(tile));
                                }
                            }
                            else
                            {
                                int abandonmentLoss = Mathf.Max(1, Mathf.RoundToInt(totalLiving * 0.05f));
                                ModifyPopulation(f, -abandonmentLoss);

                                if (EconomicsDemographyMod.Settings.enableNotifications)
                                {
                                    Find.LetterStack.ReceiveLetter("ED_SettlementAbandonedTitle".Translate(f.Name),
                                        "ED_SettlementAbandonedText".Translate(f.Name, settName, abandonmentLoss),
                                        LetterDefOf.NeutralEvent, new GlobalTargetInfo(tile));
                                }
                            }

                            groundBases--;
                            totalBases--;
                        }
                    }
                }

                // РАСЧЕТ ЛИМИТА (ПЛАВАЮЩИЙ)
                float popLimitMult = 1.0f;

                if (totalCapacity > 0)
                {
                    float currentMod = factionLimitModifiers.TryGetValue(fid, out float m) ? m : 1f;
                    float effectiveCapacity = totalCapacity * currentMod;
                    float overcrowding = (float)totalLiving / effectiveCapacity;
                    {
                        popLimitMult = Mathf.Max(0f, 1.0f - (overcrowding - 1.0f) * 2.0f);
                    }
                }
                else
                {
                    popLimitMult = 0.05f;
                }

                dailyBirths *= popLimitMult;
                
                // ГАРАНТИЯ: Если капитал меньше 20x содержания - рождаемость 0
                if (wealthAvailable < (totalDailyCost * 20f)) dailyBirths = 0f;

                factionChildren[fid] += dailyBirths;

                // УТЕЧКА ДЕТЕЙ У БРОДЯГ
                if (totalBases == 0 && factionChildren[fid] >= 1f)
                {
                    if (Rand.Value < 0.10f) 
                    {
                        Faction ally = factions.Where(x => 
                            x != f && !x.IsPlayer && !x.def.hidden && !x.defeated && 
                            x.RelationKindWith(f) == FactionRelationKind.Ally &&
                            GetPopulation(x) > 60 
                        ).RandomElementWithFallback();
                        
                        if (ally != null)
                        {
                            float moveAmount = Mathf.Max(1f, factionChildren[fid] * 0.10f);
                            factionChildren[fid] -= moveAmount;
                            int allyId = ally.loadID;
                            if (!factionChildren.ContainsKey(allyId)) factionChildren[allyId] = 0f;
                            factionChildren[allyId] += moveAmount;
                        }
                    }

                    if (Rand.Value < 0.10f)
                    {
                        Faction enemy = factions.Where(x => 
                            x != f && !x.IsPlayer && !x.def.hidden && !x.defeated && 
                            x.HostileTo(f) && GetPopulation(x) > 60 
                        ).RandomElementWithFallback();
                        
                        if (enemy != null)
                        {
                            float moveAmount = Mathf.Max(1f, factionChildren[fid] * 0.10f);
                            factionChildren[fid] -= moveAmount;
                            int enemyId = enemy.loadID;
                            if (!factionChildren.ContainsKey(enemyId)) factionChildren[enemyId] = 0f;
                            factionChildren[enemyId] += moveAmount;
                        }
                    }
                }

                // ДЕТСКАЯ СМЕРТНОСТЬ
                float childMortalityRate = 0.10f;

                switch (f.def.techLevel)
                {
                    case TechLevel.Animal:
                    case TechLevel.Neolithic:  
                        childMortalityRate = 0.25f;
                        break;
                    case TechLevel.Medieval:   
                        childMortalityRate = 0.15f;
                        break;
                    case TechLevel.Industrial: 
                        childMortalityRate = 0.05f;
                        break;
                    case TechLevel.Spacer:     
                        childMortalityRate = 0.01f;
                        break;
                    case TechLevel.Ultra:      
                    case TechLevel.Archotech:  
                        childMortalityRate = 0.002f;
                        break;
                    default:
                        childMortalityRate = 0.08f;
                        break;
                }

                float dailyChildDeaths = (factionChildren[fid] * childMortalityRate) / 60f;
                factionChildren[fid] = Mathf.Max(0, factionChildren[fid] - dailyChildDeaths);

                // ВЗРОСЛЕНИЕ
                float yearsToAdult = EconomicsDemographyMod.Settings.daysToAdult / 60f;
                float dailyMaturing = (factionChildren[fid] * (1f / Math.Max(0.25f, yearsToAdult))) / 60f;
                factionChildren[fid] -= dailyMaturing;
                maturationBuffer[fid] += dailyMaturing;

                // СТАРЕНИЕ
                float baseAdultLifespan = EconomicsDemographyMod.Settings.daysToElder / 60f; // Переводим дни в "годы" мода
                float adultLifespan = baseAdultLifespan;

                switch (f.def.techLevel)
                {
                    case TechLevel.Animal:
                    case TechLevel.Neolithic:  adultLifespan = baseAdultLifespan * 0.5f;  break;
                    case TechLevel.Medieval:   adultLifespan = baseAdultLifespan * 0.75f; break;
                    case TechLevel.Industrial: adultLifespan = baseAdultLifespan;         break;
                    case TechLevel.Spacer:     adultLifespan = baseAdultLifespan * 1.5f;  break;
                    case TechLevel.Ultra:      adultLifespan = baseAdultLifespan * 2.0f;  break;
                    case TechLevel.Archotech:  adultLifespan = baseAdultLifespan * 4.0f;  break;
                }

                float dailyAging = (adults * (1f / Math.Max(1f, adultLifespan))) / 60f;
                agingBuffer[fid] += dailyAging;

                // СМЕРТНОСТЬ СТАРИКОВ
                float elderDeathRate = (f.def.techLevel <= TechLevel.Neolithic) ? 0.20f : (f.def.techLevel >= TechLevel.Spacer ? 0.05f : 0.15f);
                factionElders[fid] = Mathf.Max(0, factionElders[fid] - (factionElders[fid] * elderDeathRate / 60f));

                // ПРИМЕНЕНИЕ БУФЕРОВ
                if (maturationBuffer[fid] >= 1f) 
                { 
                    int n = (int)maturationBuffer[fid]; 
                    ModifyPopulation(f, n); 
                    maturationBuffer[fid] -= n; 
                }
                if (agingBuffer[fid] >= 1f) 
                { 
                    int n = (int)agingBuffer[fid]; 
                    ModifyPopulation(f, -n, null, null, PopulationPool.Adult); 
                    factionElders[fid] += n; 
                    agingBuffer[fid] -= n; 
                }

                // МЕХАНИКИ КАРТЫ (БАЗЫ, ЭКСПАНСИЯ)
                if (totalBases == 0 && (f.def.defName == "TradersGuild" || f.def.defName == "Salvagers"))
                {
                    orbitalBases[fid] = 1;
                    totalBases = 1;
                }
                
                // А) Бродяги -> Возрождение
                if (totalBases == 0)
                {
                    int requiredToSettle = Mathf.RoundToInt(capacityPerBase * 0.8f);

                    if (adults >= requiredToSettle)
                    {
                        if (Rand.Value < 0.02f) 
                        {
                            int newTile = TileFinder.RandomSettlementTileFor(f);
                            if (newTile != -1)
                            {
                                Settlement newSett = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                                newSett.SetFaction(f); 
                                newSett.Tile = newTile;
                                newSett.Name = SettlementNameGenerator.GenerateSettlementName(newSett, null);
                                Find.WorldObjects.Add(newSett);

                                if (vagrantWarningsSent.Contains(fid)) vagrantWarningsSent.Remove(fid);
                                
                                if (EconomicsDemographyMod.Settings.enableNotifications)
                                {
                                    Find.LetterStack.ReceiveLetter("ED_RebirthTitle".Translate(f.Name), 
                                        "ED_RebirthText".Translate(f.Name, adults, newSett.Name), 
                                        LetterDefOf.PositiveEvent, new GlobalTargetInfo(newTile));
                                }
                            }
                        }
                    }
                }

                // Б) Потеря территорий
                if (groundBases < lastBaseCount[fid])
                {
                    int lostCount = lastBaseCount[fid] - groundBases;
                    for (int i = 0; i < lostCount; i++)
                    {
                        int currentLiving = GetTotalLiving(f);
                        int popLoss = Mathf.RoundToInt(currentLiving * Rand.Range(0.15f, 0.25f));
                        popLoss = Mathf.Max(5, popLoss);
                        
                        if ((lastBaseCount[fid] - i) > 1)
                        {
                            ModifyPopulation(f, -popLoss);
                            if (EconomicsDemographyMod.Settings.enableNotifications)
                            {
                                Find.LetterStack.ReceiveLetter("ED_TerritoryLossTitle".Translate(f.Name), 
                                    "ED_TerritoryLossText".Translate(f.Name, popLoss), 
                                    LetterDefOf.NegativeEvent);
                            }
                        }
                        else
                        {
                            int lastStandLoss = Mathf.RoundToInt(totalLiving * 0.4f);
                            ModifyPopulation(f, -lastStandLoss);
                            if (EconomicsDemographyMod.Settings.enableNotifications)
                            {
                                Find.LetterStack.ReceiveLetter("ED_LastBaseLossTitle".Translate(f.Name), 
                                    "ED_LastBaseLossText".Translate(f.Name, totalLiving - lastStandLoss), 
                                    LetterDefOf.NeutralEvent);
                            }
                        }
                    }
                }
                lastBaseCount[fid] = groundBases;

                // Г) Экспансия (Расселение при перенаселении)
                if (EconomicsDemographyMod.Settings.enableExpansion && Find.TickManager.TicksGame >= 90000) // 15 дней
                {
                    if (totalBases > 0 && f.def.defName != "TradersGuild" && f.def.defName != "Salvagers")
                    {
                        int expansionPopReq = Mathf.RoundToInt(totalCapacity * EconomicsDemographyMod.Settings.expansionThresholdFactor);
                        
                        int hardMinPop;
                        switch (f.def.techLevel)
                        {
                            case TechLevel.Animal:
                            case TechLevel.Neolithic:   hardMinPop = 110; break;
                            case TechLevel.Medieval:    hardMinPop = 90; break; 
                            case TechLevel.Industrial:  hardMinPop = 70; break;
                            case TechLevel.Spacer:      hardMinPop = 50; break;
                            case TechLevel.Ultra:       hardMinPop = 40; break;
                            case TechLevel.Archotech:   hardMinPop = 30; break;
                            default:                    hardMinPop = 70; break;
                        }

                        int finalPopRequired = Mathf.Max(hardMinPop, expansionPopReq);

                        float baseExpansionCost = capacityPerBase * 20f;
                        if (f.def.techLevel >= TechLevel.Industrial) baseExpansionCost *= 2f;

                        float expansionCost = baseExpansionCost * Mathf.Pow(1.5f, totalBases);

                        float currentWealth = stock.GetTotalWealth();

                        // ЭКСПАНСИЯ (Требует 20x содержания)
                        if (totalLiving >= finalPopRequired && currentWealth >= expansionCost && currentWealth >= (totalDailyCost * 20f))
                        {
                            float overcrowding = (float)totalLiving / totalCapacity;
                            float expansionChance = Mathf.Clamp(0.10f * overcrowding, 0.10f, 0.40f);

                            if (Rand.Value < expansionChance) 
                            {
                                int newTile = -1;
                                var currentBases = Find.WorldObjects.Settlements.Where(s => s.Faction == f).ToList();

                                if (currentBases.Any())
                                {
                                    int originTile = currentBases.RandomElement().Tile;
                                    
                                    for (int i = 0; i < 50; i++)
                                    {
                                        int c = TileFinder.RandomSettlementTileFor(f);
                                        if (c > 0 && Find.WorldGrid.ApproxDistanceInTiles(originTile, c) <= 60f) 
                                        { 
                                            newTile = c; 
                                            break; 
                                        }
                                    }
                                    
                                    if (newTile == -1)
                                    {
                                        for (int i = 0; i < 20; i++)
                                        {
                                            int c = TileFinder.RandomSettlementTileFor(f);
                                            if (c > 0 && Find.WorldGrid.ApproxDistanceInTiles(originTile, c) <= 150f) 
                                            { 
                                                newTile = c; 
                                                break; 
                                            }
                                        }
                                    }
                                }

                                if (newTile != -1)
                                {
                                    Settlement newSett = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
                                    newSett.SetFaction(f); 
                                    newSett.Tile = newTile;
                                    newSett.Name = SettlementNameGenerator.GenerateSettlementName(newSett, null);
                                    Find.WorldObjects.Add(newSett);

                                    stock.TryConsumeWealth(expansionCost, globalPriceModifiers);
                                    
                                    if (vagrantWarningsSent.Contains(fid)) vagrantWarningsSent.Remove(fid);
                                    
                                    if (EconomicsDemographyMod.Settings.enableNotifications)
                                    {
                                        Find.LetterStack.ReceiveLetter("ED_ExpansionLetterTitle".Translate(f.Name), 
                                            "ED_ExpansionLetterText".Translate(totalLiving, totalCapacity, f.Name, newSett.Name, Mathf.RoundToInt(expansionCost)), 
                                            LetterDefOf.PositiveEvent, new GlobalTargetInfo(newTile));
                                    }

                                    totalBases++;
                                    totalCapacity = totalBases * capacityPerBase;
                                }
                            }
                        }
                    }
                }
            }
            
            ProcessVirtualTrade(factions);
        }

        public float GetMaintenanceCost(Faction f)
        {
            if (f == null || f.loadID < 0) return 0f;
            int adults = GetPopulation(f);
            int fid = f.loadID;
            float kVal = factionChildren.TryGetValue(fid, out float k) ? k : 0f;
            float eVal = factionElders.TryGetValue(fid, out float e) ? e : 0f;
            
            float costPerPerson = 0.2f; 
            switch (f.def.techLevel) {
                case TechLevel.Animal:
                case TechLevel.Neolithic: costPerPerson = 0.05f; break;
                case TechLevel.Medieval:  costPerPerson = 0.10f; break;
                case TechLevel.Industrial: costPerPerson = 0.25f; break;
                case TechLevel.Spacer: costPerPerson = 0.32f; break;
                case TechLevel.Ultra: costPerPerson = 0.36f; break;
                case TechLevel.Archotech: costPerPerson = 0.45f; break;
            }
            float consumers = adults + (kVal * 0.5f) + (eVal * 0.7f);
            float adminOverhead = 1.0f + (consumers / 10000.0f);
            float techMult = 1f;
            var s = EconomicsDemographyMod.Settings;
            switch (f.def.techLevel) {
                case TechLevel.Animal: techMult = s.consAnimal; break;
                case TechLevel.Neolithic: techMult = s.consNeolithic; break;
                case TechLevel.Medieval: techMult = s.consMedieval; break;
                case TechLevel.Industrial: techMult = s.consIndustrial; break;
                case TechLevel.Spacer: techMult = s.consSpacer; break;
                case TechLevel.Ultra: techMult = s.consUltra; break;
                case TechLevel.Archotech: techMult = s.consArchotech; break;
            }
            return consumers * costPerPerson * adminOverhead * techMult;
        }
    }
}
