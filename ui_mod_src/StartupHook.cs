using System;

/// <summary>
/// Fallback loader for a plain launch without the injection flag:
///   set DOTNET_STARTUP_HOOKS=path\RussianUiMod.dll
/// .NET calls StartupHook.Initialize() before the game's Main.
/// </summary>
internal static class StartupHook
{
    public static void Initialize() => RussianUiMod.Entry.Init();
}
