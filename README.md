# 🎵 Batch Audio Trimmer CLI

A high-performance, lightweight **.NET console application** designed to trim individual audio files or batch-process entire directories. By leveraging the speed of **FFmpeg** under the hood, the application slices audio files seamlessly with interactive file pickers and real-time metric tracking.

---

## 🚀 Key Features

* **Dual Target Operations:** Process a **Single File** or an **Entire Folder (Batch Mode)** seamlessly.
* **Native Windows Dialogs:** Bypasses tedious path-typing by using native Windows **File Open** and **Folder Browser** dialog boxes.
* **In-Place Percentage Indicators:** Tracks progress (**0% to 100%**) right next to each file name on the **same terminal line** to prevent console clutter.
* **Smart Max-Duration Engine:** Press **Enter** on the duration prompt to automatically slice from your requested start point straight to the absolute end of the file.
* **Special Character Protection:** Safely handles long audio file names containing emojis, brackets, or unique symbols (like `｜`).
* **Instant Output Navigation:** Automatically prompts and opens the destination directory in **File Explorer** upon completion.

---

## 🛠️ Prerequisites & Dependencies

To run this application, your system needs the following dependencies installed and configured:

### 1. .NET 8.0 SDK
The project is built on the `.NET 8.0 Windows Desktop SDK` framework (required for rendering native system dialog popups).
* Download and install the SDK from the [Official .NET Download Page](https://microsoft.com).

### 2. FFmpeg & FFprobe
The application handles processing by calling `ffmpeg` and `ffprobe` as background process threads. They **must be added to your system's Environment PATH** so the app can call them from any directory.
* **Windows (Quickest way via terminal):**
  ```cmd
  winget install Gyan.FFmpeg
  ```
* **Verification:** Open a fresh Command Prompt window and type `ffmpeg -version` and `ffprobe -version`. If it prints version information, you are good to go!

## 🏃 How to Run the Application

Follow these steps to run the application in development mode:

1. Open your terminal (Command Prompt, PowerShell, or terminal inside VS Code).
2. Navigate to your project root folder:
   ```bash
   cd D:\codes\AudioTrimmer
   ```
3. Clear out any previous locked build artifacts (if applicable):
   ```bash
   dotnet clean
   ```
4. Build and execute the application:
   ```bash
   dotnet run
   ```

---

## 💻 How to Use the App

1. **Select Mode:** Type `1` for an isolated Single File or `2` for an Entire Folder, then press **Enter**.
2. **Visual Picker:** A native Windows browser window will slide open. Click on your target file/folder and press **Select/Open**.
3. **Set Start Time:** Type the start position timestamp (e.g., `00:01:30` or in absolute seconds like `90`). Press **Enter** for `00:00:00`.
4. **Set Duration:** Enter the length you want to keep. **Press Enter (leave blank) to automatically slice up to the maximum remaining length of the track.**
5. **Watch it Sync:** The app will stream the percentage updates (`0% -> 100%`) inline without spamming your terminal.
6. **Open Explorer:** Press **Enter** one final time at the confirmation screen to automatically launch File Explorer right into the directory containing your newly generated `_trimmed` audio copies.
