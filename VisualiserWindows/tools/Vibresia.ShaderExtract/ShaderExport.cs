namespace Vibresia.ShaderExtract;

/// <summary>
/// What the Windows port takes from upstream, and where it lands on disk.
/// One definition used by both the command-line tool and the drift test, so
/// they cannot disagree about what "up to date" means.
/// </summary>
public static class ShaderExport
{
    public const string FunShadersPath = "Sources/VisualiserMac/Render/FunShaders.swift";
    public const string ShadersPath = "Sources/VisualiserMac/Render/Shaders.swift";

    /// <summary>Relative output path → file contents.</summary>
    public static IReadOnlyDictionary<string, string> Build(string visualiserMacDir)
    {
        var fun = File.ReadAllText(Path.Combine(visualiserMacDir, FunShadersPath));
        var core = File.ReadAllText(Path.Combine(visualiserMacDir, ShadersPath));

        var funLiterals = SwiftShaderExtractor.ExtractLiterals(fun);
        var coreLiterals = SwiftShaderExtractor.ExtractLiterals(core);
        var modes = SwiftShaderExtractor.ExtractModeTable(fun);

        var files = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["prelude.glsl"] = Require(funLiterals, "prelude"),
            ["custom-wrapper.glsl"] = Require(funLiterals, "customWrapper"),
            ["fullscreen.vert"] = Require(coreLiterals, "liquidVS"),
        };
        foreach (var (mode, literal) in modes)
            files[$"builtin/{mode}.glsl"] = Require(funLiterals, literal);
        files["builtin/order.txt"] = string.Join('\n', modes.Select(m => m.Mode)) + "\n";
        return files;
    }

    public static void Write(string visualiserMacDir, string outputDir)
    {
        var files = Build(visualiserMacDir);
        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, recursive: true);
        foreach (var (rel, text) in files)
        {
            var path = Path.Combine(outputDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }

    private static string Require(IReadOnlyDictionary<string, string> literals, string name) =>
        literals.TryGetValue(name, out var v)
            ? v
            : throw new FormatException($"upstream no longer defines '{name}'");
}
