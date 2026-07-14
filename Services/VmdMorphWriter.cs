using System.Text;
using System.Text.Json;
using StarRailShaderEditor.Models;

namespace StarRailShaderEditor.Services;

internal static class VmdMorphWriter
{
    public static void Write(string path, ControllerModel model, IReadOnlyDictionary<string, float> weights)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var shiftJis = Encoding.GetEncoding(932, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        WriteFixed(writer, "Vocaloid Motion Data 0002", 30, Encoding.ASCII);
        WriteFixed(writer, model.Name, 20, shiftJis);
        writer.Write(0u);
        writer.Write((uint)model.Morphs.Count);
        foreach (var morph in model.Morphs)
        {
            WriteFixed(writer, morph.Name, 15, shiftJis);
            writer.Write(0u);
            writer.Write(Math.Clamp(weights.GetValueOrDefault(morph.Name), 0f, 1f));
        }
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    public static void SavePreset(string path, ControllerPreset preset)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static ControllerPreset LoadPreset(string path) =>
        JsonSerializer.Deserialize<ControllerPreset>(File.ReadAllText(path)) ?? throw new InvalidDataException("控制器预设无效。");

    private static void WriteFixed(BinaryWriter writer, string value, int byteLength, Encoding encoding)
    {
        var buffer = new byte[byteLength];
        var offset = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var encoded = encoding.GetBytes(rune.ToString());
            if (offset + encoded.Length > buffer.Length) break;
            encoded.CopyTo(buffer, offset);
            offset += encoded.Length;
        }
        writer.Write(buffer);
    }
}
