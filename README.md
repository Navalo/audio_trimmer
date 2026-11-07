# .NET Audio Trimmer

A Windows console tool that trims many audio files in one go. Pick your files in a normal file dialog, enter a start time and (optionally) how long to keep, and it trims each file with FFmpeg while showing a live, color-coded progress list. Tags and album art are preserved.

```
[100%] 01 - Song One.mp3          (green  = done)
[ 42%] 02 - Song Two.mp3          (blue   = in progress)
[0%]   03 - Song Three.mp3        (default = not started)
Completed 1/3
```

---

## Features

- **Multi-file selection** – press Enter, then Ctrl/Shift-click as many files as you like.
- **Flexible time input** – `90`, `1:30`, `00:01:30` or `00:00:10.5`.
- **Live progress per file** – one `[xx%] name` line per file, redrawn in place (no scrolling spam).
  - default color = not started, blue (cyan) = running, green = done, red = failed.
  - Very long lists scroll in a window that always fits your console, with a `Completed X/N` footer.
- **Keeps metadata and cover art** – title/artist/album tags are copied; embedded cover images are kept for `.mp3` and `.m4a`.
- **Safe output** – results go to a `trimmed` sub-folder; your originals are never overwritten.
- **Cancel any time** – Ctrl+C stops the running FFmpeg process cleanly.
- **Clear errors** – failed files are marked `[FAIL]` and their FFmpeg error is listed at the end.

---

## Requirements

