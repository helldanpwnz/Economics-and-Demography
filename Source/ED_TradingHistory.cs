using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using HarmonyLib;

namespace EconomicsDemography
{
    // Класс записи лога
    public class TradingLogEntry : IExposable
    {
        public int tick;
        public string itemLabel;
        public int amount;
        public float value;
        public int partnerID = -1; // Сохраняем ID фракции-партнера для иконки

        public TradingLogEntry() { }

        public TradingLogEntry(int tick, string itemLabel, int amount, float value, int partnerID = -1)
        {
            this.tick = tick;
            this.itemLabel = itemLabel;
            this.amount = amount;
            this.value = value;
            this.partnerID = partnerID;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref itemLabel, "itemLabel");
            Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref value, "value");
            Scribe_Values.Look(ref partnerID, "partnerID", -1);
        }
    }

    // Вспомогательный класс для работы с историей
    public static class TradingHistoryManager
    {
        public static void AddLog(Dictionary<int, List<TradingLogEntry>> logs, int factionID, TradingLogEntry entry)
        {
            if (logs == null) return;
            if (!logs.TryGetValue(factionID, out List<TradingLogEntry> list))
            {
                list = new List<TradingLogEntry>();
                logs[factionID] = list;
            }

            list.Insert(0, entry); // Добавляем в начало (самые свежие сверху)
            if (list.Count > 30)
            {
                list.RemoveAt(list.Count - 1); // Ограничение 30 записей
            }
        }
    }

    // Патч для добавления кнопки Гизмо на глобальной карте
    [HarmonyPatch(typeof(WorldObject), "GetGizmos")]
    public static class Patch_WorldObject_TradingHistoryGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, WorldObject __instance)
        {
            if (__result != null)
            {
                foreach (var g in __result) yield return g;
            }

            // Кнопка появляется только для баз/аванпостов СИМУЛИРУЕМЫХ фракций (не караваны)
            if (!(__instance is Caravan) && __instance.Faction != null && WorldPopulationManager.Instance != null && WorldPopulationManager.Instance.IsSimulatedFaction(__instance.Faction))
            {
                yield return new Command_Action
                {
                    defaultLabel = "ED_TradingHistory_Button".Translate(),
                    defaultDesc = "ED_TradingHistory_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Icons/UI/ED_Trading_history", false),
                    action = () =>
                    {
                        Find.WindowStack.Add(new ED_Window_TradingHistory(__instance.Faction));
                    }
                };
            }
        }
    }

    [HarmonyPatch(typeof(Settlement), "GetGizmos")]
    public static class Patch_Settlement_TradingHistory { public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> r, Settlement __instance) => Patch_WorldObject_TradingHistoryGizmos.Postfix(r, __instance); }

    // Окно истории торговли и производства
    public class ED_Window_TradingHistory : Window
    {
        private Faction faction;
        private Vector2 scrollProd = Vector2.zero;
        private Vector2 scrollSale = Vector2.zero;
        private Vector2 scrollBuy = Vector2.zero;
        private Vector2 scrollRaid = Vector2.zero;
        private Vector2 scrollSteal = Vector2.zero;
        private Vector2 scrollConsume = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(1400f, 650f);

        public ED_Window_TradingHistory(Faction faction)
        {
            this.faction = faction;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.closeOnClickedOutside = true;
            this.absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), "ED_TradingHistory_Title".Translate(faction.Name));
            Text.Font = GameFont.Small;

            float columnWidth = (inRect.width / 6f) - 10f;
            
            // 1. Потребление (Расходы)
            Rect rectConsume = new Rect(0f, 50f, columnWidth, inRect.height - 100f);
            DrawLogColumn(rectConsume, "ED_History_Consumption".Translate(), WorldPopulationManager.Instance.consumeLogs, ref scrollConsume);

            // 2. Производство
            Rect rectProd = new Rect(columnWidth + 10f, 50f, columnWidth, inRect.height - 100f);
            DrawLogColumn(rectProd, "ED_History_Production".Translate(), WorldPopulationManager.Instance.prodLogs, ref scrollProd);

            // 3. Продажи
            Rect rectSale = new Rect((columnWidth + 10f) * 2f, 50f, columnWidth, inRect.height - 100f);
            DrawLogColumn(rectSale, "ED_History_Sales".Translate(), WorldPopulationManager.Instance.saleLogs, ref scrollSale, true);

            // 4. Покупки
            Rect rectBuy = new Rect((columnWidth + 10f) * 3f, 50f, columnWidth, inRect.height - 100f);
            DrawLogColumn(rectBuy, "ED_History_Purchases".Translate(), WorldPopulationManager.Instance.buyLogs, ref scrollBuy, true);

            // 5. Лут (Карты)
            Rect rectRaid = new Rect((columnWidth + 10f) * 4f, 50f, columnWidth, inRect.height - 100f);
            DrawLogColumn(rectRaid, "ED_History_Loot".Translate(), WorldPopulationManager.Instance.raidLogs, ref scrollRaid);

            // 6. Воровство (Виртуальное)
            Rect rectSteal = new Rect((columnWidth + 10f) * 5f, 50f, columnWidth, inRect.height - 100f);
            DrawLogColumn(rectSteal, "ED_History_Theft".Translate(), WorldPopulationManager.Instance.stealLogs, ref scrollSteal, true);
        }

        private void DrawLogColumn(Rect rect, string title, Dictionary<int, List<TradingLogEntry>> logs, ref Vector2 scrollPos, bool showPartner = false)
        {
            Widgets.DrawMenuSection(rect);
            Rect innerRect = rect.ContractedBy(5f);
            
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(innerRect.x, innerRect.y, innerRect.width, 20f), title.ToUpper());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect listRect = new Rect(innerRect.x, innerRect.y + 25f, innerRect.width, innerRect.height - 30f);
            
            List<TradingLogEntry> list = (logs != null && logs.TryGetValue(faction.loadID, out var l)) ? l : new List<TradingLogEntry>();

            float viewHeight = list.Count * 45f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);
            float curY = 0f;
            foreach (var entry in list)
            {
                Rect rowRect = new Rect(0f, curY, viewRect.width, 40f);
                if (curY % 80 == 0) Widgets.DrawLightHighlight(rowRect);

                string time = (GenDate.TicksToDays(entry.tick) % 60).ToString("F1") + " " + "Days".Translate();
                string label = $"{entry.itemLabel} x{entry.amount}";
                
                // Рисуем иконку партнера, если она есть
                if (showPartner && entry.partnerID != -1)
                {
                    Faction partner = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == entry.partnerID);
                    if (partner != null)
                    {
                        Rect iconRect = new Rect(viewRect.width * 0.7f - 22f, curY + 20f, 18f, 18f);
                        GUI.color = partner.Color;
                        GUI.DrawTexture(iconRect, partner.def.FactionIcon);
                        GUI.color = Color.white;
                        TooltipHandler.TipRegion(iconRect, partner.Name);
                    }
                }

                string color = entry.amount >= 0 ? "#00ff00" : "#ff4444";
                string valStr = entry.value.ToString("F0") + "$";
                if (entry.amount < 0 && !valStr.StartsWith("-")) valStr = "-" + valStr;

                Widgets.Label(new Rect(0f, curY, viewRect.width * 0.7f, 40f), label);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(viewRect.width * 0.7f, curY, viewRect.width * 0.3f, 20f), $"<color=#888888>{time}</color>");
                Widgets.Label(new Rect(viewRect.width * 0.7f, curY + 18f, viewRect.width * 0.3f, 20f), $"<color={color}>{valStr}</color>");
                Text.Anchor = TextAnchor.UpperLeft;

                curY += 45f;
            }
            Widgets.EndScrollView();
        }
    }
}
