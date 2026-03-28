using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace EconomicsDemography
{
    /// <summary>
    /// Совместимость с RimWar. Использует рефлексию для автоматического обнаружения методов.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ED_Patch_RimWar_Compatibility
    {
        static ED_Patch_RimWar_Compatibility()
        {
            var harmony = new Harmony("rimworld.economics_demography.rimwar_compat");

            Type rimWarCompType = AccessTools.TypeByName("RimWar.RimWarSettlementComp");
            Type rimWarTrackerType = AccessTools.TypeByName("RimWar.Planet.WorldComponent_PowerTracker");

            if (rimWarCompType == null || rimWarTrackerType == null)
            {
                // Попытка без неймспейса
                if (rimWarTrackerType == null)
                    rimWarTrackerType = AccessTools.TypeByName("WorldComponent_PowerTracker");
                if (rimWarCompType == null)
                    rimWarCompType = AccessTools.TypeByName("RimWarSettlementComp");
            }

            if (rimWarCompType == null || rimWarTrackerType == null)
            {
                Log.Message("[E&D] RimWar not detected, skipping compatibility patches.");
                return;
            }

            Log.Message("[E&D] RimWar detected! Scanning methods...");

            // Логируем ВСЕ методы PowerTracker для отладки
            MethodInfo[] allMethods = rimWarTrackerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var m in allMethods)
            {
                if (m.Name.Contains("Attempt") || m.Name.Contains("Settler") || m.Name.Contains("Warband") || m.Name.Contains("Trade"))
                {
                    Log.Message($"[E&D] Found RimWar method: {m.Name} (Params: {string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
                }
            }

            int patchCount = 0;

            // 1. Патчим геттер очков
            MethodInfo mPointsGetter = AccessTools.PropertyGetter(rimWarCompType, "RimWarPoints");
            if (mPointsGetter != null)
            {
                harmony.Patch(mPointsGetter, postfix: new HarmonyMethod(typeof(ED_Patch_RimWar_Compatibility), nameof(Postfix_RimWarPoints_Getter)));
                patchCount++;
                Log.Message("[E&D] Patched: RimWarPoints getter");
            }

            // 2. Ищем и патчим ВСЕ методы, содержащие "Settler" (блок экспансии)
            foreach (var m in allMethods)
            {
                if (m.Name.Contains("Settler") && !m.Name.Contains("OffMainThread") && m.GetParameters().Length > 0)
                {
                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(typeof(ED_Patch_RimWar_Compatibility), nameof(Prefix_BlockExpansion)));
                        patchCount++;
                        Log.Message($"[E&D] Patched settler method: {m.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[E&D] Failed to patch {m.Name}: {ex.Message}");
                    }
                }
            }

            // 3. Ищем и патчим ВСЕ методы, содержащие "Warband" или "Trade" (блок рейдов)
            foreach (var m in allMethods)
            {
                if ((m.Name.Contains("Warband") || m.Name.Contains("Trade")) && m.Name.Contains("Attempt") && m.GetParameters().Length > 0)
                {
                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(typeof(ED_Patch_RimWar_Compatibility), nameof(Prefix_BlockRaid)));
                        patchCount++;
                        Log.Message($"[E&D] Patched raid/trade method: {m.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[E&D] Failed to patch {m.Name}: {ex.Message}");
                    }
                }
            }

            Log.Message($"[E&D] RimWar compatibility: {patchCount} patches applied.");
        }



        private static int GetCapacitySafe(Faction f)
        {
            if (f == null) return 100;
            var settings = EconomicsDemographyMod.Settings;
            if (settings == null) return 100;
            switch (f.def.techLevel)
            {
                case TechLevel.Animal: return Mathf.RoundToInt(settings.capAnimal);
                case TechLevel.Neolithic: return Mathf.RoundToInt(settings.capNeolithic);
                case TechLevel.Medieval: return Mathf.RoundToInt(settings.capMedieval);
                case TechLevel.Industrial: return Mathf.RoundToInt(settings.capIndustrial);
                case TechLevel.Spacer: return Mathf.RoundToInt(settings.capSpacer);
                case TechLevel.Ultra: return Mathf.RoundToInt(settings.capUltra);
                case TechLevel.Archotech: return Mathf.RoundToInt(settings.capArchotech);
                default: return 100;
            }
        }

        // Извлекает фракцию из ЛЮБОГО объекта RimWar (RimWarData, WorldComponent, etc.)
        private static Faction ExtractFaction(object obj)
        {
            if (obj == null) return null;
            
            // Пробуем Property "RimWarFaction"
            try
            {
                var prop = obj.GetType().GetProperty("RimWarFaction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null) return prop.GetValue(obj) as Faction;
            }
            catch { }

            // Пробуем Field "rimwarFaction" (private)
            try
            {
                var field = obj.GetType().GetField("rimwarFaction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(obj) as Faction;
            }
            catch { }

            return null;
        }

        // Извлекает количество поселений из RimWarData безопасно (через приватное поле, не геттер!)
        private static int GetSettlementCountSafe(object rwd)
        {
            if (rwd == null) return 1;
            try
            {
                // Читаем ПРИВАТНОЕ поле worldSettlements напрямую, чтобы НЕ вызывать геттер Property
                // (геттер Property лезет в Find.WorldObjects и вызывает FloodFill/IndexOutOfRange в потоках)
                var field = rwd.GetType().GetField("worldSettlements", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var list = field.GetValue(rwd) as List<Settlement>;
                    if (list != null && list.Count > 0) return list.Count;
                }
            }
            catch { }
            return 1;
        }

        /// <summary>
        /// Ограничивает очки поселения при низком населении. (Порог 30%)
        /// </summary>
        public static void Postfix_RimWarPoints_Getter(WorldObjectComp __instance, ref int __result)
        {
            try
            {
                if (__instance == null || __instance.parent == null || __instance.parent.Faction == null) return;
                Faction f = __instance.parent.Faction;
                
                var popManager = WorldPopulationManager.Instance;
                if (popManager == null) return;

                int fid = f.loadID;
                int adults = 0;
                popManager.factionPopulation.TryGetValue(fid, out adults);
                if (popManager.factionElders.TryGetValue(fid, out float e)) adults += Mathf.CeilToInt(e);
                if (adults <= 0) return;

                // Жёсткий потолок на основе ОБЩЕГО населения фракции (не делим на базы!)
                // 1 человек = 25 очков максимум. 100 человек = 2500, 500 = 12500.
                int hardCap = adults * 25;
                if (hardCap < 100) hardCap = 100;
                if (__result > hardCap)
                {
                    __result = hardCap;
                }
            }
            catch { }
        }

        /// <summary>
        /// Блокирует экспансию если население < 60% от ОДНОЙ базы.
        /// Первый параметр Harmony-префикса — всегда первый аргумент оригинального метода.
        /// </summary>
        public static bool Prefix_BlockExpansion(object __0)
        {
            try
            {
                // __0 = первый аргумент метода (обычно RimWarData rwd)
                Faction f = ExtractFaction(__0);
                if (f == null) return true; // Не смогли найти фракцию — не мешаем

                var popManager = WorldPopulationManager.Instance;
                if (popManager == null) return true;

                int fid = f.loadID;
                int adults = 0;
                popManager.factionPopulation.TryGetValue(fid, out adults);
                popManager.factionChildren.TryGetValue(fid, out float k);
                popManager.factionElders.TryGetValue(fid, out float e);
                int totalLiving = adults + Mathf.CeilToInt(k) + Mathf.CeilToInt(e);

                int sCount = GetSettlementCountSafe(__0);
                int oneBaseCap = GetCapacitySafe(f);

                // СТРОГАЯ ЭКСПАНСИЯ: фракция не может строить новую базу, 
                // если ее население не покрывает текущие базы + ту, что они хотят построить.
                if (totalLiving < (sCount + 1) * oneBaseCap)
                {
                    Log.Message($"[E&D] BLOCKED expansion for {f.Name}: pop {totalLiving} / need {(sCount + 1) * oneBaseCap} for next base");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[E&D] Expansion check error: {ex.Message}");
            }
            return true;
        }

        /// <summary>
        /// Блокирует рейды/торговлю если у фракции 0 взрослых.
        /// </summary>
        public static bool Prefix_BlockRaid(object __0)
        {
            try
            {
                Faction f = ExtractFaction(__0);
                if (f == null) return true;

                var popManager = WorldPopulationManager.Instance;
                if (popManager == null) return true;

                int adults = 0;
                popManager.factionPopulation.TryGetValue(f.loadID, out adults);
                
                if (adults <= 0)
                {
                    Log.Message($"[E&D] BLOCKED raid/trade for {f.Name}: 0 adults");
                    return false;
                }
            }
            catch { }
            return true;
        }

        // Метод для получения общей мощи фракции (суммы очков всех баз)
        public static int GetFactionTotalPoints(Faction f)
        {
            if (f == null) return 0;
            try
            {
                Type rwdTrackerType = AccessTools.TypeByName("RimWar.Planet.WorldComponent_PowerTracker");
                if (rwdTrackerType == null) return 0;

                var tracker = Find.World.GetComponent(rwdTrackerType);
                if (tracker == null) return 0;

                var factionDataList = AccessTools.Field(rwdTrackerType, "rimWarData")?.GetValue(tracker) as System.Collections.IEnumerable;
                if (factionDataList == null) return 0;

                foreach (var rwd in factionDataList)
                {
                    if (ExtractFaction(rwd) == f)
                    {
                        var settlements = GetSettlementListSafe(rwd);
                        if (settlements == null) return 0;

                        int total = 0;
                        Type compType = AccessTools.TypeByName("RimWar.RimWarSettlementComp");
                        var pointsProp = AccessTools.Property(compType, "RimWarPoints");
                        
                        foreach (var s in settlements)
                        {
                            var comp = s.GetComponent(compType);
                            if (comp != null)
                            {
                                total += (int)pointsProp.GetValue(comp);
                            }
                        }
                        return total;
                    }
                }
            }
            catch { }
            return 0;
        }

        private static List<Settlement> GetSettlementListSafe(object rwd)
        {
            if (rwd == null) return null;
            try
            {
                var field = rwd.GetType().GetField("worldSettlements", BindingFlags.NonPublic | BindingFlags.Instance);
                return field?.GetValue(rwd) as List<Settlement>;
            }
            catch { }
            return null;
        }
    }
}
