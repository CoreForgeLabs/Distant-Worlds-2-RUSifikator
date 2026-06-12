using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Stride.Core.Mathematics;

namespace RussianUiMod;

/// <summary>
/// Расширяет карточку описания исследования: увеличивает rectangle.Width и labelWidth
/// в игровых статических рисующих методах DrawingHelper.DrawComponent* / DrawComponentStats*.
///
/// Идентифицируем методы по типу параметров (RectangleF + опционально labelWidth float).
/// В release-build имена параметров обычно сохраняются для public методов, но на всякий
/// случай fallback по позиции (labelWidth почти всегда — float сразу за margin).
/// </summary>
internal static class WidenPatcher
{
    public static Dictionary<MethodBase, int> LabelWidthIndex = new Dictionary<MethodBase, int>();
    public static Dictionary<MethodBase, int> ComponentMarginIndex = new Dictionary<MethodBase, int>();
    public static Dictionary<MethodBase, int> MarginIndex = new Dictionary<MethodBase, int>();
    public static HashSet<MethodBase> LoggedOnce = new HashSet<MethodBase>();

    public static List<MethodBase> FindTargets(string[] methodNames, out int totalFound)
    {
        totalFound = 0;
        var list = new List<MethodBase>();
        var t = AccessTools.TypeByName("DistantWorlds.Types.UserInterfaceHelper");
        if (t == null) { ModInit.Log("[Widen] UserInterfaceHelper NOT FOUND"); return list; }

        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!methodNames.Contains(m.Name)) continue;
            var ps = m.GetParameters();
            int rectIdx = -1, labelIdx = -1, cmIdx = -1, mIdx = -1;
            for (int i = 0; i < ps.Length; i++)
            {
                if (rectIdx < 0 && ps[i].ParameterType == typeof(RectangleF)) rectIdx = i;
                if (ps[i].ParameterType == typeof(float))
                {
                    if (labelIdx < 0 && ps[i].Name == "labelWidth") labelIdx = i;
                    if (cmIdx < 0 && ps[i].Name == "componentMargin") cmIdx = i;
                    if (mIdx < 0 && ps[i].Name == "margin") mIdx = i;
                }
            }
            if (rectIdx < 0)
            {
                ModInit.Log($"[Widen] SKIP {m.Name}({ps.Length}) — no RectangleF");
                continue;
            }
            ModInit.Log($"[Widen] +TARGET {m.Name}({ps.Length})  rect={rectIdx} label={labelIdx} cm={cmIdx} m={mIdx}");
            list.Add(m);
            LabelWidthIndex[m] = labelIdx;
            ComponentMarginIndex[m] = cmIdx;
            MarginIndex[m] = mIdx;
            totalFound++;
        }
        return list;
    }

    public static Dictionary<MethodBase, int> SeenCounts = new Dictionary<MethodBase, int>();

    // Tracking "первая ли это колонка": храним минимальный X за последние 500мс,
    // если новый X > min + 100 → это [M] колонка
    static List<(DateTime t, float x)> _recentX = new List<(DateTime, float)>();
    public static bool IsSecondColumn(float rectX)
    {
        var now = DateTime.UtcNow;
        lock (_recentX)
        {
            _recentX.RemoveAll(p => (now - p.t).TotalMilliseconds > 500);
            float minX = _recentX.Count == 0 ? rectX : _recentX.Min(p => p.x);
            _recentX.Add((now, rectX));
            return rectX > minX + 100f;
        }
    }

    public static void LogParamsOnce(MethodBase m, object[] args)
    {
        int seen;
        lock (SeenCounts)
        {
            SeenCounts.TryGetValue(m, out seen);
            if (seen >= 4) return;
            SeenCounts[m] = seen + 1;
        }
        var ps = m.GetParameters();
        var parts = new List<string>();
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType == typeof(RectangleF))
            {
                var r = (RectangleF)args[i];
                parts.Add($"{ps[i].Name}=R(X={r.X:F0},W={r.Width:F0})");
            }
            else if (ps[i].ParameterType == typeof(float))
            {
                parts.Add($"{ps[i].Name}={(float)args[i]:F1}");
            }
        }
        ModInit.Log($"[IN#{seen}] {m.Name}: {string.Join(" ", parts)}");
    }
}

