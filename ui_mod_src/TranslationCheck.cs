using System;
using DistantWorlds.Types;

namespace RussianUiMod;

/// <summary>
/// An end-to-end check that does not depend on any log line of the launcher: when the main menu is built the game's
/// static data is loaded, and the names of the first races must be Russian. When they are not, the XML patches came
/// too late (the launcher changed its start-up order, or the machine is much slower than the ones this was tested on)
/// and the plaque in the menu says so, instead of leaving the player with a half-English game and no idea why.
/// </summary>
public static class TranslationCheck
{
    /// <summary>The line for the menu, or null when the data is translated (or could not be checked).</summary>
    public static string? Problem { get; private set; }

    private static bool Cyrillic(string? text)
    {
        if (text == null) return false;
        foreach (char c in text)
            if (c >= 'Ѐ' && c <= 'ӿ') return true;
        return false;
    }

    public static void Run()
    {
        Problem = null;
        try
        {
            var races = Galaxy.RacesStatic;
            if (races == null || races.Count == 0)
            {
                ModInit.Log("Translation check: the race list is empty, not checked");
                return;
            }
            int seen = 0, russian = 0;
            string first = string.Empty;
            for (int i = 0; i < races.Count && seen < 6; i++)
            {
                string? name = races[i]?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (seen == 0) first = name!;
                seen++;
                if (Cyrillic(name)) russian++;
            }
            // a test switch: CFLABS_SELFTEST=untranslated shows the notice without breaking anything
            bool forced = string.Equals(Environment.GetEnvironmentVariable("CFLABS_SELFTEST"), "untranslated", StringComparison.OrdinalIgnoreCase);
            if (seen > 0 && (russian == 0 || forced))
                Problem = "Перевод данных игры не применился (мод загрузился позже игры). Перезапустите игру; если повторяется - напишите автору.";
            ModInit.Log("Translation check: " + russian + " of " + seen + " race names are Russian (first: " + first + ")" + (Problem != null ? " - PROBLEM" : string.Empty));
        }
        catch (Exception ex)
        {
            ModInit.Log("Translation check failed: " + ex.Message);
        }
    }
}
