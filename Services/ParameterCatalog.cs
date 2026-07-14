using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using StarRailShaderEditor.Models;

namespace StarRailShaderEditor.Services;

internal static class ParameterCatalog
{
    private static readonly ParameterManifest Manifest = LoadManifest();
    private static readonly Dictionary<string, string> GroupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["纹理"] = "纹理资源",
        ["材质域"] = "材质类型",
        ["基础颜色"] = "基础颜色",
        ["子材质索引"] = "子材质",
        ["漫反射"] = "卡通阴影",
        ["高光"] = "高光",
        ["丝袜"] = "丝袜材质",
        ["黑丝"] = "丝袜材质",
        ["边缘光"] = "边缘阴影",
        ["脸部"] = "脸部细节",
        ["脸"] = "脸部细节",
        ["眼睛"] = "眼睛效果",
        ["自发光"] = "自发光",
        ["星空"] = "星空效果",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MATERIAL_DOMAIN"] = "材质类型",
        ["BASE_COLOR_FROM"] = "基础颜色来源",
        ["BASE_ALPHA_FROM"] = "透明度来源",
        ["SUB_INDEX_FROM"] = "子材质来源",
        ["SUB_INDEX_SWIZZLE"] = "子材质通道",
        ["DIFFUSE_THRESHOLD_FROM"] = "阴影阈值来源",
        ["DIFFUSE_THRESHOLD_SWIZZLE"] = "阴影阈值通道",
        ["RAMP_COLOR_FROM"] = "Ramp 来源",
        ["SHADOW_MODE"] = "自阴影",
        ["SPECULAR_THRESHOLD_FROM"] = "高光阈值来源",
        ["SPECULAR_THRESHOLD_SWIZZLE"] = "高光阈值通道",
        ["STARRYSKY"] = "启用星空",
        ["EMISSIVE_COLOR_FROM"] = "自发光颜色来源",
        ["EMISSIVE_MASK_FROM"] = "自发光遮罩来源",
        ["EMISSIVE_MASK_SWIZZLE"] = "自发光遮罩通道",
        ["COLOR_TEXTURE"] = "颜色纹理",
        ["LIGHTMAP_TEXTURE"] = "光照贴图",
        ["FACEMAP_TEXTURE"] = "脸部贴图",
        ["STOCKINGS_TEXTURE"] = "丝袜纹理",
        ["RAMP_COOL_TEXTURE"] = "冷色 Ramp",
        ["RAMP_WARM_TEXTURE"] = "暖色 Ramp",
        ["EMISSIVE_TEXTURE"] = "自发光纹理",
        ["EYES_EFFECT_TEXTURE"] = "眼睛效果纹理",
        ["STARRYSKY_TEXTURE"] = "星空基础纹理",
        ["STARRYSKY_MASK_TEXTURE"] = "星空遮罩",
        ["STARRYSKY_STAR_TEXTURE"] = "星点纹理",
        ["STARRYSKY_STAR_MASK_TEXTURE"] = "星点噪声",
        ["ShadowRamp"] = "明暗分界",
        ["AlphaMultiplier"] = "透明度",
        ["EmissiveIntensity"] = "自发光强度",
        ["EmissiveGamma"] = "自发光 Gamma",
        ["RimShadowPower"] = "边缘范围",
        ["RimShadowIntensity"] = "边缘强度",
        ["StockingsPower"] = "丝袜曲线",
        ["StockingsRoughness"] = "丝袜粗糙度",
        ["StarDensity"] = "星点密度",
        ["StarMode"] = "星空模式",
        ["SkyStarTexScale"] = "星点亮度",
        ["SkyStarDepthScale"] = "星点景深",
        ["SkyFresnelScale"] = "星空边缘亮度",
    };

    public static string NormalizeGroup(string rawGroup)
    {
        var clean = rawGroup.Trim();
        foreach (var entry in GroupNames)
        {
            if (clean.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }
        return string.IsNullOrWhiteSpace(clean) ? "其他参数" : clean;
    }

    public static string DisplayName(string name)
    {
        if (Manifest.Labels.TryGetValue(name, out var manifestLabel))
        {
            return manifestLabel;
        }
        if (Labels.TryGetValue(name, out var label))
        {
            return label;
        }

        if (name.Contains('_', StringComparison.Ordinal) && name.All(character => !char.IsLetter(character) || char.IsUpper(character)))
            return DisplayMacroName(name);

        var materialMatch = Regex.Match(name, "^(.*?)([0-7])$");
        var suffix = string.Empty;
        if (materialMatch.Success)
        {
            name = materialMatch.Groups[1].Value;
            suffix = $" · 材质 {materialMatch.Groups[2].Value}";
        }

        if (name.EndsWith("MapST", StringComparison.OrdinalIgnoreCase))
            return DisplayName(name[..^5]) + " UV 缩放与平移" + suffix;
        if (name.EndsWith("MapSpeed", StringComparison.OrdinalIgnoreCase))
            return DisplayName(name[..^8]) + " UV 移动速度" + suffix;

        var spaced = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
        var replacements = new (string English, string Chinese)[]
        {
            ("Base Color", "基础颜色"), ("Specular", "高光"), ("Shallow", "浅影"),
            ("Shadow", "阴影"), ("Rim", "边缘"), ("Emissive", "自发光"),
            ("Stockings", "丝袜"), ("Nose Line", "鼻线"), ("Eyes", "眼睛"),
            ("Starry Sky", "星空"), ("Sky Star", "星点"), ("Sky", "星空"),
            ("Diffuse", "漫反射"), ("Threshold", "阈值"), ("Shininess", "光泽度"),
            ("Roughness", "粗糙度"), ("Intensity", "强度"), ("Thickness", "厚度"),
            ("Feather", "羽化"), ("Width", "宽度"), ("Range", "范围"),
            ("Offset", "偏移"), ("Speed", "速度"), ("Mask", "遮罩"),
            ("Color", "颜色"), ("Tint", "染色"), ("Const", "常量"),
            ("Animated", "动画"), ("Power", "曲线"), ("Density", "密度"),
            ("Scale", "缩放"), ("Mode", "模式"),
        };
        foreach (var replacement in replacements)
            spaced = spaced.Replace(replacement.English, replacement.Chinese, StringComparison.OrdinalIgnoreCase);
        return spaced.Trim() + suffix;
    }

    private static string DisplayMacroName(string name)
    {
        var words = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var translated = words.Select(word => word switch
        {
            "BASE" => "基础",
            "COLOR" => "颜色",
            "ALPHA" => "透明度",
            "FROM" => "来源",
            "SUB" => "子材质",
            "INDEX" => "索引",
            "SWIZZLE" => "通道",
            "DIFFUSE" => "漫反射",
            "THRESHOLD" => "阈值",
            "RAMP" => "Ramp",
            "SHADOW" => "阴影",
            "MODE" => "模式",
            "SPECULAR" => "高光",
            "EMISSIVE" => "自发光",
            "MASK" => "遮罩",
            "STARRYSKY" => "星空",
            "STAR" => "星点",
            "STOCKINGS" => "丝袜",
            "THICKNESS" => "厚度",
            "TILE" => "平铺",
            "FACE" => "脸部",
            "NOSE" => "鼻线",
            "LINE" => string.Empty,
            "MATERIAL" => "材质",
            "DOMAIN" => "类型",
            "MAP" => "贴图",
            "SIZE" => "尺寸",
            "COOL" => "冷色",
            "WARM" => "暖色",
            "TEXTURE" => "纹理",
            "A" => "A",
            "B" => "B",
            _ => word,
        }).Where(word => word.Length > 0);
        return string.Concat(translated);
    }

    public static ParameterDefinition? ResolveDefinition(string name, string group)
    {
        var rule = Manifest.Rules.FirstOrDefault(candidate => candidate.Regex.IsMatch(name));
        if (rule is null) return null;
        var resolvedGroup = string.IsNullOrWhiteSpace(rule.Group) ? group : rule.Group;
        var nodeId = string.IsNullOrWhiteSpace(rule.NodeId) ? NodeForGroup(resolvedGroup) : rule.NodeId;
        return new ParameterDefinition(rule.Pattern, resolvedGroup, nodeId, rule.Unit, rule.Advanced, rule.Components);
    }

    public static (double Min, double Max, double Step) RangeFor(ParameterDefinition? definition, double current)
    {
        if (definition?.Components.Count > 0)
        {
            var component = definition.Components[0];
            return (component.SoftMinimum, component.SoftMaximum, component.Step);
        }
        return (Math.Min(0, current), Math.Max(1, current), 0.01);
    }

    public static IReadOnlyList<ParameterOption> OptionsFor(string name)
    {
        if (name.Equals("MATERIAL_DOMAIN", StringComparison.OrdinalIgnoreCase))
            return [new("身体或头发", "0"), new("脸部", "1")];
        if (name.EndsWith("_SWIZZLE", StringComparison.OrdinalIgnoreCase))
            return [new("R 通道", "0"), new("G 通道", "1"), new("B 通道", "2"), new("A 通道", "3")];
        if (name.Equals("SHADOW_MODE", StringComparison.OrdinalIgnoreCase) || name.Equals("STARRYSKY", StringComparison.OrdinalIgnoreCase))
            return [new("关闭", "0"), new("启用", "1")];
        if (name.Equals("SHADOW_MAP_SIZE", StringComparison.OrdinalIgnoreCase))
            return [new("1024", "1024"), new("2048", "2048"), new("4096", "4096"), new("8192", "8192")];
        if (name.Equals("BASE_COLOR_FROM", StringComparison.OrdinalIgnoreCase) || name.Equals("EMISSIVE_COLOR_FROM", StringComparison.OrdinalIgnoreCase) || name.Equals("STARRYSKY_COLOR_FROM", StringComparison.OrdinalIgnoreCase))
            return [new("常量", "0"), new("PMX 材质", "1"), new("纹理", "2")];
        if (name.EndsWith("_FROM", StringComparison.OrdinalIgnoreCase))
            return [new("常量", "0"), new("纹理", "1")];
        return [];
    }

    public static bool IsAdvanced(string name, string group)
    {
        if (name.EndsWith("0", StringComparison.Ordinal) || Labels.ContainsKey(name) || name.EndsWith("TEXTURE", StringComparison.OrdinalIgnoreCase))
            return false;
        return name.Any(char.IsDigit) || name.Contains("MapST", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("MapSpeed", StringComparison.OrdinalIgnoreCase) || group == "其他参数";
    }

    private static string NodeForGroup(string group) => group switch
    {
        "纹理资源" => "textures",
        "基础颜色" => "base",
        "子材质" => "material",
        "卡通阴影" or "阴影设置" => "shadow",
        "高光" => "specular",
        "边缘阴影" => "rim",
        "脸部细节" or "眼睛效果" => "face",
        "丝袜材质" => "stockings",
        "自发光" => "emission",
        "星空效果" => "star",
        _ => "surface",
    };

    private static ParameterManifest LoadManifest()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("parameter-definitions.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("参数清单资源不可用。");
        var json = JsonSerializer.Deserialize<ParameterManifestJson>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("参数清单无法解析。");
        return new ParameterManifest(
            new Dictionary<string, string>(json.Labels, StringComparer.OrdinalIgnoreCase),
            json.Rules.Select(rule => new ParameterRule(
                rule.Pattern,
                new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                rule.Group,
                rule.NodeId,
                rule.Unit,
                rule.Advanced,
                rule.Components.Select(component => new ParameterComponentDefinition(
                    component.Label,
                    component.SoftMinimum,
                    component.SoftMaximum,
                    component.HardMinimum,
                    component.HardMaximum,
                    component.Step)).ToArray())).ToArray());
    }

    private sealed record ParameterManifest(Dictionary<string, string> Labels, IReadOnlyList<ParameterRule> Rules);
    private sealed record ParameterRule(string Pattern, Regex Regex, string Group, string NodeId, string Unit, bool Advanced,
        IReadOnlyList<ParameterComponentDefinition> Components);
    private sealed class ParameterManifestJson
    {
        public Dictionary<string, string> Labels { get; set; } = [];
        public List<ParameterRuleJson> Rules { get; set; } = [];
    }
    private sealed class ParameterRuleJson
    {
        public string Pattern { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public bool Advanced { get; set; }
        public List<ParameterComponentJson> Components { get; set; } = [];
    }
    private sealed class ParameterComponentJson
    {
        public string Label { get; set; } = string.Empty;
        public double SoftMinimum { get; set; }
        public double SoftMaximum { get; set; }
        public double HardMinimum { get; set; }
        public double HardMaximum { get; set; }
        public double Step { get; set; }
    }
}
