namespace StarRailShaderEditor;

internal static class Theme
{
    public static readonly Color Canvas = Color.FromArgb(24, 25, 28);
    public static readonly Color Surface = Color.FromArgb(31, 32, 36);
    public static readonly Color SurfaceRaised = Color.FromArgb(39, 40, 45);
    public static readonly Color SurfaceHover = Color.FromArgb(48, 49, 55);
    public static readonly Color Border = Color.FromArgb(64, 66, 73);
    public static readonly Color BorderStrong = Color.FromArgb(91, 94, 102);
    public static readonly Color Text = Color.FromArgb(235, 234, 230);
    public static readonly Color TextMuted = Color.FromArgb(171, 171, 167);
    public static readonly Color Accent = Color.FromArgb(218, 112, 92);
    public static readonly Color AccentMuted = Color.FromArgb(95, 58, 52);
    public static readonly Color ModifiedLine = Color.FromArgb(58, 49, 34);
    public static readonly Color SelectedLine = Color.FromArgb(67, 42, 39);
    public static readonly Color Success = Color.FromArgb(111, 176, 134);
    public static readonly Color Warning = Color.FromArgb(218, 169, 91);
    public static readonly Color Error = Color.FromArgb(218, 101, 110);
    public static readonly Color NodeTexture = Color.FromArgb(91, 139, 133);
    public static readonly Color NodeLighting = Color.FromArgb(176, 126, 82);
    public static readonly Color NodeSurface = Color.FromArgb(183, 94, 82);
    public static readonly Color NodeEffect = Color.FromArgb(124, 109, 155);

    public const int Radius = 6;
    public const int Space1 = 4;
    public const int Space2 = 8;
    public const int Space3 = 12;
    public const int Space4 = 16;
    public const int Space6 = 24;

    public static readonly Font UiFont = new("Microsoft YaHei UI", 9f, FontStyle.Regular);
    public static readonly Font UiFontMedium = new("Microsoft YaHei UI", 9f, FontStyle.Bold);
    public static readonly Font HeadingFont = new("Microsoft YaHei UI", 12f, FontStyle.Bold);
    public static readonly Font MonoFont = new("Cascadia Mono", 9f, FontStyle.Regular);
    public static readonly Font MonoSmallFont = new("Cascadia Mono", 7.5f, FontStyle.Regular);
}
