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
    // Корректирует размер рейдов в зависимости от доступного населения фракции.
    [HarmonyPatch(typeof(IncidentWorker_Raid), "TryExecuteWorker")]
    public static class Patch_RaidSafety
    {
        [HarmonyPriority(1000)]
        [HarmonyPrefix]
        static bool Prefix(IncidentParms parms)
        {
            if (parms.faction == null || parms.faction.IsPlayer || parms.faction.defeated) return !parms.faction?.defeated ?? true;
            
            var manager = Find.World.GetComponent<WorldPopulationManager>();
            int adults = manager?.GetPopulation(parms.faction) ?? -1;
            int elders = 0;
            if (manager != null && manager.factionElders.TryGetValue(parms.faction.loadID, out float e))
                elders = Mathf.CeilToInt(e);
            
            int deployedPawns = 0;
            if (Find.Maps != null) 
                foreach (Map map in Find.Maps) 
                    deployedPawns += map.mapPawns.SpawnedPawnsInFaction(parms.faction).Count;
            
            int availablePop = (adults + elders) - deployedPawns;
            if (availablePop < 5) 
            {
                //Log.Message($"[E&D] Raid blocked for {parms.faction.Name}: only {availablePop} people available.");
                return false; 
            }
            
            float costPerPawn = 60f; 
            if (parms.faction.def.techLevel <= TechLevel.Neolithic) costPerPawn = 45f;
            else if (parms.faction.def.techLevel == TechLevel.Medieval) costPerPawn = 55f;
            else if (parms.faction.def.techLevel >= TechLevel.Spacer) costPerPawn = 110f;
            
            float calculatedPoints = availablePop * costPerPawn;

            // БЛОКИРОВКА ПО ДОЛГУ: фракция не может напасть, пока не погасит долг за прошлый рейд.
            if (manager != null && manager.factionRaidDebt.TryGetValue(parms.faction.loadID, out float debt) && debt > 0f)
            {
                return false;
            }

            if (parms.points > calculatedPoints) parms.points = Mathf.Max(100f, calculatedPoints);
            
            return true;
        }
    }
}
