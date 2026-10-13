using System;
using System.Collections.Generic;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Quests come from groups named by a template: "{0} Starlines", "{0} Mining", "{0} Guild", where {0} is a
/// star system or a family name. The templates live in the code (Government's static lists) and in the
/// government data; both are swapped for GameText keys "CFL Group X Starlines" = «Звёздные линии {0}»
/// (Docs/extra_gametext_groups.tsv) right before a quest text is built.
/// </summary>
[HarmonyPatch(typeof(Quest), "ResolveQuestPeaceDescription")]
public static class Patch_GroupNames
{
    private static bool _staticDone;

    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    private static int Localize(List<string>? list)
    {
        int n = 0;
        if (list == null) return n;
        for (int i = 0; i < list.Count; i++)
        {
            string template = list[i];
            if (string.IsNullOrEmpty(template) || !template.Contains("{0}")) continue;
            if (TextResolver._Text.TryGetValue("CFL Group " + template.Replace("{0}", "X"), out var ru) &&
                !string.IsNullOrEmpty(ru) && ru.Contains("{0}"))
            {
                list[i] = ru;
                n++;
            }
        }
        return n;
    }

    static void Prefix(Empire empire)
    {
        try
        {
            if (TextResolver._Text.Count == 0) return;
            if (!_staticDone)
            {
                _staticDone = true;
                int n = Localize(Government.GeneralGroupNamesStatic) + Localize(Government.MiningGroupNamesStatic) +
                        Localize(Government.PassengerGroupNamesStatic) + Localize(Government.TransportGroupNamesStatic) +
                        Localize(Government.TechGroupNamesStatic);
                ModInit.Log("GroupNames: templates localized = " + n);
            }
            Government? government = empire?.GetGovernment();
            if (government == null) return;
            Localize(government.GeneralGroupNames);
            Localize(government.MiningGroupNames);
            Localize(government.PassengerGroupNames);
            Localize(government.TransportGroupNames);
            Localize(government.TechGroupNames);
        }
        catch (Exception ex)
        {
            ModInit.Log("GroupNames failed: " + ex.Message);
        }
    }
}
