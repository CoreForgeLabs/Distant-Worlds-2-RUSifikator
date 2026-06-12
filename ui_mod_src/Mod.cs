using System;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Stride.Core.Mathematics;

namespace RussianUiMod;

/// <summary>
/// Конфиг модa. Все значения — множители, применяемые к параметрам игровых рендер-методов.
/// Меняются в RussianUiMod.config.json без перекомпиляции.
/// </summary>
public class ModConfig
{
    // --- Карточка-описание исследования: общая ширина (game-side) ---
    // ResearchTree.DetailDrawWidth — это поле в инстансе ResearchTree.
    // 1.0 = как в оригинале игры. ВНИМАНИЕ: значения >1.0 ломают дерево (чёрный экран),
    // поле используется для расчёта геометрии всего дерева, не только карточки.
    public float ResearchTree_DetailDrawWidth = 1.0f;

    // --- ResearchTree.DrawProjectDetail (карточка целиком — шапка + тело) ---
    // Финальный scale = clamp(ширина_окна / BaseWidth, MinScale, MaxScale) * SizeXScale (множитель пользователя).
    // Адаптивная база подстраивается под разрешение: FHD ~1.0, 2K ~1.3, 4K ~1.8.
    public float ProjectDetail_SizeXScale = 1.0f;            // пользовательский множитель ПОВЕРХ адаптивной базы
    public bool Enable_ProjectDetailWiden = true;
    public bool ProjectDetail_AdaptiveScale = true;          // включает зависимость от разрешения
    public float ProjectDetail_BaseWidth = 1920f;            // референсное разрешение для scale=1.0
    public float ProjectDetail_MinScale = 1.0f;              // нижний предел адаптивной базы
    public float ProjectDetail_MaxScale = 1.8f;              // верхний предел адаптивной базы

    // Tier-boost: для одиночных карточек (мало weapon-вариантов) — больше доп. растяжения, для многокомпонентных — меньше.
    // Финальный scale = adaptive * userScale * tierBoost(componentCount)
    public bool ProjectDetail_TierBoost = true;
    public float ProjectDetail_TierSingleBoost = 1.25f;      // 1 компонент → ×1.25
    public float ProjectDetail_TierMultiBoost = 1.10f;       // 2+ компонентов → ×1.10

    // --- UIH.DrawComponent / DrawComponentValues и т.п. ---
    // Растягиваем переданный rectangle.Width перед рендером (увеличивает доступное место карточки).
    public float Component_RectWidthScale = 1.25f;

    // Множитель labelWidth у UIH.Draw* методов. Русские лейблы длиннее английских — нужно больше места под метку.
    public float Component_LabelWidthScale = 1.40f;

    // --- DrawComponentStats / DrawComponentStatsComparison (короткая сигнатура без labelWidth-зависимых калк) ---
    public float Stats_RectWidthScale  = 1.25f;
    public float Stats_LabelWidthScale = 1.40f;

    // Прибавка к зазору между [S] и [M] колонками в Comparison-методах
    public float Component_ComponentMarginAdd = 0f;  // только для DrawComponentComparison
    public float Stats_MarginAdd              = 0f;  // только для DrawComponentStatsComparison

    // Сдвиг X для "второй колонки" (когда rect.X > предыдущего + 100). Создаёт зазор между [S] и [M].
    public float SecondColumnXOffset = 50f;

    public bool VerboseLog = true;

    // Включение/отключение групп патчей (для изоляции при падениях)
    public bool Enable_ResearchTreeWidth = true;
    public bool Enable_DrawComponent     = true;
    public bool Enable_DrawComponentStats = true;
}

public static class ModInit
{
    public static ModConfig Config = new ModConfig();
    public static bool _initialized;

    private static string ModDir =>
        Path.GetDirectoryName(typeof(ModInit).Assembly.Location) ?? ".";
    private static string LogPath    => Path.Combine(ModDir, "RussianUiMod.log");
    private static string ConfigPath => Path.Combine(ModDir, "RussianUiMod.config.json");

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            LoadOrCreateConfig();
            Log("=== RussianUiMod (ui_mod2) loaded ===");
            foreach (var f in typeof(ModConfig).GetFields())
                Log($"  {f.Name} = {f.GetValue(Config)}");

