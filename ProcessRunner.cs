using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using AudioTrimmer.Abstractions;
using AudioTrimmer.Domain;

namespace AudioTrimmer.Infrastructure;

/// <summary>Runs a process, drains stdout/stderr asynchronously (no deadlocks), supports cancellation.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onStdOutLine = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdOut) stdOut.AppendLine(e.Data);
            onStdOutLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdErr) stdErr.AppendLine(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, "", $"Could not start '{fileName}' (is it installed and on PATH?): {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
}

/// <summary>Synchronous IProgress (Progress&lt;T&gt; posts to the thread pool and can reorder console output).</summary>
public sealed class InlineProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    public InlineProgress(Action<T> handler) => _handler = handler;
    public void Report(T value) => _handler(value);
}
