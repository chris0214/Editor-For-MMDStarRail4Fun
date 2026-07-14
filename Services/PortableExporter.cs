namespace StarRailShaderEditor.Services;

internal sealed record PortableExportIssue(string Document, string Parameter, string Path);
internal sealed record PortableExportResult(bool Success, string OutputPath, IReadOnlyList<PortableExportIssue> Issues);

internal static class PortableExporter
{
    public static IReadOnlyList<PortableExportIssue> Preflight(string shaderDirectory, IEnumerable<FxDocument> documents)
    {
        return documents.Where(document => document.Kind == Models.ShaderDocumentKind.Material)
            .SelectMany(document => document.Parameters
                .Where(parameter => parameter.Kind == Models.ShaderParameterKind.Texture && parameter.IsEnabled)
                .Select(parameter => new
                {
                    Document = document,
                    Parameter = parameter,
                    Path = FxDocument.ResolveResourcePath(parameter.Value, shaderDirectory)
                }))
            .Where(item => !File.Exists(item.Path))
            .Select(item => new PortableExportIssue(item.Document.Name, item.Parameter.Name, item.Path))
            .ToArray();
    }

    public static PortableExportResult Export(string shaderDirectory, IEnumerable<FxDocument> sourceDocuments, string outputParent)
    {
        var documents = sourceDocuments.Distinct().ToArray();
        var issues = Preflight(shaderDirectory, documents);
        if (issues.Count > 0) return new PortableExportResult(false, string.Empty, issues);

        outputParent = Path.GetFullPath(outputParent);
        Directory.CreateDirectory(outputParent);
        var staging = Path.Combine(outputParent, $".starrail-export-{Guid.NewGuid():N}");
        var final = UniqueFinalDirectory(outputParent);
        Directory.CreateDirectory(staging);
        try
        {
            var textureOutput = Path.Combine(staging, "Texture2D");
            Directory.CreateDirectory(textureOutput);

            foreach (var sourceDocument in documents.Where(document => document.Kind != Models.ShaderDocumentKind.Reference))
            {
                var relativeDocument = Path.GetRelativePath(shaderDirectory, sourceDocument.FilePath);
                if (relativeDocument.StartsWith("..", StringComparison.Ordinal))
                    relativeDocument = Path.GetFileName(sourceDocument.FilePath);
                var outputDocument = Path.Combine(staging, relativeDocument);
                Directory.CreateDirectory(Path.GetDirectoryName(outputDocument)!);

                var portable = FxDocument.Load(sourceDocument.FilePath);
                portable.RestoreText(sourceDocument.GetText());
                foreach (var texture in portable.Parameters.Where(parameter =>
                             parameter.Kind == Models.ShaderParameterKind.Texture && parameter.IsEnabled))
                {
                    var source = FxDocument.ResolveResourcePath(texture.Value, shaderDirectory);
                    var destination = UniqueDestination(textureOutput, Path.GetFileName(source), source);
                    if (!File.Exists(destination)) File.Copy(source, destination);
                    var relativeTexture = Path.GetRelativePath(Path.GetDirectoryName(outputDocument)!, destination).Replace('\\', '/');
                    portable.SetValue(texture, relativeTexture);
                }
                portable.SaveAs(outputDocument);
            }

            CopyDirectoryIfPresent(Path.Combine(shaderDirectory, "internal"), Path.Combine(staging, "internal"));
            foreach (var file in new[] { "Shadow.x", "effect.png", "fun_controller.pmx" })
            {
                var source = Path.Combine(shaderDirectory, file);
                if (File.Exists(source)) File.Copy(source, Path.Combine(staging, file), overwrite: true);
            }

            Directory.Move(staging, final);
            return new PortableExportResult(true, final, []);
        }
        catch
        {
            if (Directory.Exists(staging) && Path.GetDirectoryName(staging)!.Equals(outputParent, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static string UniqueFinalDirectory(string outputParent)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = Path.Combine(outputParent, $"MMDStarRail4Fun-Portable-{stamp}");
        for (var suffix = 2; Directory.Exists(candidate); suffix++)
            candidate = Path.Combine(outputParent, $"MMDStarRail4Fun-Portable-{stamp}-{suffix}");
        return candidate;
    }

    private static string UniqueDestination(string directory, string fileName, string source)
    {
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination) || FilesEqual(destination, source)) return destination;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; ; index++)
        {
            destination = Path.Combine(directory, $"{stem}-{index}{extension}");
            if (!File.Exists(destination) || FilesEqual(destination, source)) return destination;
        }
    }

    private static bool FilesEqual(string first, string second)
    {
        var a = new FileInfo(first);
        var b = new FileInfo(second);
        if (a.Length != b.Length) return false;
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        var left = new byte[8192];
        var right = new byte[8192];
        while (true)
        {
            var leftCount = firstStream.Read(left);
            var rightCount = secondStream.Read(right);
            if (leftCount != rightCount) return false;
            if (leftCount == 0) return true;
            if (!left.AsSpan(0, leftCount).SequenceEqual(right.AsSpan(0, rightCount))) return false;
        }
    }

    private static void CopyDirectoryIfPresent(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectoryIfPresent(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
