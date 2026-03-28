using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace EconomicsDemography
{
    // Отдельный патч для спавна гражданского населения (детей и стариков) в поселениях.
    [HarmonyPatch(typeof(MapGenerator), "GenerateMap")]
    public static class Patch_SettlementCivilianSpawning
    {
        [HarmonyPostfix]
        static void Postfix(Map __result)
        {
            Map map = __result;

            if (map == null || map.Parent == null || map.IsPlayerHome) return;
            if (!(map.Parent is Settlement settlement)) return;

            Faction f = settlement.Faction;
            if (f == null || f.IsPlayer || !f.def.humanlikeFaction) return;

            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null || !EconomicsDemographyMod.Settings.enableCivilianSpawning) return;

            int fid = f.loadID;
            
            // Получаем количество взрослых защитников для расчета пропорции
            int adultsOnMap = map.mapPawns.SpawnedPawnsInFaction(f).Count;
            if (adultsOnMap <= 0) adultsOnMap = 5; 

            float virtualAdults = Mathf.Max(1f, manager.factionPopulation.TryGetValue(fid, out int p) ? p : 0f);
            float virtualKids = manager.factionChildren.TryGetValue(fid, out float k) ? k : 0f;
            float virtualElders = manager.factionElders.TryGetValue(fid, out float e) ? e : 0f;

            int kSpawn = Mathf.Min(30, GenMath.RoundRandom((virtualKids / virtualAdults) * adultsOnMap));
            int eSpawn = Mathf.Min(20, GenMath.RoundRandom((virtualElders / virtualAdults) * adultsOnMap));

            if (kSpawn <= 0 && eSpawn <= 0) return;

            List<IntVec3> diningCells = new List<IntVec3>();
            List<IntVec3> bedroomCells = new List<IntVec3>();
            AnalyzeMapRooms(map, f, diningCells, bedroomCells);

            PawnKindDef kind = f.def.basicMemberKind;
            if (kind == null && f.def.pawnGroupMakers != null)
            {
                kind = f.def.pawnGroupMakers.SelectMany(gm => gm.options).OrderBy(o => o.selectionWeight).FirstOrDefault()?.kind;
            }
            if (kind == null) return;

            // Спавн детей
            for (int i = 0; i < kSpawn; i++)
            {
                GenerateAndSpawnCivilian(map, kind, f, bedroomCells, diningCells, true);
            }

            // Спавн стариков
            for (int i = 0; i < eSpawn; i++)
            {
                GenerateAndSpawnCivilian(map, kind, f, diningCells, bedroomCells, false);
            }
        }

        private static void GenerateAndSpawnCivilian(Map map, PawnKindDef kind, Faction f, List<IntVec3> preferredCells, List<IntVec3> fallbackCells, bool isChild)
        {
            try {
                IntVec3 cell = (preferredCells.Count > 0) ? preferredCells.RandomElement() : (fallbackCells.Count > 0 ? fallbackCells.RandomElement() : map.Center);
                if (!cell.Standable(map)) return;

                float? age = isChild ? Rand.Range(3f, 12f) : Rand.Range(65f, 85f);
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind, 
                    f, 
                    context: PawnGenerationContext.NonPlayer, 
                    forceGenerateNewPawn: true, 
                    mustBeCapableOfViolence: false, 
                    fixedBiologicalAge: age, 
                    inhabitant: true
                );
                
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn != null)
                {
                    // РАЗОРУЖЕНИЕ 
                    pawn.equipment?.DestroyAllEquipment();
                    
                    if (pawn.apparel != null)
                    {
                        // Удаляем БРОНЮ (шлемы, бронежилеты), оставляя обычную одежду
                        List<Apparel> armorItems = pawn.apparel.WornApparel.Where(x => 
                            x.def.apparel.bodyPartGroups.Any(g => g == BodyPartGroupDefOf.FullHead || g == BodyPartGroupDefOf.Torso) && 
                            x.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.25f
                        ).ToList();
                        
                        foreach (var armor in armorItems)
                        {
                            pawn.apparel.Remove(armor);
                        }

                        // Совместимость с чужими расами: если пешка голая, пробуем одеть её безопасно
                        bool needsTorso = !pawn.apparel.WornApparel.Any(arg => arg.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Torso));
                        bool needsLegs = !pawn.apparel.WornApparel.Any(arg => arg.def.apparel.bodyPartGroups.Contains(BodyPartGroupDefOf.Legs));

                        if (needsTorso || needsLegs)
                        {
                            bool isAlien = pawn.def.GetType().Name.Contains("AlienRace");
                            
                            if (needsTorso)
                            {
                                ThingDef shirtDef = isAlien ? FindAppropriateApparel(pawn, BodyPartGroupDefOf.Torso) : (DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_BasicShirt") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Tshirt"));
                                if (shirtDef != null) WearSafe(pawn, shirtDef);
                            }
                            if (needsLegs)
                            {
                                ThingDef pantsDef = isAlien ? FindAppropriateApparel(pawn, BodyPartGroupDefOf.Legs) : DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Pants");
                                if (pantsDef != null) WearSafe(pawn, pantsDef);
                            }
                        }
                    }

                    GenSpawn.Spawn(pawn, cell, map);
                    
                    if (f.HostileTo(Faction.OfPlayer)) 
                    {
                        pawn.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.PanicFlee, null, true);
                    }
                }
            } catch (Exception ex) { Log.ErrorOnce("ED_CivilianSpawnError: " + ex, 192839); }
        }

        // Ищет подходящую одежду для конкретной расы, если стандартная не подходит.
        private static ThingDef FindAppropriateApparel(Pawn pawn, BodyPartGroupDef group)
        {
            // Пытаемся найти что-то из тегов его KindDef, что не является броней
            if (pawn.kindDef.apparelTags != null)
            {
                var candidate = DefDatabase<ThingDef>.AllDefs.Where(d => 
                    d.IsApparel && 
                    d.apparel.bodyPartGroups.Contains(group) && 
                    pawn.kindDef.apparelTags.Any(tag => d.apparel.tags.Contains(tag)) &&
                    d.GetStatValueAbstract(StatDefOf.ArmorRating_Sharp) < 0.25f
                ).OrderBy(d => d.BaseMarketValue).FirstOrDefault();

                if (candidate != null) return candidate;
            }
            // Фаллбек на человеческую одежду (большинство рас HAR её поддерживают)
            return group == BodyPartGroupDefOf.Torso ? (DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Tshirt")) : DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_Pants");
        }

        private static void WearSafe(Pawn pawn, ThingDef def)
        {
            try {
                if (!ApparelUtility.HasPartsToWear(pawn, def)) return;
                
                Apparel ap = (Apparel)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                pawn.apparel.Wear(ap);
            } catch { }
        }

        private static void AnalyzeMapRooms(Map map, Faction f, List<IntVec3> dining, List<IntVec3> bedroom)
        {
            List<Room> allRooms = Traverse.Create(map.regionGrid).Field("allRooms").GetValue<List<Room>>();
            if (allRooms == null) return;

            foreach (Room room in allRooms)
            {
                if (room.PsychologicallyOutdoors || !room.ContainedAndAdjacentThings.Any(t => t.Faction == f)) continue;

                bool hasBed = false;
                bool hasTable = false;

                foreach (Thing t in room.ContainedAndAdjacentThings)
                {
                    if (t.def.IsBed) { hasBed = true; break; }
                    if (t.def.IsTable || (t.def.building != null && t.def.building.isSittable)) hasTable = true;
                }

                List<IntVec3> targetList = hasBed ? bedroom : (hasTable ? dining : null);
                if (targetList == null) continue;

                foreach (IntVec3 c in room.Cells)
                {
                    if (c.Standable(map) && c.GetFirstItem(map) == null) targetList.Add(c);
                }
            }
            dining.Shuffle();
            bedroom.Shuffle();
        }
    }
}
