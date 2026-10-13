using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DistantWorlds.Types;
using DistantWorlds.UI;
using HarmonyLib;
using Stride.Core.Mathematics;
using Stride.Graphics;

namespace RussianUiMod;

/// <summary>supporters.json next to the DLL: the link and the list shown in the «Экипаж» window.</summary>
public sealed class SupportersFile
{
    public string? Boosty { get; set; }
    public List<SupporterTier>? Tiers { get; set; }
}

public sealed class SupporterTier
{
    public string? Title { get; set; }
    public string? Color { get; set; }
    public List<string>? Names { get; set; }
}

/// <summary>
/// A panel about the author in the main menu: a picture, a few lines, a link to Boosty and the «Экипаж»
/// window with the people who support the mod. The list changes between releases, so it is read from
/// supporters.json in the mod folder. Built from the game's own controls (DWPanel, DWLabel, DWButton).
/// </summary>
public static class AuthorPanel
{
    private const string Header = "// CFLABS - РУСИФИКАЦИЯ DW2";
    private const string Body = "Мод живёт благодаря вашей поддержке.";
    private const string CrewNote = "Подписывайся на Boosty! Голосуй за проекты, присылай баг-репорты - автор рад любой помощи :)";
    private const string PostScript = "P.S. Разработкой модификации занимается один человек. Удачи, капитан!";
    private const string CrewHeader = "// БОРТОВОЙ МАНИФЕСТ ЭКИПАЖА";
    private const string CrewSubHeader = "те, благодаря кому мод вообще существует";
    private const string CrewThanks = "Спасибо каждому из вас - без экипажа этот корабль не взлетел бы.";
    private const string DefaultLink = "https://boosty.to/coreforgelabs";

    private static readonly Color Accent = new Color(25, 200, 255, 255);
    private static readonly Color Dim = new Color(170, 180, 190, 255);
    private static readonly Color PanelBack = new Color(10, 14, 20, 232);
    private static readonly Color CrewBack = new Color(8, 11, 16, 248);

    private static DWPanel? _attachedTo;
    private static DWPanel? _crew;
    private static Texture? _picture;
    private static bool _pictureTried;
    private static DWButton? _boostyButton, _crewButton, _closeButton;
    private static string _link = DefaultLink;

    private static float S(float v) => UserInterfaceHelper.CalculateScaledValue(v);

    private static float TextHeight(string text, FontSize size, bool bold, float width) =>
        DrawingHelper.MeasureStringDropShadowWordWrapWithSize(text, size, bold, width).Y;

    private static DWLabel Label(DWPanel parent, string name, string text, float x, float y, float width, Color color, bool bold, FontSize size, TextAlignment align = TextAlignment.Left) =>
        DWLabel.Generate(text, name, new Vector2(x, y), new Vector2(width, 0f), 0f, color, bold, size, align, parent, null, Side.Undefined);

    private static DWPanel Panel(string name, Vector2 size, Vector2 position, Color back)
    {
        var panel = new DWPanel { Name = name };
        panel.SetSizeAndPosition(size, position);
        panel.BackColor = back;
        panel.Visible = true;
        panel.UseRenderedBorder = true;
        panel.SetFeatureDefaults();
        return panel;
    }

