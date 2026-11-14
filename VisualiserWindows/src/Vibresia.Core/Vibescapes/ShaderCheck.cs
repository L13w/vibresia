using System.Text;
using System.Text.RegularExpressions;

namespace Vibresia.Core.Vibescapes;

/// <summary>
/// What is wrong with a custom vibescape, in words a person can act on.
/// <see cref="DriverLog"/> keeps the driver's own text for the log.
/// </summary>
/// <param name="Line">The line in the user's own code, when it can be told.</param>
public sealed record ShaderProblem(string Message, int? Line = null, string DriverLog = "")
{
    public string Title => Line is { } l ? $"Line {l} needs a fix" : "That didn't compile";

    /// <summary>One block for the screen: title, the sentence, and the driver's first line when the sentence is only a lead-in to it.</summary>
    public string ForScreen()
    {
        var sb = new StringBuilder(Title).Append(": ").Append(Message);
        if (Message.EndsWith(':') && DriverLog.Length > 0) sb.Append('\n').Append(DriverLog);
        return sb.ToString();
    }
}

/// <summary>
/// Upstream's checks on a custom vibescape (VisualiserMac
/// Render/ShaderValidator.swift), ported so a broken shader is explained the
/// same way as on the Mac: cheap shape checks before compiling, then the
/// driver's compile log turned into a sentence and a line in the user's code.
/// </summary>
public static partial class ShaderCheck
{
    [GeneratedRegex(@"vec3\s+render\s*\(\s*vec2\s+\w+\s*,\s*float\s+\w+\s*\)")]
    private static partial Regex RenderSignature();

    [GeneratedRegex(@"render\s*\(")]
    private static partial Regex RenderCall();

    [GeneratedRegex(@"void\s+main\s*\(")]
    private static partial Regex VoidMain();

    /// <returns>Null when the shape is fine and it is worth asking the driver.</returns>
    public static ShaderProblem? CheckShape(string source)
    {
        var trimmed = source.Trim();
        if (trimmed.Length == 0)
            return new("There's nothing in the box yet. Paste in a vibescape, or start from the example.");
        // The prelude already supplies the version line, the uniforms and main().
        if (trimmed.Contains("#version"))
            return new("Delete the line starting with #version. Vibresia already says which version of graphics it uses — leaving yours in causes an error.");
        if (trimmed.Contains("#include"))
            return new("Delete any line starting with #include. Vibresia already provides everything a vibescape needs.");
        if (VoidMain().IsMatch(trimmed))
            return new("Vibescapes use a function called render, not main. Change \"void main()\" to \"vec3 render(vec2 p, float t)\", and at the end return your colour instead of assigning to fragColor.");

        var code = StripComments(trimmed);
        // A write to fragColor compiles but is thrown away: the colour drawn is what render() returns.
        if (code.Contains("fragColor") || code.Contains("gl_FragColor"))
            return new("You wrote to fragColor, but a vibescape's colour is what render() returns. Take the fragColor line out and return the colour instead — `return vec3(1.0, 0.0, 0.5);` for red, green and blue.");
        if (!RenderSignature().IsMatch(code))
            return RenderCall().IsMatch(code)
                ? new("Found something called render, but not in the shape Vibresia expects. It needs to be exactly: vec3 render(vec2 p, float t)")
                : new("A vibescape needs a function called render that returns a colour. Copy the example above to start from a working one.");
        return null;
    }

    /// <summary>Blanks out // and /* */ comments, keeping every newline so positions still line up.</summary>
    public static string StripComments(string s)
    {
        var c = s.ToCharArray();
        var inBlock = false;
        for (var i = 0; i < c.Length;)
        {
            if (inBlock)
            {
                if (c[i] == '*' && i + 1 < c.Length && c[i + 1] == '/') { inBlock = false; c[i] = c[i + 1] = ' '; i += 2; continue; }
                if (c[i] != '\n') c[i] = ' ';
                i++;
                continue;
            }
            if (c[i] == '/' && i + 1 < c.Length && c[i + 1] == '/')
            {
                while (i < c.Length && c[i] != '\n') c[i++] = ' ';
                continue;
            }
            if (c[i] == '/' && i + 1 < c.Length && c[i + 1] == '*') { inBlock = true; c[i] = c[i + 1] = ' '; i += 2; continue; }
            i++;
        }
        return new string(c);
    }

    // The three shapes drivers write a source position in. The line is the
    // number after the source-string index; Mesa adds the column in brackets.
    [GeneratedRegex(@"\b\d+:(\d+)\(\d+\)")]          // Mesa:          0:12(5): error: …
    private static partial Regex MesaPosition();
    [GeneratedRegex(@"\b\d+\((\d+)\)\s*:")]          // NVIDIA:        0(12) : error C1008: …
    private static partial Regex NvidiaPosition();
    [GeneratedRegex(@"\b\d+:(\d+):")]                // Intel and AMD: ERROR: 0:12: …
    private static partial Regex ColonPosition();

