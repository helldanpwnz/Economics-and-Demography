using System;
using UnityEngine;
using Verse;

namespace EconomicsDemography
{
    public class EconomicsDemographySettings : ModSettings
    {
        // 1. Инфляция
        public bool enableGlobalInflation = true;
        public bool enableGoldStandard = false;
        public float homeostasisEfficiency = 0.001f;
        private Vector2 scrollPosition = Vector2.zero;
        public bool enableLogs = false;
        public float capAnimal = 150f;
        public float capNeolithic = 150f;
        public float capMedieval = 130f;
        public float capIndustrial = 100f;
        public float capSpacer = 80f;
        public float capUltra = 70f;
        public float capArchotech = 60f;
        public float birthAnimal = 1f;
        public float birthNeolithic = 1f;
        public float birthMedieval = 1f;
        public float birthIndustrial = 1f;
        public float birthSpacer = 1f;
        public float birthUltra = 1f;
        public float birthArchotech = 1f;
        public float eventPopMultiplier = 1f;
        public float daysToAdult = 240f;
        public float daysToElder = 1200f;
        public float consAnimal = 1f;
        public float consNeolithic = 1f;
        public float consMedieval = 1f;
        public float consIndustrial = 1f;
        public float consSpacer = 1f;
        public float consUltra = 1f;
        public float consArchotech = 1f;
        public float prodAnimal = 1f;
        public float prodNeolithic = 1f;
        public float prodMedieval = 1f;
        public float prodIndustrial = 1f;
        public float prodSpacer = 1f;
        public float prodUltra = 1f;
        public float prodArchotech = 1f;
        public float expansionThresholdFactor = 1.1f;
        public float updateIntervalHours = 24f;
        public float priceUpdateFactor = 0.2f;
        public float inflationUpdateFactor = 0.2f;
        public float collapseThresholdFactor = 0.5f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableGlobalInflation, "enableGlobalInflation", true);
            Scribe_Values.Look(ref enableGoldStandard, "enableGoldStandard", false);
            Scribe_Values.Look(ref homeostasisEfficiency, "homeostasisEfficiency", 0.001f);
            Scribe_Values.Look(ref enableLogs, "enableLogs", false);
            Scribe_Values.Look(ref capAnimal, "capAnimal", 150f);
            Scribe_Values.Look(ref capNeolithic, "capNeolithic", 150f);
            Scribe_Values.Look(ref capMedieval, "capMedieval", 130f);
            Scribe_Values.Look(ref capIndustrial, "capIndustrial", 100f);
            Scribe_Values.Look(ref capSpacer, "capSpacer", 80f);
            Scribe_Values.Look(ref capUltra, "capUltra", 70f);
            Scribe_Values.Look(ref capArchotech, "capArchotech", 60f);
            Scribe_Values.Look(ref birthAnimal, "birthAnimal", 1f);
            Scribe_Values.Look(ref birthNeolithic, "birthNeolithic", 1f);
            Scribe_Values.Look(ref birthMedieval, "birthMedieval", 1f);
            Scribe_Values.Look(ref birthIndustrial, "birthIndustrial", 1f);
            Scribe_Values.Look(ref birthSpacer, "birthSpacer", 1f);
            Scribe_Values.Look(ref birthUltra, "birthUltra", 1f);
            Scribe_Values.Look(ref birthArchotech, "birthArchotech", 1f);
            Scribe_Values.Look(ref eventPopMultiplier, "eventPopMultiplier", 1f);
            Scribe_Values.Look(ref daysToAdult, "daysToAdult", 240f);
            Scribe_Values.Look(ref daysToElder, "daysToElder", 1200f);
            Scribe_Values.Look(ref consAnimal, "consAnimal", 1f);
            Scribe_Values.Look(ref consNeolithic, "consNeolithic", 1f);
            Scribe_Values.Look(ref consMedieval, "consMedieval", 1f);
            Scribe_Values.Look(ref consIndustrial, "consIndustrial", 1f);
            Scribe_Values.Look(ref consSpacer, "consSpacer", 1f);
            Scribe_Values.Look(ref consUltra, "consUltra", 1f);
            Scribe_Values.Look(ref consArchotech, "consArchotech", 1f);
            Scribe_Values.Look(ref prodAnimal, "prodAnimal", 1f);
            Scribe_Values.Look(ref prodNeolithic, "prodNeolithic", 1f);
            Scribe_Values.Look(ref prodMedieval, "prodMedieval", 1f);
            Scribe_Values.Look(ref prodIndustrial, "prodIndustrial", 1f);
            Scribe_Values.Look(ref prodSpacer, "prodSpacer", 1f);
            Scribe_Values.Look(ref prodUltra, "prodUltra", 1f);
            Scribe_Values.Look(ref prodArchotech, "prodArchotech", 1f);
            Scribe_Values.Look(ref expansionThresholdFactor, "expansionThresholdFactor", 1.1f);
            Scribe_Values.Look(ref updateIntervalHours, "updateIntervalHours", 24f);
            Scribe_Values.Look(ref priceUpdateFactor, "priceUpdateFactor", 0.2f);
            Scribe_Values.Look(ref inflationUpdateFactor, "inflationUpdateFactor", 0.2f);
            Scribe_Values.Look(ref collapseThresholdFactor, "collapseThresholdFactor", 0.5f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Log.Enabled = enableLogs;
            }
        }

        public void ResetToDefaults()
        {
            capAnimal = 150f;
            capNeolithic = 150f;
            capMedieval = 130f;
            capIndustrial = 100f;
            capSpacer = 80f;
            capUltra = 70f;
            capArchotech = 60f;
            birthAnimal = 1f;
            birthNeolithic = 1f;
            birthMedieval = 1f;
            birthIndustrial = 1f;
            birthSpacer = 1f;
            birthUltra = 1f;
            birthArchotech = 1f;
            eventPopMultiplier = 1f;
            daysToAdult = 240f;
            daysToElder = 1200f;
            consAnimal = 1f;
            consNeolithic = 1f;
            consMedieval = 1f;
            consIndustrial = 1f;
            consSpacer = 1f;
            consUltra = 1f;
            consArchotech = 1f;
            prodAnimal = 1f;
            prodNeolithic = 1f;
            prodMedieval = 1f;
            prodIndustrial = 1f;
            prodSpacer = 1f;
            prodUltra = 1f;
            prodArchotech = 1f;
            expansionThresholdFactor = 1.1f;
            updateIntervalHours = 24f;
            priceUpdateFactor = 0.2f;
            inflationUpdateFactor = 0.2f;
            collapseThresholdFactor = 0.5f;
            enableGlobalInflation = true;
            enableGoldStandard = false;
            homeostasisEfficiency = 0.001f;
            enableLogs = false;
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            // Сдвигаем начало прокрутки на 45 пикселей вниз, чтобы не налезать на заголовок мода
            Rect outRect = new Rect(0f, 45f, inRect.width, inRect.height - 45f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 24f, 2800f);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.Label("ED_GeneralSettings".Translate());
            listing.GapLine();
            
            listing.Label("ED_UpdateInterval".Translate(updateIntervalHours.ToString("F0")));
            updateIntervalHours = listing.Slider(updateIntervalHours, 1f, 300f);
            
            listing.Gap(6f);
            listing.Label("ED_CollapseThreshold".Translate((collapseThresholdFactor * 100f).ToString("F0")));
            collapseThresholdFactor = listing.Slider(collapseThresholdFactor, 0.05f, 0.95f);
            
            listing.Gap(12f);
            listing.Label("ED_EconomySettings".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("ED_InflationEnabled".Translate(), ref enableGlobalInflation, "ED_InflationDesc".Translate());
            
            // Золотой стандарт
            listing.CheckboxLabeled("ED_GoldStandard".Translate(), ref enableGoldStandard, "ED_GoldStandardDesc".Translate());

            listing.Gap(6f);
            listing.Label("ED_PriceUpdateFactor".Translate((priceUpdateFactor * 100f).ToString("F0")));
            priceUpdateFactor = listing.Slider(priceUpdateFactor, 0.01f, 1.0f);
            
            listing.Label("ED_InflationUpdateFactor".Translate((inflationUpdateFactor * 100f).ToString("F0")));
            inflationUpdateFactor = listing.Slider(inflationUpdateFactor, 0.01f, 1.0f);

            if (EconomicsDemographyMod.Settings.enableLogs) // Tooltip-like label
                listing.Label("  <color=gray>" + "ED_InflationUpdateFactorDesc".Translate() + "</color>", 12f);
            if (EconomicsDemographyMod.Settings.enableLogs) // Tooltip-like label
                listing.Label("  <color=gray>" + "ED_PriceUpdateFactorDesc".Translate() + "</color>", 12f);

            listing.Gap(6f);
            listing.Label("ED_HomeostasisEfficiency".Translate((homeostasisEfficiency * 100f).ToString("F2") + "%"));
            homeostasisEfficiency = listing.Slider(homeostasisEfficiency, 0f, 0.05f);

            if (EconomicsDemographyMod.Settings.enableLogs) // Tooltip-like label
                listing.Label("  <color=gray>" + "ED_HomeostasisDesc".Translate() + "</color>", 12f);

            listing.Gap(12f);
            listing.Label("ED_CapacitySettings".Translate());
            listing.GapLine();
            listing.Label($"  - {"ED_Tech_Animal".Translate()}: {capAnimal:F0}");
            capAnimal = Math.Max(1f, listing.Slider(capAnimal, 1f, 500f));
            listing.Label($"  - {"ED_Tech_Neolithic".Translate()}: {capNeolithic:F0}");
            capNeolithic = Math.Max(1f, listing.Slider(capNeolithic, 1f, 500f));
            listing.Label($"  - {"ED_Tech_Medieval".Translate()}: {capMedieval:F0}");
            capMedieval = Math.Max(1f, listing.Slider(capMedieval, 1f, 500f));
            listing.Label($"  - {"ED_Tech_Industrial".Translate()}: {capIndustrial:F0}");
            capIndustrial = Math.Max(1f, listing.Slider(capIndustrial, 1f, 500f));
            listing.Label($"  - {"ED_Tech_Spacer".Translate()}: {capSpacer:F0}");
            capSpacer = Math.Max(1f, listing.Slider(capSpacer, 1f, 500f));
            listing.Label($"  - {"ED_Tech_Ultra".Translate()}: {capUltra:F0}");
            capUltra = Math.Max(1f, listing.Slider(capUltra, 1f, 500f));
            listing.Label($"  - {"ED_Tech_Archotech".Translate()}: {capArchotech:F0}");
            capArchotech = Math.Max(1f, listing.Slider(capArchotech, 1f, 500f));

            listing.Gap(12f);
            listing.Label("ED_ExpansionThreshold".Translate((expansionThresholdFactor * 100f).ToString("F0")));
            expansionThresholdFactor = listing.Slider(expansionThresholdFactor, 0.5f, 1.5f);

            listing.Gap(12f);
            listing.Label("ED_BirthRates".Translate());
            listing.GapLine();
            listing.Label($"  - {"ED_Tech_Animal".Translate()}: {birthAnimal:F1}x");
            birthAnimal = listing.Slider(birthAnimal, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Neolithic".Translate()}: {birthNeolithic:F1}x");
            birthNeolithic = listing.Slider(birthNeolithic, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Medieval".Translate()}: {birthMedieval:F1}x");
            birthMedieval = listing.Slider(birthMedieval, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Industrial".Translate()}: {birthIndustrial:F1}x");
            birthIndustrial = listing.Slider(birthIndustrial, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Spacer".Translate()}: {birthSpacer:F1}x");
            birthSpacer = listing.Slider(birthSpacer, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Ultra".Translate()}: {birthUltra:F1}x");
            birthUltra = listing.Slider(birthUltra, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Archotech".Translate()}: {birthArchotech:F1}x");
            birthArchotech = listing.Slider(birthArchotech, 0f, 5f);

            listing.Gap(6f);
            listing.Label("ED_EventMultiplier".Translate(eventPopMultiplier.ToString("F1")));
            eventPopMultiplier = listing.Slider(eventPopMultiplier, 0f, 5f);

            listing.Gap(12f);
            listing.Label("ED_LifecycleSettings".Translate());
            listing.GapLine();
            listing.Label("ED_DaysToAdult".Translate(daysToAdult.ToString("F0")));
            daysToAdult = listing.Slider(daysToAdult, 15f, 1200f);
            listing.Label("ED_DaysToElder".Translate(daysToElder.ToString("F0")));
            daysToElder = listing.Slider(daysToElder, 60f, 6000f);

            listing.Gap(12f);
            listing.Label("ED_ProductionEff".Translate());
            listing.GapLine();
            listing.Label($"  - {"ED_Tech_Animal".Translate()}: {prodAnimal:F1}x");
            prodAnimal = listing.Slider(prodAnimal, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Neolithic".Translate()}: {prodNeolithic:F1}x");
            prodNeolithic = listing.Slider(prodNeolithic, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Medieval".Translate()}: {prodMedieval:F1}x");
            prodMedieval = listing.Slider(prodMedieval, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Industrial".Translate()}: {prodIndustrial:F1}x");
            prodIndustrial = listing.Slider(prodIndustrial, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Spacer".Translate()}: {prodSpacer:F1}x");
            prodSpacer = listing.Slider(prodSpacer, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Ultra".Translate()}: {prodUltra:F1}x");
            prodUltra = listing.Slider(prodUltra, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Archotech".Translate()}: {prodArchotech:F1}x");
            prodArchotech = listing.Slider(prodArchotech, 0f, 5f);

            listing.Gap(12f);
            listing.Label("ED_ConsumptionCoeff".Translate());
            listing.GapLine();
            listing.Label($"  - {"ED_Tech_Animal".Translate()}: {consAnimal:F1}x");
            consAnimal = listing.Slider(consAnimal, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Neolithic".Translate()}: {consNeolithic:F1}x");
            consNeolithic = listing.Slider(consNeolithic, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Medieval".Translate()}: {consMedieval:F1}x");
            consMedieval = listing.Slider(consMedieval, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Industrial".Translate()}: {consIndustrial:F1}x");
            consIndustrial = listing.Slider(consIndustrial, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Spacer".Translate()}: {consSpacer:F1}x");
            consSpacer = listing.Slider(consSpacer, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Ultra".Translate()}: {consUltra:F1}x");
            consUltra = listing.Slider(consUltra, 0f, 5f);
            listing.Label($"  - {"ED_Tech_Archotech".Translate()}: {consArchotech:F1}x");
            consArchotech = listing.Slider(consArchotech, 0f, 5f);

            listing.Gap(24f);
            listing.Label("ED_SystemHeader".Translate());
            listing.GapLine();
            listing.CheckboxLabeled("ED_EnableLogs".Translate(), ref enableLogs, "ED_EnableLogsDesc".Translate());
            Log.Enabled = enableLogs;

            listing.Gap(50f);
            if (listing.ButtonText("ED_ResetSettings".Translate()))
            {
                ResetToDefaults();
            }

            listing.End();
            Widgets.EndScrollView();
        }
    }

    public class EconomicsDemographyMod : Mod
    {
        public static EconomicsDemographySettings Settings;

        public EconomicsDemographyMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<EconomicsDemographySettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Economics & Demography";
        }
    }
}
