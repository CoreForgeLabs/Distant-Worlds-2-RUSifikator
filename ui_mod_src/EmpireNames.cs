using System;
using System.Collections.Generic;
using HarmonyLib;
using DistantWorlds.Types;

namespace RussianUiMod;

[HarmonyPatch]
public static class Patch_TextHelper_GenerateEmpireName
{
    // Gate the patch behind config flag.
    static bool Prepare() => ModInit.Config != null && ModInit.Config.Enable_TextFixes;

    static System.Reflection.MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(DistantWorlds.Types.TextHelper), "GenerateEmpireName");
    }

    // Local mirror of the private IsEmpireNameInUse (see decompiled TextHelper).
    static bool IsEmpireNameInUse(Galaxy galaxy, string name)
    {
        if (galaxy?.Empires == null) return false;
        for (int i = 0; i < galaxy.Empires.Count; i++)
        {
            var empire = galaxy.Empires[i];
            if (empire != null && empire.Name == name) return true;
        }
        return false;
    }

    // Fetch translation. GetText answers "KEY NOT FOUND: ..." for a missing key, so the table is asked directly.
    static bool TryGet(string key, out string value)
    {
        if (!TextResolver._Text.TryGetValue(key, out value) || string.IsNullOrEmpty(value))
        {
            value = null;
            return false;
        }
        return true;
    }

    // Noun value format: "Слово|род" where род in {м,ж,с,мн}. Returns false if malformed.
    static bool TryGetNoun(string english, out string word, out string gender)
    {
        word = null; gender = null;
        if (!TryGet("CFL EmpireNoun " + english, out var raw)) return false;
        int bar = raw.LastIndexOf('|');
        if (bar <= 0 || bar == raw.Length - 1) return false;
        word = raw.Substring(0, bar).Trim();
        gender = raw.Substring(bar + 1).Trim();
        if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(gender)) return false;
        return gender is "м" or "ж" or "с" or "мн";
    }

    // Adjective value format: "мужской|женский|средний|множественное".
    static bool TryGetAdjective(string english, out string m, out string f, out string n, out string pl)
    {
        m = f = n = pl = null;
        if (!TryGet("CFL EmpireAdj " + english, out var raw)) return false;
        var parts = raw.Split('|');
        if (parts.Length != 4) return false;
        m = parts[0].Trim(); f = parts[1].Trim(); n = parts[2].Trim(); pl = parts[3].Trim();
        if (string.IsNullOrEmpty(m) || string.IsNullOrEmpty(f) ||
            string.IsNullOrEmpty(n) || string.IsNullOrEmpty(pl)) return false;
        return true;
    }

    static bool TryGetMiddle(string english, out string mid)
    {
        mid = null;
        if (!TryGet("CFL EmpireMid " + english, out var raw)) return false;
        // Middle may legitimately be empty in the source; treat empty as "missing".
        if (string.IsNullOrEmpty(raw)) return false;
        mid = raw;
        return true;
    }

    static string AdjectiveForGender(string gender, string m, string f, string n, string pl)
    {
        return gender switch
        {
            "м"  => m,
            "ж"  => f,
            "с"  => n,
            "мн" => pl,
            _    => m,
        };
    }

    // Get genitive plural of race name from GameText: key "CFL RaceGen <RaceId>".
    // Returns false if missing or unchanged key returned.
    static bool TryGetRaceGenitive(Race race, out string genitive)
    {
        genitive = null;
        if (race == null) return false;
        int id = race.RaceId;
        if (!TryGet("CFL RaceGen " + id, out var raw)) return false;
        genitive = raw.Trim();
        return !string.IsNullOrEmpty(genitive);
    }

    // Capitalize the very first letter of the assembled name, keep the rest as-is.
    static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (char.IsUpper(s[0])) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    static string BuildRussianEmpire(
        Galaxy galaxy,
        StellarObject capital,
        Race dominantRace,
        Government government,
        bool isPirate)
    {
        string result = string.Empty;
        string nameSource = string.Empty; // text3 in the original: race name or system/star name
        int attempts = 0;

        while (attempts < 50)
        {
            // --- Pick the "anchor" name: race name OR star/system name (mirrors original). ---
            nameSource = string.Empty;
            if ((Galaxy.Rnd.Next(0, 2) == 1 && dominantRace != null &&
                 dominantRace.Name != null &&
                 dominantRace.Name.ToLowerInvariant() != "human") || capital == null)
            {
                nameSource = dominantRace?.Name ?? string.Empty;
            }
            else if (capital != null)
            {
                StarSystem system = capital.GetSystem(galaxy);
                if (system != null)
                {
                    Orb star = system.Star;
                    if (star != null) nameSource = star.Name ?? string.Empty;
                }
            }

            // --- Pull lists from government (same as original). ---
            List<string> adjectives = new List<string>();
            List<string> middles = new List<string>();
            List<string> nouns = new List<string>();
            if (government != null)
            {
                adjectives = government.EmpireNameAdjectives ?? new List<string>();
                middles    = government.EmpireNameMiddles    ?? new List<string>();
                nouns      = government.EmpireNameNouns      ?? new List<string>();
            }

            string englishAdj = null, englishMid = null, englishNoun = null;

            if (isPirate)
            {
                if (adjectives.Count == 0)
                {
                    adjectives = new List<string>
                    {
                        "Bloody","Dread","Black","Dirty","Evil","Iron","Red","Fierce","Cruel","Sinister",
                        "Vicious","Lone","Savage","Fearsome","Deadly","Venomous","Murderous","Dark","Grim","Haunted",
                        "Menacing","Blood","Burning","Hidden","Fire","Lost"
                    };
                }
                if (middles.Count == 0)
                {
                    middles = new List<string> { "Sun","Star","Rock","Moon","Storm","Fang","Claw","Dagger" };
                }
                if (nouns.Count == 0)
                {
                    nouns = new List<string>
                    {
                        "Pirates","Marauders","Bandits","Raiders","Buccaneers","Outlaws","Corsairs","Pillagers","Gangsters","Ravagers",
                        "Prowlers","Intruders","Invaders","Skyjackers","Gang","Mercenaries","Council","Network","League","Force",
                        "Clan","Authority","Confederacy","Confederation","Security","Warriors","Army","Fleet","Horde","Corporation",
                        "Consortium","Syndicate","Cartel","Guild"
                    };
                }
                englishAdj  = adjectives[Galaxy.RndSeeded.Next(0, adjectives.Count)];
                englishMid  = middles  [Galaxy.RndSeeded.Next(0, middles.Count)];
                englishNoun = nouns    [Galaxy.RndSeeded.Next(0, nouns.Count)];
            }
            else
            {
                if (adjectives.Count == 0)
                {
                    adjectives = new List<string> { "United","Combined","Imperial","Great","Grand" };
                }
                if (nouns.Count == 0)
                {
                    nouns = new List<string>
                    {
                        "Empire","Alliance","Group","Dominion","Territory","Nation","Realm","Federation","Authority","Enclave",
                        "Confederacy","Coalition","Domain"
                    };
                }
                englishNoun = nouns[Galaxy.RndSeeded.Next(0, nouns.Count)];
                // Original: only in 1/4 of cases with short noun+name does it prepend adjective.
                // We mirror that: decide here, don't require adjective otherwise.
                string candidateNounPlusName = (nameSource ?? string.Empty) + " " + englishNoun;
                if (Galaxy.RndSeeded.Next(0, 4) == 1 && candidateNounPlusName.Length < 16 && adjectives.Count > 0)
                {
                    englishAdj = adjectives[Galaxy.RndSeeded.Next(0, adjectives.Count)];
                }
            }

            // --- Translate parts; any missing -> bail out and let original handle it. ---
            if (!TryGetNoun(englishNoun, out string nounRu, out string gender))
                return null;
            string adjRu = null;
            if (englishAdj != null)
            {
                if (!TryGetAdjective(englishAdj, out var am, out var af, out var an, out var apl))
                    return null;
                adjRu = AdjectiveForGender(gender, am, af, an, apl);
            }
            string midRu = null;
            if (englishMid != null)
            {
                // Middle may be missing -> for pirates we can drop it (branch 1 fallback).
                TryGetMiddle(englishMid, out midRu);
            }

            // --- Anchor translation: race genitive if it's a race; system name as-is otherwise. ---
            bool anchorIsRace = dominantRace != null && nameSource == (dominantRace.Name ?? string.Empty);
            string anchorRu = nameSource;
            string raceGenitive = null;
            if (anchorIsRace && TryGetRaceGenitive(dominantRace, out var gen))
            {
                raceGenitive = gen;
            }

            // --- Assemble per Russian rules. ---
            string built;
            if (isPirate)
            {
                // Original shapes: 0 = "Adj Noun", 1 = "Adj Mid Noun", 2 = "Anchor Noun" (or shape 1 without an anchor).
                // Russian: "Кровавые пираты", "Пираты «Кровавый клык»", "Пираты веккарусов" / "Пираты Ундос".
                TryGetAdjective(englishAdj, out var pm, out var pf, out var pn, out var ppl);
                string badge = null; // «Кровавый клык»: the adjective agrees with the middle word, not with the noun
                if (midRu != null && pm != null)
                {
                    string[] mid = midRu.Split('|');
                    string midGender = mid.Length > 1 ? mid[1] : "м";
                    badge = "«" + CapitalizeFirst(AdjectiveForGender(midGender, pm, pf, pn, ppl)) + " " + mid[0].ToLowerInvariant() + "»";
                }
                int branch = Galaxy.RndSeeded.Next(0, 3);
                if (branch == 2 && !string.IsNullOrEmpty(nameSource))
                    built = nounRu + " " + (raceGenitive ?? nameSource);
                else if (branch != 0 && badge != null)
                    built = nounRu + " " + badge;
                else if (pm != null)
                    built = AdjectiveForGender(gender, pm, pf, pn, ppl) + " " + nounRu;
                else
                    return null;
            }
            else
            {
                // Non-pirates: original order is [Adj] Anchor Noun. Russian: [Adj] Noun Anchor.
                string tail = raceGenitive ?? nameSource;
                if (!string.IsNullOrEmpty(adjRu))
                    built = adjRu + " " + nounRu + " " + tail;
                else
                    built = nounRu + " " + tail;
            }

            built = CapitalizeFirst(built);

            // --- Uniqueness check identical to the original. ---
            if (!IsEmpireNameInUse(galaxy, built))
            {
                result = built;
                break;
            }

            attempts++;
        }

        return result;
    }

    static bool Prefix(
        Galaxy galaxy,
        StellarObject capital,
        Race dominantRace,
        Government government,
        bool isPirate,
        ref string __result)
    {
        try
        {
            if (galaxy == null || dominantRace == null || government == null)
                return true; // let original handle degenerate cases

            string built = BuildRussianEmpire(galaxy, capital, dominantRace, government, isPirate);
            if (built == null)
                return true; // missing translation -> original

            __result = built;
            return false;
        }
        catch (Exception ex)
        {
            ModInit.Log("GenerateEmpireName patch failed: " + ex);
            return true;
        }
    }
}
