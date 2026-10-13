using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DistantWorlds.Types;
using HarmonyLib;

namespace RussianUiMod;

/// <summary>
/// Start-up order with DW2 Mod Launcher. Its loader calls the entry points of the mods first, then hooks
/// its status widget (the first Harmony patch of the process, about 3 seconds), and only then reads the XML
/// patch files and hooks the game's data loading (a Harmony postfix on Stride's VirtualFileSystem.OpenStream)
/// - all on one thread, while the game itself is starting on another. When the hooks come later than the
/// game's first data file, everything loaded before them stays English: races, governments, components,
/// research, resources, facilities. Seen on the stand: 75 of 144 patch files "did nothing" when this mod's
/// entry point took 4 seconds; with 1.4 seconds the loader won by 0.2 s.
///
/// Three measures:
/// 1. the entry point returns at once (ModInit.Initialize);
/// 2. the mod's own patches are applied on a background thread only after the loader reported its hooks;
/// 3. a gate on the game's session log holds the game at its next log line - "Loading GameSettings file",
///    the one before the base data - until the loader reports its hooks. It is installed after the
///    loader's first patch, when a Harmony patch costs milliseconds.
///
/// What was tried and must not be repeated:
/// - a gate on Galaxy.LoadStaticBaseData: Harmony's copy of a patched method is compiled with inlining,
///   OpenStream gets inlined into it and the loader's postfix never runs (142 of 144 patch files did
///   nothing). Never patch a method that opens the game's data files itself;
/// - installing the gate from the entry point: as the first patch of the process it took 3.5-5 seconds.
/// </summary>
public static class StartupOrder
{
    public const string WidgetHooked = "status widget";                 // dw2modlauncher.log, after the loader's first patch
    public static readonly string[] XmlHooked = { "hooks installed", "patch file(s) loaded", "nothing installed" };

    public static bool LoaderPresent() =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "DW2ModLauncher.Loader");

    /// <summary>Waits until one of the loader's logs has one of the markers. Returns the time spent, -1 on timeout.</summary>
    public static long WaitFor(int timeoutMs, params string[] markers)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "data", "Logs");
            DateTime notBefore = Process.GetCurrentProcess().StartTime.AddSeconds(-5);
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Reported(dir, notBefore, markers)) return sw.ElapsedMilliseconds;
                Thread.Sleep(15);
            }
        }
        catch
        {
        }
        return -1;
    }

    private static bool Reported(string dir, DateTime notBefore, string[] markers)
    {
        foreach (string name in new[] { "dw2modlauncher-patches.log", "dw2modlauncher.log" })
        {
            try
            {
                string path = Path.Combine(dir, name);
                if (!File.Exists(path) || File.GetLastWriteTime(path) < notBefore) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new byte[8192];
                int n = stream.Read(buffer, 0, buffer.Length);
                string head = Encoding.UTF8.GetString(buffer, 0, n);
                if (markers.Any(head.Contains)) return true;
            }
            catch
            {
            }
        }
        return false;
    }
}

/// <summary>Holds the game at one session log line until the loader's XML hooks are in place.</summary>
public static class StartupGate
{
    private static int _closed = 1;                                        // 1 while the gate still has to hold the game

    public static void Install()
    {
        var sw = Stopwatch.StartNew();
        var target = AccessTools.Method(typeof(TextHelper), "SessionLog", new[] { typeof(string) });
        if (target == null)
        {
            ModInit.Log("StartupGate: TextHelper.SessionLog not found, the game is not held back");
            return;
        }
        new Harmony("ru.dw2.ui2.gate").Patch(target, prefix: new HarmonyMethod(typeof(StartupGate), nameof(Hold)));
        ModInit.Log("StartupGate: installed in " + sw.ElapsedMilliseconds + " ms");
    }

    public static void Hold(string text)
    {
        if (Interlocked.Exchange(ref _closed, 0) == 0) return;
        try
        {
            long ms = StartupOrder.WaitFor(12000, StartupOrder.XmlHooked);
            // the line shows whether the gate was in time: it has to come earlier than the first "Loading file: ..."
            string line = text == null ? "" : text.Length > 50 ? text.Substring(0, 50) : text;
            ModInit.Log("StartupGate: game held for " + ms + " ms at \"" + line + "\"" + (ms < 0 ? " - the loader did not report its xml hooks" : ""));
        }
        catch (Exception ex)
        {
            ModInit.Log("StartupGate failed: " + ex.Message);
        }
    }
}
