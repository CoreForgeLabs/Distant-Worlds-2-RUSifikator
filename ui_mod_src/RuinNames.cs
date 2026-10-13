using System;
using System.Linq;
using System.Threading;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Ruins that no event named get their name from PlanetaryFacilityDefinition.GenerateRuins: an adjective, a
/// kind of building and either "of the system" or "of the race" in English word order ("Lofty Archives of
/// Kappa Minor", "Hall of the Tikans"; the race name there is the Russian one with an English "s"). The
/// words come from GameText keys "CFL Ruin First/Noun ..." (Docs/extra_gametext_ruins.tsv); the result is
/// «Заоблачные архивы системы Каппа Минор» or «Великий зал (Тиканы)». An unknown word leaves the name as it was.
/// </summary>
public static class RuinText
{
    private static int _logged;

    private static bool Get(string key, out string value) =>
        TextResolver._Text.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value);

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    public static string? Translate(string en)
    {
        if (string.IsNullOrEmpty(en)) return null;
        string head = en, tail = string.Empty;
        int of = en.IndexOf(" of ", StringComparison.Ordinal);
        if (of > 0)
        {
            head = en.Substring(0, of);
            tail = en.Substring(of + 4);
        }
        string[] words = head.Split(' ');
        if (!Get("CFL Ruin Noun " + words[words.Length - 1], out var noun)) return null;
        string[] np = noun.Split('|');
        string gender = np.Length > 1 ? np[1] : "м";
        string name = np[0];
        if (words.Length > 1)
        {
            if (!Get("CFL Ruin First " + string.Join(" ", words, 0, words.Length - 1), out var first)) return null;
            string[] fp = first.Split('|');
            if (fp.Length != 4) return null;
            name = (gender switch { "ж" => fp[1], "с" => fp[2], "мн" => fp[3], _ => fp[0] }) + " " + name;
        }
        name = Cap(name);
        if (tail.Length == 0) return name;
        if (tail.StartsWith("the ", StringComparison.Ordinal) && tail.EndsWith("s", StringComparison.Ordinal) && tail.Length > 5)
        {
            string race = tail.Substring(4, tail.Length - 5);                  // the Russian name of the race: "Человек / Люди"
            int slash = race.LastIndexOf('/');
            if (slash >= 0) race = race.Substring(slash + 1);
            return name + " (" + race.Trim() + ")";
        }
        return name + " " + (Get("CFL Ruin Of System", out var of2) ? of2 : "системы") + " " + tail;
    }

    public static void Apply(PlanetaryFacility? ruins)
    {
        try
        {
            if (ruins == null || TextResolver._Text.Count == 0) return;
            string? ru = Translate(ruins.Name);
            if (ru == null)
            {
                if (_logged++ < 20) ModInit.Log("Ruin name left as is: " + ruins.Name);
                return;
            }
            if (_logged++ < 20) ModInit.Log("Ruin name: " + ruins.Name + " -> " + ru);
            ruins.Name = ru;
        }
        catch (Exception ex)
        {
            ModInit.Log("Ruin name patch failed: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(PlanetaryFacilityDefinition), "GenerateRuins",
    new[] { typeof(Galaxy), typeof(Random), typeof(string), typeof(byte), typeof(Orb), typeof(float), typeof(bool) })]
public static class Patch_RuinNames
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    // only the names the game made up itself: an event that gave a name has it from Docs/extra_gametext_items.tsv
    static void Prefix(string name, out bool __state) => __state = string.IsNullOrEmpty(name);

    static void Postfix(PlanetaryFacility __result, bool __state)
    {
        if (__state) RuinText.Apply(__result);
    }
}

/// <summary>
/// A save made without this mod keeps the English names of its facilities inside the file («Moray Abyss»,
/// «High Chamber of Kappa Minor»). They are shown in Russian when read: a name from the events table or a
/// generated ruin name is replaced, every other name passes. The lookups by name accept both languages
/// (Patch_ItemLookup_Facility), so the story events still find the facility.
/// </summary>
[HarmonyPatch(typeof(PlanetaryFacility), nameof(PlanetaryFacility.Name), MethodType.Getter)]
public static class Patch_FacilityNameDisplay
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> Seen = new();
    private static int _logged;

    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    // the getter is called in loops over every facility of the galaxy: no allocations on the common path
    private static bool Latin(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (s[i] >= 'A' && s[i] <= 'Z' || s[i] >= 'a' && s[i] <= 'z') return true;
        return false;
    }

    static void Postfix(ref string __result)
    {
        try
        {
            string name = __result;
            if (string.IsNullOrEmpty(name)) return;
            if (!Seen.TryGetValue(name, out var ru))
            {
                if (TextResolver._Text.Count == 0) return;                     // the texts are not loaded yet: ask again later
                ru = Latin(name) ? ItemNames.Russian(name) ?? RuinText.Translate(name) : null;
                if (Seen.TryAdd(name, ru) && ru != null && Interlocked.Increment(ref _logged) <= 30)
                    ModInit.Log("Facility name shown in Russian: " + name + " -> " + ru);
            }
            if (ru != null) __result = ru;
        }
        catch
        {
        }
    }
}
