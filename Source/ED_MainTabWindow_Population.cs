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
    // Окно новой вкладки "Популяция" в нижней панели.
    // Позволяет просматривать детальную статистику всех фракций без наложения интерфейсов.
    public class MainTabWindow_Population : MainTabWindow
    {
        private Vector2 scrollPosition = Vector2.zero;
        private float lastHeight = 0f;

        // Кэш для ускорения отрисовки интерфейса (обновляем раз в секунду)
        private float cachedTotalPop = 0f;
        private float cachedTotalWealth = 0f;
        private float cachedTotalSilver = 0f;
        private int lastCacheTick = -1;

        public override Vector2 InitialSize => new Vector2(1400f, 820f);

        public override void DoWindowContents(Rect inRect)
        {
            var manager = Find.World.GetComponent<WorldPopulationManager>();
            if (manager == null)
            {
                Widgets.Label(new Rect(0f, 40f, inRect.width, 30f), "ED_UI_ErrorNoData".Translate());
                return;
            }

            Text.Font = GameFont.Medium;
            // Глобальные показатели сверху (без общего заголовка)
            float topY = 0f;
            
            if (lastCacheTick < 0 || Find.TickManager.TicksGame > lastCacheTick + 60)
            {
                cachedTotalPop = manager.CalculateTotalWorldPopulation();
                cachedTotalWealth = manager.CalculateTotalWorldWealth();
                cachedTotalSilver = manager.CalculateTotalWorldSilver();
                lastCacheTick = Find.TickManager.TicksGame;
            }

            Widgets.Label(new Rect(0f, topY, 350f, 40f), "ED_UI_GlobalResidents".Translate($"<color=#ffee00>{cachedTotalPop:N0}</color>"));
            Widgets.Label(new Rect(350f, topY, 350f, 40f), "ED_UI_GlobalCapital".Translate($"<color=#00ff00>{cachedTotalWealth:N0}</color>"));
            Widgets.Label(new Rect(700f, topY, 350f, 40f), "ED_UI_GlobalSilver".Translate($"<color=#77ffff>{cachedTotalSilver:N0}</color>"));

            if (EconomicsDemographyMod.Settings.enableGlobalInflation)
            {
                float inflPercent = (manager.currentInflation - 1f) * 100f;
                string inflationColor = manager.currentInflation > 1.1f ? "red" : (manager.currentInflation < 0.9f ? "cyan" : "green");
                string inflationArrow = manager.currentInflation > 1.001f ? "▲" : (manager.currentInflation < 0.999f ? "▼" : "");
                string sign = inflPercent > 0 ? "+" : "";
                string inflString = $"{inflationArrow} {sign}{inflPercent:F1}%";
                Widgets.Label(new Rect(1050f, topY, 350f, 40f), "ED_UI_GlobalInflation".Translate($"<color={inflationColor}>{inflString}</color>"));
            }
            Text.Font = GameFont.Small;

            // Фильтруем и сортируем фракции (самые густонаселенные выше)
            var factions = Find.FactionManager.AllFactionsVisible
                .Where(f => !f.IsPlayer && !f.def.hidden && f.def.humanlikeFaction)
                .OrderByDescending(f => manager.GetTotalLiving(f))
                .ToList();

            Rect viewRect = new Rect(0f, 0f, inRect.width - 25f, lastHeight);
            Rect scrollRect = new Rect(0f, 45f, inRect.width, inRect.height - 50f);

            Widgets.BeginScrollView(scrollRect, ref scrollPosition, viewRect);

            float curY = 0f;
            foreach (var f in factions)
            {
                DrawFactionRow(new Rect(0f, curY, viewRect.width, 180f), f, manager);
                curY += 190f;
            }

            lastHeight = curY;
            Widgets.EndScrollView();
        }

        private string TranslateTech(TechLevel level)
        {
            switch (level)
            {
                case TechLevel.Animal: return "ED_Tech_Animal".Translate();
                case TechLevel.Neolithic: return "ED_Tech_Neolithic".Translate();
                case TechLevel.Medieval: return "ED_Tech_Medieval".Translate();
                case TechLevel.Industrial: return "ED_Tech_Industrial".Translate();
                case TechLevel.Spacer: return "ED_Tech_Spacer".Translate();
                case TechLevel.Ultra: return "ED_Tech_Ultra".Translate();
                case TechLevel.Archotech: return "ED_Tech_Archotech".Translate();
                default: return level.ToString();
            }
        }

        private string TranslateTrait(string trait)
        {
            string key = "ED_Trait_" + trait;
            if (key.CanTranslate()) return key.Translate();
            return trait;
        }

        private void DrawFactionRow(Rect rect, Faction f, WorldPopulationManager manager)
        {
            Widgets.DrawWindowBackground(rect);
            Rect innerRect = rect.ContractedBy(10f);
            float midY = rect.y + (rect.height / 2f);
            Text.Anchor = TextAnchor.MiddleLeft;

            // 1. Иконка и Название
            Rect iconRect = new Rect(innerRect.x, midY - 32f, 64f, 64f);
            GUI.color = f.Color;
            Widgets.DrawTextureFitted(iconRect, f.def.FactionIcon, 1f);
            GUI.color = Color.white;

            Rect nameRect = new Rect(iconRect.xMax + 20f, midY - 45f, 450f, 40f);
            Text.Font = GameFont.Medium;
            Widgets.Label(nameRect, f.Name.Truncate(440f));
            Text.Font = GameFont.Small;

            Rect techRect = new Rect(nameRect.x, midY + 5f, 450f, 35f);
            GUI.color = Color.gray;
            string trait = manager.factionTraits.TryGetValue(f.loadID, out string t) ? t : "Generalist";
            
            string techText = "ED_UI_TechLevelLabel".Translate(TranslateTech(f.def.techLevel));
            string traitText = "ED_UI_TraitLabel".Translate(TranslateTrait(trait));
            Widgets.Label(techRect, $"{techText} | {traitText}");
            GUI.color = Color.white;

            // Кнопка истории (История торговли и производства)
            Rect historyBtnRect = new Rect(nameRect.xMax - 30f, midY - 45f, 28f, 28f);
            if (Widgets.ButtonImage(historyBtnRect, ContentFinder<Texture2D>.Get("Icons/UI/ED_Trading_history", false)))
            {
                Find.WindowStack.Add(new ED_Window_TradingHistory(f));
            }
            TooltipHandler.TipRegion(historyBtnRect, "ED_TradingHistory_Button".Translate());

            // 2. Статистика населения
            int adults = manager.GetPopulation(f);
            int kids = Mathf.CeilToInt(manager.factionChildren.TryGetValue(f.loadID, out float kc) ? kc : 0f);
            int elders = Mathf.CeilToInt(manager.factionElders.TryGetValue(f.loadID, out float ec) ? ec : 0f);
            int total = manager.GetTotalLiving(f);
            int femalesTask = manager.factionFemales.TryGetValue(f.loadID, out int fem) ? fem : 0;
            int males = adults - femalesTask;

            float statsX = rect.x + 580f;
            // Заголовок на одной линии с капиталом
            Rect statsTitleRect = new Rect(statsX, midY - 80f, 280f, 30f);
            int groundBases = Find.WorldObjects.Settlements.Count(s => s.Faction == f);
            int orbBases = manager.orbitalBases.TryGetValue(f.loadID, out int ob) ? ob : 0;
            if (groundBases + orbBases == 0)
                Widgets.Label(statsTitleRect, $"<color=#a020f0>{"ED_UI_Wanderers".Translate(total)}</color>");
            else
                Widgets.Label(statsTitleRect, "ED_UI_TotalPop".Translate($"<color=#ffee00>{total}</color>"));

            // Список под заголовок
            Rect statsListRect = new Rect(statsX, midY - 45f, 280f, 130f);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("ED_UI_Adults".Translate(adults));
            sb.AppendLine("  - " + "ED_UI_Males".Translate(males));
            sb.AppendLine("  - " + "ED_UI_Females".Translate(femalesTask));
            sb.AppendLine(); 
            sb.AppendLine("ED_UI_Children".Translate(kids));
            sb.AppendLine("ED_UI_Elders".Translate(elders));
            Widgets.Label(statsListRect, sb.ToString());

            // 3. Экономика
            VirtualStockpile stock = manager.GetStockpile(f);
            float wealth = stock.GetTotalWealth();
            
            float econX = rect.x + 880f;
            // Заголовок на одной линии с жителями
            Rect econTitleRect = new Rect(econX, midY - 80f, 350f, 30f);
            string capValue = $"<color=#00ff00>{wealth:F0}</color>";
            string silValue = "ED_UI_Silver".Translate(stock.silver);
            string capLine = "ED_UI_Capital".Translate(capValue) + " " + silValue;
            
            // Долг за рейд
            float raidDebt = manager.factionRaidDebt.TryGetValue(f.loadID, out float rd) ? rd : 0f;
            
            Widgets.Label(econTitleRect, capLine);
            
            float invY = midY - 45f;

            if (raidDebt > 0f)
            {
                Rect debtRect = new Rect(econX, invY, 350f, 20f);
                Widgets.Label(debtRect, $"<color=#ff4444>▼ {"ED_UI_RaidDebt".Translate(raidDebt.ToString("F0"))}</color>");
                invY += 18f;
            }
            
            // Список под заголовок
            Rect econInvRect = new Rect(econX, invY, 350f, 130f);
            if (stock.inventory.Count > 0)
            {
                var allItems = stock.inventory
                    .Select(kvp => {
                        string dName; int q;
                        VirtualStockpile.ParseKey(kvp.Key, out dName, out q);
                        ThingDef d = DefDatabase<ThingDef>.GetNamedSilentFail(dName);
                        float unitPrice = (d != null) ? d.BaseMarketValue * VirtualStockpile.GetQualityMultiplier(q) : 0f;
                        return new { Key = kvp.Key, Quantity = kvp.Value, UnitPrice = unitPrice, Def = d, Quality = q };
                    })
                    .Where(x => x.Def != null)
                    .ToList();

                var expensive = allItems.OrderByDescending(x => x.UnitPrice).Take(2).ToList();
                var numerous = allItems.Except(expensive).OrderByDescending(x => x.Quantity).Take(3).ToList();
                var top = expensive.Concat(numerous).ToList();

                StringBuilder sbE = new StringBuilder();
                sbE.AppendLine("<color=#bbbbbb>" + "ED_UI_MainStocks".Translate() + "</color>");
                foreach(var x in top) {
                    string qualityLabel = (x.Quality >= 0) ? $" ({((QualityCategory)x.Quality).GetLabel()})" : "";
                    sbE.AppendLine($"  - <color=#77ffff>{x.Def.label}</color>{qualityLabel} x{x.Quantity}");
                }
                Widgets.Label(econInvRect, sbE.ToString());
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
