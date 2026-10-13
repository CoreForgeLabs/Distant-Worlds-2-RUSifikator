using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using DistantWorlds.Types;

namespace RussianUiMod;

/// <summary>
/// Localizable replacement for Empire.GenerateSituationDescription (non-pirate branch).
/// All literal English phrases are routed through TextResolver with GameText keys
/// so that the GameText.txt translation reaches them.
/// If IsPirate == true, the original method is left untouched.
/// </summary>
[HarmonyPatch]
public static class Patch_Empire_GenerateSituationDescription
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static MethodBase TargetMethod()
        => AccessTools.Method(typeof(DistantWorlds.Types.Empire), "GenerateSituationDescription");

    // Single local helper: resolves a GameText key. Falls back to the key itself
    // (so missing keys are visible rather than crashing).
    static string T(string key)
    {
        string s = TextResolver.GetText(key);
        return string.IsNullOrEmpty(s) ? key : s;
    }

    static bool Prefix(Empire __instance, GameStartSettingsEmpire empireStart, ref string __result)
    {
        // Pirates keep the vanilla (already localized) path.
        if (__instance.IsPirate)
            return true;
        // Without the mod's own GameText keys (text module switched off) the original English text is better
        // than "KEY NOT FOUND".
        if (!TextResolver._Text.ContainsKey("CFL Intro Our government is X"))
            return true;

        try
        {
            var self = __instance;
            string empty = string.Empty;

            // ---- Our faction is known as the X ----
            empty += string.Format(T("Our faction is known as the X"), self.Name);
            empty += ".";

            // ---- Government ----
            Government government = self.GetGovernment();
            if (government != null)
            {
                empty += " " + string.Format(T("CFL Intro Our government is X"), government.Name);
                empty += ".";
            }
            empty += "\n\n";

            // ---- Races ----
            RaceList colonyRacesAsRaces = self.GetColonyRacesAsRaces(0L);
            if (colonyRacesAsRaces != null)
            {
                colonyRacesAsRaces.ReverseSort();
                if (colonyRacesAsRaces.Count > 0)
                {
                    string text = colonyRacesAsRaces[0].Name;
                    if (text == "Human")
                        text = "Humans";

                    SortableTextValueList sortableTextValueList =
                        TextHelper.GenerateRaceCharacteristics(colonyRacesAsRaces[0]);

                    if (sortableTextValueList.Count <= 0)
                    {
                        empty += string.Format(T("We are the RACE"), text) + ". ";
                    }
                    else
                    {
                        empty += string.Format(T("We are the RACE"), text) + ", ";
                        // Build the "a, b and c" list of characteristics, lowercased.
                        string traits = string.Empty;
                        for (int i = 0; i < sortableTextValueList.Count; i++)
                        {
                            traits += sortableTextValueList[i].Text.ToLowerInvariant();
                            if (i == sortableTextValueList.Count - 2)
                                traits += " " + T("and") + " ";
                            else if (i < sortableTextValueList.Count - 1)
                                traits += ", ";
                        }
                        empty += string.Format(T("who are typically X"), traits);
                        empty += ". ";
                    }

                    BonusList aboveThreshold = colonyRacesAsRaces[0].Bonuses.GetAboveThreshold(0f, 0f);
                    if (aboveThreshold.Count > 0)
                    {
                        // Whole sentence in one template so Russian word order can be fixed.
                        string skills = string.Empty;
                        for (int j = 0; j < aboveThreshold.Count; j++)
                        {
                            skills += TextHelper.ResolveDescription(aboveThreshold[j].Type).ToLowerInvariant();
                            if (j == aboveThreshold.Count - 2)
                                skills += " " + T("and") + " ";
                            else if (j < aboveThreshold.Count - 1)
                                skills += ", ";
                        }
                        empty += string.Format(T("RACE have natural skills in X"), text, skills);
                        empty += ".";
                    }
                    empty += "\n\n";

                    if (colonyRacesAsRaces.Count > 1)
                    {
                        // Collect race names (skipping nulls) as a comma/and list,
                        // then wrap them in one template with {0}.
                        string races = string.Empty;
                        var names = new List<string>();
                        for (int k = 1; k < colonyRacesAsRaces.Count; k++)
                        {
                            Race race = colonyRacesAsRaces[k];
                            if (race != null)
                                names.Add(race.Name);
                        }
                        for (int k = 0; k < names.Count; k++)
                        {
                            races += names[k];
                            if (k == names.Count - 2)
                                races += " " + T("and") + " ";
                            else if (k < names.Count - 1)
                                races += ", ";
                        }
                        if (names.Count > 0)
                        {
                            empty += string.Format(T("CFL Intro We also have X amongst our population"), races);
                            empty += "\n\n";
                        }
                    }
                }
            }

            // ---- Leader ----
            if (self.Leader != null)
            {
                BonusList aboveThreshold2 = self.Leader.BonusesComplete.GetAboveThreshold(0f, 0f);
                if (aboveThreshold2.Count > 0)
                {
                    string skills2 = string.Empty;
                    for (int l = 0; l < aboveThreshold2.Count; l++)
                    {
                        skills2 += TextHelper.ResolveDescription(aboveThreshold2[l].Type).ToLowerInvariant();
                        if (l == aboveThreshold2.Count - 2)
                            skills2 += " " + T("and") + " ";
                        else if (l < aboveThreshold2.Count - 1)
                            skills2 += ", ";
                    }
                    empty += string.Format(T("Our leader is NAME, skilled in X"), self.Leader.Name, skills2);
                }
                else
                {
                    empty += string.Format(T("Our leader is NAME"), self.Leader.Name);
                }
                empty += ".";
                empty += "\n\n";
            }

            // ---- Tech level ----
            float num = 0f;
            if (empireStart != null)
                num = empireStart.TechLevel;

            // ---- Home colony / refugee fleet ----
            if (government != null && government.SpecialMode == GovernmentSpecialMode.RefugeeFleet)
            {
                if (empireStart != null)
                    num = empireStart.RefugeeFleetTechLevel;
                empty += T("Refugee Fleet Situation Introduction");
                empty += "\n\n";
            }
            else if (self.Capital != null && self.Capital is Orb orb)
            {
                empty += string.Format(T("CFL Intro Our home colony is X"), orb.Name);

                OrbType orbType = orb.GetOrbType();
                if (orbType != null)
                {
                    List<string> vowels = new List<string> { "a", "e", "i", "o", "u" };
                    bool startsWithVowel = false;
                    string otLower = orbType.Name.ToLowerInvariant();
                    if (otLower.Length > 0)
                        startsWithVowel = vowels.Contains(otLower.Substring(0, 1));

                    // "a/an <orbtype> <category>" — one template per branch.
                    string articleKey = startsWithVowel
                        ? "CFL Intro an PLANETTYPE PLANETMOON"
                        : "CFL Intro a PLANETTYPE PLANETMOON";
                    string colonyPhrase = string.Format(
                        T(articleKey),
                        otLower,
                        TextHelper.ResolveDescription(orbType.Category).ToLowerInvariant());
                    empty += ", " + colonyPhrase;

                    StarSystem system = orb.GetSystem(self._Galaxy);
                    if (system != null)
                        empty += " " + string.Format(T("CFL Intro in the X system"), system.Name);
                    empty += ". ";

                    Location location = orb.GetLocation(self._Galaxy);
                    if (location != null)
                    {
                        OrbList byCategory = location.Orbs.GetByCategory(OrbCategory.Planet);
                        OrbList byCategory2 = location.Orbs.GetByCategory(OrbCategory.Moon);
                        byCategory.Remove(orb);
                        byCategory2.Remove(orb);

                        if (byCategory.Count > 0 && byCategory[0] != null)
                        {
                            OrbType orbType2 = byCategory[0].GetOrbType();
                            if (orbType2 != null)
                            {
                                empty += string.Format(
                                    T("Nearby is the PLANETTYPE PLANETMOON NAME"),
                                    orbType2.Name.ToLowerInvariant(),
                                    TextHelper.ResolveDescription(orbType2.Category).ToLowerInvariant(),
                                    byCategory[0].Name);
                                empty += ". ";
                            }
                        }

                        if (byCategory2.Count > 0)
                        {
                            // Whole sentence built from template(s), one per "nearby" phrasing.
                            string moonIntroKey;
                            if (byCategory.Count > 0)
                                moonIntroKey = (byCategory2.Count != 1)
                                    ? "CFL Intro Also nearby are X"
                                    : "CFL Intro Also nearby is X";
                            else
                                moonIntroKey = (byCategory2.Count != 1)
                                    ? "CFL Intro Nearby are X"
                                    : "CFL Intro Nearby is X";

                            string moons = string.Empty;
                            for (int m = 0; m < byCategory2.Count; m++)
                            {
                                Orb orb2 = byCategory2[m];
                                if (orb2 == null)
                                    continue;
                                OrbType orbType3 = orb2.GetOrbType();
                                if (orbType3 == null)
                                    continue;
                                moons += string.Format(
                                    T("CFL Intro the PLANETTYPE MOON NAME"),
                                    orbType3.Name.ToLowerInvariant(),
                                    TextHelper.ResolveDescription(OrbCategory.Moon).ToLowerInvariant(),
                                    orb2.Name);
                                if (m == byCategory2.Count - 2)
                                    moons += " " + T("and") + " ";
                                else if (m < byCategory2.Count - 1)
                                    moons += ", ";
                            }
                            empty += string.Format(T(moonIntroKey), moons);
                            empty += ".";
                        }
                    }
                }
                empty += "\n\n";

                // ---- Other colonies ----
                if (self.Colonies.Count > 1)
                {
                    OrbList orbList = new OrbList();
                    orbList.AddRange(self.Colonies);
                    orbList.Remove(orb);
                    if (orbList.Count == 1)
                    {
                        OrbType orbType4 = orbList[0].GetOrbType();
                        if (orbType4 != null)
                        {
                            empty += string.Format(
                                T("CFL Intro We also have another colony X"),
                                string.Format(
                                    T("CFL Intro PLANETTYPE PLANETMOON NAME"),
                                    orbType4.Name.ToLowerInvariant(),
                                    TextHelper.ResolveDescription(orbType4.Category).ToLowerInvariant(),
                                    orbList[0].Name));
                        }
                    }
                    else if (orbList.Count < 10)
                    {
                        string names = string.Empty;
                        for (int n = 0; n < orbList.Count; n++)
                        {
                            names += orbList[n].Name;
                            if (n == orbList.Count - 2)
                                names += " " + T("and") + " ";
                            else if (n < orbList.Count - 1)
                                names += ", ";
                        }
                        empty += string.Format(
                            T("CFL Intro We also have X other colonies Y"),
                            orbList.Count.ToString("0"),
                            names);
                        empty += ".";
                    }
                    else
                    {
                        empty += string.Format(
                            T("CFL Intro We also have X other colonies"),
                            orbList.Count.ToString("0"));
                    }
                    empty += "\n\n";
                }
            }

            // ---- Tech-level sentences ----
            if (num < 0f)
            {
                empty += T("CFL Intro tech primitive");
                empty += "\n\n";
            }
            else if (num < 1f)
            {
                empty += T("CFL Intro tech basic");
                empty += "\n\n";
            }
            else if (num < 2f)
            {
                empty += T("CFL Intro tech expanding");
                empty += "\n\n";
            }
            else if (num <= 3f)
            {
                empty += T("CFL Intro tech advanced");
                empty += "\n\n";
            }
            else
            {
                empty += T("CFL Intro tech high");
                empty += "\n\n";
            }

            // ---- Contacted empires ----
            EmpireRelationList empiresHaveMet = self.Relations.GetEmpiresHaveMet(
                self._Galaxy, -1,
                includeIndependentEmpire: false,
                includeIndependentColonies: true,
                includePirates: true);

            if (empiresHaveMet.Count > 0)
            {
                EmpireRelationList empiresAtWarWith = empiresHaveMet.GetEmpiresAtWarWith();
                if (empiresAtWarWith.Count > 0)
                {
                    string warNames = string.Empty;
                    // Build the list first (skipping nulls), then the template.
                    var warList = new List<string>();
                    for (int num2 = 0; num2 < empiresAtWarWith.Count; num2++)
                    {
                        EmpireRelation empireRelation = empiresAtWarWith[num2];
                        if (empireRelation == null)
                            continue;
                        Empire otherEmpire = empireRelation.GetOtherEmpire(self._Galaxy);
                        if (otherEmpire != null)
                            warList.Add(otherEmpire.Name);
                    }
                    for (int i = 0; i < warList.Count; i++)
                    {
                        warNames += warList[i];
                        if (i == warList.Count - 2)
                            warNames += " " + T("and") + " ";
                        else if (i < warList.Count - 1)
                            warNames += ", ";
                    }
                    if (warList.Count > 0)
                    {
                        empty += string.Format(T("CFL Intro We are at war with X"), warNames);
                        empty += ".\n\n";
                    }
                }

                empiresHaveMet.AssignSortValuesProximity(self._Galaxy, self);
                empiresHaveMet.Sort();
                if (empiresHaveMet.Count > 0)
                {
                    List<string> list2 = new List<string>();
                    for (int num3 = 0; num3 < empiresHaveMet.Count; num3++)
                    {
                        EmpireRelation empireRelation2 = empiresHaveMet[num3];
                        if (empireRelation2 == null || empiresAtWarWith.Contains(empireRelation2))
                            continue;

                        Orb colony = null;
                        if (empireRelation2.IsIndependentColony(self._Galaxy, out colony))
                        {
                            if (colony != null && colony.Colony != null)
                            {
                                // Whole entry in one template: "{0} (an independent colony of {1})".
                                list2.Add(string.Format(
                                    T("CFL Intro X an independent colony of Y"),
                                    colony.Name,
                                    colony.Colony.Population.DominantRace.Name));
                            }
                            continue;
                        }
                        Empire otherEmpire2 = empireRelation2.GetOtherEmpire(self._Galaxy);
                        if (otherEmpire2 != null)
                        {
                            if (otherEmpire2.IsPirate)
                            {
                                list2.Add(string.Format(
                                    T("CFL Intro X a RACE pirate faction"),
                                    otherEmpire2.Name,
                                    otherEmpire2.GetDominantRace().Name));
                            }
                            else
                            {
                                list2.Add(string.Format(
                                    T("CFL Intro X a RACE faction"),
                                    otherEmpire2.Name,
                                    otherEmpire2.GetDominantRace().Name));
                            }
                        }
                    }
                    if (list2.Count > 0)
                    {
                        string contacts = string.Empty;
                        for (int num4 = 0; num4 < list2.Count; num4++)
                        {
                            contacts += list2[num4];
                            if (num4 == list2.Count - 2)
                                contacts += " " + T("and") + " ";
                            else if (num4 < list2.Count - 1)
                                contacts += ", ";
                        }
                        empty += string.Format(T("CFL Intro We are in contact with X"), contacts);
                        empty += ".\n\n";
                    }
                }
            }

            // ---- Closing line ----
            empty += T("Explore, Expand and Conquer!");

            // Some templates carry their own full stop and the original code adds another one after them.
            __result = System.Text.RegularExpressions.Regex.Replace(empty, @"(?<!\.)\.\.(?!\.)", ".");
            return false; // skip original
        }
        catch (Exception ex)
        {
            ModInit.Log("[SituationText] GenerateSituationDescription prefix failed: " + ex);
            return true; // let vanilla run
        }
    }
}
