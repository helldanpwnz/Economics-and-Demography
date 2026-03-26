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
    // Трекер генерации карт – устанавливает флаг во время генерации.
    [HarmonyPatch(typeof(MapGenerator), "GenerateContentsIntoMap")]
    public static class Patch_MapGen_Tracker
    {
        public static bool IsGeneratingMap = false;

        [HarmonyPriority(Priority.First)] 
        [HarmonyPrefix]
        static void Prefix()
        {
            IsGeneratingMap = true;
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyFinalizer]
        static void Finalizer(Exception __exception)
        {
            IsGeneratingMap = false;
        }
    }
}
