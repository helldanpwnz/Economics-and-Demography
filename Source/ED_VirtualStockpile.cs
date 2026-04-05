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
    // Виртуальный склад фракции: хранит количество предметов и серебро.
    // Содержит логику добавления, потребления и генерации реальных вещей.
    public class VirtualStockpile : IExposable
    {
        public Dictionary<string, int> inventory = new Dictionary<string, int>();
        public int silver = 1000;
        public int maxSlots = 100;
        public bool isWarrior = false;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref inventory, "inventory", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref silver, "silver", 1000);
            Scribe_Values.Look(ref maxSlots, "maxSlots", 50);
            Scribe_Values.Look(ref isWarrior, "isWarrior", false);
        }

        public void AddItem(ThingDef def, int count, int quality = -1)
        {
            // 1. Базовая проверка на null и типы (пешки и книги игнорируются)
            // Добавляем def.Minifiable — это отсечет всю мебель и постройки, которые можно сворачивать
            if (def == null || def.category == ThingCategory.Pawn || (def.thingClass != null && def.thingClass.Name == "MinifiedThing")) return;

            // Ограничение по качеству: не-воины всегда получают Normal (quality = -1). 
            // Воины не могут хранить качество ниже Normal (ванильные пешки-рейдеры часто генерируются с плохой экипировкой).
            if (def.IsWeapon || def.IsApparel)
            {
                // Если фракция не воинская И качество не было явно указано,
                // тогда это обычное производство — ставим Normal (-1).
                // Но если качество было ПЕРЕДАНО (например, возврат рейда), то сохраняем его.
                if (!isWarrior && (quality == -1)) 
                {
                    quality = -1;
                }
                // Для воинов при производстве (или возврате) качество не может быть ниже Normal.
                else if (isWarrior && quality >= 0 && quality < (int)QualityCategory.Normal) 
                {
                    quality = (int)QualityCategory.Normal;
                }
            }

            // 2. ЛЕЧИЛКА ТЕГОВ (Критически важно для предотвращения NullReferenceException в StockGenerator_Tag)
            // Мы гарантируем, что списки не null, прежде чем ванильная игра попытается к ним обратиться.
            if (def.tradeTags == null) def.tradeTags = new List<string>();
            if (def.thingCategories == null) def.thingCategories = new List<ThingCategoryDef>();

            // 3. СЕРЕБРО (обрабатываем отдельно, без лимитов склада)
            if (def == ThingDefOf.Silver) 
            { 
                silver += count; 
                if (silver < 0) silver = 0; 
                return; 
            }

            string name = def.defName;
            if (quality >= 0) name += "_" + quality;

            // 4. ЗАПИСЬ (БЕЗ ЛИМИТОВ)
            if (!inventory.ContainsKey(name)) inventory[name] = 0;
            inventory[name] += count;

            // Удаляем из словаря, если количество ушло в ноль или минус
        }

        public static float GetQualityMultiplier(int quality)
        {
            switch (quality)
            {
                case 0: return 0.5f;   // Awful
                case 1: return 0.75f;  // Poor
                case 2: return 1.0f;   // Normal
                case 3: return 1.25f;  // Good
                case 4: return 1.5f;   // Excellent
                case 5: return 2.5f;   // Masterwork
                case 6: return 5.0f;   // Legendary
                default: return 1.0f;
            }
        }

        // === ГЛАВНАЯ ФИШКА: РАСЧЕТ БОГАТСТВА ===
        public float GetTotalWealth()
        {
            float total = (float)silver; // 1 серебро = 1.0 стоимости
            
            foreach (var kvp in inventory)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                ParseKey(kvp.Key, out string defName, out int q);
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null) continue;
                if (def.tradeTags == null) def.tradeTags = new List<string>();
                if (def.thingCategories == null) def.thingCategories = new List<ThingCategoryDef>();
                
                // BaseMarketValue - базовая цена предмета * коэффициент качества
                total += def.BaseMarketValue * GetQualityMultiplier(q) * kvp.Value;
            }
            return total;
        }

        // Добавили аргумент Dictionary с модификаторами цен
        public bool TryConsumeWealth(float valueNeeded, Dictionary<ThingDef, float> priceModifiers, bool isMilitary = false)
        {
            // 1. Проверка: а есть ли у нас столько добра? (СОХРАНЕНО)
            if (GetTotalWealth() < valueNeeded) return false;

            float remainingNeed = valueNeeded;
            float currentInf = EconomicsDemography.WorldPopulationManager.Instance?.currentInflation ?? 1.0f;

            // --- МАКРОЭКОНОМИЧЕСКАЯ РЕГУЛЯЦИЯ ---
            bool deflationCrisis = currentInf < 0.95f;
            bool highInflation = currentInf > 1.05f;

            // Сортировка - что потреблять первым (отдельно для войны и инфляции)
            var itemsList = inventory
                .Select(kvp => {
                    VirtualStockpile.ParseKey(kvp.Key, out string rawDefA, out int parsedQ);
                    string key = kvp.Key;
                    ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(rawDefA);
                    int milSort = 99;
                    if (isMilitary && itemDef != null)
                    {
                        if (itemDef.IsWeapon) milSort = 1;
                        else if (itemDef.IsApparel) milSort = 2;
                        else if (itemDef.IsDrug) milSort = 3;
                        else if (itemDef.IsIngestible) milSort = 4;
                    }
                    
                    float globM = (priceModifiers != null && itemDef != null && priceModifiers.TryGetValue(itemDef, out float m)) ? m : 1.0f;
                    
                    return new { 
                        Key = key, 
                        Def = itemDef,
                        MilSort = milSort,
                        Mult = globM * GetQualityMultiplier(parsedQ),
                        ParsedQ = parsedQ
                    };
                })
                .Where(x => x.Def != null)
                .OrderBy(x => isMilitary ? x.MilSort : 0) // Если война - сортируем по типам предметов
                .ThenBy(x => x.Mult) // Всегда от дешевых (хлам) к дорогим
                .ToList();

            if (isMilitary)
            {
                // При военных действиях тратим в первую очередь экипировку, медикаменты и еду 
                foreach (var item in itemsList)
                {
                    if (remainingNeed <= 0) break;
                    remainingNeed = ProcessItemConsumption(item.Key, item.Def, item.Mult, remainingNeed);
                }
                
                if (remainingNeed > 0 && silver > 0)
                {
                    int silverToTake = Mathf.Min(silver, Mathf.CeilToInt(remainingNeed));
                    silver -= silverToTake;
                    remainingNeed -= silverToTake;
                }
            }
            else if (deflationCrisis)
            {
                foreach (var item in itemsList)
                {
                    if (remainingNeed <= 0) break;
                    remainingNeed = ProcessItemConsumption(item.Key, item.Def, item.Mult, remainingNeed);
                }
            }
            else if (highInflation)
            {
                if (silver > 0)
                {
                    int silverToTake = Mathf.Min(silver, Mathf.CeilToInt(remainingNeed));
                    silver -= silverToTake;
                    remainingNeed -= silverToTake;
                }
            }
            else
            {
                var cheapItems = itemsList.Where(x => x.Mult <= 1.0f).ToList();
                foreach (var item in cheapItems)
                {
                    if (remainingNeed <= 0) break;
                    remainingNeed = ProcessItemConsumption(item.Key, item.Def, item.Mult, remainingNeed);
                }

                if (remainingNeed > 0 && silver > 0)
                {
                    int silverToTake = Mathf.Min(silver, Mathf.CeilToInt(remainingNeed));
                    silver -= silverToTake;
                    remainingNeed -= silverToTake;
                }

                var expensiveItems = itemsList.Where(x => x.Mult > 1.0f).ToList();
                foreach (var item in expensiveItems)
                {
                    if (remainingNeed <= 0) break;
                    remainingNeed = ProcessItemConsumption(item.Key, item.Def, item.Mult, remainingNeed);
                }
            }

            // === ФОЛБЕК ===
            // Если была дефляция, и у нас кончились все товары, платим серебром от безысходности:
            if (remainingNeed > 0 && silver > 0)
            {
                int silverToTake = Mathf.Min(silver, Mathf.CeilToInt(remainingNeed));
                silver -= silverToTake;
                remainingNeed -= silverToTake;
            }

            // Если была инфляция, мы слили все серебро, но налог еще не закрыт - платим товарами в нужном объеме:
            if (remainingNeed > 0 && highInflation)
            {
                foreach (var item in itemsList)
                {
                    if (remainingNeed <= 0) break;
                    remainingNeed = ProcessItemConsumption(item.Key, item.Def, item.Mult, remainingNeed);
                }
            }

            if (remainingNeed <= 0) goto Refund;
            return true;

        Refund:
            // === ГЛАВНЫЙ ФИКС: СДАЧА С КРУПНОЙ КУПЮРЫ (СОХРАНЕНО) ===
            if (remainingNeed < 0)
            {
                silver += Mathf.RoundToInt(Mathf.Abs(remainingNeed));
            }

            return true;
        }

        // Вспомогательный метод списания, чтобы не дублировать код
        private float ProcessItemConsumption(string key, ThingDef def, float mult, float need)
        {
            if (!inventory.ContainsKey(key)) return need;

            float price = def.BaseMarketValue * mult;
            if (price <= 0) return need;

            int countToTake = Mathf.CeilToInt(need / price);
            int actualTake = Mathf.Min(inventory[key], countToTake);

            inventory[key] -= actualTake;
            float valueTaken = actualTake * price;

            if (inventory[key] <= 0) inventory.Remove(key);
            
            return need - valueTaken;
        }

        public bool HasThingsFor(TraderKindDef traderKind)
        {
            // МЫ УБРАЛИ ПРОВЕРКУ СЕРЕБРА.
            // Теперь, даже если у них миллион денег, но нет товара — они не придут.
            
            foreach (var kvp in inventory)
            {
                if (kvp.Value <= 0) continue;
                
                // Быстрая проверка без лишних загрузок
                ParseKey(kvp.Key, out string defName, out _);
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                
                // Исключаем людей (рабов), если хочешь, чтобы они считались "товаром", удали проверку category
                if (def == null || def.category == ThingCategory.Pawn) continue;

                // Если нашли ХОТЯ БЫ ОДНУ вещь, которой торгует этот тип — разрешаем
                if (traderKind.WillTrade(def)) return true;
            }
            
            return false; // Товаров нет
        }

        public List<Thing> GenerateRealThings(TraderKindDef traderKind, bool isBaseTrade)
        {
            List<Thing> things = new List<Thing>();

            // 1. СЕРЕБРО
            int silverToTake = isBaseTrade ? silver : Mathf.Max(500, Mathf.CeilToInt(silver * 0.2f));
            silverToTake = Mathf.Min(silverToTake, silver);
            if (silverToTake > 0)
            {
                silver -= silverToTake;
                Thing s = ThingMaker.MakeThing(ThingDefOf.Silver);
                s.stackCount = silverToTake;
                things.Add(s);
            }

            // 2. ТОВАРЫ
            foreach (var kvp in inventory.ToList())
            {
                if (kvp.Value <= 0) continue;
                ParseKey(kvp.Key, out string defName, out int storedQuality);

                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                
                // Исключаем Xenogerm и Genepack (Biotech), так как они требуют генного набора и ломают CanStackTogether
                if (def == null || def.category == ThingCategory.Pawn || def.defName == "Xenogerm" || def.defName == "Genepack") continue;

                if (def.tradeTags == null) def.tradeTags = new List<string>();
                if (def.thingCategories == null) def.thingCategories = new List<ThingCategoryDef>();

                bool allowed = isBaseTrade || (traderKind != null && traderKind.WillTrade(def));

                if (allowed)
                {
                    try 
                    {
                        // 1. ЗАЩИТА ОТ КРАША: Если вещь должна иметь материал, но его нет — даем Сталь.
                        ThingDef stuff = def.MadeFromStuff ? (GenStuff.DefaultStuffFor(def) ?? ThingDefOf.Steel) : null;
                        int countToTake = isBaseTrade ? kvp.Value : Mathf.Max(1, (int)(kvp.Value * 0.2f));
                        // Броне-наценка: разрешаем торговцу брать до 5 единиц, даже если 20% меньше этого.
                        if (!isBaseTrade && def.stackLimit == 1) countToTake = Mathf.Max(countToTake, Rand.RangeInclusive(1, 10));
                        countToTake = Mathf.Min(countToTake, kvp.Value);

                        if (countToTake > 0)
                        {
                            // 2. УНИКАЛЬНЫЕ ВЕЩИ (Книги, Арт, Оружие)
                            if (def.stackLimit == 1 || def.HasComp(typeof(CompQuality)) || typeof(Book).IsAssignableFrom(def.thingClass))
                            {
                                    // ВИТРИНА: максимум 10 штук на тип (остальное на складе)
                                    int finalGenCount = Mathf.Min(countToTake, 10);

                                    // СПИСАНИЕ: Списываем ТОЛЬКО то, что реально покажем в окне (а не всё подряд)
                                    inventory[kvp.Key] -= finalGenCount;
                                    if (inventory[kvp.Key] <= 0) inventory.Remove(kvp.Key);

                                    // ИНКРУСТАЦИЯ ЗАЩИТЫ: Полностью изолируем генерацию каждой единицы товара
                                    for (int i = 0; i < finalGenCount; i++)
                                    {
                                        Thing t = null;
                                        try 
                                        {
                                            t = ThingMaker.MakeThing(def, stuff);
                                            if (t == null) continue;

                                            // ПРИСВОЕНИЕ КАЧЕСТВА
                                            var qualityComp = t.TryGetComp<CompQuality>();
                                            if (qualityComp != null)
                                            {
                                                QualityCategory q = (storedQuality >= 0) ? (QualityCategory)storedQuality : GenerateRandomQuality(def);
                                                qualityComp.SetQuality(q, ArtGenerationContext.Outsider);
                                            }
                                            if (t.def.useHitPoints) t.HitPoints = t.MaxHitPoints;

                                            // ИНИЦИАЛИЗАЦИЯ: Книги и Арт (могут быть битыми в модах)
                                            if (t is Book book) book.PostMake();
                                            if (t.TryGetComp<CompArt>() is CompArt art) art.InitializeArt(ArtGenerationContext.Outsider);

                                            // ДОПОЛНИТЕЛЬНАЯ ПРОВЕРКА
                                            _ = t.LabelCap;
                                            _ = t.DescriptionDetailed;

                                            things.Add(t);
                                        } 
                                        catch (Exception ex)
                                        {
                                            Log.Warning(string.Format((string)"ED_Log_ItemGenerationError".Translate(), def.defName, ex.Message));
                                            t = ThingMaker.MakeThing(ThingDefOf.Silver);
                                            t.stackCount = Mathf.Clamp(Mathf.RoundToInt(def.BaseMarketValue), 1, 500);
                                            things.Add(t);
                                        }
                                    }
                                }
                                else 
                                {
                                    // 3. ОБЫЧНЫЕ СТАКУЮЩИЕСЯ РЕСУРСЫ (Блоки, кожа, мясо)
                                    inventory[kvp.Key] -= countToTake;
                                    if (inventory[kvp.Key] <= 0) inventory.Remove(kvp.Key);

                                    try
                                    {
                                        Thing t = ThingMaker.MakeThing(def, stuff);
                                        t.stackCount = countToTake;
                                        // Проверка безопасности
                                        _ = t.LabelCap;
                                        things.Add(t);
                                    }
                                    catch 
                                    {
                                        Thing s = ThingMaker.MakeThing(ThingDefOf.Silver);
                                        s.stackCount = countToTake; 
                                        things.Add(s);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(string.Format((string)"ED_Log_ItemProcessError".Translate(), kvp.Key, ex.Message));
                        }
                    }
                }

                things.RemoveAll(x => x == null);

                // 3. ПРИНУДИТЕЛЬНОЕ РАЗНООБРАЗИЕ (Variety Force) - Если товаров мало, дозакупаем за серебро фракции
                if (!isBaseTrade && traderKind != null && things.Count < 40 && silver > 200)
                {
                    var manager = WorldPopulationManager.Instance;
                    if (manager != null)
                    {
                        HashSet<ThingDef> alreadyHas = new HashSet<ThingDef>(things.Select(x => x.def));
                        List<ThingDef> possible = WorldPopulationManager.rawResourcesCache.Values.SelectMany(x => x)
                            .Concat(WorldPopulationManager.manufacturedCache.Values.SelectMany(x => x))
                            .Concat(WorldPopulationManager.foodCache.Values.SelectMany(x => x))
                            .Where(d => d != null && !alreadyHas.Contains(d) && traderKind.WillTrade(d) && d.BaseMarketValue > 0)
                            .InRandomOrder()
                            .Take(100).ToList();

                        int maxSpend = silver / 2; // Тратим не больше половины бюджета
                        int spent = 0;

                        foreach (ThingDef d in possible)
                        {
                            if (things.Count >= 55 || spent >= maxSpend) break;
                            try 
                            {
                                int buyCount = d.stackLimit > 1 ? Rand.Range(15, 45) : 1;
                                float cost = d.BaseMarketValue * buyCount;
                                if (spent + cost > maxSpend) continue;

                                ThingDef stuff = d.MadeFromStuff ? (GenStuff.DefaultStuffFor(d) ?? ThingDefOf.Steel) : null;
                                Thing t = ThingMaker.MakeThing(d, stuff);
                                t.stackCount = buyCount;

                                var qc = t.TryGetComp<CompQuality>();
                                if (qc != null) qc.SetQuality(GenerateRandomQuality(d), ArtGenerationContext.Outsider);

                                things.Add(t);
                                spent += Mathf.CeilToInt(cost);
                            } catch { }
                        }
                        silver -= spent;
                    }
                }

                return things;
            }

        public QualityCategory GenerateRandomQuality(ThingDef def = null)
        {
            if (isWarrior && def != null && (def.IsWeapon || def.IsApparel))
            {
                float roll = Rand.Value;
                if (roll < 0.01f) return QualityCategory.Legendary;   // 1%
                if (roll < 0.05f) return QualityCategory.Excellent;   // 4%
                if (roll < 0.20f) return QualityCategory.Good;        // 15%
            }
            return QualityCategory.Normal;
        }
        public int GetCount(ThingDef def)
        {
            if (def == null) return 0;
            int total = 0;
            string prefix = def.defName + "_";
            foreach (var kvp in inventory)
            {
                if (kvp.Key == def.defName || kvp.Key.StartsWith(prefix)) total += kvp.Value;
            }
            return total;
        }

        public static void ParseKey(string key, out string defName, out int quality)
        {
            defName = key;
            quality = -1;
            int idx = key.LastIndexOf('_');
            if (idx > 0 && idx < key.Length - 1)
            {
                if (int.TryParse(key.Substring(idx + 1), out int q) && q >= 0 && q <= 6)
                {
                    string testDefName = key.Substring(0, idx);
                    if (DefDatabase<ThingDef>.GetNamedSilentFail(testDefName) != null)
                    {
                        defName = testDefName;
                        quality = q;
                    }
                }
            }
        }
        public void MaintainVariety(WorldPopulationManager manager)
        {
            // Стремление фракции поддерживать около 50 уникальных позиций в фоне (через закупку у соседей)
            if (inventory.Count >= 50 || silver < 500) return;

            int budget = silver / 5; // Бюджет на "закупку разнообразия" - 20% от текущего серебра
            int spent = 0;

            // 1. Получаем список всех остальных симулируемых фракций
            var otherStocks = manager.factionStockpiles.Values.Where(s => s != this).ToList();
            if (otherStocks.Count == 0) return;

            // 2. Перемешиваем партнеров
            otherStocks.Shuffle();

            foreach (var seller in otherStocks)
            {
                if (inventory.Count >= 50 || spent >= budget) break;

                // 3. Выбираем случайные товары у продавца, которых нет у нас
                var potentialKeys = seller.inventory.Keys
                    .Where(k => !inventory.ContainsKey(k))
                    .InRandomOrder()
                    .Take(5).ToList(); 

                foreach (var key in potentialKeys)
                {
                    if (inventory.Count >= 50 || spent >= budget) break;

                    ParseKey(key, out string dName, out int q);
                    ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(dName);
                    if (d == null) continue;

                    // 4. Расчет стоимости
                    float price = d.BaseMarketValue * GetQualityMultiplier(q);
                    
                    int amount = d.stackLimit > 1 ? Rand.Range(5, 15) : 1;
                    
                    if (seller.inventory.TryGetValue(key, out int sellerHas))
                    {
                        amount = Mathf.Min(amount, sellerHas);
                    }
                    else continue;

                    float totalCost = price * amount;

                    // 5. Проведение транзакции (Серебро переходит от нас к продавцу)
                    if (spent + totalCost <= budget && amount > 0)
                    {
                        seller.inventory[key] -= amount;
                        if (seller.inventory[key] <= 0) seller.inventory.Remove(key);
                        
                        this.AddItem(d, amount, q);
                        
                        int finalSilver = Mathf.RoundToInt(totalCost);
                        this.silver -= finalSilver;
                        seller.silver += finalSilver;
                        
                        spent += finalSilver;
                    }
                }
            }
        }
    }
}
