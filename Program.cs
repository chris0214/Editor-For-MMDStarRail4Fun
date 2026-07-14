using StarRailShaderEditor.Services;

namespace StarRailShaderEditor;

internal static class Program
{
    private static string? _startupLogPath;

    [STAThread]
    private static int Main(string[] args)
    {
        _startupLogPath = ReadStringArgument(args, "--startup-log");
        TraceStartup($"Process started. Args: {string.Join(" ", args)}");
        var shaderDirectory = FindShaderDirectoryArgument(args);
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            if (shaderDirectory is null)
            {
                Console.Error.WriteLine("--self-test requires --root <MMDStarRail4Fun folder>.");
                return 2;
            }
            return SelfTest.Run(shaderDirectory);
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => TraceStartup("UI exception: " + eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => TraceStartup("Unhandled exception: " + eventArgs.ExceptionObject);
        TraceStartup("WinForms initialized.");
        shaderDirectory ??= PromptForShaderDirectory();
        if (shaderDirectory is null) return 0;
        TraceStartup($"Shader directory: {shaderDirectory}");
        if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            using var smokeForm = new MainForm(shaderDirectory) { Size = new Size(1440, 900) };
            smokeForm.Show();
            Application.DoEvents();
            Console.WriteLine(smokeForm.RunUiSmokeTests());
            smokeForm.Hide();
            return 0;
        }
        if (args.Contains("--benchmark", StringComparer.OrdinalIgnoreCase))
        {
            using var benchmarkForm = new MainForm(shaderDirectory) { Size = new Size(1440, 900) };
            benchmarkForm.Show();
            Application.DoEvents();
            Console.WriteLine(benchmarkForm.RunBenchmarks());
            benchmarkForm.Hide();
            return 0;
        }
        var screenshotIndex = Array.FindIndex(args, value => value.Equals("--screenshot", StringComparison.OrdinalIgnoreCase));
        if (screenshotIndex >= 0 && screenshotIndex + 1 < args.Length)
        {
            var width = ReadIntArgument(args, "--width", 1440);
            var height = ReadIntArgument(args, "--height", 900);
            using var form = new MainForm(shaderDirectory) { Size = new Size(width, height) };
            var materialIndex = Array.FindIndex(args, value => value.Equals("--material", StringComparison.OrdinalIgnoreCase));
            var material = materialIndex >= 0 && materialIndex + 1 < args.Length ? args[materialIndex + 1] : "body";
            form.SetVerificationState(material, args.Contains("--advanced", StringComparer.OrdinalIgnoreCase),
                args.Contains("--source", StringComparer.OrdinalIgnoreCase));
            var changeIndex = Array.FindIndex(args, value => value.Equals("--change", StringComparison.OrdinalIgnoreCase));
            if (changeIndex >= 0 && changeIndex + 2 < args.Length)
                form.SetVerificationChange(args[changeIndex + 1], args[changeIndex + 2]);
            form.Show();
            Application.DoEvents();
            if (args.Contains("--window-cycle", StringComparer.OrdinalIgnoreCase))
                form.RunWindowMoveCycleForTest();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.GetFullPath(args[screenshotIndex + 1]), System.Drawing.Imaging.ImageFormat.Png);
            form.Hide();
            return 0;
        }
        try
        {
            TraceStartup("Constructing MainForm.");
            using var mainForm = new MainForm(shaderDirectory);
            mainForm.Shown += (_, _) => TraceStartup("MainForm shown.");
            mainForm.FormClosed += (_, _) => TraceStartup("MainForm closed.");
            mainForm.Show();
            TraceStartup($"MainForm shown explicitly. Handle={mainForm.Handle}.");
            TraceStartup("Entering message loop.");
            Application.Run(mainForm);
            TraceStartup("Message loop exited.");
            return 0;
        }
        catch (Exception exception)
        {
            TraceStartup("Startup failed: " + exception);
            MessageBox.Show($"编辑器启动失败：\n{exception.Message}\n\n详细信息已写入启动日志。", "启动失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string? FindShaderDirectoryArgument(string[] args)
    {
        var rootIndex = Array.FindIndex(args, value => value.Equals("--root", StringComparison.OrdinalIgnoreCase));
        if (rootIndex >= 0 && rootIndex + 1 < args.Length)
            return NormalizeShaderDirectory(args[rootIndex + 1]);
        return null;
    }

    private static string NormalizeShaderDirectory(string path)
    {
        if (TryNormalizeShaderDirectory(path, out var normalized)) return normalized;
        throw new DirectoryNotFoundException($"The selected folder does not contain sr_body.fx: {path}");
    }

    private static string? PromptForShaderDirectory()
    {
        var selectedPath = LoadLastShaderDirectory() ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        while (true)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "选择包含 sr_body.fx 的 MMDStarRail4Fun Shader 文件夹。软件只读取并编辑本机文件，不内置或复制 Shader 源码。",
                ShowNewFolderButton = false,
                SelectedPath = Directory.Exists(selectedPath) ? selectedPath : string.Empty,
            };
            if (dialog.ShowDialog() != DialogResult.OK) return null;
            selectedPath = dialog.SelectedPath;
            if (TryNormalizeShaderDirectory(selectedPath, out var normalized))
            {
                SaveLastShaderDirectory(normalized);
                return normalized;
            }
            MessageBox.Show("所选目录中没有找到 sr_body.fx。\n\n请选择 MMDStarRail4Fun 文件夹，或包含该文件夹的上一级目录。",
                "不是有效的 Shader 文件夹", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static bool TryNormalizeShaderDirectory(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            foreach (var candidate in new[] { fullPath, Path.Combine(fullPath, "MMDStarRail4Fun") })
            {
                if (!File.Exists(Path.Combine(candidate, "sr_body.fx"))) continue;
                normalized = candidate;
                return true;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    private static string? LoadLastShaderDirectory()
    {
        try
        {
            var path = File.ReadAllText(LastShaderDirectoryPath()).Trim();
            return TryNormalizeShaderDirectory(path, out var normalized) ? normalized : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveLastShaderDirectory(string path)
    {
        try
        {
            var settingsPath = LastShaderDirectoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, path);
        }
        catch
        {
        }
    }

    private static string LastShaderDirectoryPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StarRailShaderEditor", "last-shader-folder.txt");

    private static int ReadIntArgument(string[] args, string name, int fallback)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed) ? parsed : fallback;
    }

    private static string? ReadStringArgument(string[] args, string name)
    {
        var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void TraceStartup(string message)
    {
        if (string.IsNullOrWhiteSpace(_startupLogPath)) return;
        try
        {
            var fullPath = Path.GetFullPath(_startupLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.AppendAllText(fullPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