            var h = new Harmony("ru.dw2.ui2");
            h.PatchAll(typeof(ModInit).Assembly);
            Log("Harmony PatchAll done");
            DumpResearchTreeInfo();
        }
        catch (Exception ex)
        {
            Log("INIT ERROR: " + ex);
        }
    }

    private static void LoadOrCreateConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                Config = JsonSerializer.Deserialize<ModConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    IncludeFields = true,
                }) ?? new ModConfig();
                Log("Config loaded: " + ConfigPath);
            }
            else
            {
                WriteDefaultConfig();
                Log("Config created at: " + ConfigPath);
            }
        }
        catch (Exception ex)
        {
            Log("Config load err, using defaults: " + ex.Message);
            Config = new ModConfig();
        }
    }

    private static void WriteDefaultConfig()
    {
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
        });
        File.WriteAllText(ConfigPath, json);
    }

    internal static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("HH:mm:ss ") + msg + "\r\n");
        }
        catch { }
    }

    private static void DumpDrawingHelperMethods()
    {
        // (no-op, был использован для разовой диагностики)
    }

    private static void DumpResearchTreeInfo()
    {
        try
        {
            var t = AccessTools.TypeByName("DistantWorlds.UI.ResearchTree");
            if (t == null) { Log("[DUMP-RT] type NOT FOUND"); return; }
            Log($"[DUMP-RT] type={t.FullName} assembly={t.Assembly.GetName().Name}");

            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                if (f.Name.IndexOf("Width", StringComparison.OrdinalIgnoreCase) >= 0
                 || f.Name.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0
                 || f.Name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log($"[DUMP-RT] FIELD {f.FieldType.Name} {f.Name}  pub={f.IsPublic} static={f.IsStatic}");

            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                if (p.Name.IndexOf("Width", StringComparison.OrdinalIgnoreCase) >= 0
                 || p.Name.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0
                 || p.Name.IndexOf("Draw", StringComparison.OrdinalIgnoreCase) >= 0)
                    Log($"[DUMP-RT] PROP  {p.PropertyType.Name} {p.Name}  canRead={p.CanRead} canWrite={p.CanWrite}");

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                if (m.Name == "Draw" || m.Name == "Update" || m.Name == "Render" || m.Name == "Layout" || m.Name == "Init" || m.Name == "Initialize" || m.Name == "OnLoad" || m.Name.StartsWith("Set") || m.Name.StartsWith("Draw"))
                {
                    var ps = m.GetParameters();
                    var sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name));
                    Log($"[DUMP-RT] METHOD {m.ReturnType.Name} {m.Name}({sig})");
                }
            }
        }
        catch (Exception ex) { Log("[DUMP-RT] err: " + ex); }
    }
}

/// <summary>
/// Расширяет ResearchTree.DetailDrawWidth (поле инстанса) после Initialize().
/// Это меняет ширину окна-карточки описания исследования.
/// Patch на конструктор крашит игру (поле читается до полной готовности), поэтому используем Initialize.
/// </summary>
[HarmonyPatch]
public static class Patch_ResearchTree_Ctor
{
    static bool Prepare() => ModInit.Config.Enable_ResearchTreeWidth;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var t = AccessTools.TypeByName("DistantWorlds.UI.ResearchTree");
        if (t == null) { ModInit.Log("ResearchTree NOT FOUND"); return Array.Empty<MethodBase>(); }
        var list = new List<MethodBase>();
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            if (m.Name == "Initialize") list.Add(m);
        ModInit.Log($"[ResearchTree.Initialize] targets={list.Count}");
        return list;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _scaled =
        new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();

