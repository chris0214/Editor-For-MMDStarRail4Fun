using System.Text;
using StarRailShaderEditor.Models;

namespace StarRailShaderEditor.Services;

internal static class PmxControllerReader
{
    public static ControllerModel Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "PMX ")
            throw new InvalidDataException("控制器不是有效的 PMX 文件。");
        var version = reader.ReadSingle();
        if (version < 2.0f || version >= 2.2f)
            throw new InvalidDataException($"不支持 PMX {version:0.0}，仅支持 2.0/2.1。");

        var headerSize = reader.ReadByte();
        var header = reader.ReadBytes(headerSize);
        if (header.Length < 8) throw new InvalidDataException("PMX 文件头不完整。");
        var context = new PmxContext(
            header[0] == 0 ? Encoding.Unicode : new UTF8Encoding(false, true),
            header[1], header[2], header[3], header[4], header[5], header[6], header[7]);

        var name = ReadText(reader, context.Encoding);
        var englishName = ReadText(reader, context.Encoding);
        _ = ReadText(reader, context.Encoding);
        _ = ReadText(reader, context.Encoding);

        SkipVertices(reader, context);
        SkipBytes(reader, checked(reader.ReadInt32() * context.VertexIndexSize));
        SkipTextArray(reader, context.Encoding);
        SkipMaterials(reader, context);
        SkipBones(reader, context);

        var morphCount = ReadCount(reader, "Morph");
        var morphs = new List<ControllerMorph>(morphCount);
        for (var index = 0; index < morphCount; index++)
        {
            var morphName = ReadText(reader, context.Encoding);
            var morphEnglishName = ReadText(reader, context.Encoding);
            var panel = reader.ReadByte();
            var type = reader.ReadByte();
            var offsetCount = ReadCount(reader, "Morph offset");
            morphs.Add(new ControllerMorph(morphName, morphEnglishName, panel, type));
            SkipMorphOffsets(reader, context, type, offsetCount);
        }

        return new ControllerModel(name, englishName, morphs);
    }

    private static void SkipVertices(BinaryReader reader, PmxContext context)
    {
        var count = ReadCount(reader, "vertex");
        for (var index = 0; index < count; index++)
        {
            SkipBytes(reader, 32 + context.AdditionalUvCount * 16);
            var deform = reader.ReadByte();
            switch (deform)
            {
                case 0: SkipBytes(reader, context.BoneIndexSize); break;
                case 1: SkipBytes(reader, context.BoneIndexSize * 2 + 4); break;
                case 2:
                case 4: SkipBytes(reader, context.BoneIndexSize * 4 + 16); break;
                case 3: SkipBytes(reader, context.BoneIndexSize * 2 + 4 + 36); break;
                default: throw new InvalidDataException($"未知 PMX 顶点权重类型：{deform}。");
            }
            SkipBytes(reader, 4);
        }
    }

    private static void SkipMaterials(BinaryReader reader, PmxContext context)
    {
        var count = ReadCount(reader, "material");
        for (var index = 0; index < count; index++)
        {
            _ = ReadText(reader, context.Encoding);
            _ = ReadText(reader, context.Encoding);
            SkipBytes(reader, 65 + context.TextureIndexSize * 2);
            _ = reader.ReadByte();
            var sharedToon = reader.ReadByte();
            SkipBytes(reader, sharedToon == 0 ? context.TextureIndexSize : 1);
            _ = ReadText(reader, context.Encoding);
            SkipBytes(reader, 4);
        }
    }

    private static void SkipBones(BinaryReader reader, PmxContext context)
    {
        var count = ReadCount(reader, "bone");
        for (var index = 0; index < count; index++)
        {
            _ = ReadText(reader, context.Encoding);
            _ = ReadText(reader, context.Encoding);
            SkipBytes(reader, 12 + context.BoneIndexSize + 4);
            var flags = reader.ReadUInt16();
            SkipBytes(reader, (flags & 0x0001) != 0 ? context.BoneIndexSize : 12);
            if ((flags & 0x0300) != 0) SkipBytes(reader, context.BoneIndexSize + 4);
            if ((flags & 0x0400) != 0) SkipBytes(reader, 12);
            if ((flags & 0x0800) != 0) SkipBytes(reader, 24);
            if ((flags & 0x2000) != 0) SkipBytes(reader, 4);
            if ((flags & 0x0020) == 0) continue;
            SkipBytes(reader, context.BoneIndexSize + 8);
            var linkCount = ReadCount(reader, "IK link");
            for (var link = 0; link < linkCount; link++)
            {
                SkipBytes(reader, context.BoneIndexSize);
                if (reader.ReadByte() != 0) SkipBytes(reader, 24);
            }
        }
    }

    private static void SkipMorphOffsets(BinaryReader reader, PmxContext context, byte type, int count)
    {
        var size = type switch
        {
            0 or 9 => context.MorphIndexSize + 4,
            1 => context.VertexIndexSize + 12,
            2 => context.BoneIndexSize + 28,
            >= 3 and <= 7 => context.VertexIndexSize + 16,
            8 => context.MaterialIndexSize + 113,
            10 => context.RigidBodyIndexSize + 25,
            _ => throw new InvalidDataException($"未知 PMX Morph 类型：{type}。"),
        };
        SkipBytes(reader, checked(size * count));
    }

    private static void SkipTextArray(BinaryReader reader, Encoding encoding)
    {
        var count = ReadCount(reader, "text");
        for (var index = 0; index < count; index++) _ = ReadText(reader, encoding);
    }

    private static string ReadText(BinaryReader reader, Encoding encoding)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > 16 * 1024 * 1024) throw new InvalidDataException("PMX 文本长度无效。");
        return encoding.GetString(reader.ReadBytes(length));
    }

    private static int ReadCount(BinaryReader reader, string section)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 10_000_000) throw new InvalidDataException($"PMX {section} 数量无效。");
        return count;
    }

    private static void SkipBytes(BinaryReader reader, int count)
    {
        if (count < 0 || reader.BaseStream.Seek(count, SeekOrigin.Current) > reader.BaseStream.Length)
            throw new EndOfStreamException("PMX 数据提前结束。");
    }

    private sealed record PmxContext(Encoding Encoding, byte AdditionalUvCount, byte VertexIndexSize,
        byte TextureIndexSize, byte MaterialIndexSize, byte BoneIndexSize, byte MorphIndexSize, byte RigidBodyIndexSize);
}
