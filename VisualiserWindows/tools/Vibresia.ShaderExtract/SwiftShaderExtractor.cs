using System.Text;
using System.Text.RegularExpressions;

namespace Vibresia.ShaderExtract;

/// <summary>
/// Pulls GLSL out of the macOS app's Swift sources, so the Windows port runs
/// the very shaders the Mac does and can be refreshed when upstream changes
/// them. It understands exactly the Swift that upstream uses for shaders —
/// <c>static let name = """ ... """</c> multi-line literals — and refuses
/// anything it can't reproduce faithfully (interpolation, unknown escapes)
/// rather than guessing, because a silently mangled shader is worse than a
/// loud extraction failure.
/// </summary>
public static partial class SwiftShaderExtractor
{
    [GeneratedRegex(@"^(?<indent>[ \t]*)static let (?<name>\w+) = """"""[ \t]*$")]
    private static partial Regex OpeningLine();

    [GeneratedRegex(@"\(\.(?<mode>\w+),\s*(?<value>\w+)\)")]
    private static partial Regex ModePair();

    /// <summary>Every <c>static let x = """…"""</c> literal in the source, by name.</summary>
    public static IReadOnlyDictionary<string, string> ExtractLiterals(string swiftSource)
    {
        var lines = swiftSource.Replace("\r\n", "\n").Split('\n');
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var m = OpeningLine().Match(lines[i]);
            if (!m.Success) continue;
            var name = m.Groups["name"].Value;
            var body = new List<string>();
            var j = i + 1;
            for (; j < lines.Length; j++)
            {
                if (lines[j].Trim() == "\"\"\"") break;
                body.Add(lines[j]);
            }
            if (j == lines.Length)
                throw new FormatException($"'{name}': no closing \"\"\" found");
            var closingIndent = lines[j][..lines[j].IndexOf('"')];
            result[name] = Decode(name, body, closingIndent);
            i = j;
        }
        return result;
    }

    /// <summary>
    /// The <c>modes</c> table — (vibescape, literal name) pairs in upstream's
    /// order — so the catalog lists built-ins the way the Mac does.
    /// </summary>
    public static IReadOnlyList<(string Mode, string Literal)> ExtractModeTable(string swiftSource)
    {
        var start = swiftSource.IndexOf("static let modes:", StringComparison.Ordinal);
        if (start < 0) throw new FormatException("no 'static let modes:' table found");
        // Skip the type annotation, whose own brackets would end the table early.
        var open = swiftSource.IndexOf("= [", start, StringComparison.Ordinal);
        if (open < 0) throw new FormatException("'modes' table has no '= [' opening");
        var end = swiftSource.IndexOf(']', open);
        var table = swiftSource[open..end];
        var pairs = ModePair().Matches(table)
            .Select(m => (m.Groups["mode"].Value, m.Groups["value"].Value))
            .ToList();
        return pairs.Count > 0 ? pairs : throw new FormatException("'modes' table is empty");
    }

    // Swift multi-line literal rules: the closing delimiter's indentation is
    // stripped from every line, the newline before the closing delimiter is
    // not part of the string, and a backslash at the end of a line joins it
    // to the next.
    private static string Decode(string name, List<string> lines, string indent)
    {
        var sb = new StringBuilder();
        for (var k = 0; k < lines.Count; k++)
        {
            var line = lines[k];
            if (line.Length > 0 && line.Trim().Length > 0)
            {
                if (!line.StartsWith(indent, StringComparison.Ordinal))
                    throw new FormatException($"'{name}': line {k + 1} is indented less than its closing delimiter");
                line = line[indent.Length..];
            }
            else
            {
                line = "";
            }

            var joined = AppendUnescaped(sb, name, line);
            if (!joined && k < lines.Count - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <returns>true when the line ended in a continuation backslash.</returns>
    private static bool AppendUnescaped(StringBuilder sb, string name, string line)
    {
        for (var c = 0; c < line.Length; c++)
        {
            if (line[c] != '\\') { sb.Append(line[c]); continue; }
            if (c == line.Length - 1) return true;
            var next = line[++c];
            switch (next)
            {
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case '\'': sb.Append('\''); break;
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case '0': sb.Append('\0'); break;
                case '(':
                    throw new FormatException($"'{name}': string interpolation can't be extracted");
                default:
                    throw new FormatException($"'{name}': unsupported escape \\{next}");
            }
        }
        return false;
    }
}
