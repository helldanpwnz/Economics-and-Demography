using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EconomicsDemography
{
    // === Универсальный класс-обертка для патчей богатства ===
    // Используем точки входа, которые точно есть в RimWorld 1.5.
    
    public static class WealthPatcherUtility
    {
        public static void Prefix()
        {
            WorldPopulationManager.WealthCalculationDepth++;
        }

        public static void Finalizer(Exception __exception)
        {
            WorldPopulationManager.WealthCalculationDepth--;
            if (WorldPopulationManager.WealthCalculationDepth < 0)
                WorldPopulationManager.WealthCalculationDepth = 0;
        }
    }

    // Точка пересчета богатства карты (точно есть в 1.5)
    [HarmonyPatch(typeof(WealthWatcher), "ForceRecount")]
    public static class Patch_WealthWatcher_Recount
    {
        [HarmonyPrefix] public static void Prefix() => WealthPatcherUtility.Prefix();
        [HarmonyFinalizer] public static void Finalizer(Exception __exception) => WealthPatcherUtility.Finalizer(__exception);
    }

    // Глобальная точка расчета силы рейдов (StorytellerUtility)
    [HarmonyPatch(typeof(StorytellerUtility), "DefaultThreatPointsNow")]
    public static class Patch_Storyteller_ThreatPoints
    {
        [HarmonyPrefix] public static void Prefix() => WealthPatcherUtility.Prefix();
        [HarmonyFinalizer] public static void Finalizer(Exception __exception) => WealthPatcherUtility.Finalizer(__exception);
    }
}
