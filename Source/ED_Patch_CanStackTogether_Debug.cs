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
    // Отлавливает NRE в TransferableUtility.CanStackTogether и выводит подробную информацию.
    [HarmonyPatch(typeof(TransferableUtility), nameof(TransferableUtility.CanStackTogether))]
    public static class Patch_FP_Debug_CanStackTogether_NRE
    {
        static Exception Finalizer(Thing thing, Thing otherThing, Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;
            if (!(__exception is NullReferenceException)) return __exception;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[E&D][DEBUG] NRE in TransferableUtility.CanStackTogether");
                sb.AppendLine("=== A ===");
                sb.AppendLine(DescribeThing(thing));
                sb.AppendLine("=== B ===");
                sb.AppendLine(DescribeThing(otherThing));
                sb.AppendLine("=== EX ===");
                sb.AppendLine(__exception.ToString());
                Log.Error(sb.ToString());
            }
            catch (Exception ex2)
            {
                Log.Error("[E&D][DEBUG] Failed to log CanStackTogether NRE: " + ex2);
            }

            __result = false;
            return null;
        }

        private static string DescribeThing(Thing t)
        {
            if (t == null) return "Thing: null";

            var sb = new StringBuilder();
            sb.AppendLine($"ThingType={t.GetType().FullName}");
            sb.AppendLine($"Destroyed={t.Destroyed} Spawned={t.Spawned} StackCount={t.stackCount}");
            sb.AppendLine($"Label={SafeStr(() => t.LabelCap)}");

            var d = t.def;
            if (d == null)
            {
                sb.AppendLine("def: null");
                return sb.ToString();
            }

            sb.AppendLine($"defName={d.defName} category={d.category} thingClass={(d.thingClass != null ? d.thingClass.FullName : "null")}");
            sb.AppendLine($"stackLimit={d.stackLimit} Minifiable={d.Minifiable} MadeFromStuff={d.MadeFromStuff}");

            ThingDef stuff = null;
            try { stuff = t.Stuff; } catch { }
            sb.AppendLine($"stuff={(stuff != null ? stuff.defName : "null")}");

            if (t is ThingWithComps twc)
            {
                try
                {
                    var q = twc.TryGetComp<CompQuality>();
                    if (q != null) sb.AppendLine($"CompQuality.Quality={q.Quality}");
                }
                catch { }

                try
                {
                    var art = twc.TryGetComp<CompArt>();
                    if (art != null) sb.AppendLine($"CompArt.TitleNull={(art.Title == null)}");
                }
                catch { }

                try
                {
                    var comps = twc.AllComps;
                    sb.AppendLine($"AllCompsCount={(comps != null ? comps.Count : -1)}");
                    if (comps != null)
                        for (int i = 0; i < comps.Count; i++)
                            sb.AppendLine($"  comp[{i}]={(comps[i] != null ? comps[i].GetType().FullName : "null")}");
                }
                catch { }
            }
            else
            {
                sb.AppendLine("ThingWithComps=false");
            }

            return sb.ToString();
        }

        private static string SafeStr(System.Func<string> f)
        {
            try { return f(); } catch { return "<err>"; }
        }
    }
}
