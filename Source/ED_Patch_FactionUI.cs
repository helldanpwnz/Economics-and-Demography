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
    // [HarmonyPatch(typeof(FactionUIUtility), "DrawFactionRow")]
    // ДАННЫЙ ПАТЧ ОТКЛЮЧЕН. Теперь весь интерфейс перенесен в отдельную вкладку "Популяция".
    public static class Patch_FactionUI
    {
        // static void Postfix(Faction faction, float rowY, Rect fillRect) { }
    }
}
