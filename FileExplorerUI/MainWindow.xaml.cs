using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;

namespace FileExplorerUI
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly string _initialPath;

        public MainWindow(string? initialPath = null)
        {
            InitializeComponent();
            _initialPath = string.IsNullOrWhiteSpace(initialPath)
                ? ShellMyComputerPath
                : initialPath.Trim();
            _detailsRepeaterLayoutProfile = new EntriesRepeaterLayoutProfile(
                isVertical: true,
                primaryItemExtentProvider: () => Math.Max(32.0, EntryItemMetrics.RowHeight + 4),
                totalItemCountProvider: () => checked((int)Math.Max((uint)_entries.Count, _totalEntries)),
                crossAxisExtentProvider: () => Math.Max(1, DetailsRowWidth),
                viewportPrimaryExtentProvider: () =>
                {
                    double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                        ? DetailsEntriesScrollViewer.ViewportHeight
                        : DetailsEntriesScrollViewer.ActualHeight;
                    return Math.Max(1, viewportHeight);
                });
            _detailsVirtualizingLayout = new FixedExtentVirtualizingLayout(_detailsRepeaterLayoutProfile);
            _workspaceLayoutHost = new WorkspaceLayoutHost(_workspaceShellState);
            _fileManagementCoordinator = new FileManagementCoordinator(_explorerService);
            _entriesContextCommand = new DelegateCommand(ExecuteEntriesContextCommand);
            _engineVersion = _explorerService.GetEngineVersion();
            WireRootAndViewportEvents();
            InitializeViewHostsAndSettings();
            WireShellCommandsAndStartup();
        }

    }

}
