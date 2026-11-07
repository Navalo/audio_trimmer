namespace AudioTrimmer.Domain;

public enum MessageLevel { Info, Success, Warning, Error }

/// <summary>Files chosen by the user plus the directory the output folder lives in.</summary>
public sealed record SourceSelection(string BaseDirectory, IReadOnlyList<string> Files);

/// <summary>Where to start (seconds) and how long to keep (null = until the end).</summary>
public sealed record TrimSettings(double StartSeconds, double? DurationSeconds);

public sealed record TrimJob(
    string InputPath,
    string OutputPath,
    TrimSettings Settings,
    double? ExpectedOutputSeconds);

public sealed record TrimResult(bool Success, string? Error = null);

public sealed record BatchResult(int Succeeded, int Total, string OutputDirectory);

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

public static class AudioFormats
{
    public static readonly IReadOnlyList<string> Supported = new[] { ".mp3", ".wav", ".m4a", ".ogg" };
}