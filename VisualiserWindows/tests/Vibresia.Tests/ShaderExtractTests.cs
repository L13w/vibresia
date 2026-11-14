using Vibresia.ShaderExtract;

namespace Vibresia.Tests;

public class ShaderExtractTests
{
    private const string Q = "\"\"\"";

    [Fact]
    public void StripsClosingIndentAndDropsFinalNewline()
    {
        var src = $"enum X {{\n    static let a = {Q}\n    void main() {{\n        x;\n    }}\n    {Q}\n}}";
        var lits = SwiftShaderExtractor.ExtractLiterals(src);
        Assert.Equal("void main() {\n    x;\n}", lits["a"]);
    }

    [Fact]
    public void KeepsBlankLinesAndDecodesEscapes()
    {
        var src = $"  static let a = {Q}\n  a\\\\b \\\"q\\\"\n\n  end\n  {Q}";
        Assert.Equal("a\\b \"q\"\n\nend", SwiftShaderExtractor.ExtractLiterals(src)["a"]);
    }

    [Fact]
    public void TrailingBackslashJoinsLines()
    {
        var src = $"static let a = {Q}\nab\\\ncd\n{Q}";
        Assert.Equal("abcd", SwiftShaderExtractor.ExtractLiterals(src)["a"]);
    }

    [Fact]
    public void RefusesInterpolation()
    {
        var src = $"static let a = {Q}\nx \\(y)\n{Q}";
        var e = Assert.Throws<FormatException>(() => SwiftShaderExtractor.ExtractLiterals(src));
        Assert.Contains("interpolation", e.Message);
    }

    [Fact]
    public void RefusesUnderIndentedLine()
    {
        var src = $"    static let a = {Q}\n  x\n    {Q}";
        Assert.Throws<FormatException>(() => SwiftShaderExtractor.ExtractLiterals(src));
    }

    [Fact]
    public void RefusesUnterminatedLiteral()
    {
        Assert.Throws<FormatException>(() => SwiftShaderExtractor.ExtractLiterals($"static let a = {Q}\nx\n"));
    }

    [Fact]
    public void ReadsModeTableInOrder()
    {
        var src = "static let modes: [(Vibescape, String)] = [\n (.badger, badger),\n (.danceFloor, danceFloor),\n]\n";
        Assert.Equal(new[] { ("badger", "badger"), ("danceFloor", "danceFloor") },
            SwiftShaderExtractor.ExtractModeTable(src));
    }

    /// <summary>
    /// The committed shaders are exactly what the extractor makes from the
    /// current upstream source. Fails when Syd changes a shader and nobody
    /// re-ran the extractor — or when someone hand-edited a generated file.
    /// </summary>
    [Fact]
    public void CommittedShadersMatchUpstream()
    {
        var expected = ShaderExport.Build(Paths.VisualiserMac);
        var committed = Directory.EnumerateFiles(Paths.Shaders, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(Paths.Shaders, f).Replace('\\', '/'), File.ReadAllText);

        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal),
            committed.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (rel, text) in expected)
            Assert.True(text == committed[rel],
                $"shaders/{rel} differs from upstream; re-run tools/Vibresia.ShaderExtract");
    }
}
