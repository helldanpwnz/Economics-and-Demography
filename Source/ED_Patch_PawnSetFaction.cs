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

            // 1. Убыль в старой фракции (если она не игрок и не скрытая)
            if (oldFaction != null && !oldFaction.IsPlayer && !oldFaction.def.hidden)
            {
                // Уменьшаем популяцию, так как пешка покинула ряды фракции (вербовка, переход)
                manager.ModifyPopulation(oldFaction, -1, __instance.gender, __instance);
                Log.Message($"[E&D] Пешка {__instance.Name} покинула {oldFaction.Name}. Популяция скорректирована (-1).");
            }

            // 2. Прирост в новой фракции (если она не игрок и не скрытая)
            if (newFaction != null && !newFaction.IsPlayer && !newFaction.def.hidden)
            {
                // Увеличиваем популяцию, так как пешка вступила во фракцию (например, деффект или переход между ИИ фракциями)
                manager.ModifyPopulation(newFaction, 1, __instance.gender, __instance);
                Log.Message($"[E&D] Пешка {__instance.Name} вступила в {newFaction.Name}. Популяция скорректирована (+1).");
            }
        }
    }
}
