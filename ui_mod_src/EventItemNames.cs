using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Story events name the things they create: a pirate faction "Hidden Vacuum Dwellers", a base "Prime
/// Research Station", a planet "Terra". The name sits in GameEventAction.GeneratedItemName, and later
/// actions find the object by the same English string, so the data cannot be translated. Here the action
/// gets the Russian name ("CFL Item ..." in GameText, Docs/extra_gametext_items.tsv) right before it runs,
/// and the lookups by name accept the other language when the first try finds nothing - so old saves with
/// English names and new games with Russian names both keep working.
/// </summary>
public static class ItemNames
{
    private const string Prefix = "CFL Item ";
    private static Dictionary<string, string>? _english;                 // Russian name -> English name
    private static int _logged, _loggedFacilities, _lookups;
    [ThreadStatic] private static bool _busy;

    // action types whose GeneratedItemName is a visible name (the same list is in tools/event_names.py);
    // for the other types it is an identifier: an event, a variable, a facility definition
    public static readonly HashSet<GameEventActionType> Naming = new(
        new[]
            {
                "GeneratePirateFaction", "GenerateEmpire", "ChangeItemName", "GenerateAbandonedShipBase", "GenerateShipBase",
                "GenerateIndependentColony", "GeneratePlanetarySystem", "GenerateStar", "GenerateCharacter",
                "GenerateDebrisField", "AddSpecialLocationGalaxy", "AddSpecialLocationEmpire", "GenerateBonusAtOrb",
                "BuildFacility"
            }
            .Select(n => Enum.TryParse<GameEventActionType>(n, out var t) ? t : GameEventActionType.Undefined)
            .Where(t => t != GameEventActionType.Undefined));

    public static string? Russian(string? english) =>
        !string.IsNullOrEmpty(english) && TextResolver._Text.TryGetValue(Prefix + english, out var ru) &&
        !string.IsNullOrEmpty(ru) && ru != english ? ru : null;

    /// <summary>The same thing in the other language, or null when the name is not one of ours.</summary>
    public static string? Alias(string? name)
    {
        if (string.IsNullOrEmpty(name) || TextResolver._Text.Count == 0) return null;
        string? ru = Russian(name);
        if (ru != null) return ru;
        var english = _english;
        if (english == null)
        {
            english = new Dictionary<string, string>();
            foreach (var pair in TextResolver._Text)
                if (pair.Key.StartsWith(Prefix, StringComparison.Ordinal) && !string.IsNullOrEmpty(pair.Value))
                    english[pair.Value] = pair.Key.Substring(Prefix.Length);
            _english = english;
        }
        return english.TryGetValue(name!, out var en) && en != name ? en : null;
    }

    public static T? Retry<T>(T? found, string name, Func<string, T?> lookup) where T : class
    {
        if (found != null || _busy) return found;
        try
        {
            string? alias = Alias(name);
            if (alias == null) return null;
            _busy = true;
            T? other = lookup(alias);
            if (other != null && _lookups++ < 30) ModInit.Log("Event item found by its other name: " + name + " -> " + alias);
            return other;
        }
        catch (Exception ex)
        {
            ModInit.Log("ItemNames lookup failed: " + ex.Message);
            return null;
        }
        finally
        {
            _busy = false;
        }
    }

    public static void Rename(GameEventAction action)
    {
        try
        {
            if (action == null || !Naming.Contains(action.Type)) return;
            string? ru = Russian(action.GeneratedItemName);
            if (ru == null) return;
            // facilities have their own allowance: the first sixty lines are used up by pirates and colonies
            bool facility = action.Type == GameEventActionType.BuildFacility;
            if (facility ? _loggedFacilities++ < 40 : _logged++ < 60)
                ModInit.Log("Event item (" + action.Type + "): " + action.GeneratedItemName + " -> " + ru);
            action.GeneratedItemName = ru;
        }
        catch (Exception ex)
        {
            ModInit.Log("ItemNames rename failed: " + ex.Message);
        }
    }
}

[HarmonyPatch]
public static class Patch_EventItemRename
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(GameEventAction)).Where(m => m.Name == "ExecuteAction").Cast<MethodBase>();

    static void Prefix(GameEventAction __instance) => ItemNames.Rename(__instance);
}

[HarmonyPatch(typeof(EmpireList), "GetFirstByName", new[] { typeof(string) })]
public static class Patch_ItemLookup_Empire
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(EmpireList __instance, string empireName, ref Empire __result) =>
        __result = ItemNames.Retry(__result, empireName, __instance.GetFirstByName)!;
}

[HarmonyPatch(typeof(OrbList), "GetByName", new[] { typeof(string) })]
public static class Patch_ItemLookup_Orb
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(OrbList __instance, string orbName, ref Orb __result) =>
        __result = ItemNames.Retry(__result, orbName, __instance.GetByName)!;
}

[HarmonyPatch(typeof(ShipList), "GetByName", new[] { typeof(string) })]
public static class Patch_ItemLookup_Ship
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(ShipList __instance, string shipName, ref Ship __result) =>
        __result = ItemNames.Retry(__result, shipName, __instance.GetByName)!;
}

[HarmonyPatch(typeof(CharacterList), "GetFirstByName", new[] { typeof(string) })]
public static class Patch_ItemLookup_Character
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(CharacterList __instance, string name, ref Character __result) =>
        __result = ItemNames.Retry(__result, name, __instance.GetFirstByName)!;
}

[HarmonyPatch(typeof(CharacterList), "GetFirstByName", new[] { typeof(string), typeof(CharacterRole), typeof(int) })]
public static class Patch_ItemLookup_CharacterRole
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(CharacterList __instance, string name, CharacterRole role, int empireId, ref Character __result) =>
        __result = ItemNames.Retry(__result, name, alias => __instance.GetFirstByName(alias, role, empireId))!;
}

[HarmonyPatch(typeof(CharacterList), "GetFirstCharacterLocationByName")]
public static class Patch_ItemLookup_CharacterLocation
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(CharacterList __instance, Galaxy galaxy, string name, ref StellarObject __result) =>
        __result = ItemNames.Retry(__result, name, alias => __instance.GetFirstCharacterLocationByName(galaxy, alias))!;
}

[HarmonyPatch(typeof(OrbList), "GetFirstFacilityOrbByName")]
public static class Patch_ItemLookup_Facility
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(OrbList __instance, string facilityName, ref Orb __result) =>
        __result = ItemNames.Retry(__result, facilityName, __instance.GetFirstFacilityOrbByName)!;
}

[HarmonyPatch(typeof(SpecialLocationList), "GetFirstOrbBySpecialLocationName")]
public static class Patch_ItemLookup_SpecialLocation
{
    static bool Prepare() => ModInit.Config.Enable_TextFixes;

    static void Postfix(SpecialLocationList __instance, Galaxy galaxy, string specialLocationName, ref Orb __result) =>
        __result = ItemNames.Retry(__result, specialLocationName, alias => __instance.GetFirstOrbBySpecialLocationName(galaxy, alias))!;
}
