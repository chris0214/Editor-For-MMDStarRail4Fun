using StarRailShaderEditor.Models;

namespace StarRailShaderEditor.Services;

internal static class SelfTest
{
    public static int Run(string shaderDirectory)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"starrail-editor-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            TestExternalShaderDirectorySelection(shaderDirectory, temporaryRoot);
            var files = new[]
            {
                "sr_body.fx", "sr_face.fx", "sr_hair.fx",
                Path.Combine("Lohen", "Lohen_body .fx"), Path.Combine("Lohen", "Lohen_face.fx"), Path.Combine("Lohen", "Lohen_hair.fx"),
                "Shadow.fx", "Shadow_zbuffer.fx",
            };
            var total = 0;
            foreach (var file in files)
            {
                var path = Path.Combine(shaderDirectory, file);
                var document = FxDocument.Load(path);
                var expected = document.Kind == ShaderDocumentKind.Material ? 224 : document.Kind == ShaderDocumentKind.Shadow ? 2 : 1;
                Require(document.Parameters.Count == expected, $"{file} 应解析 {expected} 项，实际 {document.Parameters.Count} 项。");
                var missingMetadata = document.Parameters.Where(parameter => parameter.Definition is null).Select(parameter => parameter.Name).ToArray();
                Require(missingMetadata.Length == 0, $"{file} 缺少参数元数据：{string.Join(", ", missingMetadata)}");
                if (!document.GetText().Equals(document.OriginalText, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{file} 未修改时无法无损往返。");

                var parameter = document.Parameters.First(item => item.Kind is ShaderParameterKind.Float or ShaderParameterKind.Integer);
                var original = document.GetText();
                document.SetValue(parameter, parameter.Value == "0.5" ? "0.55" : "0.5");
                if (document.GetText().Equals(original, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{file} 参数写入没有生效。");

                var temporary = Path.Combine(temporaryRoot, Path.GetFileName(file));
                document.SaveAs(temporary);
                var reloaded = FxDocument.Load(temporary);
                if (reloaded.Find(parameter.Name)?.Value != parameter.Value)
                    throw new InvalidOperationException($"{file} 保存后重新解析的参数值不一致。");
                File.Delete(temporary);

                document.RestoreText(original);
                if (!document.GetText().Equals(original, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{file} 撤销恢复失败。");
                total += document.Parameters.Count;
            }

            Require(total == 1347, $"FX 可调实例应为 1347，实际为 {total}。");
            TestUvMetadata(shaderDirectory);
            TestRapidBackups(shaderDirectory, temporaryRoot);
            TestStrictExport(shaderDirectory, temporaryRoot);
            TestController(shaderDirectory, temporaryRoot);

            Console.WriteLine($"PASS: {files.Length} FX documents, {total} FX parameters, 18 controller morphs, 1365 editable instances.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: " + exception);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void TestExternalShaderDirectorySelection(string shaderDirectory, string temporaryRoot)
    {
        Require(Program.TryNormalizeShaderDirectory(shaderDirectory, out var direct) &&
                Path.GetFullPath(direct).Equals(Path.GetFullPath(shaderDirectory), StringComparison.OrdinalIgnoreCase),
            "直接选择 Shader 根目录时无法识别 sr_body.fx。");

        var parent = Directory.GetParent(shaderDirectory)?.FullName;
        if (parent is not null && Directory.Exists(Path.Combine(parent, "MMDStarRail4Fun")))
        {
            Require(Program.TryNormalizeShaderDirectory(parent, out var nested) &&
                    Path.GetFullPath(nested).Equals(Path.GetFullPath(shaderDirectory), StringComparison.OrdinalIgnoreCase),
                "选择资源包上一级目录时无法识别 MMDStarRail4Fun 子目录。");
        }

        var empty = Path.Combine(temporaryRoot, "not-a-shader-folder");
        Directory.CreateDirectory(empty);
        Require(!Program.TryNormalizeShaderDirectory(empty, out _), "不含 sr_body.fx 的目录不应通过验证。");
    }

    private static void TestUvMetadata(string shaderDirectory)
    {
        var document = FxDocument.Load(Path.Combine(shaderDirectory, "sr_body.fx"));
        var uv = document.Find("BaseColorMapST") ?? throw new InvalidOperationException("找不到 BaseColorMapST。");
        Require(uv.ComponentAt(0).HardMaximum > 1, "BaseColorMapST 缩放必须允许大于 1。");
        Require(uv.ComponentAt(2).HardMinimum < 0, "BaseColorMapST 偏移必须允许负值。");
        var color = document.Find("BaseColorTint0") ?? throw new InvalidOperationException("找不到 BaseColorTint0。");
        Require(color.ComponentAt(0).HardMinimum == 0 && color.ComponentAt(0).HardMaximum == 1,
            "颜色分量必须限制为 0–1。");
    }

    private static void TestRapidBackups(string shaderDirectory, string temporaryRoot)
    {
        var directory = Path.Combine(temporaryRoot, "rapid-save");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "sr_body.fx");
        File.Copy(Path.Combine(shaderDirectory, "sr_body.fx"), target);
        var document = FxDocument.Load(target);
        var parameter = document.Find("AlphaMultiplier") ?? throw new InvalidOperationException("找不到 AlphaMultiplier。");
        for (var index = 0; index < 10; index++)
        {
            document.SetValue(parameter, (0.5 + index * 0.01).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            document.Save(createBackup: true);
        }
        var backups = Directory.GetFiles(Path.Combine(directory, ".starrail-editor-backups"), "*.fx");
        Require(backups.Length == 10 && backups.Select(Path.GetFileName).Distinct().Count() == 10,
            "连续保存必须生成 10 个唯一备份。");
    }

    private static void TestStrictExport(string shaderDirectory, string temporaryRoot)
    {
        var documents = new[] { "sr_body.fx", "sr_face.fx", "sr_hair.fx" }
            .Select(file => FxDocument.Load(Path.Combine(shaderDirectory, file))).ToArray();
        var output = Path.Combine(temporaryRoot, "must-not-exist");
        var result = PortableExporter.Export(shaderDirectory, documents, output);
        Require(!result.Success && result.Issues.Count > 0, "样本缺失纹理时导出必须失败并返回问题清单。");
        Require(!Directory.Exists(output), "预检失败时不得创建最终或 staging 导出目录。");
    }

    private static void TestController(string shaderDirectory, string temporaryRoot)
    {
        var model = PmxControllerReader.Read(Path.Combine(shaderDirectory, "fun_controller.pmx"));
        Require(model.Morphs.Count == 18, $"fun_controller.pmx 应有 18 个 Morph，实际 {model.Morphs.Count}。 ");
        var weights = model.Morphs.ToDictionary(morph => morph.Name, _ => 0.25f, StringComparer.Ordinal);
        var vmd = Path.Combine(temporaryRoot, "controller.vmd");
        VmdMorphWriter.Write(vmd, model, weights);
        using var stream = File.OpenRead(vmd);
        using var reader = new BinaryReader(stream);
        Require(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(30)).TrimEnd('\0') == "Vocaloid Motion Data 0002",
            "VMD 文件头无效。");
        _ = reader.ReadBytes(20);
        Require(reader.ReadUInt32() == 0, "控制器 VMD 不应包含骨骼帧。");
        Require(reader.ReadUInt32() == 18, "控制器 VMD 必须写入 18 个 Morph 帧。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
