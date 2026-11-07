using System.Runtime.InteropServices;
using AudioTrimmer.Abstractions;
using AudioTrimmer.Domain;
using AudioTrimmer.Infrastructure;
using AudioTrimmer.Input;
using AudioTrimmer.Services;
using AudioTrimmer.UI;

namespace AudioTrimmer;

internal static class Program
{
    /// <summary>Composition root: the only place that knows concrete types.</summary>
    // [STAThread] is ignored on an async Main, so Main must be synchronous for the WinForms dialogs to work.
    [STAThread]
    private static int Main() => Run();

    private static int Run()
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var writer = new ConsoleMessageWriter();
        var prompter = new ConsolePrompter(writer, new TimeParser());
        var progress = new ConsoleProgressReporter();

        var runner = new ProcessRunner();
        var probe = new FfprobeMediaProbe(runner);
        var trimmer = new FfmpegAudioTrimmer(runner);

        var locator = new SubfolderOutputLocator("trimmed");
        var jobFactory = new TrimJobFactory(probe, locator);
        var batch = new BatchTrimService(locator, jobFactory, trimmer, progress);

        ISourceSelector selector = new MultiFileSelector(AudioFormats.Supported);

        var opener = new CompositeFolderOpener(new IFolderOpener[]
        {
            new CommandFolderOpener(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "explorer.exe", useShellExecute: true),
            new CommandFolderOpener(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), "open"),
            new CommandFolderOpener(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "xdg-open")
        });

        var app = new TrimApplication(prompter, selector, batch, opener, writer);
        return app.RunAsync(cts.Token).GetAwaiter().GetResult();
    }
}