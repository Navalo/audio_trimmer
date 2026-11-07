using AudioTrimmer.Abstractions;
using AudioTrimmer.Domain;

namespace AudioTrimmer.Input;

/// <summary>Opens a file dialog that allows selecting multiple audio files at once.</summary>
public sealed class MultiFileSelector : ISourceSelector
{
    private readonly IReadOnlyCollection<string> _extensions;
    public MultiFileSelector(IReadOnlyCollection<string> extensions) => _extensions = extensions;

    public SourceSelection? Select()
    {
        var patterns = string.Join(";", _extensions.Select(e => "*" + e));
        using var dialog = new OpenFileDialog
        {
            Title = "Select audio files to trim (Ctrl/Shift for multiple)",
            Filter = $"Audio Files ({patterns})|{patterns}|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != DialogResult.OK || dialog.FileNames.Length == 0) return null;

        var files = dialog.FileNames;
        var directory = Path.GetDirectoryName(files[0]) ?? Directory.GetCurrentDirectory();
        return new SourceSelection(directory, files);
    }
}