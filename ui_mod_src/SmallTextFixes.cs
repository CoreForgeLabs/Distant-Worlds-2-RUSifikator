using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Russian name of a weapon family ("Blasters", "Beams", ...). The family is also an identifier compared
/// by game logic, so the data stays English and only the place that draws it asks for the translation.
/// </summary>
public static class FamilyText
{
    private static readonly Dictionary<string, string> Cache = new();

    public static string Localize(string family)
    {
        try
        {
            if (string.IsNullOrEmpty(family)) return family;
            lock (Cache)
            {
                if (Cache.TryGetValue(family, out var done)) return done;
                if (TextResolver._Text.Count == 0) return family; // texts not loaded yet: do not cache
                if (!TextResolver._Text.TryGetValue("CFL Family " + family, out var ru) || string.IsNullOrEmpty(ru))
                    ru = family;
                Cache[family] = ru;
                return ru;
            }
        }
        catch
        {
            return family;
        }
    }
}

/// <summary>Component card: "weapon type, family" - the family is read from data and shown as is.</summary>
[HarmonyPatch]
public static class Patch_DrawComponent_FamilyText
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var m in typeof(UserInterfaceHelper).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name == "DrawComponent"))
        {
            try { RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch { }
            yield return m;
        }
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var localize = AccessTools.Method(typeof(FamilyText), nameof(FamilyText.Localize));
        int n = 0;
        foreach (var ins in instructions)
        {
            yield return ins;
            bool readsFamily =
                (ins.opcode == OpCodes.Ldfld && ins.operand is FieldInfo f && f.Name == "Family" && f.FieldType == typeof(string)) ||
                ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodInfo g &&
                 g.Name == "get_Family" && g.ReturnType == typeof(string));
            if (readsFamily)
            {
                yield return new CodeInstruction(OpCodes.Call, localize);
                n++;
            }
        }
        ModInit.Log("FamilyText: family reads wrapped in DrawComponent = " + n);
    }
}

/// <summary>The tour window takes its header from the tour's internal English title; show the GameText translation.</summary>
[HarmonyPatch]
public static class Patch_TourDialog_Header
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static MethodBase TargetMethod() => AccessTools.Method(typeof(DistantWorlds.UI.TourDialog), "BindData");

    static void Postfix(DistantWorlds.UI.TourDialog __instance, TourItem tourItem)
    {
        try
        {
            if (tourItem?.Title != null && TextResolver._Text.TryGetValue(tourItem.Title, out var ru) &&
                !string.IsNullOrEmpty(ru))
                __instance.HeaderText = ru.ToUpperInvariant();
        }
        catch
        {
        }
    }
}

/// <summary>
/// Tour hints on the research and ship design screens insert the tour's internal English title
/// (it doubles as the tour id). GameText has a translation under the same key: use it in the hint.
/// </summary>
[HarmonyPatch]
public static class Patch_TourHints_Title
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var found = new List<MethodBase>();
        void Add(string typeName, string method)
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null) return;
            foreach (var m in AccessTools.GetDeclaredMethods(t).Where(m => m.Name == method))
            {
                try { RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch { }
                found.Add(m);
            }
        }
        Add("DistantWorlds.UI.ResearchTree", "Render");
        Add("DistantWorlds2.ShipDesignRenderer", "Update");
        Add("DistantWorlds2.ShipDesignRenderer", "DrawCorePostScene");
        return found;
    }

    /// <summary>The tour title handed to the two hint templates: its GameText translation when there is one.</summary>
    public static object LocalizeTitle(object title)
    {
        try
        {
            if (title is string s && TextResolver._Text.TryGetValue(s, out var ru) && !string.IsNullOrEmpty(ru))
                return ru;
        }
        catch
        {
        }
        return title;
    }

    // string.Format(GetText("A tour is available for X"), title) -> ... LocalizeTitle(title)
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var localize = AccessTools.Method(typeof(Patch_TourHints_Title), nameof(LocalizeTitle));
        bool pending = false;
        int n = 0;
        foreach (var ins in instructions)
        {
            if (ins.opcode == OpCodes.Ldstr && ins.operand is string s &&
                (s == "A tour is available for X" || s == "Click to launch a tour of X"))
            {
                pending = true;
            }
            else if (pending && ins.opcode == OpCodes.Call && ins.operand is MethodInfo m &&
                     m.DeclaringType == typeof(string) && m.Name == "Format")
            {
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[1].ParameterType == typeof(object))
                {
                    yield return new CodeInstruction(OpCodes.Call, localize);
                    n++;
                }
                pending = false;
            }
            yield return ins;
        }
        if (n > 0) ModInit.Log("TourHints: titles localized in " + original.DeclaringType?.Name + "." + original.Name + " = " + n);
    }
}

/// <summary>
/// "Unknown" + " " + "Planet" is glued in English word order and gives «Неизвестно Планета».
/// The three places take the whole phrase from GameText instead ("CFL Unknown Planet" = «неизвестная планета»).
/// </summary>
[HarmonyPatch]
public static class Patch_UnknownOrb
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var found = new List<MethodBase>();
        void Add(Type t, string method)
        {
            foreach (var m in AccessTools.GetDeclaredMethods(t).Where(m => m.Name == method))
            {
                try { RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch { }
                found.Add(m);
            }
        }
        Add(typeof(TextHelper), "ResolveMissionDescription");
        Add(typeof(UserInterfaceHelper), "DrawOrbHover");
        return found;
    }

    public static string Phrase(OrbCategory category)
    {
        try
        {
            if (TextResolver._Text.TryGetValue("CFL Unknown " + category, out var ru) && !string.IsNullOrEmpty(ru))
                return ru;
        }
        catch
        {
        }
        return TextResolver.GetText("Unknown") + " " + TextHelper.ResolveDescription(category);
    }

    // GetText("Unknown") + " " + ResolveDescription(orb.Category)  ->  "" + "" + Phrase(orb.Category)
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var code = instructions.ToList();
        var phrase = AccessTools.Method(typeof(Patch_UnknownOrb), nameof(Phrase));
        int n = 0;
        for (int i = 0; i + 3 < code.Count; i++)
        {
            if (!(code[i].opcode == OpCodes.Ldstr && (code[i].operand as string) == "Unknown")) continue;
            if (!(code[i + 1].opcode == OpCodes.Call && code[i + 1].operand is MethodInfo get && get.Name == "GetText")) continue;
            // a + b + c is Concat(a, b, c); with brackets around it the parts are stored into an array one by one
            int space = -1, resolve = -1;
            for (int j = i + 2; j < code.Count && j <= i + 6 && space < 0; j++)
                if (code[j].opcode == OpCodes.Ldstr && (code[j].operand as string) == " ") space = j;
            for (int j = space + 1; space > 0 && j < code.Count && j <= space + 10 && resolve < 0; j++)
                if (code[j].opcode == OpCodes.Call && code[j].operand is MethodInfo m && m.Name == "ResolveDescription" &&
                    m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(OrbCategory)) resolve = j;
            if (resolve < 0) continue;
            code[i].operand = string.Empty;
            code[i + 1].opcode = OpCodes.Nop;
            code[i + 1].operand = null;
            code[space].operand = string.Empty;
            code[resolve].operand = phrase;
            n++;
        }
        ModInit.Log("UnknownOrb: phrases replaced in " + original.DeclaringType?.Name + "." + original.Name + " = " + n);
        return code;
    }
}
