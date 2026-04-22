using FileExplorerUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;

namespace FileExplorerUI.Controls;

public sealed partial class FileOperationProgressOverlay : UserControl
{
    private Action? _cancelAction;

    public FileOperationProgressOverlay()
    {
        InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Margin = new Thickness(-8);
        Visibility = Visibility.Collapsed;
    }

    public void Show(string title, string message, string cancelText, Action cancelAction)
    {
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = message;
        CurrentItemTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = string.Empty;
        OperationProgressBar.Value = 0;
        OperationProgressBar.Maximum = 1;
        CancelButton.Content = cancelText;
        CancelButton.IsEnabled = true;
        _cancelAction = cancelAction;
        Visibility = Visibility.Visible;
    }

    public void Update(FileOperationProgress progress, string progressFormat)
    {
        long totalUnits = progress.HasByteProgress
            ? Math.Max(1, progress.TotalBytes)
            : Math.Max(1, progress.TotalItems);
        long completedUnits = progress.HasByteProgress
            ? Math.Clamp(progress.CompletedBytes, 0, totalUnits)
            : Math.Clamp(progress.CompletedItems, 0, totalUnits);
        OperationProgressBar.Maximum = totalUnits;
        OperationProgressBar.Value = completedUnits;
        CurrentItemTextBlock.Text = GetDisplayPath(progress.CurrentPath);
        ProgressTextBlock.Text = progress.HasByteProgress
            ? $"{FormatBytes(completedUnits)} / {FormatBytes(totalUnits)}"
            : string.Format(progressFormat, completedUnits, totalUnits);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.0} {units[unitIndex]}";
    }

    public void MarkCanceling(string message)
    {
        MessageTextBlock.Text = message;
        CancelButton.IsEnabled = false;
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _cancelAction = null;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancelAction?.Invoke();
    }

    private static string GetDisplayPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}
