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
        public int maxSlots = 50;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref inventory, "inventory", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref silver, "silver", 1000);
            Scribe_Values.Look(ref maxSlots, "maxSlots", 50);
        }

        public void AddItem(ThingDef def, int count)
        {
            // 1. Базовая проверка на null и типы (пешки и книги игнорируются)
            // Добавляем def.Minifiable — это отсечет всю мебель и постройки, которые можно сворачивать
            if (def == null || def.category == ThingCategory.Pawn || (def.thingClass != null && def.thingClass.Name == "MinifiedThing")) return;

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

            // 4. ЛОГИКА ОГРАНИЧЕНИЯ "3 ШТУКИ" ДЛЯ УНИКАЛЬНЫХ ВЕЩЕЙ
            // Если это оружие, одежда или предмет со стаком 1
            bool isUniqueItem = ((def.stackLimit <= 1 || def.IsApparel || def.IsWeapon) && !def.IsStuff) || (def.thingCategories != null && def.thingCategories.Any(c => c.defName == "AlcoholicBeverages"));

            if (count > 0 && isUniqueItem)
            {
                int current = inventory.ContainsKey(name) ? inventory[name] : 0;
                
                if (current >= 3)
                {
                    // У фракции уже есть 3 таких предмета. Больше не берем.
                    return; 
                }
                
                // Если пришло слишком много, срезаем до лимита в 3 штуки
                if (current + count > 3)
                {
                    count = 3 - current;
                }
            }

            // 5. ПРОВЕРКА СЛОТОВ СКЛАДА (maxSlots)
            if (!inventory.ContainsKey(name))
            {
                // Если это новый тип товара и места нет — выходим
                if (count > 0 && inventory.Count >= maxSlots) return;
                inventory[name] = 0;
            }

            // 6. ЗАПИСЬ
            inventory[name] += count;

            // Удаляем из словаря, если количество ушло в ноль или минус
            if (inventory[name] <= 0) inventory.Remove(name);
        }
        
        // === ГЛАВНАЯ ФИШКА: РАСЧЕТ БОГАТСТВА ===
        public float GetTotalWealth()
        {
            float total = (float)silver; // 1 серебро = 1.0 стоимости
            
            foreach (var kvp in inventory)
            {
                if (string.IsNullOrEmpty(kvp.Key)) continue;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
                if (def == null) continue;
                if (def.tradeTags == null) def.tradeTags = new List<string>();
                if (def.thingCategories == null) def.thingCategories = new List<ThingCategoryDef>();
                
                // BaseMarketValue - базовая цена предмета
                total += def.BaseMarketValue * kvp.Value;
            }
            return total;
        }

        // Добавили аргумент Dictionary с модификаторами цен
        public bool TryConsumeWealth(float valueNeeded, Dictionary<ThingDef, float> priceModifiers)
        {
            // 1. Проверка: а есть ли у нас столько добра? (СОХРАНЕНО)
            if (GetTotalWealth() < valueNeeded) return false;

            float remainingNeed = valueNeeded;
            float currentInf = EconomicsDemography.WorldPopulationManager.Instance?.currentInflation ?? 1.0f;

            // --- МАКРОЭКОНОМИЧЕСКАЯ РЕГУЛЯЦИЯ ---
            // Логика поведения фракции меняется в зависимости от состояния глобальной экономики:
            bool deflationCrisis = currentInf < 0.95f;  // Дефляция: серебра в мире мало
            bool highInflation = currentInf > 1.05f;    // Инфляция: серебра в мире слишком много

            var itemsList = inventory.Keys
                .Select(key => {
                    ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(key);
                    return new { 
                    Key = key, 
                    Def = itemDef,
                    Mult = (priceModifiers != null && itemDef != null && priceModifiers.TryGetValue(itemDef, out float m)) ? m : 1.0f 
                    };
                })
                .Where(x => x.Def != null)
                .OrderBy(x => x.Mult) // Всегда от дешевых (хлам) к дорогим
                .ToList();

            if (deflationCrisis)
            {
                // При дефляции: выгодно держать серебро! Платим исключительно товарами (сбрасывая цены на них и сохраняя массу серебра)
                foreach (var item in itemsList)
                {
                    if (remainingNeed <= 0) break;
                    remainingNeed = ProcessItemConsumption(item.Key, item.Def, item.Mult, remainingNeed);
                }
            }
            else if (highInflation)
            {
                // При инфляции: серебро обесценивается быстрее товаров. Фракции в первую очередь сливают серебро на погашение налогов.
                if (silver > 0)
                {
                    int silverToTake = Mathf.Min(silver, Mathf.CeilToInt(remainingNeed));
                    silver -= silverToTake;
                    remainingNeed -= silverToTake;
                }
            }
            else
            {
                // Стабильный рынок (~1.0): сбалансированная модель (сначала дешевое, потом серебро, потом ценное)
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
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
                
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
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(kvp.Key);
                
                // Оставляем только защиту от пешек и битых ссылок
                if (def == null || def.category == ThingCategory.Pawn) continue;

                if (def.tradeTags == null) def.tradeTags = new List<string>();
                if (def.thingCategories == null) def.thingCategories = new List<ThingCategoryDef>();

                bool allowed = isBaseTrade || (traderKind != null && traderKind.WillTrade(def));

                if (allowed)
                {
                    try 
                    {
                        // 1. ЗАЩИТА ОТ КРАША: Если вещь должна иметь материал, но его нет — даем Сталь.
                        ThingDef stuff = def.MadeFromStuff ? (GenStuff.DefaultStuffFor(def) ?? ThingDefOf.Steel) : null;
                        int countToTake = isBaseTrade ? kvp.Value : Mathf.Min(Rand.RangeInclusive(1, 5) * def.stackLimit, kvp.Value);

                        if (countToTake > 0)
                        {
                            inventory[kvp.Key] -= countToTake;
                            if (inventory[kvp.Key] <= 0) inventory.Remove(kvp.Key);

                            // 2. УНИКАЛЬНЫЕ ВЕЩИ (Книги, Арт, Оружие)
                            if (def.stackLimit == 1 || def.HasComp(typeof(CompQuality)) || typeof(Book).IsAssignableFrom(def.thingClass))
                            {
                                    int finalGenCount = isBaseTrade ? countToTake : Mathf.Min(countToTake, 2);
                                    // ИНКРУСТАЦИЯ ЗАЩИТЫ: Полностью изолируем генерацию каждой единицы товара
                                    for (int i = 0; i < finalGenCount; i++)
                                    {
                                        Thing t = null;
                                        try 
                                        {
                                            t = ThingMaker.MakeThing(def, stuff);
                                            if (t == null) continue;

                                            // РАНДОМИЗАЦИЯ КАЧЕСТВА
                                            var qualityComp = t.TryGetComp<CompQuality>();
                                            if (qualityComp != null)
                                            {
                                                qualityComp.SetQuality(GenerateRandomQuality(), ArtGenerationContext.Outsider);
                                            }

                                            // ИНИЦИАЛИЗАЦИЯ: Книги и Арт (могут быть битыми в модах)
                                            if (t is Book book) book.PostMake();
                                            if (t.TryGetComp<CompArt>() is CompArt art) art.InitializeArt(ArtGenerationContext.Outsider);

                                            // ДОПОЛНИТЕЛЬНАЯ ПРОВЕРКА: Пробуем вызвать LabelCap прямо сейчас.
                                            // Если предмет "битый" (например, мод ожидает parent, которого нет),
                                            // он крашнется здесь, а не в окне торговли.
                                            _ = t.LabelCap;
                                            _ = t.DescriptionDetailed;
                                            
                                            things.Add(t);
                                        } 
                                        catch (Exception ex)
                                        {
                                            Log.Warning($"[E&D] Ошибка генерации предмета {def.defName}: {ex.Message}. Замена на серебро.");
                                            t = ThingMaker.MakeThing(ThingDefOf.Silver);
                                            t.stackCount = Mathf.Clamp(Mathf.RoundToInt(def.BaseMarketValue), 1, 500);
                                            things.Add(t);
                                        }
                                    }
                                }
                                else 
                                {
                                    // 3. ОБЫЧНЫЕ СТАКУЮЩИЕСЯ РЕСУРСЫ (Блоки, кожа, мясо)
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
                            Log.Warning($"[E&D] Не удалось создать или обработать предмет {kvp.Key}: {ex.Message}");
                        }
                    }
                }

                things.RemoveAll(x => x == null);
                return things;
            }

        private QualityCategory GenerateRandomQuality()
        {
            float r = Rand.Value;

            // Взвешенное распределение:
            // 50% - Normal
            // 20% - Good
            // 15% - Poor
            // 8%  - Excellent
            // 5%  - Awful (ужасное)
            // 2%  - Masterwork (шедевр)
            // 0%  - Legendary (легендарное заблокировано для торговцев по умолчанию для баланса)

            if (r < 0.50f) return QualityCategory.Normal;
            if (r < 0.70f) return QualityCategory.Good;
            if (r < 0.85f) return QualityCategory.Poor;
            if (r < 0.93f) return QualityCategory.Excellent;
            if (r < 0.98f) return QualityCategory.Awful;
            return QualityCategory.Masterwork;
        }
    }
}
