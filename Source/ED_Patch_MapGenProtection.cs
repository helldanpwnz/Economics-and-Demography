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
    // Защищает популяцию фракции при генерации карты (не даёт исчезнуть людям при создании карты поселения).
    [HarmonyPatch(typeof(MapGenerator), "GenerateMap")]
    public static class Patch_MapGenProtection
    {
        [HarmonyPrefix]
        static void Prefix(RimWorld.Planet.MapParent parent, ref GenState __state)
        {
            if (parent?.Faction != null && !parent.Faction.IsPlayer)
            {
                var manager = Find.World.GetComponent<WorldPopulationManager>();
                if (manager != null) 
                    __state = new GenState { faction = parent.Faction, startPop = manager.GetPopulation(parent.Faction) };
            }
        }
        
        [HarmonyPostfix]
        static void Postfix(GenState __state)
        {
            if (__state?.faction == null) return;
            
            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager != null && manager.GetPopulation(__state.faction) < __state.startPop) 
            {
                manager.SilentRestorePopulation(__state.faction, __state.startPop);
            }
        }
    }
}
