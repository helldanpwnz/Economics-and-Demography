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
    // Хранит прогресс производства для фракции по каждому товару.
    // Используется для накопительного производства (cumulative progress).
    public class FactionProductionProgress : IExposable
    {
        // Тот самый внутренний словарь: [Название товара -> Накопленное серебро]
        public Dictionary<string, float> progress = new Dictionary<string, float>();
        public Dictionary<string, int> lastUpdateTick = new Dictionary<string, int>();

        // Обязательный пустой конструктор для Scribe
        public FactionProductionProgress() { }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref progress, "progress", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref lastUpdateTick, "lastUpdateTick", LookMode.Value, LookMode.Value);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (progress == null) progress = new Dictionary<string, float>();
                if (lastUpdateTick == null) lastUpdateTick = new Dictionary<string, int>();
            }
        }
    }
}
