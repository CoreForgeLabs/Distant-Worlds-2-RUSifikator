using System;
using System.IO;
using System.Reflection;

internal static class StartupHook
{
    private const string LogPath = @"D:\Games\Distant Worlds 2\Russian_MOD\ui_mod2\build\RussianUiMod.log";

    public static void Initialize()
    {
        WriteLog("StartupHook.Initialize fired. AppDomain=" + AppDomain.CurrentDomain.FriendlyName);
        try
        {
            AppDomain.CurrentDomain.AssemblyLoad += (s, e) =>
            {
                var name = e.LoadedAssembly.GetName().Name;
                if (name == "DistantWorlds.UI")
                {
                    WriteLog("DistantWorlds.UI loaded -> calling ModInit.Initialize");
                    try { RussianUiMod.ModInit.Initialize(); }
                    catch (Exception ex) { WriteLog("Init err: " + ex); }
                }
            };
            WriteLog("AssemblyLoad handler registered. Waiting for DistantWorlds.UI...");
        }
        catch (Exception ex) { WriteLog("Hook setup err: " + ex); }
    }

    private static void WriteLog(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss ") + msg + "\r\n");
        }
        catch { }
    }
}

