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
    // Контролирует пол генерируемых пешек для неигровых фракций на основе демографии.
    [HarmonyPatch(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator
    {
        [HarmonyPriority(1000)] 
        [HarmonyPrefix]
        static bool Prefix(ref PawnGenerationRequest request)
        {
            if (request.Faction == null || !request.Faction.def.humanlikeFaction) return true;
            
            if (request.Faction.IsPlayer) return true;

            if (request.FixedGender.HasValue) return true;

            if (Find.World == null) return true;
            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null) return true;

            try
            {
                int fid = request.Faction.loadID;
                
                int females = manager.factionFemales.TryGetValue(fid, out int f) ? f : 0;
                int population = manager.factionPopulation.TryGetValue(fid, out int p) ? p : 1;
                
                float total = Mathf.Max(1, population);
                float femaleChance = (float)females / total;

                Gender targetGender = (Rand.Value < femaleChance) ? Gender.Female : Gender.Male;

                request.FixedGender = targetGender;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[E&D] Ошибка в генераторе пола: {ex}", 94321);
            }

            return true;
        }
    }
}
