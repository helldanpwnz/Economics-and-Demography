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
    // Генерирует лут на карте при создании поселения на основе виртуального склада.
    [HarmonyPatch(typeof(MapGenerator), "GenerateMap")]
    public static class Patch_SpawnFactionLoot
    {
        [HarmonyPostfix]
        static void Postfix(Map __result)
        {
            Map map = __result;

            if (map == null || map.Parent == null || map.IsPlayerHome) return;
            if (!(map.Parent is Settlement settlement)) return;

            Faction f = settlement.Faction;
            if (f == null || f.IsPlayer) return;

            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null) return;

            if (manager.processedSettlements.Contains(settlement.ID)) return;
            manager.processedSettlements.Add(settlement.ID);

            VirtualStockpile stock = manager.GetStockpile(f);
            if (stock == null) return;

            int basesCount = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
            if (basesCount == 0) basesCount = 1;

            IntVec3 rootSpot = FindBestLootSpot(map, f);
            
            // Считаем стоимость ванильных предметов, которые игра уже разложила на карте.
            // ReabsorbLootOnLeave соберёт их при уходе, поэтому списываем заранее.
            float vanillaGroundValue = 0f;
            foreach (var t in map.listerThings.AllThings)
            {
                if (t.def.category != ThingCategory.Item) continue;
                if (t.ParentHolder is Pawn) continue;
                if (t.def.defName.Contains("Chunk")) continue;
                vanillaGroundValue += t.def.BaseMarketValue * t.stackCount;
            }

            List<Thing> allLoot = new List<Thing>();

            if (stock.silver > 0)
            {
                int share = stock.silver / basesCount;
                if (share > 10)
                {
                    stock.silver -= share;
                    while (share > 0)
                    {
                        int stack = Mathf.Min(share, 500);
                        Thing s = ThingMaker.MakeThing(ThingDefOf.Silver);
                        s.stackCount = stack;
                        allLoot.Add(s);
                        share -= stack;
                    }
                }
            }

            List<string> keys = stock.inventory.Keys.ToList();
            foreach (var key in keys)
            {
                int totalAmount = stock.inventory[key];
                if (totalAmount <= 0) continue;

                string defName;
                int q;
                VirtualStockpile.ParseKey(key, out defName, out q);

                int share = totalAmount / basesCount;
                if (share == 0 && totalAmount > 0 && Rand.Value < 0.3f) share = 1;

                if (share > 0)
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def != null)
                    {
                        // На полу в поселении — по 1 экземпляру каждого вида оружия/брони
                        if (def.stackLimit == 1) share = 1;

                        stock.inventory[key] -= share;
                        if (stock.inventory[key] < 0) stock.inventory[key] = 0;
                        CreateAndAddThings(def, share, allLoot, q);
                    }
                }
            }

            if (allLoot.Count > 0)
            {
                DistributeLootThematic(map, rootSpot, allLoot, f);
            }

            // Списываем стоимость ИНВЕНТАРЯ защитников (только то, что WorldPawns_SaveInventory вернёт при уходе).
            // Броню и оружие НЕ считаем — они не возвращаются на склад при удалении карты поселения.
            float defenderGearValue = 0f;
            foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(f))
            {
                if (pawn.inventory != null)
                    foreach (var i in pawn.inventory.innerContainer) defenderGearValue += (i.def.BaseMarketValue * i.stackCount);
            }

            float totalMapCost = defenderGearValue + vanillaGroundValue;

            if (totalMapCost > 0f)
            {
                if (!stock.TryConsumeWealth(totalMapCost, manager.globalPriceModifiers, true))
                {
                    float wealthBefore = stock.GetTotalWealth();
                    float unpaid = totalMapCost - wealthBefore;
                    int fid = f.loadID;
                    if (!manager.factionRaidDebt.ContainsKey(fid)) manager.factionRaidDebt[fid] = 0f;
                    manager.factionRaidDebt[fid] += unpaid;
                    stock.silver = 0;
                    stock.inventory.Clear();
                }
            }
        }

        private static void DistributeLootThematic(Map map, IntVec3 rootSpot, List<Thing> loot, Faction f)
        {
            List<IntVec3> storageCells = new List<IntVec3>();
            List<IntVec3> diningCells = new List<IntVec3>();
            List<IntVec3> bedroomCells = new List<IntVec3>();

            List<Room> allRooms = Traverse.Create(map.regionGrid).Field("allRooms").GetValue<List<Room>>();

            if (allRooms != null)
            {
                foreach (Room room in allRooms)
                {
                    if (room.PsychologicallyOutdoors || !room.ContainedAndAdjacentThings.Any(t => t.Faction == f)) continue;

                    bool hasBed = false;
                    bool hasTable = false;

                    foreach (Thing t in room.ContainedAndAdjacentThings)
                    {
                        if (t.def.IsBed)
                        {
                            hasBed = true;
                            break; 
                        }
                        if (t.def.IsTable || (t.def.building != null && t.def.building.isSittable))
                        {
                            hasTable = true;
                        }
                    }

                    List<IntVec3> targetList;
                    if (hasBed) targetList = bedroomCells;
                    else if (hasTable) targetList = diningCells;
                    else targetList = storageCells;

                    foreach (IntVec3 c in room.Cells)
                    {
                        if (c.Standable(map) && c.GetFirstItem(map) == null)
                        {
                            targetList.Add(c);
                        }
                    }
                }
            }

            storageCells.Shuffle();
            diningCells.Shuffle();
            bedroomCells.Shuffle();

            Log.Message("ED_Log_MapLootDebug".Translate(storageCells.Count, diningCells.Count, bedroomCells.Count, loot.Count));

            List<Thing> foodLoot = new List<Thing>();
            List<Thing> weaponLoot = new List<Thing>();
            List<Thing> resourceLoot = new List<Thing>();

            foreach (Thing t in loot)
            {
                if (t.def.IsIngestible) foodLoot.Add(t);
                else if (t.def.IsWeapon || t.def.IsApparel) weaponLoot.Add(t);
                else resourceLoot.Add(t);
            }

            PlaceCategory(foodLoot, map, rootSpot, diningCells, storageCells, bedroomCells);
            PlaceCategory(weaponLoot, map, rootSpot, storageCells, bedroomCells, diningCells);
            PlaceCategory(resourceLoot, map, rootSpot, storageCells, diningCells, bedroomCells);
        }

        private static void PlaceCategory(List<Thing> items, Map map, IntVec3 fallbackSpot, List<IntVec3> p1, List<IntVec3> p2, List<IntVec3> p3)
        {
            if (items.Count == 0) return;

            List<IntVec3> masterList = new List<IntVec3>(p1.Count + p2.Count + p3.Count);
            masterList.AddRange(p1);
            masterList.AddRange(p2);
            masterList.AddRange(p3);

            int cellIndex = 0;

            foreach (Thing t in items)
            {
                bool placed = false;

                while (cellIndex < masterList.Count)
                {
                    IntVec3 cell = masterList[cellIndex];

                    if (GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Direct, null, null))
                    {
                        placed = true;
                        cellIndex++;
                        break;
                    }
                    cellIndex++;
                }

                if (!placed)
                {
                    IntVec3 spillSpot = (masterList.Count > 0) ? masterList.Last() : fallbackSpot;
                    GenPlace.TryPlaceThing(t, spillSpot, map, ThingPlaceMode.Near, null, null);
                }
            }
        }

        private static void CreateAndAddThings(ThingDef def, int count, List<Thing> outList, int quality = -1)
        {
            int loopGuard = 0;
            while (count > 0 && loopGuard < 500)
            {
                int stack = Mathf.Min(count, def.stackLimit);
                ThingDef stuff = null;
                if (def.MadeFromStuff)
                {
                    stuff = GenStuff.DefaultStuffFor(def);
                    if (stuff == null)
                    {
                        if (def.IsMeleeWeapon) stuff = ThingDefOf.Steel;
                        else if (def.IsApparel) stuff = ThingDefOf.Cloth;
                        else stuff = ThingDefOf.WoodLog;
                    }
                }

                Thing t = ThingMaker.MakeThing(def, stuff);
                t.stackCount = stack;
                if (quality >= 0) t.TryGetComp<CompQuality>()?.SetQuality((QualityCategory)quality, ArtGenerationContext.Outsider);
                else t.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Normal, ArtGenerationContext.Outsider);
                outList.Add(t);
                count -= stack;
                loopGuard++;
            }
        }

        private static IntVec3 FindBestLootSpot(Map map, Faction f)
        {
            Building b = (f != null) ? map.listerBuildings.allBuildingsNonColonist.FirstOrDefault(x => x.Faction == f) : null;
            IntVec3 center = (b != null) ? b.Position : map.Center;
            IntVec3 result;
            
            bool found = CellFinder.TryFindRandomCellNear(center, map, 25, (c) => 
            {
                if (!c.Standable(map) || !c.Roofed(map)) return false;
                
                var room = c.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors || !room.ContainedAndAdjacentThings.Any(t => t.Faction == f)) return false;

                var edifice = c.GetEdifice(map);
                if (edifice != null && edifice is Building_Door) return false;

                if (c.GetFirstItem(map) != null) return false;

                return true;
            }, out result);

            if (found) return result;

            if (CellFinder.TryFindRandomCellNear(center, map, 30, (c) => 
                c.Standable(map) && (c.GetEdifice(map) == null || !(c.GetEdifice(map) is Building_Door)), 
                out result)) 
            {
                return result;
            }

            return center;
        }
    }
}