    static void Postfix(object __instance)
    {
        if (__instance == null) return;
        try
        {
            // Применяем scale ОДИН раз на инстанс — Initialize вызывается многократно (каждый Update/Layout).
            if (_scaled.TryGetValue(__instance, out _)) return;
            _scaled.Add(__instance, null);

            var t = __instance.GetType();

            // Один раз дампим значения интересных полей ИНСТАНСА — чтобы найти откуда берётся 1280px ширина карточки.
            if (!_dumpedInstanceFields)
            {
                _dumpedInstanceFields = true;
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    var n = f.Name;
                    if (n.IndexOf("Width", StringComparison.OrdinalIgnoreCase) >= 0
                     || n.IndexOf("Detail", StringComparison.OrdinalIgnoreCase) >= 0
                     || n.IndexOf("Project", StringComparison.OrdinalIgnoreCase) >= 0
                     || n.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        object val;
                        try { val = f.GetValue(__instance); }
                        catch (Exception ex) { val = "<ERR " + ex.Message + ">"; }
                        ModInit.Log($"[RT.INST] {f.FieldType.Name} {f.Name} = {val}");
                    }
                }
            }

            var fdw = t.GetField("DetailDrawWidth", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (fdw == null || fdw.FieldType != typeof(float)) return;
            float v = (float)fdw.GetValue(__instance);
            float scale = ModInit.Config.ResearchTree_DetailDrawWidth;
            if (scale != 1f && v > 0f && !float.IsInfinity(v) && !float.IsNaN(v))
            {
                fdw.SetValue(__instance, v * scale);
                ModInit.Log($"ResearchTree.DetailDrawWidth: {v:F1} -> {v * scale:F1} (x{scale}) [one-shot]");
            }
        }
        catch (Exception ex) { ModInit.Log("[RT.Init.Postfix] " + ex.Message); }
    }

    private static bool _dumpedInstanceFields;
}

/// <summary>
/// Расширяет ВСЮ карточку описания исследования (шапка + тело).
/// Patch на ResearchTree.DrawProjectDetail(SpriteBatch, ResearchProject, Vector2 position,
/// Vector2 projectSize, Side preferredSide, bool isSummaryMode) — увеличиваем projectSize.X.
/// </summary>
[HarmonyPatch]
public static class Patch_ResearchTree_DrawProjectDetail
{
    static bool Prepare() => ModInit.Config.Enable_ProjectDetailWiden;