    private static SupportersFile LoadSupporters()
    {
        try
        {
            string path = Path.Combine(ModInit.ModDir, "supporters.json");
            if (File.Exists(path))
                return JsonSerializer.Deserialize<SupportersFile>(File.ReadAllText(path),
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                       ?? new SupportersFile();
            ModInit.Log("AuthorPanel: supporters.json not found in " + ModInit.ModDir);
        }
        catch (Exception ex)
        {
            ModInit.Log("AuthorPanel: supporters.json is not readable: " + ex.Message);
        }
        return new SupportersFile();
    }

    private static Texture? LoadPicture(GraphicsDevice? device)
    {
        if (_pictureTried || device == null) return _picture;
        _pictureTried = true;
        try
        {
            string path = Path.Combine(ModInit.ModDir, "cflabs_astronaut.png");
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            _picture = Texture.Load(device, stream, TextureFlags.ShaderResource, GraphicsResourceUsage.Immutable, loadAsSRGB: true);
        }
        catch (Exception ex)
        {
            ModInit.Log("AuthorPanel: picture not loaded: " + ex.Message);
        }
        return _picture;
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (hex != null && hex.Length == 7 && hex[0] == '#')
                return new Color(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), (byte)255);
        }
        catch
        {
        }
        return fallback;
    }

    private static void Button(ref DWButton? button, DWPanel parent, string name, string text, float x, float y, float width, float height, EventHandler<DWEventArgs> click)
    {
        DWButton b = button ?? new DWButton();
        DWControl.SetupButton(ref b, name, new Vector2(width, height), new Vector2(x, y), text, click, false);
        b.Visible = true;
        parent.AddControl(b);
        button = b;
    }

    private static void OpenLink()
    {
        try
        {
            // the address comes from a file: only a web link is ever handed to the shell
            if (!_link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            Process.Start(new ProcessStartInfo { FileName = _link, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ModInit.Log("AuthorPanel: link not opened: " + ex.Message);
        }
    }

    public static void Attach(object menuRenderer)
    {
        try
        {
            var menu = Traverse.Create(menuRenderer);
            var menuPanel = menu.Field("_MenuPanel").GetValue<DWPanel>();
            if (menuPanel == null || ReferenceEquals(menuPanel, _attachedTo)) return;     // the menu is rebuilt on a resolution change
            var screen = menu.Field("_ScreenSize").GetValue<Vector2>();
            var logo = menu.Field("_LogoPanel").GetValue<DWPanel>();
            SupportersFile data = LoadSupporters();
            if (!string.IsNullOrWhiteSpace(data.Boosty)) _link = data.Boosty!.Trim();
            Texture? picture = LoadPicture(logo?.BackgroundImage?.GraphicsDevice);

            // --- the plaque, always whole (picture, title, the line about the mod, the call to join, buttons): right under
            // the column of menu buttons, as wide as the column; when the column leaves too little room below it (a big
            // UI scale on a small screen) the same plaque goes beside the column, at the bottom of the screen, as far
            // from the lines with the game version in the bottom right corner as the screen allows. It never covers
            // the menu buttons.
            var column = menu.Field("_ButtonPanel").GetValue<DWPanel>();
            float pad = S(10f);
            float gap = S(6f);
            float buttonH = S(30f);
            float left = column != null ? column.Position.X : S(20f);
            float width = column != null ? column.Size.X : S(480f);
            float top = column != null ? column.Position.Y + column.Size.Y + S(10f) : screen.Y - S(150f);
            if (width < S(260f) || width > screen.X - left) return;                        // no room on this screen
            float pictureSize = picture != null ? S(76f) : 0f;
            float pictureHeight = picture != null ? pictureSize * picture.Height / Math.Max(1, picture.Width) : 0f;
            bool showPicture = picture != null, showHeader = true, showBody = true, showNote = true;
            float textX = 0f, textWidth = 0f, headerH = 0f, bodyH = 0f, noteH = 0f;
            float Measure()
            {
                textX = pad + (showPicture ? pictureSize + pad : 0f);
                textWidth = width - textX - pad;
                headerH = showHeader ? TextHeight(Header, FontSize.Small, true, textWidth) : 0f;
                bodyH = showBody ? TextHeight(Body, FontSize.Small, false, textWidth) : 0f;
                noteH = showNote ? TextHeight(CrewNote, FontSize.Small, false, textWidth) : 0f;
                float text = pad + (showHeader ? headerH + gap : 0f) + (showBody ? bodyH + gap : 0f) + (showNote ? noteH + gap : 0f) + buttonH;
                return Math.Max(text, showPicture ? pad + pictureHeight : 0f) + pad;
            }
            float below = screen.Y - top - S(8f);                                           // room under the column
            var position = new Vector2(left, top);
            float height = Measure();
            if (height > below)
            {
                left = left + width + S(10f);
                width = Math.Min(S(480f), Math.Max(S(300f), screen.X - S(330f) - left));
                width = Math.Min(width, screen.X - left - S(20f));
                if (width < S(260f)) return;
                height = Measure();
                position = new Vector2(left, screen.Y - height - S(8f));
            }

            DWPanel panel = Panel("CFLabsAuthorPanel", new Vector2(width, height), position, PanelBack);
            if (showPicture)
            {
                var image = new DWPanel { Name = "CFLabsAuthorPicture" };
                image.SetSizeAndPosition(new Vector2(pictureSize, pictureHeight), new Vector2(pad, 0.5f * (height - pictureHeight)));
                image.BackColor = Color.Transparent;
                image.BackgroundImage = picture;
                image.BackgroundImageFill = ImageFill.Stretch;
                image.Visible = true;
                panel.AddControl(image);
            }
            float y = pad;
            if (showHeader)
            {
                Label(panel, "CFLabsAuthorHeader", Header, textX, y, textWidth, Accent, true, FontSize.Small);
                y += headerH + gap;
            }
            if (showBody)
            {
                Label(panel, "CFLabsAuthorBody", Body, textX, y, textWidth, Color.White, false, FontSize.Small);
                y += bodyH + gap;
            }
            if (showNote)
            {
                Label(panel, "CFLabsAuthorNote", CrewNote, textX, y, textWidth, Color.White, false, FontSize.Small);
                y += noteH + gap;
            }
            float boostyW = Math.Min(S(260f), textWidth * 0.64f);
            Button(ref _boostyButton, panel, "CFLabsBoosty", "ПОДДЕРЖАТЬ НА BOOSTY", textX, y, boostyW, buttonH, (_, _) => OpenLink());
            Button(ref _crewButton, panel, "CFLabsCrew", "ЭКИПАЖ", textX + boostyW + gap, y, Math.Min(S(140f), textWidth - boostyW - gap), buttonH,
                (_, _) => { if (_crew != null) _crew.Visible = !_crew.Visible; });
            menuPanel.AddControl(panel);

            // --- the «Экипаж» window, over the middle of the screen
            float crewW = Math.Min(S(620f), screen.X - S(80f));
            float inner = crewW - pad * 2f;
            var tiers = (data.Tiers ?? new List<SupporterTier>()).Where(t => t?.Names != null && t.Names.Count > 0).ToList();
            float crewH = pad + TextHeight(CrewHeader, FontSize.Large, true, inner) + S(4f) + TextHeight(CrewSubHeader, FontSize.Small, false, inner) + gap * 2f;
            foreach (var t in tiers)
                crewH += TextHeight(t.Title ?? "", FontSize.Normal, true, inner) + S(2f) + TextHeight(string.Join(", ", t.Names!), FontSize.Normal, false, inner) + gap * 1.5f;
            crewH += TextHeight(CrewNote, FontSize.Small, false, inner) + gap + TextHeight(PostScript, FontSize.Small, false, inner) + gap * 1.5f;
            crewH += TextHeight(CrewThanks, FontSize.Small, false, inner) + gap * 2f + buttonH + pad;
            DWPanel crew = Panel("CFLabsCrewPanel", new Vector2(crewW, crewH), new Vector2(0.5f * (screen.X - crewW), 0.5f * (screen.Y - crewH)), CrewBack);
            y = pad;
            Label(crew, "CFLabsCrewHeader", CrewHeader, pad, y, inner, Accent, true, FontSize.Large);
            y += TextHeight(CrewHeader, FontSize.Large, true, inner) + S(4f);
            Label(crew, "CFLabsCrewSub", CrewSubHeader, pad, y, inner, Dim, false, FontSize.Small);
            y += TextHeight(CrewSubHeader, FontSize.Small, false, inner) + gap * 2f;
            int n = 0;
            foreach (var t in tiers)
            {
                string names = string.Join(", ", t.Names!);
                Label(crew, "CFLabsTier" + n, t.Title ?? "", pad, y, inner, ParseColor(t.Color, Accent), true, FontSize.Normal);
                y += TextHeight(t.Title ?? "", FontSize.Normal, true, inner) + S(2f);
                Label(crew, "CFLabsTierNames" + n, names, pad, y, inner, Color.White, false, FontSize.Normal);
                y += TextHeight(names, FontSize.Normal, false, inner) + gap * 1.5f;
                n++;
            }
            Label(crew, "CFLabsCrewNote", CrewNote, pad, y, inner, Color.White, false, FontSize.Small);
            y += TextHeight(CrewNote, FontSize.Small, false, inner) + gap;
            Label(crew, "CFLabsCrewPs", PostScript, pad, y, inner, Dim, false, FontSize.Small);
            y += TextHeight(PostScript, FontSize.Small, false, inner) + gap * 1.5f;
            Label(crew, "CFLabsCrewThanks", CrewThanks, pad, y, inner, Dim, false, FontSize.Small);
            y += TextHeight(CrewThanks, FontSize.Small, false, inner) + gap * 2f;
            Button(ref _closeButton, crew, "CFLabsCrewClose", "ЗАКРЫТЬ", 0.5f * (crewW - S(160f)), y, S(160f), buttonH, (_, _) => { if (_crew != null) _crew.Visible = false; });
            crew.Visible = false;
            menuPanel.AddControl(crew);

            _crew = crew;
            _attachedTo = menuPanel;
            ModInit.Log("AuthorPanel: attached, picture=" + showPicture + ", header=" + showHeader + ", body=" + showBody + ", note=" + showNote + ", tiers=" + tiers.Count + ", room=" + (int)below + ", size=" + (int)width + "x" + (int)height);
        }
        catch (Exception ex)
        {
            ModInit.Log("AuthorPanel failed: " + ex);
        }
    }
}

[HarmonyPatch]
public static class Patch_MenuAuthorPanel
{
    static bool Prepare() => ModInit.Config.Enable_AuthorPanel && AccessTools.TypeByName("DistantWorlds2.MenuRenderer") != null;

    static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("DistantWorlds2.MenuRenderer"), "SetupUserInterfaceControls");

    static void Postfix(object __instance) => AuthorPanel.Attach(__instance);
}
