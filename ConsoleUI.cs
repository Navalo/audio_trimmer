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
/// Shows "[xx%] file name" lines that are redrawn in place (never appended).
/// If there are more files than fit on screen, a window of rows that DOES fit is shown and it scrolls
/// along with the file being processed. Colors: default = not started, cyan = running, green = done, red = failed.
/// </summary>
public sealed class ConsoleProgressReporter : IProgressReporter
{
    private enum State { Pending, Running, Done, Failed }

    private const int StatusWidth = 6;   // fits "[100%]" and "[FAIL]"
    private const int ReservedRows = 4;  // rows kept free: footer + prompts below the list
    private const int RowsAboveActive = 3;

    // Cyan is the readable "blue" on dark consoles; ConsoleColor.Blue is very dark on a black background.
    private const ConsoleColor ProgressColor = ConsoleColor.Cyan;
    private const ConsoleColor DoneColor = ConsoleColor.Green;
    private const ConsoleColor FailColor = ConsoleColor.Red;

    private readonly object _gate = new(); // progress callbacks arrive on ffmpeg's output thread
    private readonly List<(string Label, string Error)> _errors = new();

    private IReadOnlyList<string> _labels = Array.Empty<string>();
    private string[] _names = Array.Empty<string>();   // file names shortened to fit the window
    private State[] _state = Array.Empty<State>();
    private int[] _percent = Array.Empty<int>();       // -1 = unknown
    private int _baseRow;      // console row of the first visible file line
    private int _visible;      // how many file lines are shown at once
    private int _viewStart;    // index of the first file currently shown
    private int _lineWidth;
    private int _doneCount;
    private bool _interactive;

    // Public entry points: every draw is serialized so cursor moves and writes never interleave.
    public void Start(IReadOnlyList<string> labels) { lock (_gate) StartCore(labels); }
    public void Begin(int index) { lock (_gate) BeginCore(index); }
    public void Update(int index, double? percent) { lock (_gate) UpdateCore(index, percent); }
    public void Complete(int index, bool success, string? error) { lock (_gate) CompleteCore(index, success, error); }
    public void Finish() { lock (_gate) FinishCore(); }

    private void StartCore(IReadOnlyList<string> labels)
    {
        _labels = labels;
        _state = new State[labels.Count];
        _percent = new int[labels.Count];
        _viewStart = 0;
        _doneCount = 0;
        _errors.Clear();
        _interactive = CanMoveCursor();

        if (!_interactive) return; // redirected output: only print results as they finish

        try { Console.CursorVisible = false; } catch (IOException) { }

        _lineWidth = SafeWidth();
        _names = labels.Select(l => Truncate(l, _lineWidth - StatusWidth - 1)).ToArray();
        _visible = Math.Min(labels.Count, Math.Max(1, SafeHeight() - ReservedRows));

        // Reserve the rows (file lines + one footer line), then remember where they start.
        for (int i = 0; i < _visible + 1; i++) Console.WriteLine();
        _baseRow = Console.CursorTop - (_visible + 1);
        if (_baseRow < 0) { _interactive = false; return; }

        Draw(RedrawAll);
    }

    private void BeginCore(int index)
    {
        _state[index] = State.Running;
        _percent[index] = 0;
        if (!_interactive) return;

        if (index < _viewStart || index >= _viewStart + _visible - 1)
        {
            _viewStart = Math.Clamp(index - RowsAboveActive, 0, _labels.Count - _visible);
            Draw(RedrawAll);
        }
        else
        {
            Draw(() => DrawIndex(index));
        }
    }

    private void UpdateCore(int index, double? percent)
    {
        if (_state[index] != State.Running) return; // ignore late ticks for finished files

        int whole = percent is { } p ? (int)Math.Clamp(Math.Floor(p), 0, 100) : -1;
        if (whole == _percent[index]) return;        // redraw only when the number changes

        _percent[index] = whole;
        if (_interactive) Draw(() => DrawIndex(index));
    }

    private void CompleteCore(int index, bool success, string? error)
    {
        _state[index] = success ? State.Done : State.Failed;
        _percent[index] = 100;
        _doneCount++;
        if (!success) _errors.Add((_labels[index], error ?? "unknown error"));

        if (_interactive)
        {
            Draw(() => { DrawIndex(index); DrawFooter(); MoveBelowList(); });
            return;
        }

        // Fallback for redirected output
        Console.ForegroundColor = success ? DoneColor : FailColor;
        Console.WriteLine($"{Status(success ? "100%" : "FAIL")} {_labels[index]}");
        Console.ResetColor();
    }

    private void FinishCore()
    {
        if (_interactive)
        {
            _viewStart = Math.Max(0, _labels.Count - _visible); // leave the last files visible
            Draw(RedrawAll);
            Draw(MoveBelowList);
        }

        try { Console.CursorVisible = true; } catch (IOException) { }

        foreach (var (label, error) in _errors)
        {
            Console.ForegroundColor = FailColor;
            Console.WriteLine($"  {label}: {error}");
            Console.ResetColor();
        }
    }

    // ---- drawing (always called under the lock) ----

    private void Draw(Action drawing)
    {
        try { drawing(); }
        catch (IOException) { _interactive = false; }
        catch (ArgumentOutOfRangeException) { _interactive = false; }
    }

    private void RedrawAll()
    {
        for (int row = 0; row < _visible; row++) DrawRow(row);
        DrawFooter();
        MoveBelowList();
    }

    private void DrawIndex(int index)
    {
        int row = index - _viewStart;
        if (row >= 0 && row < _visible)
        {
            DrawRow(row);
            MoveBelowList();
        }
    }

    private void DrawRow(int row)
    {
        int index = _viewStart + row;
        string status;
        ConsoleColor? color;
        switch (_state[index])
        {
            case State.Running:
                status = _percent[index] < 0 ? "..." : $"{_percent[index]}%";
                color = ProgressColor;
                break;
            case State.Done:
                status = "100%";
                color = DoneColor;
                break;
            case State.Failed:
                status = "FAIL";
                color = FailColor;
                break;
            default: // not started: default console color
                status = "0%";
                color = null;
                break;
        }

        // Padded to a constant width (kept a few columns short of the window edge, so it never wraps),
        // which also fully overwrites whatever file was on this row before.
        string text = $"{Status(status)} {_names[index]}".PadRight(_lineWidth);

        Console.SetCursorPosition(0, _baseRow + row);
        if (color is { } c) Console.ForegroundColor = c;
        Console.Write(text);
        if (color is not null) Console.ResetColor();
    }

    private void DrawFooter()
    {
        Console.SetCursorPosition(0, _baseRow + _visible);
        Console.Write($"Completed {_doneCount}/{_labels.Count}".PadRight(_lineWidth));
    }

    private void MoveBelowList() => Console.SetCursorPosition(0, _baseRow + _visible + 1);

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

    private static int SafeHeight()
    {
        try { return Console.WindowHeight; }
        catch (IOException) { return 25; }
    }

    private static string Truncate(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 3)] + "...";
}