    static IEnumerable<MethodBase> TargetMethods()
    {
        var t = AccessTools.TypeByName("DistantWorlds.UI.ResearchTree");
        if (t == null) { ModInit.Log("[DPD] ResearchTree NOT FOUND"); return Array.Empty<MethodBase>(); }
        var list = new List<MethodBase>();
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            // Цепляем ВСЕ варианты Draw*Project*Detail* с Vector2 projectSize:
            // DrawProjectDetail(6-param), DrawProjectDetailSimple(5-param), и т.д.
            if (m.Name != "DrawProjectDetail" && m.Name != "DrawProjectDetailSimple") continue;
            var ps = m.GetParameters();
            int vec2Count = 0;
            int projectSizeIdx = -1;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].ParameterType == typeof(Vector2))
                {
                    vec2Count++;
                    if (ps[i].Name == "projectSize") projectSizeIdx = i;
                    else if (projectSizeIdx < 0 && vec2Count == 2) projectSizeIdx = i; // fallback
                }
            }
            if (projectSizeIdx < 0)
            {
                ModInit.Log($"[DPD] SKIP {m.Name}({ps.Length}) — no projectSize");
                continue;
            }
            _projectSizeIndex[m] = projectSizeIdx;
            // Найдём индекс isSummaryMode (Boolean) — если TRUE, это inline-превью в дереве (не масштабируем)
            int summaryModeIdx = -1;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].ParameterType == typeof(bool) && ps[i].Name == "isSummaryMode")
                {
                    summaryModeIdx = i;
                    break;
                }
            }
            _summaryModeIndex[m] = summaryModeIdx;
            var sig = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name));
            ModInit.Log($"[DPD] +TARGET {m.Name}({sig}) projectSizeIdx={projectSizeIdx} summaryModeIdx={summaryModeIdx}");
            list.Add(m);
        }
        ModInit.Log($"[DPD] total targets: {list.Count}");
        return list;
    }

    private static readonly Dictionary<MethodBase, int> _projectSizeIndex = new Dictionary<MethodBase, int>();
    private static readonly Dictionary<MethodBase, int> _summaryModeIndex = new Dictionary<MethodBase, int>();
    private static readonly Dictionary<MethodBase, int> _callCounts = new Dictionary<MethodBase, int>();
    [ThreadStatic] private static float _savedDetailDrawWidth;
    [ThreadStatic] private static bool _detailWidthSaved;

    // Кешированная ширина back-buffer (обновляется при каждом вызове из spriteBatch)
    private static int _cachedViewportWidth = 0;
    private static int _lastLoggedWidth = 0;

    /// <summary>Достаём ширину экрана через SpriteBatch.GraphicsDevice.Presenter.BackBuffer.Width (рефлексия).</summary>
    private static int TryGetViewportWidth(object spriteBatch)
    {
        // 1) Через рефлексию SpriteBatch -> GraphicsDevice -> Presenter -> BackBuffer.Width
        try
        {
            if (spriteBatch != null)
            {
                var gd = spriteBatch.GetType().GetProperty("GraphicsDevice",
                    BindingFlags.Public | BindingFlags.Instance)?.GetValue(spriteBatch);
                if (gd != null)
                {
                    var presenter = gd.GetType().GetProperty("Presenter",
                        BindingFlags.Public | BindingFlags.Instance)?.GetValue(gd);
                    if (presenter != null)
                    {
                        var bb = presenter.GetType().GetProperty("BackBuffer",
                            BindingFlags.Public | BindingFlags.Instance)?.GetValue(presenter);
                        if (bb != null)
                        {
                            var w = bb.GetType().GetProperty("Width",
                                BindingFlags.Public | BindingFlags.Instance)?.GetValue(bb);
                            if (w is int iw && iw > 100) return iw;
                        }
                    }
                }
            }
        }
        catch { }

        // 2) Fallback: GetClientRect на главное окно процесса
        try
        {
            var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd != IntPtr.Zero)
            {
                RECT r;
                if (GetClientRect(hwnd, out r))
                {
                    int w = r.Right - r.Left;
                    if (w > 100) return w;
                }
            }
        }
        catch { }

        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    // Кеш имени найденного свойства/поля компонентов ResearchProject
    private static MemberInfo _cachedComponentMember = null;
    private static bool _componentMemberSearched = false;

    /// <summary>Считаем кол-во компонентов в ResearchProject через рефлексию (Components/Designs/Items...)</summary>
    private static int TryGetComponentCount(object project)
    {
        try
        {
            if (project == null) return 1;
            var type = project.GetType();

            if (!_componentMemberSearched)
            {
                _componentMemberSearched = true;
                // Ищем свойство или поле с типом ICollection и именем содержащим "Component"/"Design"
                string[] candidates = { "Components", "Designs", "AssociatedComponents", "ProjectComponents", "ComponentList", "Items" };
                foreach (var name in candidates)
                {
                    var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (prop != null && typeof(System.Collections.ICollection).IsAssignableFrom(prop.PropertyType))
                    { _cachedComponentMember = prop; ModInit.Log($"[DPD.Tier] using PROP {name} of {prop.PropertyType.Name}"); break; }
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field != null && typeof(System.Collections.ICollection).IsAssignableFrom(field.FieldType))
                    { _cachedComponentMember = field; ModInit.Log($"[DPD.Tier] using FIELD {name} of {field.FieldType.Name}"); break; }
                }
                if (_cachedComponentMember == null)
                {
                    ModInit.Log("[DPD.Tier] no Components/Designs member found on ResearchProject, dumping:");
                    foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                    {
                        if (m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                            ModInit.Log($"  {m.MemberType} {m.Name}");
                    }
                }
            }

            if (_cachedComponentMember is PropertyInfo p)
            {
                if (p.GetValue(project) is System.Collections.ICollection c) return Math.Max(1, c.Count);
            }
            else if (_cachedComponentMember is FieldInfo f)
            {
                if (f.GetValue(project) is System.Collections.ICollection c) return Math.Max(1, c.Count);
            }
        }
        catch (Exception ex) { ModInit.Log("[DPD.Tier] err: " + ex.Message); }
        return 1;
    }

    /// <summary>Финальный scale = clamp(width/BaseWidth, Min, Max) * userScale.</summary>
    private static float ComputeAdaptiveScale(object spriteBatch)
    {
        var cfg = ModInit.Config;
        float userScale = cfg.ProjectDetail_SizeXScale;
        if (!cfg.ProjectDetail_AdaptiveScale) return userScale;

        int w = TryGetViewportWidth(spriteBatch);
        if (w > 100) _cachedViewportWidth = w;
        if (_cachedViewportWidth <= 0) return userScale; // не смогли узнать — используем userScale как fallback

        float baseW = cfg.ProjectDetail_BaseWidth > 0 ? cfg.ProjectDetail_BaseWidth : 1920f;
        float ratio = _cachedViewportWidth / baseW;
        float adaptive = Math.Max(cfg.ProjectDetail_MinScale, Math.Min(cfg.ProjectDetail_MaxScale, ratio));

        if (_cachedViewportWidth != _lastLoggedWidth)
        {
            _lastLoggedWidth = _cachedViewportWidth;
            ModInit.Log($"[DPD.Adaptive] viewport={_cachedViewportWidth} base={baseW:F0} ratio={ratio:F2} adaptive={adaptive:F2} userScale={userScale:F2} final={adaptive*userScale:F2}");
        }
        return adaptive * userScale;
    }

    static void Prefix(MethodBase __originalMethod, object __instance, object[] __args)
    {
        try
        {
            if (!_projectSizeIndex.TryGetValue(__originalMethod, out int idx)) return;

            // Если это inline summary-превью в дереве — НЕ масштабируем (иначе сдвигаются соседи).
            // Только полный hover-тултип (isSummaryMode == false) расширяем.
            if (_summaryModeIndex.TryGetValue(__originalMethod, out int smIdx) && smIdx >= 0)
            {
                bool isSummary = (bool)__args[smIdx];
                if (isSummary) return;
            }

            // __args[0] = SpriteBatch, __args[1] = ResearchProject
            var sb = __args.Length > 0 ? __args[0] : null;
            var project = __args.Length > 1 ? __args[1] : null;
            var scale = ComputeAdaptiveScale(sb);
            var sz = (Vector2)__args[idx];

            // Tier-boost: считаем кол-во компонентов в проекте через рефлексию
            var cfg = ModInit.Config;
            if (cfg.ProjectDetail_TierBoost && project != null)
            {
                int componentCount = TryGetComponentCount(project);
                float boost = componentCount <= 1 ? cfg.ProjectDetail_TierSingleBoost : cfg.ProjectDetail_TierMultiBoost;
                scale *= boost;
            }
            int n;
            lock (_callCounts)
            {
                _callCounts.TryGetValue(__originalMethod, out n);
                if (n < 8) _callCounts[__originalMethod] = n + 1;
            }
            if (scale != 1f)
            {
                var newSz = new Vector2(sz.X * scale, sz.Y);
                __args[idx] = newSz;

                // ВРЕМЕННО расширить DetailDrawWidth — body внутри метода читает это поле для верстки.
                // Восстановим в Postfix, чтобы не повредить геометрию дерева.
                if (__instance != null)
                {
                    var fdw = __instance.GetType().GetField("DetailDrawWidth",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fdw != null && fdw.FieldType == typeof(float))
                    {
                        _savedDetailDrawWidth = (float)fdw.GetValue(__instance);
                        if (!float.IsInfinity(_savedDetailDrawWidth) && !float.IsNaN(_savedDetailDrawWidth) && _savedDetailDrawWidth > 0f)
                        {
                            fdw.SetValue(__instance, _savedDetailDrawWidth * scale);
                            _detailWidthSaved = true;
                        }
                    }
                }

                if (n < 8) ModInit.Log($"[DPD] {__originalMethod.Name}: {sz.X:F0}x{sz.Y:F0} -> {newSz.X:F0}x{newSz.Y:F0} (xX={scale}) DDW={_savedDetailDrawWidth:F0}->{_savedDetailDrawWidth*scale:F0}");
            }
            else if (n < 8)
            {
                ModInit.Log($"[DPD] {__originalMethod.Name}: {sz.X:F0}x{sz.Y:F0} (scale=1, no-op)");
            }
        }
        catch (Exception ex) { ModInit.Log("[DPD.Prefix] " + ex.Message); }
    }

    static void Postfix(object __instance)
    {
        try
        {
            if (_detailWidthSaved && __instance != null)
            {
                var fdw = __instance.GetType().GetField("DetailDrawWidth",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (fdw != null && fdw.FieldType == typeof(float))
                {
                    fdw.SetValue(__instance, _savedDetailDrawWidth);
                }
                _detailWidthSaved = false;
            }
        }
        catch (Exception ex) { ModInit.Log("[DPD.Postfix] " + ex.Message); }
    }
}