    /// <summary>The line in the complete shader the driver blames, or null if its log names none.</summary>
    public static int? DriverLine(string log)
    {
        foreach (var re in new[] { MesaPosition(), NvidiaPosition(), ColonPosition() })
            if (re.Match(log) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n))
                return n;
        return null;
    }

    /// <summary>
    /// A driver line number as a line in the user's own code. The compiler
    /// counts the prelude, which the user never sees; a line inside the
    /// prelude or wrapper is clamped to the body's first line.
    /// </summary>
    public static int UserLine(int driverLine, int preludeLines) => Math.Max(1, driverLine - preludeLines);

    /// <summary>Turns a driver compile log into the closest thing to a plain sentence, as upstream does.</summary>
    /// <param name="preludeLines">Lines of the complete shader before the user's first line.</param>
    public static ShaderProblem Explain(string log, int preludeLines)
    {
        var lowered = log.ToLowerInvariant();
        var lines = log.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var first = lines.FirstOrDefault() ?? log;
        int? line = DriverLine(log) is { } d ? UserLine(d, preludeLines) : null;

        ShaderProblem Problem(string message) => new(message, line, log);

        if (lowered.Contains("redefinition") || lowered.Contains("already defined") || lowered.Contains("redeclar"))
        {
            var name = QuotedName(first) ?? "Something";
            return name == "main"
                ? Problem("You have a main() function, but Vibresia supplies its own. Delete yours and use render() instead.")
                : Problem($"You declared {name}, which Vibresia already provides. Remove your line declaring it — using the existing one is usually all that's needed.");
        }
        if (lowered.Contains("undeclared identifier") || lowered.Contains("unknown identifier") || lowered.Contains("undeclared variable"))
        {
            var name = QuotedName(first) ?? "that name";
            if (name == "render")
                return Problem("Your render function isn't quite the right shape. It needs to be exactly: vec3 render(vec2 p, float t) — returning a vec3, taking a vec2 and a float, in that order.");
            if (KnownUniformName(name) is { } known)
                return Problem($"{name} isn't a thing Vibresia provides. The closest is {known} — check the spelling.");
            return Problem($"Vibresia doesn't recognise {name}. If it's a uniform or helper you expected, check the spelling against the list below. If it's your own variable, make sure you declare it before you use it.");
        }
        if (lowered.Contains("must match return type") || lowered.Contains("no available implicit conversion"))
            return Problem("The type is wrong. render() has to give back a vec3 — one number each for red, green and blue, like `return vec3(1.0, 0.0, 0.5);`. A single number won't do.");
        if (lowered.Contains("no matching overloaded function"))
            return Problem("A function was called with the wrong number or type of arguments. Check any brackets you've just edited.");
        if (lowered.Contains("expected") && lowered.Contains(';'))
            return Problem("Something is missing a semicolon, or a bracket doesn't close. Check the line above this one too.");
        if (lowered.Contains("syntax error"))
            return Problem("There's a typo or a missing bracket on this line. Check that every { has a matching }.");
        if (lowered.Contains("undeclared") || lowered.Contains("undefined"))
            return Problem("Something in this line isn't defined yet — a variable used before it's created, most likely.");
        if (lowered.Contains("fragment") && lowered.Contains("out"))
            return Problem("The shader didn't produce an output. Make sure render() returns a colour.");
        // Nothing matched: show the driver's first real line rather than pretend to understand it.
        return new("This didn't compile. The graphics system said:", line, first);
    }

    [GeneratedRegex("['\"`]([^'\"`]+)['\"`]")]
    private static partial Regex Quoted();

    private static string? QuotedName(string line) => Quoted().Match(line) is { Success: true } m ? m.Groups[1].Value : null;

    private static readonly string[] RealUniforms =
    [
        "uTime", "uBass", "uMid", "uTreble", "uBeat", "uBeatStrength", "uLevel",
        "uResolution", "uIdle", "uBands", "uActive", "uBeats", "uScroll",
        "uClock", "uLevelSmooth", "uBassSmooth", "uPalette",
        "uMouse", "uMouseDown", "uKey", "uKeyDown",
        "uMidiNote", "uMidiVelocity", "uMidiNotes", "uMidiCC", "uMidiCCValue",
    ];

    /// <summary>The nearest real uniform name, for "did you mean". Only offers names that exist.</summary>
    public static string? KnownUniformName(string name)
    {
        if (RealUniforms.Contains(name)) return null;
        var best = RealUniforms.MinBy(r => EditDistance(r, name))!;
        return EditDistance(best, name) <= Math.Max(2, name.Length / 3) ? best : null;
    }

    public static int EditDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        var cur = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
