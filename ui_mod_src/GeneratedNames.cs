using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Names the game invents on the fly. When the list of star names (SystemNames.txt) runs out, a system
/// is called after another one with an English word ("Bakora Junction") or gets a random name of Latin
/// syllables ("Erkeugehe"); planets and moons get such syllable names too. Here the word is taken from
/// GameText ("CFL System Junction" = «{0} Узловая») and the syllables are written in Cyrillic.
/// </summary>
public static class GeneratedNames
{
    private static readonly string[] Suffixes = { "Major", "Minor", "Prime", "Junction" };
    private const string Vowels = "aeiouy";
    private static int _logged;

    private static bool Vowel(char c) => Vowels.IndexOf(char.ToLowerInvariant(c)) >= 0;

    /// <summary>"Erkeugehe" -> «Эркеугехе». Anything that is not plain Latin letters is returned as is.</summary>
    public static string Cyrillic(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')) return name;
        string s = name.ToLowerInvariant();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            char next = i + 1 < s.Length ? s[i + 1] : '\0';
            bool afterVowel = i > 0 && Vowel(s[i - 1]);
            switch (c)
            {
                case 'a': sb.Append('а'); break;
                case 'b': sb.Append('б'); break;
                case 'c': sb.Append(next == 'e' || next == 'i' ? 'с' : 'к'); break;
                case 'd': sb.Append('д'); break;
                case 'e': sb.Append(i == 0 || afterVowel ? 'э' : 'е'); break;
                case 'f': sb.Append('ф'); break;
                case 'g': sb.Append('г'); break;
                case 'h': sb.Append('х'); break;
                case 'i': sb.Append('и'); break;
                case 'j': sb.Append("дж"); break;
                case 'k': sb.Append('к'); break;
                case 'l': sb.Append('л'); break;
                case 'm': sb.Append('м'); break;
                case 'n': sb.Append('н'); break;
                case 'o': sb.Append('о'); break;
                case 'p': sb.Append('п'); break;
                case 'q': sb.Append('к'); break;
                case 'r': sb.Append('р'); break;
                case 's': sb.Append('с'); break;
                case 't': sb.Append('т'); break;
                case 'u': sb.Append('у'); break;
                case 'v': sb.Append('в'); break;
                case 'w': sb.Append('в'); break;
                case 'x': sb.Append("кс"); break;
                case 'z': sb.Append('з'); break;
                case 'y':
                    // before a vowel it is a glide (ya, yu, ye, yo), after a vowel "й", otherwise the vowel "и"
                    if (next == 'a') { sb.Append('я'); i++; }
                    else if (next == 'u') { sb.Append('ю'); i++; }
                    else if (next == 'e') { sb.Append('е'); i++; }
                    else if (next == 'o') { sb.Append('ё'); i++; }
                    else if (next == 'i') { sb.Append('и'); i++; }
                    else sb.Append(afterVowel ? 'й' : 'и');
                    break;
            }
        }
        if (sb.Length == 0) return name;
        sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    public static void SystemSuffix(ref string result)
    {
        try
        {
            if (string.IsNullOrEmpty(result)) return;
            foreach (string word in Suffixes)
            {
                if (!result.EndsWith(" " + word, StringComparison.Ordinal)) continue;
                if (TextResolver._Text.TryGetValue("CFL System " + word, out var template) && !string.IsNullOrEmpty(template) && template.Contains("{0}"))
                {
                    string ru = string.Format(template, result.Substring(0, result.Length - word.Length - 1));
                    if (_logged++ < 10) ModInit.Log("System name: " + result + " -> " + ru);
                    result = ru;
                }
                return;
            }
        }
        catch (Exception ex)
        {
            ModInit.Log("System name patch failed: " + ex.Message);
        }
    }
}

[HarmonyPatch]
public static class Patch_RandomSyllableNames
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods() =>
        new[] { "GenerateRandomNameLong", "GenerateRandomNameShort" }
            .Select(n => (MethodBase)AccessTools.Method(typeof(TextHelper), n))
            .Where(m => m != null);

    static void Postfix(ref string __result)
    {
        try { __result = GeneratedNames.Cyrillic(__result); } catch { }
    }
}

[HarmonyPatch(typeof(Galaxy), "GenerateSystemName")]
public static class Patch_SystemNameSuffix
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(ref string __result) => GeneratedNames.SystemSuffix(ref __result);
}