[HarmonyPatch]
public static class Patch_DrawComponent_FullSignature
{
    static bool Prepare() => ModInit.Config.Enable_DrawComponent;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var names = new[] { "DrawComponent", "DrawComponentValues", "DrawComponentComparison" };
        int n;
        var list = WidenPatcher.FindTargets(names, out n);
        ModInit.Log($"[Widen-Component] total targets: {n}");
        return list;
    }

    static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        var c = ModInit.Config;
        if (!c.Enable_DrawComponent) return;
        WidenPatcher.LogParamsOnce(__originalMethod, __args);
        var ps = __originalMethod.GetParameters();
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType == typeof(RectangleF))
            {
                var r = (RectangleF)__args[i];
                bool isSecond = c.SecondColumnXOffset != 0f && WidenPatcher.IsSecondColumn(r.X);
                if (c.SecondColumnXOffset != 0f)
                {
                    // New semantic: GROW each column outward by N px.
                    // [S] (first):  shift X left by N, grow W by N -> grows leftward, keeps right edge.
                    // [M] (second): grow W by N -> grows rightward, keeps left edge.
                    if (isSecond) { r.Width += c.SecondColumnXOffset; }
                    else          { r.X -= c.SecondColumnXOffset; r.Width += c.SecondColumnXOffset; }
                }
                if (c.Component_RectWidthScale != 1f)
                {
                    r.Width *= c.Component_RectWidthScale;
                }
                __args[i] = r;
                break;
            }
        }

        if (c.Component_LabelWidthScale != 1f
            && WidenPatcher.LabelWidthIndex.TryGetValue(__originalMethod, out int li)
            && li >= 0)
        {
            __args[li] = (float)__args[li] * c.Component_LabelWidthScale;
        }

        if (c.Component_ComponentMarginAdd != 0f
            && WidenPatcher.ComponentMarginIndex.TryGetValue(__originalMethod, out int cmi)
            && cmi >= 0)
        {
            __args[cmi] = (float)__args[cmi] + c.Component_ComponentMarginAdd;
        }
    }
}

[HarmonyPatch]
public static class Patch_DrawComponentStats_Family
{
    static bool Prepare() => ModInit.Config.Enable_DrawComponentStats;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var names = new[] { "DrawComponentStats", "DrawComponentStatsComparison" };
        int n;
        var list = WidenPatcher.FindTargets(names, out n);
        ModInit.Log($"[Widen-Stats] total targets: {n}");
        return list;
    }

    static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        var c = ModInit.Config;
        if (!c.Enable_DrawComponentStats) return;
        WidenPatcher.LogParamsOnce(__originalMethod, __args);
        var ps = __originalMethod.GetParameters();
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType == typeof(RectangleF))
            {
                var r = (RectangleF)__args[i];
                bool isSecond = c.SecondColumnXOffset != 0f && WidenPatcher.IsSecondColumn(r.X);
                if (c.SecondColumnXOffset != 0f)
                {
                    // grow outward (see Patch_DrawComponent_FullSignature for semantic)
                    if (isSecond) { r.Width += c.SecondColumnXOffset; }
                    else          { r.X -= c.SecondColumnXOffset; r.Width += c.SecondColumnXOffset; }
                }
                if (c.Stats_RectWidthScale != 1f)
                {
                    r.Width *= c.Stats_RectWidthScale;
                }
                __args[i] = r;
                break;
            }
        }

        if (c.Stats_LabelWidthScale != 1f
            && WidenPatcher.LabelWidthIndex.TryGetValue(__originalMethod, out int li)
            && li >= 0)
        {
            __args[li] = (float)__args[li] * c.Stats_LabelWidthScale;
        }

        if (c.Stats_MarginAdd != 0f
            && WidenPatcher.MarginIndex.TryGetValue(__originalMethod, out int mi)
            && mi >= 0)
        {
            __args[mi] = (float)__args[mi] + c.Stats_MarginAdd;
        }
    }
}
