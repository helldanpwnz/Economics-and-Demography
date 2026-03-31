using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EconomicsDemography
{
    // ПАТЧ: Возврат снаряжения выживших рейдеров/торговцев на склад при уходе с карты.
    [HarmonyPatch(typeof(Pawn), "ExitMap")]
    public static class Patch_ReabsorbGearOnExit
    {
        [HarmonyPostfix]
        static void Postfix(Pawn __instance)
        {
            if (__instance == null || __instance.Faction == null || __instance.Faction.IsPlayer) return;

            var manager = Find.World?.GetComponent<WorldPopulationManager>();
            if (manager != null && manager.IsSimulatedFaction(__instance.Faction))
            {
                List<Thing> gear = new List<Thing>();
                
                // Собираем всё снаряжение для возврата в виртуальный капитал
                if (__instance.apparel != null) gear.AddRange(__instance.apparel.WornApparel);
                if (__instance.equipment != null) gear.AddRange(__instance.equipment.AllEquipmentListForReading);
                if (__instance.inventory != null) gear.AddRange(__instance.inventory.innerContainer);

                if (gear.Count > 0)
                {
                    int fid = __instance.Faction.loadID;
                    float debt = 0f;
                    manager.factionRaidDebt.TryGetValue(fid, out debt);

                    if (debt > 0f)
                    {
                        // Считаем реальную стоимость возвращаемого снаряжения с учетом качества
                        float gearValue = 0f;
                        foreach (var x in gear)
                        {
                            var inner = (x is MinifiedThing m) ? m.InnerThing : x;
                            int q = inner.TryGetComp<CompQuality>() != null ? (int)inner.TryGetComp<CompQuality>().Quality : 2;
                            gearValue += inner.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * x.stackCount;
                        }

                        Log.Warning($"[ED-DEBUG-GEAR] ExitMap ДОЛГ: {__instance.LabelShort} ({__instance.Faction.Name}) возвращает {gearValue:F0} на погашение долга {debt:F0}. Карта: {__instance.Map?.Parent?.GetType().Name}");

                        if (gearValue <= debt)
                        {
                            // Всё снаряжение уходит на погашение долга
                            manager.factionRaidDebt[fid] = debt - gearValue;
                        }
                        else
                        {
                            // Долг погашен, остаток возвращается как серебро
                            manager.factionRaidDebt.Remove(fid);
                            float surplus = gearValue - debt;
                            var stock = manager.GetStockpile(__instance.Faction);
                            stock.silver += UnityEngine.Mathf.RoundToInt(surplus);
                        }
                    }
                    else
                    {
                        float gearValue = 0f;
                        foreach (var x in gear)
                        {
                            var inner = (x is MinifiedThing m) ? m.InnerThing : x;
                            int q = inner.TryGetComp<CompQuality>() != null ? (int)inner.TryGetComp<CompQuality>().Quality : 2;
                            gearValue += inner.def.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) * x.stackCount;
                        }

                        Log.Warning($"[ED-DEBUG-GEAR] ExitMap ВОЗВРАТ: {__instance.LabelShort} ({__instance.Faction.Name}) возвращает {gearValue:F0} на склад. Карта: {__instance.Map?.Parent?.GetType().Name}");
                        // Нет долга — нормальный возврат
                        manager.DepositGoods(__instance.Faction, gear);
                    }
                }
            }
        }
    }
}
