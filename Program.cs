using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("        .NET AUDIO TRIMMER CLI          ");
        Console.WriteLine("========================================\n");

        // 1. Choose Mode: Single File or Batch Folder
        Console.WriteLine("Select target type:");
        Console.WriteLine("1. Single File");
        Console.WriteLine("2. Entire Folder (Batch Mode)");
        Console.Write("Choose an option (1 or 2, default is 2): ");
        string modeInput = Console.ReadLine() ?? "2";

        List<string> audioFiles = new List<string>();
        string explorerFolderTarget = "";

        if (modeInput == "1")
        {
            Console.WriteLine("\nOpening file browser window... Please select an audio file.");

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Audio Files (*.mp3;*.wav;*.m4a;*.ogg)|*.mp3;*.wav;*.m4a;*.ogg|All Files (*.*)|*.*";
                openFileDialog.Title = "Select an Audio File to Trim";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Selected file: {Path.GetFileName(filePath)}");
                    Console.ResetColor();

                    audioFiles.Add(filePath);
                    explorerFolderTarget = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("File selection was canceled.");
                    Console.ResetColor();
                    return;
                }
            }
        }
        else
        {
            Console.WriteLine("\nOpening folder browser window... Please select a directory.");

            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select the folder containing audio files to batch trim";
                folderDialog.UseDescriptionForTitle = true;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string targetDirectory = folderDialog.SelectedPath;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Selected folder: {targetDirectory}");
                    Console.ResetColor();

                    explorerFolderTarget = targetDirectory;

                    string[] extensions = { "*.mp3", "*.wav", "*.m4a", "*.ogg" };
                    foreach (var ext in extensions)
                    {
                        audioFiles.AddRange(Directory.GetFiles(targetDirectory, ext));
                    }
                }
                else
                {
                    explorerFolderTarget = Directory.GetCurrentDirectory();
                    string[] extensions = { "*.mp3", "*.wav", "*.m4a", "*.ogg" };
                    foreach (var ext in extensions)
                    {
                        audioFiles.AddRange(Directory.GetFiles(explorerFolderTarget, ext));
                    }
                    Console.WriteLine($"Selection canceled. Defaulting to execution folder: {explorerFolderTarget}");
                }
            }

            if (audioFiles.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No compatible audio files found in the target directory.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"\nFound {audioFiles.Count} matching audio files.");
        }

        // 2. Get Trim Timestamps from User
        Console.Write("\nEnter start time (Format: HH:MM:SS or seconds, e.g., 00:00:10. Enter for 0): ");
        string startTimeStr = Console.ReadLine() ?? "00:00:00";
        if (string.IsNullOrWhiteSpace(startTimeStr)) startTimeStr = "00:00:00";

        double startTimeSeconds = ParseTimeToSeconds(startTimeStr);

        Console.Write("Enter duration to keep (Format: HH:MM:SS or seconds. Press Enter for MAX remaining): ");
        string durationInput = Console.ReadLine() ?? "";

        Console.WriteLine($"\nStarting processing...\n");

        // 3. Process loop
        int successCount = 0;

        for (int i = 0; i < audioFiles.Count; i++)
        {
            string filePath = audioFiles[i];

            if (Path.GetFileNameWithoutExtension(filePath).EndsWith("_trimmed"))
                continue;

            string directory = Path.GetDirectoryName(filePath) ?? explorerFolderTarget;
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            string outputPath = Path.Combine(directory, $"{fileName}_trimmed{extension}");

            double totalDuration = GetMediaDuration(filePath);
            double targetDurationSeconds = 0;
            bool useLivePercentageOutput = false;

            string finalDuration = durationInput;

            if (string.IsNullOrWhiteSpace(finalDuration))
            {
                finalDuration = "";

                if (totalDuration > startTimeSeconds)
                {
                    targetDurationSeconds = totalDuration - startTimeSeconds;
                    useLivePercentageOutput = true;
                }
            }
            else
            {
                targetDurationSeconds = ParseTimeToSeconds(finalDuration);
                useLivePercentageOutput = targetDurationSeconds > 0;
            }

            string fileLabel = $"Processing ({i + 1}/{audioFiles.Count}): {Path.GetFileName(filePath)}";

            // Print the initial file label status without moving to a new line
            Console.Write($"{fileLabel} -> 0%");

            if (TrimAudioWithProgress(filePath, outputPath, startTimeStr, finalDuration, targetDurationSeconds, fileLabel, useLivePercentageOutput))
            {
                successCount++;
            }
        }

        Console.WriteLine("\n========================================");
        Console.WriteLine($"Finished! Successfully processed {successCount} out of {audioFiles.Count} files.");
        Console.WriteLine("========================================");

        // 4. Final Action Prompt
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\nPress ENTER to open the destination folder...");
        Console.ResetColor();
        Console.ReadLine();

        OpenFolderInExplorer(explorerFolderTarget);
    }

    static void OpenFolderInExplorer(string folderPath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{folderPath}\"");
            }
            else
            {
                Process.Start("xdg-open", $"\"{folderPath}\"");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nCould not open folder automatically: {ex.Message}");
        }
    }

    static bool TrimAudioWithProgress(string inputPath, string outputPath, string start, string duration, double targetDurationSec, string fileLabel, bool calculatePercentage)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-ss");
        psi.ArgumentList.Add(start);
        if (!string.IsNullOrWhiteSpace(duration))
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(duration);
        }
        psi.ArgumentList.Add(outputPath);

        try
        {
            using (var process = Process.Start(psi))
            {
                if (process == null) return false;

                using (StreamReader reader = process.StandardError)
                {
                    string? line;
                    Regex timeRegex = new Regex(@"time=(\d+):(\d+):(\d+\.\d+)");

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (calculatePercentage)
                        {
                            var match = timeRegex.Match(line);
                            if (match.Success)
                            {
                                int hours = int.Parse(match.Groups[1].Value);
                                int minutes = int.Parse(match.Groups[2].Value);
                                double seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

                                double currentProgressSeconds = (hours * 3600) + (minutes * 60) + seconds;

                                double pct = (currentProgressSeconds / targetDurationSec) * 100;
                                if (pct > 100) pct = 100;
                                // \r drops the cursor to the beginning of the line, updating the percentage in-place
                                Console.Write($"\r{fileLabel} -> {pct:0}%");
                            }
                        }
                        else
                        {
                            Console.Write($"\r{fileLabel} -> Processing...");
                        }
                    }
                }
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    // Cleanly mark completion on the exact same row, then hit a new line via WriteLine
                    Console.Write($"\r{fileLabel} -> ");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("100% Done");
                    Console.ResetColor();
                    return true;
                }
                else
                {
                    Console.Write($"\r{fileLabel} -> ");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed");
                    Console.ResetColor();
                    return false;
                }
            }
        }
        catch
        {
            Console.Write($"\r{fileLabel} -> ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error Executing");
            Console.ResetColor();
            return false;
        }
    }
    static double GetMediaDuration(string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_entries");
        psi.ArgumentList.Add("format=duration");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("csv=p=0");
        psi.ArgumentList.Add(filePath);
        try
        {
            using (var process = Process.Start(psi))
            {
                if (process == null) return 0;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                {
                    return duration;
                }
            }
        }
        catch { }
        return 0;
    }
    static double ParseTimeToSeconds(string timeStr)
    {
        if (TimeSpan.TryParse(timeStr, out TimeSpan timeSpan))
        {
            return timeSpan.TotalSeconds;
        }
        if (double.TryParse(timeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
        {
            return seconds;
        }
        return 0;
    }
}