| Requirement | Notes |
|---|---|
| Windows | The file dialog uses Windows Forms. |
| [.NET 8 SDK](https://dotnet.microsoft.com/download) | To build and run from source. |
| [FFmpeg](https://ffmpeg.org/download.html) | `ffmpeg` **and** `ffprobe` must be on your `PATH`. |

Install FFmpeg quickly with winget:

```powershell
winget install Gyan.FFmpeg
```

Check it worked (open a **new** terminal first):

```powershell
ffmpeg -version
ffprobe -version
```

---

## Build and run

```powershell
cd AudioTrimmer
dotnet build
dotnet run
```

Create a standalone executable (still needs FFmpeg on PATH):

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

The result is in `bin\Release\net8.0-windows\win-x64\publish\`.

> **Terminal tip:** run it in **Windows Terminal** or **cmd/PowerShell**. Consoles that cannot move the cursor (for example the VS Code *Debug Console*) cannot redraw lines in place, so the tool falls back to printing one plain line per finished file.

---

## How to use

1. **Start the program.** You will see *"Press ENTER to select audio files…"*.
2. **Press Enter.** A file dialog opens. Select one or more audio files (Ctrl/Shift for multiple) and click Open. Closing the dialog exits the program.
3. **Enter the start time.** Press Enter alone to start from `0`.
4. **Enter the duration to keep.** Press Enter alone to keep everything from the start time to the end of each file.
5. **Watch the progress list.** Each file is trimmed in turn.
6. **Finish.** When prompted:
   - **Enter** – open the output folder.
   - **Esc** – exit.

### Time formats

| You type | Meaning |
|---|---|
| `90` | 90 seconds |
| `1:30` | 1 minute 30 seconds |
| `00:01:30` | 1 minute 30 seconds |
| `00:00:10.5` | 10.5 seconds |

Invalid input is rejected and you are asked again. Duration must be greater than zero.

### Example

Keep 30 seconds starting at 1:15 from every selected file:

```
Enter start time ...:   1:15
Enter duration to keep ...:   30
```

---

## Output

- Trimmed files are written to a **`trimmed`** folder next to the first file you selected, using the **same file names**.
- Existing files in `trimmed` with the same name are overwritten.
- If an output path would equal the input file, `_trimmed` is added to the name so the source is never clobbered.
- Supported input types in the dialog: `.mp3`, `.wav`, `.m4a`, `.ogg` (choose *All Files* in the dialog to try other formats).

### Metadata and cover art

- Tags (title, artist, album, …) are copied from the source.
- Cover art is preserved for `.mp3` and `.m4a`. The picture is read from a second, un-seeked input of the same file and copied without re-encoding, because seeking would otherwise discard the one-frame image.
- `.wav` cannot hold embedded art; `.ogg` stores it inside its tags, which are copied.
- The audio itself is **re-encoded** with FFmpeg's default codec for the container.

---

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Every file was trimmed successfully |
| `1` | Selection canceled (dialog closed) |
| `2` | Canceled with Ctrl+C |
| `3` | Finished, but at least one file failed |

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `Could not start 'ffmpeg' (is it installed and on PATH?)` | Install FFmpeg and open a **new** terminal so the updated `PATH` is picked up. |
| File dialog does not appear | Make sure you run the built app (`dotnet run`) on Windows. `Main` must stay synchronous with `[STAThread]` for Windows Forms dialogs. |
| Every file shows `[FAIL]` | Read the error printed under the list; it is FFmpeg's last error line (bad path, unsupported codec, locked file…). |
| Progress shows `[...]` instead of a number | `ffprobe` could not read the file's duration, so no percentage can be calculated. The trim itself still works. |
| Lines look garbled or repeat | Use Windows Terminal / cmd. Consoles without cursor control use the plain fallback output. |
| Folder does not open | The tool prints `Could not open folder automatically` with the reason; open the `trimmed` folder manually. |
| Build error `already contains a definition` | A file exists twice in your project folder. Delete old copies so each `.cs` file appears once. |

---

## Project structure

```
AudioTrimmer/
├── AudioTrimmer.csproj
├── Program.cs                     Composition root (wires everything together)
├── Abstractions/
│   └── Interfaces.cs              All interfaces
├── Domain/
│   └── Models.cs                  Records, enums, supported formats
├── Infrastructure/
│   ├── ProcessRunner.cs           Runs external processes (+ InlineProgress)
│   └── Ffmpeg.cs                  FfprobeMediaProbe, FfmpegAudioTrimmer
├── Input/
│   ├── TimeParser.cs              "90", "1:30", "00:01:30" → seconds
│   └── Selectors.cs               MultiFileSelector (file dialog)
├── Services/
│   └── Services.cs                Output locator, job factory, batch service,
│                                  folder opener, TrimApplication (the flow)
└── UI/
    └── ConsoleUI.cs               Message writer, prompter, progress reporter
```

---

## Design (SOLID)

The program is built around small interfaces and one composition root (`Program.cs`), so every part can be replaced or tested in isolation.

| Principle | How it is applied |
|---|---|
| **S**ingle responsibility | One job per class: `TimeParser` parses, `ProcessRunner` runs processes, `FfmpegAudioTrimmer` builds the FFmpeg command, `TrimJobFactory` plans a job, `BatchTrimService` loops over files, `TrimApplication` runs the flow. |
| **O**pen/closed | Add behavior by adding classes: a new `ISourceSelector`, `IOutputLocator`, `IAudioTrimmer` or platform `IFolderOpener` needs no edits to existing code. |
| **L**iskov substitution | Implementations honor their contracts (selectors return `null` on cancel, trimmers return a `TrimResult` instead of throwing), so they are interchangeable. |
| **I**nterface segregation | UI is split into `IMessageWriter`, `IUserPrompter` and `IProgressReporter`; media tooling into `IMediaProbe` and `IAudioTrimmer`. |
| **D**ependency inversion | Services depend on interfaces only; concrete types are created in `Program.cs`. |

### Flow

```
Program → TrimApplication
            ├─ IUserPrompter       (Enter to start, ask start/duration, Enter/Esc at the end)
            ├─ ISourceSelector     (multi-file dialog)
            └─ IBatchTrimService
                 ├─ IOutputLocator      (trimmed folder, safe names)
                 ├─ ITrimJobFactory     (output path + expected length, via IMediaProbe)
                 ├─ IAudioTrimmer       (FFmpeg, via IProcessRunner)
                 └─ IProgressReporter   (colored in-place progress)
```

### Extending

- **Change the output folder name:** edit `new SubfolderOutputLocator("trimmed")` in `Program.cs`.
- **Add formats to the dialog:** edit `AudioFormats.Supported` in `Models.cs`.
- **Keep cover art for more formats:** add the extension to `CoverArtExtensions` in `Ffmpeg.cs` (only if FFmpeg can embed pictures in that container).
- **Lossless, faster trimming:** add `-c:a copy` to the argument list in `FfmpegAudioTrimmer`.
- **Different progress colors:** change the color constants at the top of `ConsoleProgressReporter` (for example `ConsoleColor.Blue` instead of `Cyan`).
- **New platforms or a GUI:** implement `ISourceSelector`, `IUserPrompter` and `IProgressReporter` for the new front end and register them in `Program.cs`.

---

## Known limitations

- Windows only, because of the Windows Forms file dialog.
- All files in one run come from the single folder shown in the dialog (the dialog selects within one folder).
- Folders are not scanned; pick files directly.
- Audio is re-encoded rather than stream-copied.
- If you cancel with Ctrl+C, a partially written file for the file being processed may remain in `trimmed`.