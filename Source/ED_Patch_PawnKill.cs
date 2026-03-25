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
    // Учитывает смерть пешек во фракциях.
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class Patch_PawnKill
    {
        [HarmonyPrefix]
        static void Prefix(Pawn __instance)
        {
            if (__instance?.RaceProps != null && __instance.RaceProps.Humanlike && 
                __instance.Faction != null && !__instance.Faction.IsPlayer)
            {
                Find.World.GetComponent<WorldPopulationManager>()?.ModifyPopulation(__instance.Faction, -1, __instance.gender, __instance);
            }
        }
    }
}
