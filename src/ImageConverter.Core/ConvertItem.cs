using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageConverter.Core;

public enum ItemStatus { Pending, Converting, Done, Skipped, Failed, Cancelled }

/// <summary>One source image in the list.</summary>
public partial class ConvertItem : ObservableObject
{
    public ConvertItem(string fullPath, string baseFolder)
    {
        FullPath = fullPath;
        BaseFolder = baseFolder;
        try { SizeBytes = new FileInfo(fullPath).Length; } catch (IOException) { }
    }

    public string FullPath { get; }

    /// <summary>The folder the user opened; used to rebuild the folder tree in another output folder.</summary>
    public string BaseFolder { get; }

    public string FileName => Path.GetFileName(FullPath);
    public string Stem => Path.GetFileNameWithoutExtension(FullPath);
    public string SourceFormat => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    public long SizeBytes { get; }

    [ObservableProperty] private string? _targetPath;
    [ObservableProperty] private ItemStatus _status;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private long _outputBytes;

    public string TargetName => TargetPath == null ? "" : Path.GetFileName(TargetPath);

    partial void OnTargetPathChanged(string? value) => OnPropertyChanged(nameof(TargetName));

    /// <summary>What screen readers announce for a row in the list.</summary>
    public override string ToString() => FileName;
}
