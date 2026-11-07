using AudioTrimmer.Abstractions;
using AudioTrimmer.Domain;

namespace AudioTrimmer.UI;

public sealed class ConsoleMessageWriter : IMessageWriter
{
    public void Write(string message, MessageLevel level = MessageLevel.Info)
    {
        ConsoleColor? color = level switch
        {
            MessageLevel.Success => ConsoleColor.Green,
            MessageLevel.Warning => ConsoleColor.Yellow,
            MessageLevel.Error => ConsoleColor.Red,
            _ => null
        };

        if (color is { } c) Console.ForegroundColor = c;
        Console.WriteLine(message);
        if (color is not null) Console.ResetColor();
    }
}

public sealed class ConsolePrompter : IUserPrompter
{
    private readonly IMessageWriter _writer;
    private readonly ITimeParser _timeParser;

    public ConsolePrompter(IMessageWriter writer, ITimeParser timeParser)
    {
        _writer = writer;
        _timeParser = timeParser;
    }

    public TrimSettings AskTrimSettings()
    {
        double start = AskTime(
            "\nEnter start time (HH:MM:SS or seconds, e.g. 00:00:10. Enter for 0): ",
            emptyMeans: 0)!.Value;

        double? duration = AskTime(
            "Enter duration to keep (HH:MM:SS or seconds. Enter for MAX remaining): ",
            emptyMeans: null,
            mustBePositive: true);

        return new TrimSettings(start, duration);
    }

    public void WaitForEnter(string message)
    {
        _writer.Write($"\n{message}", MessageLevel.Warning);
        Console.ReadLine();
    }

    public bool ConfirmEnterOrEscape(string message)
    {
        _writer.Write($"\n{message}", MessageLevel.Warning);

        if (Console.IsInputRedirected) // no key access: fall back to a plain line read
        {
            Console.ReadLine();
            return true;
        }

        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Enter) return true;
            if (key == ConsoleKey.Escape) return false;
        }
    }

    private double? AskTime(string prompt, double? emptyMeans, bool mustBePositive = false)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) return emptyMeans;

            if (_timeParser.TryParse(input, out var seconds) && (!mustBePositive || seconds > 0))
                return seconds;

            _writer.Write("Invalid time. Try again.", MessageLevel.Error);
        }
    }
}

/// <summary>
/// Prints "[0%] file name" for every file once (default color), then redraws that file's single line by
/// moving the cursor: blue while in progress, green when done, red on failure. No extra lines are printed.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter
{
    private const int StatusWidth = 6; // fits "[100%]" and "[FAIL]"

    // Cyan is the readable "blue" on dark consoles; ConsoleColor.Blue is very dark on a black background.
    private const ConsoleColor ProgressColor = ConsoleColor.Cyan;
    private const ConsoleColor DoneColor = ConsoleColor.Green;
    private const ConsoleColor FailColor = ConsoleColor.Red;

    private IReadOnlyList<string> _labels = Array.Empty<string>();
    private readonly List<(string Label, string Error)> _errors = new();
    private readonly object _gate = new(); // progress callbacks arrive on ffmpeg's output thread
    private int[] _lastPercent = Array.Empty<int>();
    private bool[] _done = Array.Empty<bool>();
    private string[] _display = Array.Empty<string>(); // file names, already shortened to fit the window
    private int _baseRow;
    private bool _interactive;

    private void StartCore(IReadOnlyList<string> labels)
    {
        _labels = labels;
        _lastPercent = Enumerable.Repeat(-1, labels.Count).ToArray();
        _done = new bool[labels.Count];
        _errors.Clear();
        _interactive = CanMoveCursor();

        if (!_interactive) return; // redirected output: only print results as they finish

        try { Console.CursorVisible = false; } catch (IOException) { }

        int width = SafeWidth();
        _display = labels.Select(l => Truncate(l, width - StatusWidth - 1)).ToArray();

        // Not started yet: default console color
        for (int i = 0; i < labels.Count; i++)
            Console.WriteLine(Line("0%", i));

        _baseRow = Console.CursorTop - labels.Count;
        if (_baseRow < 0) _interactive = false; // scrolled past the buffer; stop redrawing
    }

    private void BeginCore(int index)
    {
        if (!_interactive) return;

        _lastPercent[index] = 0;
        WriteStatus(index, "0%", ProgressColor); // in progress: blue
    }

    private void UpdateCore(int index, double? percent)
    {
        if (!_interactive || _done[index]) return; // ignore late ticks for finished files

        if (percent is not { } p)
        {
            if (_lastPercent[index] != -2) WriteStatus(index, "...", ProgressColor);
            _lastPercent[index] = -2;
            return;
        }

        int whole = (int)Math.Clamp(Math.Floor(p), 0, 100);
        if (whole == _lastPercent[index]) return; // redraw only when the number changes

        _lastPercent[index] = whole;
        WriteStatus(index, $"{whole}%", ProgressColor);
    }

    private void CompleteCore(int index, bool success, string? error)
    {
        _done[index] = true;
        if (!success) _errors.Add((_labels[index], error ?? "unknown error"));

        if (_interactive)
        {
            if (success) WriteStatus(index, "100%", DoneColor);
            else WriteStatus(index, "FAIL", FailColor);
            return;
        }

        // Fallback for redirected output
        Console.ForegroundColor = success ? DoneColor : FailColor;
        Console.WriteLine($"{Status(success ? "100%" : "FAIL")} {_labels[index]}");
        Console.ResetColor();
    }

    private void FinishCore()
    {
        if (_interactive) MoveBelowList();
        try { Console.CursorVisible = true; } catch (IOException) { }

        foreach (var (label, error) in _errors)
        {
            Console.ForegroundColor = FailColor;
            Console.WriteLine($"  {label}: {error}");
            Console.ResetColor();
        }
    }

    // Public entry points: every draw is serialized so cursor moves and writes never interleave.
    public void Start(IReadOnlyList<string> labels) { lock (_gate) StartCore(labels); }
    public void Begin(int index) { lock (_gate) BeginCore(index); }
    public void Update(int index, double? percent) { lock (_gate) UpdateCore(index, percent); }
    public void Complete(int index, bool success, string? error) { lock (_gate) CompleteCore(index, success, error); }
    public void Finish() { lock (_gate) FinishCore(); }

    private void WriteStatus(int index, string status, ConsoleColor? color)
    {
        try
        {
            Console.SetCursorPosition(0, _baseRow + index);
            if (color is { } c) Console.ForegroundColor = c;
            Console.Write(Line(status, index)); // same length every time (fixed-width status), so no padding that could wrap
            if (color is not null) Console.ResetColor();
            MoveBelowList();
        }
        catch (IOException) { _interactive = false; }
        catch (ArgumentOutOfRangeException) { _interactive = false; }
    }

    private void MoveBelowList() => Console.SetCursorPosition(0, _baseRow + _labels.Count);

    private string Line(string status, int index) => $"{Status(status)} {_display[index]}";

    private static string Status(string text) => $"[{text}]".PadRight(StatusWidth);

    private static bool CanMoveCursor()
    {
        if (Console.IsOutputRedirected) return false;
        try { _ = Console.CursorTop; return true; }
        catch (IOException) { return false; }
    }

    private static int SafeWidth()
    {
        // -4 is a safety margin so a line never reaches the right edge and wraps
        try { return Math.Max(20, Console.WindowWidth - 4); }
        catch (IOException) { return 120; }
    }

    private static string Truncate(string text, int width) =>
        text.Length <= width ? text : text[..(width - 3)] + "...";
}