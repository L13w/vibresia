using Vibresia.ShaderExtract;

// Usage: dotnet run --project tools/Vibresia.ShaderExtract [VisualiserMac dir] [output dir]
// Defaults assume it is run from VisualiserWindows/.
var macDir = args.Length > 0 ? args[0] : Path.Combine("..", "VisualiserMac");
var outDir = args.Length > 1 ? args[1] : "shaders";
try
{
    ShaderExport.Write(macDir, outDir);
    Console.WriteLine($"Extracted shaders from {macDir} into {outDir}");
    return 0;
}
catch (Exception e) when (e is FormatException or IOException)
{
    Console.Error.WriteLine($"Extraction failed: {e.Message}");
    return 1;
}
