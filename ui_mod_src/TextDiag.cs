using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Developer aid, off by default (config Diag_TextHelper). The game builds part of its text in code from
/// English fragments (TextHelper.*): this patch writes every distinct result that still contains Latin
/// words, with the method and its arguments, to data/Logs/RussianUiMod.textdiag.tsv - the to-do list for
/// HardcodedText.cs.
/// </summary>
[HarmonyPatch]
public static class Patch_TextHelper_Diag
{
    private static readonly HashSet<string> Seen = new();
    private static readonly HashSet<string> Traced = new();
    private static readonly object Gate = new();
    private static string DiagPath => Path.Combine(AppContext.BaseDirectory, "data", "Logs", "RussianUiMod.textdiag.tsv");

    static bool Prepare() => ModInit.Config.Diag_TextHelper;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var t = AccessTools.TypeByName("DistantWorlds.Types.TextHelper");
        if (t == null) yield break;
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (m.ReturnType != typeof(string) || m.IsGenericMethod) continue;
            if (m.Name is "SessionLog" or "MakeValidFileName" or "ToStringAwayFromZero" or "RomanNumeral") continue;
            yield return m;
        }
    }

    static void Postfix(MethodBase __originalMethod, object[] __args, string __result)
    {
        try
        {
            if (string.IsNullOrEmpty(__result) || !HasLatinWord(__result)) return;
            string key = __originalMethod.Name + "\t" + __result;
            lock (Gate)
            {
                if (Seen.Count > 20000 || !Seen.Add(key)) return;
                var sb = new StringBuilder();
                sb.Append(__originalMethod.Name).Append('\t');
                sb.Append(string.Join(" ; ", (__args ?? Array.Empty<object>()).Select(Describe))).Append('\t');
                sb.Append(__result.Replace("\r", "").Replace("\n", "\\n")).Append("\r\n");
                // who asks for the composite texts: shows where the surrounding sentence is assembled
                if (__originalMethod.Name is "ResolveRaceSummary" or "GenerateEmpireName" && Traced.Add(__originalMethod.Name))
                    sb.Append("#STACK ").Append(Environment.StackTrace.Replace("\r", "").Replace("\n", " | ")).Append("\r\n");
                File.AppendAllText(DiagPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch { }
    }

    private static bool HasLatinWord(string s)
    {
        int run = 0;
        foreach (char c in s)
        {
            run = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ? run + 1 : 0;
            if (run >= 3) return true;
        }
        return false;
    }

    private static string Describe(object? arg)
    {
        if (arg == null) return "null";
        if (arg is string s) return "\"" + s + "\"";
        var t = arg.GetType();
        if (t.IsPrimitive || t.IsEnum) return arg.ToString() ?? "";
        if (arg is IEnumerable<string> list) return "[" + string.Join("|", list.Take(12)) + "]";
        object? name = t.GetProperty("Name")?.GetValue(arg) ?? t.GetField("Name")?.GetValue(arg);
        return name != null ? t.Name + "(" + name + ")" : t.Name;
    }
}
