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
    // Вспомогательный класс для сохранения состояния при генерации карты.
    public class GenState
    {
        public Faction faction;
        public int startPop;
    }
}
