using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Names of ships and bases ("Quiet Novelty", "Roan Renegade", "Yitur Station", "Ijojoome Mining Station")
/// are put together in Galaxy from English word lists that live in the code, in English word order. The
/// original result is kept as the source and rebuilt in Russian: «Тихая новинка», «Отступник Роан»,
/// «Станция Йитур», «Добывающая станция Ijojoome». Words come from GameText keys "<prefix> First ..." and
/// "<prefix> Noun ..." (Docs/extra_gametext_ships.tsv, extra_gametext_bases.tsv); an unknown word leaves
/// the name untouched.
/// </summary>
public static class NameText
{
    private static int _logged;

    private static bool Get(string key, out string value) =>
        TextResolver._Text.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value);

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    private static bool Cyrillic(string s) => s.Any(c => c >= 'А' && c <= 'я' || c == 'ё' || c == 'Ё');

    private static bool Latin(string s) => s.Any(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z');

    private static bool Noun(string prefix, string en, out string ru, out string gender)
    {
        ru = en;
        gender = "м";
        if (!Get(prefix + " Noun " + en, out var value)) return false;
        string[] p = value.Split('|');
        ru = p[0];
        if (p.Length > 1) gender = p[1];
        return true;
    }

    /// <param name="heads">names the generator may have put around the word: the planet, the system</param>
    public static string? Translate(string prefix, string en, params string?[] heads)
    {
        if (string.IsNullOrEmpty(en)) return null;
        string number = string.Empty;                                    // "Research Center 003"
        int sp = en.LastIndexOf(' ');
        if (sp > 0 && en.Length - sp == 4 && en.Substring(sp + 1).All(char.IsDigit))
        {
            number = en.Substring(sp);
            en = en.Substring(0, sp);
        }

        foreach (string? head in heads)
        {
            if (string.IsNullOrEmpty(head)) continue;
            string? tail = null;
            if (en.StartsWith(head + " ", StringComparison.Ordinal)) tail = en.Substring(head!.Length + 1);
            else if (en.EndsWith(" of " + head, StringComparison.Ordinal)) tail = en.Substring(0, en.Length - head!.Length - 4);
            if (tail == null) continue;
            if (Noun(prefix, tail, out var ru, out _)) return Cap(ru) + " " + head + number;      // «Станция Йитур»
            // GameText already gave the Russian words, only the order is English: «Добывающая станция X»
            return Cyrillic(tail) && !Latin(tail) ? Cap(tail) + " " + head + number : null;
        }

        string[] words = en.Split(' ');
        if (words.Length == 1) return Noun(prefix, en, out var one, out _) ? Cap(one) + number : null;
        for (int i = 1; i < words.Length; i++)                           // the noun is the longest known tail
        {
            if (!Noun(prefix, string.Join(" ", words, i, words.Length - i), out var nounRu, out var gender)) continue;
            string first = string.Join(" ", words, 0, i);
            if (Get(prefix + " First " + first, out var firstValue))
            {
                string[] fp = firstValue.Split('|');
                if (fp.Length != 4) return Cap(nounRu) + " " + firstValue + number;               // «Убийца планет»
                string adj = gender switch { "ж" => fp[1], "с" => fp[2], "мн" => fp[3], _ => fp[0] };
                return Cap(adj) + " " + nounRu + number;                                          // «Тихая новинка»
            }
            // a translated description of the planet's scenery in front of an English word: «Отель Радужные кольца»
            return Cyrillic(first) && !Latin(first) ? Cap(nounRu) + " " + first + number : null;
        }
        return null;
    }

    public static void Apply(ref string result, string prefix, params string?[] heads)
    {
        try
        {
            if (string.IsNullOrEmpty(result) || TextResolver._Text.Count == 0) return;
            string? ru = prefix == "CFL Port" ? ShadowportText.Translate(result, heads.FirstOrDefault()) : Translate(prefix, result, heads);
            if (ru == null)
            {
                if (Latin(result) && _logged++ < 40) ModInit.Log("Name left as is (" + prefix + "): " + result);
                return;
            }
            if (_logged++ < 40) ModInit.Log("Name: " + result + " -> " + ru);
            result = ru;
        }
        catch (Exception ex)
        {
            ModInit.Log("Name patch failed: " + ex.Message);
        }
    }

    public static string? SystemName(Orb? orb)
    {
        try { return orb?.GetSystemForOrb()?.Name; } catch { return null; }
    }
}

/// <summary>Adjective + noun, or the star system and a noun: warships, civilian ships, hive ships, planet destroyers.</summary>
[HarmonyPatch]
public static class Patch_RandomShipNames
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods() =>
        new[] { "GenerateRandomUniqueMilitaryShipName", "GenerateRandomUniqueCivilianShipName" }
            .Select(n => (MethodBase)AccessTools.Method(typeof(Galaxy), n))
            .Where(m => m != null);

    static void Postfix(ref string __result, Orb parentOrb) => NameText.Apply(ref __result, "CFL Ship", NameText.SystemName(parentOrb));
}

[HarmonyPatch]
public static class Patch_SpecialShipNames
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods() =>
        new[] { "GenerateRandomHiveShipName", "GenerateRandomPlanetDestroyerName" }
            .Select(n => (MethodBase)AccessTools.Method(typeof(Galaxy), n))
            .Where(m => m != null);

    static void Postfix(ref string __result) => NameText.Apply(ref __result, "CFL Ship");
}

/// <summary>Pirate bases use the same word lists as shadowports ("Eagles Cave", "Smugglers Cove").</summary>
[HarmonyPatch(typeof(Galaxy), "GeneratePirateBaseName")]
public static class Patch_PirateBaseName
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(ref string __result, Orb orb) => NameText.Apply(ref __result, "CFL Port", NameText.SystemName(orb));
}

[HarmonyPatch(typeof(Galaxy), "GenerateResortBaseName")]
public static class Patch_ResortBaseName
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(ref string __result, Orb orb) => NameText.Apply(ref __result, "CFL Base", NameText.SystemName(orb));
}

/// <summary>Stations and bases: "planet or system + kind" becomes "kind + planet or system".</summary>
[HarmonyPatch(typeof(Galaxy), "GenerateShipName")]
public static class Patch_BaseNames
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(ref string __result, ShipRole shipRole, Orb parentOrb, Empire empire)
    {
        switch (shipRole)
        {
            case ShipRole.SpaceportSmall:
            case ShipRole.SpaceportMedium:
            case ShipRole.SpaceportLarge:
                if (empire != null && empire.IsPirate) return;           // named by GeneratePirateBaseName
                break;
            case ShipRole.MiningStation:
            case ShipRole.ResearchStation:
            case ShipRole.DefensiveBase:
            case ShipRole.MonitoringStation:
                break;
            default:
                return;
        }
        NameText.Apply(ref __result, "CFL Base", parentOrb?.Name, NameText.SystemName(parentOrb));
    }
}

/// <summary>Black holes: "Devil's Gate", "Dark Vortex" - «Врата дьявола», «Тёмный вихрь».</summary>
[HarmonyPatch(typeof(TextHelper), "GenerateBlackHoleName")]
public static class Patch_BlackHoleName
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(ref string __result) => NameText.Apply(ref __result, "CFL Hole");
}
