using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Shadowport names ("Desperado Outpost", "Smugglers Cove", "Undos Mercenary Guild") are put together in
/// TextHelper from English word lists that live in the code. The original result is kept as the source and
/// rebuilt in Russian word order: «Аванпост головорезов», «Бухта контрабандистов», «Гильдия наёмников Ундос».
/// Words come from GameText keys "CFL Port ..." (Docs/extra_gametext_ports.tsv); an unknown word leaves the
/// English name untouched.
/// </summary>
public static class ShadowportText
{
    private static int _logged;

    private static bool Get(string key, out string value) =>
        TextResolver._Text.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value);

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    public static string? Translate(string en, string? system)
    {
        if (string.IsNullOrEmpty(en)) return null;
        string? head = null;
        string rest = en;
        if (!string.IsNullOrEmpty(system) && en.StartsWith(system + " ", StringComparison.Ordinal))
        {
            head = system;
            rest = en.Substring(system!.Length + 1);
        }
        // whole phrases: the faction names of a shadowport ("Mercenary Guild")
        if (Get("CFL Port Guild " + rest, out var guild))
            return head == null ? guild : guild + " " + head;

        // "<First> <Noun>" or "<System> <Noun>": the noun is the longest known tail
        string[] words = rest.Split(' ');
        for (int i = head == null ? 1 : 0; i < words.Length; i++)
        {
            string noun = string.Join(" ", words, i, words.Length - i);
            if (!Get("CFL Port Noun " + noun, out var nounValue)) continue;
            string[] np = nounValue.Split('|');
            string nounRu = np[0];
            string gender = np.Length > 1 ? np[1] : "м";
            if (i == 0) return Cap(nounRu) + " " + head;                 // «Логово Ундос»
            if (head != null) return null;
            if (!Get("CFL Port First " + string.Join(" ", words, 0, i), out var firstValue)) return null;
            string[] fp = firstValue.Split('|');
            if (fp.Length == 4)                                           // adjective: agrees with the noun
            {
                string adj = gender switch { "ж" => fp[1], "с" => fp[2], "мн" => fp[3], _ => fp[0] };
                return Cap(adj) + " " + nounRu;                           // «Тайное логово»
            }
            return Cap(nounRu) + " " + firstValue;                        // «Бухта контрабандистов»
        }
        return null;
    }

    public static void Apply(ref string result, Galaxy galaxy, Orb orb)
    {
        try
        {
            if (string.IsNullOrEmpty(result)) return;
            string? system = null;
            try { system = orb?.GetSystem(galaxy)?.Name; } catch { }
            string? ru = Translate(result, system);
            if (ru == null) return;
            if (_logged++ < 6) ModInit.Log("Shadowport name: " + result + " -> " + ru);
            result = ru;
        }
        catch (Exception ex)
        {
            ModInit.Log("Shadowport name patch failed: " + ex.Message);
        }
    }
}

[HarmonyPatch]
public static class Patch_ShadowportNames
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods() =>
        new[] { "GenerateShadowportName", "GenerateShadowportBaseName", "GenerateShadowportEmpireName" }
            .Select(n => (MethodBase)AccessTools.Method(typeof(TextHelper), n))
            .Where(m => m != null);

    static void Postfix(ref string __result, Galaxy galaxy, Orb orb) => ShadowportText.Apply(ref __result, galaxy, orb);
}
