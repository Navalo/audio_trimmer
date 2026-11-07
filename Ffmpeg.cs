using System.Globalization;
using AudioTrimmer.Abstractions;
using AudioTrimmer.Domain;

namespace AudioTrimmer.Infrastructure;

public sealed class FfprobeMediaProbe(IProcessRunner runner) : IMediaProbe
{
    private readonly IProcessRunner _runner = runner;

    public async Task<double> GetDurationSecondsAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            "ffprobe",
            new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path },
            cancellationToken: cancellationToken);

        return result.ExitCode == 0 &&
               double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 0;
    }
}

public sealed class FfmpegAudioTrimmer(IProcessRunner runner) : IAudioTrimmer
{
    private const string ProgressKey = "out_time_us=";

    // Containers where ffmpeg can embed a picture as an attached stream.
    private static readonly string[] CoverArtExtensions = { ".mp3", ".m4a" };

    private readonly IProcessRunner _runner = runner;

    public async Task<TrimResult> TrimAsync(TrimJob job, IProgress<double> processedSeconds, CancellationToken cancellationToken)
    {
        // The audio input is seeked/trimmed. The cover art lives in a one-frame video stream stamped at t=0,
        // so seeking past 0 throws it away. We therefore read the file a SECOND time, un-seeked, and copy
        // only the picture from that second input.
        bool keepCover = CoverArtExtensions.Contains(Path.GetExtension(job.OutputPath), StringComparer.OrdinalIgnoreCase);

        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error", "-nostats",
            "-progress", "pipe:1",
            "-ss", Format(job.Settings.StartSeconds)
        };

        if (job.Settings.DurationSeconds is { } duration)
        {
            args.Add("-t");
            args.Add(Format(duration));
        }

        args.Add("-i");
        args.Add(job.InputPath);                 // input 0: trimmed audio

        if (keepCover)
        {
            args.Add("-i");
            args.Add(job.InputPath);             // input 1: untouched, for the cover image
        }

        args.AddRange(new[] { "-map", "0:a" });

        if (keepCover)
        {
            args.AddRange(new[]
            {
                "-map", "1:v?",                  // optional: files without cover art still work
                "-c:v", "copy",                  // never re-encode the picture
                "-disposition:v:0", "attached_pic"
            });
        }

        args.AddRange(new[]
        {
            "-map_metadata", "0",                // title, artist, album, ...
            "-id3v2_version", "3",
            job.OutputPath
        });

        var result = await _runner.RunAsync(
            "ffmpeg", args,
            line =>
            {
                if (line.StartsWith(ProgressKey, StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(ProgressKey.Length), out var micros))
                {
                    processedSeconds.Report(Math.Max(0, micros / 1_000_000.0));
                }
            },
            cancellationToken);

        if (result.ExitCode == 0) return new TrimResult(true);

        TryDelete(job.OutputPath);
        return new TrimResult(false, LastNonEmptyLine(result.StdErr));
    }

    private static string Format(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string? LastNonEmptyLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}