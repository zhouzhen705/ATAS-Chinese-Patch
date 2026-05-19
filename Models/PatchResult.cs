namespace ATASChinesePatch.Models;

public sealed class PatchResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public int TotalStringCount { get; set; }
    public int ReplacedCount { get; set; }
    public List<DetectedFont> DetectedFonts { get; } = [];
    public List<SuspiciousFontString> SuspiciousFontStrings { get; } = [];
    public List<string> Logs { get; } = [];
}

public sealed class DetectedFont
{
    public string FontName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
}

public sealed class SuspiciousFontString
{
    public string Value { get; set; } = string.Empty;
    public string MatchedKeyword { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
}
