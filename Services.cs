using System.Diagnostics;
using AudioTrimmer.Abstractions;
using AudioTrimmer.Domain;
using AudioTrimmer.Infrastructure;

namespace AudioTrimmer.Services;

/// <summary>Puts results in a sub-folder next to the sources and never overwrites an input file.</summary>
public sealed class SubfolderOutputLocator : IOutputLocator
{
    private readonly string _subfolderName;
    public SubfolderOutputLocator(string subfolderName) => _subfolderName = subfolderName;

    public string PrepareOutputDirectory(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, _subfolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetOutputPath(string inputPath, string outputDirectory)
    {
        var candidate = Path.Combine(outputDirectory, Path.GetFileName(inputPath));

        if (!string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
            return candidate;

        var name = Path.GetFileNameWithoutExtension(inputPath) + "_trimmed" + Path.GetExtension(inputPath);
        return Path.Combine(outputDirectory, name);
    }
}

/// <summary>Works out the output path and how long the output should be (for % progress).</summary>
public sealed class TrimJobFactory : ITrimJobFactory
{
    private readonly IMediaProbe _probe;
    private readonly IOutputLocator _locator;

    public TrimJobFactory(IMediaProbe probe, IOutputLocator locator)
    {
        _probe = probe;
        _locator = locator;
    }

    public async Task<TrimJob> CreateAsync(string inputPath, string outputDirectory, TrimSettings settings, CancellationToken cancellationToken)
    {
        double total = await _probe.GetDurationSecondsAsync(inputPath, cancellationToken);
        double? remaining = total > settings.StartSeconds ? total - settings.StartSeconds : null;

        double? expected = (remaining, settings.DurationSeconds) switch
        {
            ({ } r, { } d) => Math.Min(r, d),
            ({ } r, null) => r,
            (null, { } d) when total <= 0 => d, // duration unknown: trust the user's value
            _ => null
        };

        return new TrimJob(inputPath, _locator.GetOutputPath(inputPath, outputDirectory), settings, expected);
    }
}

public sealed class BatchTrimService : IBatchTrimService
{
    private readonly IOutputLocator _locator;
    private readonly ITrimJobFactory _jobFactory;
    private readonly IAudioTrimmer _trimmer;
    private readonly IProgressReporter _reporter;

    public BatchTrimService(IOutputLocator locator, ITrimJobFactory jobFactory, IAudioTrimmer trimmer, IProgressReporter reporter)
    {
        _locator = locator;
        _jobFactory = jobFactory;
        _trimmer = trimmer;
        _reporter = reporter;
    }

    public async Task<BatchResult> RunAsync(SourceSelection selection, TrimSettings settings, CancellationToken cancellationToken)
    {
        string outputDirectory = _locator.PrepareOutputDirectory(selection.BaseDirectory);
        int succeeded = 0;

        _reporter.Start(selection.Files.Select(f => Path.GetFileName(f) ?? f).ToList());

        try
        {
            for (int i = 0; i < selection.Files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int index = i;
                _reporter.Begin(index);
                var job = await _jobFactory.CreateAsync(selection.Files[i], outputDirectory, settings, cancellationToken);

                var progress = new InlineProgress<double>(seconds =>
                    _reporter.Update(index, job.ExpectedOutputSeconds is > 0
                        ? seconds / job.ExpectedOutputSeconds.Value * 100
                        : null));

                var result = await _trimmer.TrimAsync(job, progress, cancellationToken);
                _reporter.Complete(index, result.Success, result.Error);

                if (result.Success) succeeded++;
            }
        }
        finally
        {
            _reporter.Finish();
        }

        return new BatchResult(succeeded, selection.Files.Count, outputDirectory);
    }
}

/// <summary>Opens a folder using a platform-specific command (explorer / open / xdg-open).</summary>
public sealed class CommandFolderOpener : IFolderOpener
{
    private readonly string _command;
    private readonly bool _useShellExecute;
    public CommandFolderOpener(bool isSupported, string command, bool useShellExecute = false)
    {
        IsSupported = isSupported;
        _command = command;
        _useShellExecute = useShellExecute;
    }

    public bool IsSupported { get; }

    public void Open(string folderPath)
    {
        var psi = new ProcessStartInfo(_command) { UseShellExecute = _useShellExecute };
        if (_useShellExecute)
            psi.Arguments = $"\"{folderPath}\"";
        else
            psi.ArgumentList.Add(folderPath);
        Process.Start(psi);
    }
}

/// <summary>Delegates to the first opener that supports the current platform (add new ones without editing this class).</summary>
public sealed class CompositeFolderOpener : IFolderOpener
{
    private readonly IReadOnlyList<IFolderOpener> _openers;
    public CompositeFolderOpener(IEnumerable<IFolderOpener> openers) => _openers = openers.ToList();

    public bool IsSupported => _openers.Any(o => o.IsSupported);

    public void Open(string folderPath)
    {
        var opener = _openers.FirstOrDefault(o => o.IsSupported)
                     ?? throw new PlatformNotSupportedException("No folder opener for this platform.");
        opener.Open(folderPath);
    }
}

/// <summary>The use-case flow: choose source -> ask settings -> trim -> report -> open folder.</summary>
public sealed class TrimApplication
{
    private readonly IUserPrompter _prompter;
    private readonly ISourceSelector _selector;
    private readonly IBatchTrimService _batch;
    private readonly IFolderOpener _opener;
    private readonly IMessageWriter _writer;

    public TrimApplication(
        IUserPrompter prompter,
        ISourceSelector selector,
        IBatchTrimService batch,
        IFolderOpener opener,
        IMessageWriter writer)
    {
        _prompter = prompter;
        _selector = selector;
        _batch = batch;
        _opener = opener;
        _writer = writer;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        _writer.Write("========================================");
        _writer.Write("        .NET AUDIO TRIMMER CLI          ");
        _writer.Write("========================================\n");

        _prompter.WaitForEnter("Press ENTER to select audio files (you can pick multiple)...");

        var selection = _selector.Select();
        if (selection is null)
        {
            _writer.Write("Selection was canceled.", MessageLevel.Warning);
            return 1;
        }

        _writer.Write($"Selected {selection.Files.Count} audio file(s).", MessageLevel.Success);

        var settings = _prompter.AskTrimSettings();
        _writer.Write("\nStarting processing...\n");

        BatchResult result;
        try
        {
            result = await _batch.RunAsync(selection, settings, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _writer.Write("\nCanceled.", MessageLevel.Warning);
            return 2;
        }

        _writer.Write("\n========================================");
        _writer.Write($"Finished! Successfully processed {result.Succeeded} out of {result.Total} files.");
        _writer.Write("========================================");

        if (_opener.IsSupported)
        {
            if (_prompter.ConfirmEnterOrEscape("Press ENTER to open the output folder, or ESC to exit."))
            {
                try { _opener.Open(result.OutputDirectory); }
                catch (Exception ex) { _writer.Write($"Could not open folder automatically: {ex.Message}", MessageLevel.Error); }
            }
        }

        return result.Succeeded == result.Total ? 0 : 3;
    }
}