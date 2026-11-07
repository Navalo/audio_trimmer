using AudioTrimmer.Domain;

namespace AudioTrimmer.Abstractions;

// ---- Input / parsing ----
public interface ITimeParser
{
    bool TryParse(string? input, out double seconds);
}

// ---- User interaction (split so each consumer depends only on what it uses) ----
public interface IMessageWriter
{
    void Write(string message, MessageLevel level = MessageLevel.Info);
}

public interface IUserPrompter
{
    TrimSettings AskTrimSettings();
    void WaitForEnter(string message);

    /// <summary>Shows the message and waits: returns true on ENTER, false on ESC.</summary>
    bool ConfirmEnterOrEscape(string message);
}

public interface IProgressReporter
{
    /// <summary>Prints one line per file up front; later updates only change the status part of that line.</summary>
    void Start(IReadOnlyList<string> labels);
    void Begin(int index);
    void Update(int index, double? percent);
    void Complete(int index, bool success, string? error);
    void Finish();
}

// ---- Source selection ----
public interface ISourceSelector
{
    /// <summary>Lets the user pick one or more audio files. Returns null if canceled.</summary>
    SourceSelection? Select();
}

// ---- Output paths ----
public interface IOutputLocator
{
    string PrepareOutputDirectory(string baseDirectory);
    string GetOutputPath(string inputPath, string outputDirectory);
}

// ---- External processes / media tooling ----
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onStdOutLine = null,
        CancellationToken cancellationToken = default);
}

public interface IMediaProbe
{
    Task<double> GetDurationSecondsAsync(string path, CancellationToken cancellationToken);
}

public interface IAudioTrimmer
{
    /// <param name="processedSeconds">Receives how many seconds of output have been written so far.</param>
    Task<TrimResult> TrimAsync(TrimJob job, IProgress<double> processedSeconds, CancellationToken cancellationToken);
}

// ---- Orchestration ----
public interface ITrimJobFactory
{
    Task<TrimJob> CreateAsync(string inputPath, string outputDirectory, TrimSettings settings, CancellationToken cancellationToken);
}

public interface IBatchTrimService
{
    Task<BatchResult> RunAsync(SourceSelection selection, TrimSettings settings, CancellationToken cancellationToken);
}

public interface IFolderOpener
{
    bool IsSupported { get; }
    void Open(string folderPath);
}