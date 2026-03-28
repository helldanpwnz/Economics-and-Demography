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
                if (!manager.factionPopulation.ContainsKey(fid)) return true;

                // Защита от конфликтов с бесполыми расами, HAR и монополыми расами
                if (request.KindDef != null)
                {
                    if (request.KindDef.fixedGender.HasValue) return true;
                    
                    if (request.KindDef.race != null)
                    {
                        if (request.KindDef.race.race != null && !request.KindDef.race.race.hasGenders) return true;
                        
                        // Если это инопланетная раса (HAR), мы не должны вмешиваться в пол,
                        // потому что у них свои настройки вероятности (maleGenderProbability) и текстур!
                        if (request.KindDef.race.GetType().Name.Contains("AlienRace")) return true;
                    }
                }

                int females = manager.factionFemales.TryGetValue(fid, out int f) ? f : 0;
                int population = manager.factionPopulation[fid];
                
                float total = Mathf.Max(1, population);
                float femaleChance = (float)females / total;

                Gender targetGender = (Rand.Value < femaleChance) ? Gender.Female : Gender.Male;

                request.FixedGender = targetGender;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("ED_Log_GenderGeneratorError".Translate(ex.ToString()), 94321);
            }

            return true;
        }
    }
}
