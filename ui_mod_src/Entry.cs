using System;

namespace RussianUiMod;

/// <summary>
/// Entry point for the game's own injection flag and for DW2 Mod Launcher:
///   DistantWorlds2.exe --low-level-inject "path\RussianUiMod.dll!RussianUiMod.Entry.Init"
/// The launcher finds this class by its name (public static Entry with Init / InitWithOptions).
/// </summary>
public static class Entry
{
    public static void Init() => Start(null);

    /// <summary>Called by DW2 Mod Launcher with the values of settings.schema.json as JSON.</summary>
    public static void InitWithOptions(string json) => Start(json);

    private static void Start(string? json)
    {
        ModInit.LauncherOptions = json;
        // The patches target DistantWorlds.UI types, so they can only be applied once it is loaded.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "DistantWorlds.UI")
            {
                ModInit.Initialize();
                return;
            }
        }
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
        {
            if (e.LoadedAssembly.GetName().Name != "DistantWorlds.UI") return;
            try { ModInit.Initialize(); }
            catch (Exception ex) { ModInit.Log("Init error: " + ex); }
        };
    }
}
