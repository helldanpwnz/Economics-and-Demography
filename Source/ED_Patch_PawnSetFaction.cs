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
    // Отслеживает вербовку и смену фракции пешек, чтобы корректировать популяцию.
    [HarmonyPatch(typeof(Pawn), "SetFaction")]
    public static class Patch_PawnSetFaction
    {
        [HarmonyPrefix]
        static void Prefix(Pawn __instance, Faction newFaction)
        {
            if (__instance == null || !__instance.RaceProps.Humanlike) return;

            Faction oldFaction = __instance.Faction;
            if (oldFaction == newFaction) return;

            var manager = Find.World?.GetComponent<WorldPopulationManager>();
            if (manager == null) return;

            // 1. Убыль в старой фракции
            if (manager.IsSimulatedFaction(oldFaction))
            {
                if (manager.IsInitialized(oldFaction))
                {
                    manager.ModifyPopulation(oldFaction, -1, __instance.gender, __instance);
                    Log.Message(string.Format((string)"ED_Log_PawnLeftFaction".Translate(), __instance.NameShortColored, oldFaction.Name));
                }
            }
            
            // 2. Прирост в новой фракции
            if (manager.IsSimulatedFaction(newFaction))
            {
                if (manager.IsInitialized(newFaction))
                {
                    manager.ModifyPopulation(newFaction, 1, __instance.gender, __instance);
                    Log.Message(string.Format((string)"ED_Log_PawnJoinedFaction".Translate(), __instance.NameShortColored, newFaction.Name));
                }
            }
        }
    }
}
