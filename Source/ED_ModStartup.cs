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
    // Инициализация мода: запускает Harmony и выводит сообщение о загрузке.
    [StaticConstructorOnStartup]
    public static class ModStartup
    {
        static ModStartup()
        {
            var harmony = new Harmony("helldan.finitepopulation");
            harmony.PatchAll();
            Log.Message("<color=green>[Finite Population]</color> v49.00: FINAL MATRIX (Lifecycle, Migration, Tech-Balance).");
        }
    }
}
