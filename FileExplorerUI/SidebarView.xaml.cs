using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using FileExplorerUI.Services;
using System;
using System.Collections.Generic;

namespace FileExplorerUI
{
    public sealed partial class SidebarView : UserControl
    {
        private const int CompactTreeProbeLimit = 1;
        private const int CompactTreeChildLimit = 200;
        private readonly ExplorerService _explorerService = new();
        private readonly Dictionary<string, string> _pinnedPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SidebarVisualItem> _visualItems = new();
        private readonly List<TextBlock> _labelBlocks = new();
        private readonly List<TextBlock> _headerBlocks = new();
        private bool _isPinnedExpanded = true;
        private bool _isCloudExpanded = true;
        private bool _isNetworkExpanded = true;
        private bool _isTagsExpanded = true;
        private bool _isCompact;
        private string? _selectedPath;
        private TreeView? _attachedTreeView;
        private readonly CompactSidebarMenuController _compactMenuController = new();
        private bool _compactButtonsAttached;

        public event EventHandler<SidebarNavigateRequestedEventArgs>? NavigateRequested;

        public SidebarView()
        {
            InitializeComponent();
            _compactMenuController.NavigateRequested += (_, path) => NavigateToPath(path);

            _headerBlocks.Add(PinnedGroupTextBlock);
            _headerBlocks.Add(CloudHeaderTextBlock);
            _headerBlocks.Add(NetworkHeaderTextBlock);
            _headerBlocks.Add(TagsHeaderTextBlock);

            RegisterStaticItem("Desktop", DesktopItemBorder, DesktopSelectionIndicator, DesktopTextBlock, SidebarSection.Pinned);
            RegisterStaticItem("Documents", DocumentsItemBorder, DocumentsSelectionIndicator, DocumentsTextBlock, SidebarSection.Pinned);
            RegisterStaticItem("Downloads", DownloadsItemBorder, DownloadsSelectionIndicator, DownloadsTextBlock, SidebarSection.Pinned);
            RegisterStaticItem("Pictures", PicturesItemBorder, PicturesSelectionIndicator, PicturesTextBlock, SidebarSection.Pinned);
            RegisterStaticItem("OneDrive", OneDriveItemBorder, OneDriveSelectionIndicator, OneDriveTextBlock, SidebarSection.Extra, selectable: false);
            RegisterStaticItem("TagWork", TagWorkItemBorder, TagWorkSelectionIndicator, TagWorkTextBlock, SidebarSection.Extra, selectable: false);
            RegisterStaticItem("TagFocus", TagFocusItemBorder, TagFocusSelectionIndicator, TagFocusTextBlock, SidebarSection.Extra, selectable: false);
            RegisterStaticItem("TagArchive", TagArchiveItemBorder, TagArchiveSelectionIndicator, TagArchiveTextBlock, SidebarSection.Extra, selectable: false);

            RegisterGroupHover(PinnedGroupBorder);
            RegisterGroupHover(TreeCompactBorder);
            RegisterGroupHover(CloudGroupBorder);
            RegisterGroupHover(NetworkGroupBorder);
            RegisterGroupHover(TagsGroupBorder);

            ToolTipService.SetToolTip(PinnedGroupBorder, "固定");
            ToolTipService.SetToolTip(CloudGroupBorder, "云盘");
            ToolTipService.SetToolTip(NetworkGroupBorder, "网络");
            ToolTipService.SetToolTip(TagsGroupBorder, "标签");
            ToolTipService.SetToolTip(TreeCompactBorder, "我的电脑");
        }

        public void ConfigurePinnedPaths(string desktopPath, string documentsPath, string downloadsPath, string picturesPath)
        {
            _pinnedPaths["Desktop"] = desktopPath;
            _pinnedPaths["Documents"] = documentsPath;
            _pinnedPaths["Downloads"] = downloadsPath;
            _pinnedPaths["Pictures"] = picturesPath;
            SetSelectedPath(_selectedPath);
        }

        public void SetExtraItems(IEnumerable<SidebarNavItemModel> items)
        {
            // Extras are statically composed in XAML to match the pinned-group presentation.
        }

        public void AttachTreeView(TreeView treeView)
        {
            _attachedTreeView = treeView;
            if (treeView.Parent is Panel currentParent)
            {
                currentParent.Children.Remove(treeView);
            }

            treeView.Margin = new Thickness(0);
            treeView.Padding = new Thickness(0, 0, 8, 0);
            treeView.HorizontalAlignment = HorizontalAlignment.Stretch;
            treeView.SetValue(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled);
            treeView.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            treeView.SetValue(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled);
            treeView.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);

            TreeHostGrid.Children.Clear();
            TreeHostGrid.Children.Add(treeView);
        }

        public void SetCompact(bool compact)
        {
            _isCompact = compact;
            if (!compact)
            {
                _compactMenuController.Hide();
            }

            if (compact)
            {
                AttachCompactButtons();
            }
            else
            {
                DetachCompactButtons();
            }

            SidebarScrollViewer.Padding = compact ? new Thickness(0, 0, 0, 0) : new Thickness(0, 0, 12, 0);
            CompactButtonsPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            PinnedSectionPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            TreeSectionPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CloudSectionPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            NetworkSectionPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            TagsSectionPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            TreeCompactBorder.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            TreeHostBorder.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            foreach (TextBlock block in _labelBlocks)
            {
                block.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            }

            foreach (TextBlock block in _headerBlocks)
            {
                block.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            }

            PinnedChevron.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CloudChevron.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            NetworkChevron.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            TagsChevron.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            ApplyGroupHeaderLayout(PinnedGroupBorder, PinnedGroupGrid, PinnedGroupSelectionIndicator, PinnedGroupIcon, compact);
            ApplyGroupHeaderLayout(TreeCompactBorder, TreeCompactGrid, TreeCompactSelectionIndicator, TreeCompactIcon, compact);
            ApplyGroupHeaderLayout(CloudGroupBorder, CloudGroupGrid, CloudGroupSelectionIndicator, CloudGroupIcon, compact);
            ApplyGroupHeaderLayout(NetworkGroupBorder, NetworkGroupGrid, NetworkGroupSelectionIndicator, NetworkGroupIcon, compact);
            ApplyGroupHeaderLayout(TagsGroupBorder, TagsGroupGrid, TagsGroupSelectionIndicator, TagsGroupIcon, compact);

            PinnedItemsPanel.Visibility = compact ? Visibility.Collapsed : (_isPinnedExpanded ? Visibility.Visible : Visibility.Collapsed);
            CloudItemsPanel.Visibility = compact ? Visibility.Collapsed : (_isCloudExpanded ? Visibility.Visible : Visibility.Collapsed);
            NetworkItemsPanel.Visibility = compact
                ? Visibility.Collapsed
                : (_isNetworkExpanded && NetworkItemsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed);
            TagsItemsPanel.Visibility = compact ? Visibility.Collapsed : (_isTagsExpanded ? Visibility.Visible : Visibility.Collapsed);
            SetSelectedPath(_selectedPath);
        }

        private void AttachCompactButtons()
        {
            if (_compactButtonsAttached)
            {
                return;
            }

            RemoveFromParent(PinnedGroupBorder);
            RemoveFromParent(TreeCompactBorder);
            RemoveFromParent(CloudGroupBorder);
            RemoveFromParent(NetworkGroupBorder);
            RemoveFromParent(TagsGroupBorder);

            CompactButtonsPanel.Children.Clear();
            CompactButtonsPanel.Children.Add(PinnedGroupBorder);
            CompactButtonsPanel.Children.Add(TreeCompactBorder);
            CompactButtonsPanel.Children.Add(CloudGroupBorder);
            CompactButtonsPanel.Children.Add(NetworkGroupBorder);
            CompactButtonsPanel.Children.Add(TagsGroupBorder);
            _compactButtonsAttached = true;
        }

        private void DetachCompactButtons()
        {
            if (!_compactButtonsAttached)
            {
                return;
            }

            RemoveFromParent(PinnedGroupBorder);
            RemoveFromParent(TreeCompactBorder);
            RemoveFromParent(CloudGroupBorder);
            RemoveFromParent(NetworkGroupBorder);
            RemoveFromParent(TagsGroupBorder);

            PinnedSectionPanel.Children.Insert(0, PinnedGroupBorder);
            TreeSectionPanel.Children.Insert(0, TreeCompactBorder);
            CloudSectionPanel.Children.Insert(0, CloudGroupBorder);
            NetworkSectionPanel.Children.Insert(0, NetworkGroupBorder);
            TagsSectionPanel.Children.Insert(0, TagsGroupBorder);
            _compactButtonsAttached = false;
        }

        private static void RemoveFromParent(FrameworkElement element)
        {
            if (element.Parent is Panel panel)
            {
                panel.Children.Remove(element);
            }
        }

        public void SetSelectedPath(string? path)
        {
            _selectedPath = path;
            ClearSelection();

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (_isCompact)
            {
                return;
            }

            SidebarVisualItem? selected = null;
            int bestLength = -1;
            string current = NormalizePath(path);

            foreach (SidebarVisualItem item in _visualItems)
            {
                if (!item.Selectable)
                {
                    continue;
                }

                string? itemPath = item.Path;
                if (string.IsNullOrWhiteSpace(itemPath) && item.Section == SidebarSection.Pinned)
                {
                    _pinnedPaths.TryGetValue(item.Key, out itemPath);
                }

                if (string.IsNullOrWhiteSpace(itemPath))
                {
                    continue;
                }

                string candidate = NormalizePath(itemPath);
                bool matched = string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)
                    || current.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase);
                if (!matched || candidate.Length <= bestLength)
                {
                    continue;
                }

                bestLength = candidate.Length;
                selected = item;
            }

            if (selected is null)
            {
                return;
            }

            if (selected.Section == SidebarSection.Pinned && !_isPinnedExpanded)
            {
                PinnedGroupSelectionIndicator.Visibility = Visibility.Visible;
                PinnedGroupBorder.Background = SelectedBackgroundBrush();
                return;
            }

            selected.Indicator.Visibility = Visibility.Visible;
            selected.Border.Background = SelectedBackgroundBrush();
        }

        private void RegisterStaticItem(string key, Border border, Border indicator, TextBlock labelBlock, SidebarSection section, bool selectable = true)
        {
            border.PointerEntered += Item_PointerEntered;
            border.PointerExited += Item_PointerExited;
            _labelBlocks.Add(labelBlock);
            _visualItems.Add(new SidebarVisualItem(key, null, border, indicator, labelBlock, section, selectable));
        }

        private void PopulateDynamicItems(StackPanel panel, IEnumerable<SidebarNavItemModel> items, SidebarSection section)
        {
            panel.Children.Clear();
            _visualItems.RemoveAll(item => item.Section == section);

            foreach (SidebarNavItemModel item in items)
            {
                Border border = CreateDynamicItem(item, section, out Border indicator, out TextBlock labelBlock);
                panel.Children.Add(border);
                _labelBlocks.Add(labelBlock);
                _visualItems.Add(new SidebarVisualItem(item.Key, item.Path, border, indicator, labelBlock, section, item.Selectable));
            }

            SetSelectedPath(_selectedPath);
        }

        private Border CreateDynamicItem(SidebarNavItemModel item, SidebarSection section, out Border indicator, out TextBlock labelBlock)
        {
            var grid = new Grid
            {
                ColumnSpacing = 0
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            indicator = new Border
            {
                Width = 3,
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = (CornerRadius)Application.Current.Resources["ListViewItemSelectionIndicatorCornerRadius"],
                Background = (Brush)Application.Current.Resources["ListViewItemSelectionIndicatorBrush"],
                Visibility = Visibility.Collapsed
            };

            var icon = new FontIcon
            {
                Width = 12,
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Glyph = item.Glyph
            };
            Grid.SetColumn(icon, 1);

            labelBlock = new TextBlock
            {
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Text = item.Label,
                Visibility = _isCompact ? Visibility.Collapsed : Visibility.Visible
            };
            Grid.SetColumn(labelBlock, 2);

            grid.Children.Add(indicator);
            grid.Children.Add(icon);
            grid.Children.Add(labelBlock);

            var border = new Border
            {
                Height = 32,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(Colors.Transparent),
                CornerRadius = (CornerRadius)Application.Current.Resources["ListViewItemCornerRadius"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = grid,
                Tag = item
            };

            if (item.Selectable && !string.IsNullOrWhiteSpace(item.Path))
            {
                border.PointerPressed += DynamicItem_Click;
            }

            border.PointerEntered += Item_PointerEntered;
            border.PointerExited += Item_PointerExited;

            return border;
        }

        private void PinnedGroup_Click(object sender, PointerRoutedEventArgs e)
        {
            if (_isCompact)
            {
                ShowPinnedCompactFlyout(PinnedGroupBorder);
                return;
            }

            _isPinnedExpanded = !_isPinnedExpanded;
            PinnedItemsPanel.Visibility = _isPinnedExpanded ? Visibility.Visible : Visibility.Collapsed;
            PinnedChevron.Glyph = _isPinnedExpanded ? "\uE70E" : "\uE70D";

            if (!_isPinnedExpanded)
            {
                SetSelectedPath(_selectedPath);
                return;
            }

            SetSelectedPath(_selectedPath);
        }

        private void CloudGroup_Click(object sender, PointerRoutedEventArgs e)
        {
            if (_isCompact)
            {
                ShowStaticItemsFlyout(
                    CloudGroupBorder,
                    new[]
                    {
                        new CompactFlyoutItem("OneDrive", "\uE753", null, false)
                    });
                return;
            }

            _isCloudExpanded = !_isCloudExpanded;
            CloudItemsPanel.Visibility = _isCloudExpanded ? Visibility.Visible : Visibility.Collapsed;
            CloudChevron.Glyph = _isCloudExpanded ? "\uE70E" : "\uE70D";
        }

        private void NetworkGroup_Click(object sender, PointerRoutedEventArgs e)
        {
            if (_isCompact)
            {
                ShowStaticItemsFlyout(
                    NetworkGroupBorder,
                    new[]
                    {
                        new CompactFlyoutItem("暂无项目", "\uE774", null, false)
                    });
                return;
            }

            if (NetworkItemsPanel.Children.Count == 0)
            {
                NetworkItemsPanel.Visibility = Visibility.Collapsed;
                _isNetworkExpanded = !_isNetworkExpanded;
                NetworkChevron.Glyph = _isNetworkExpanded ? "\uE70E" : "\uE70D";
                return;
            }

            _isNetworkExpanded = !_isNetworkExpanded;
            NetworkItemsPanel.Visibility = _isNetworkExpanded ? Visibility.Visible : Visibility.Collapsed;
            NetworkChevron.Glyph = _isNetworkExpanded ? "\uE70E" : "\uE70D";
        }

        private void TagsGroup_Click(object sender, PointerRoutedEventArgs e)
        {
            if (_isCompact)
            {
                ShowStaticItemsFlyout(
                    TagsGroupBorder,
                    new[]
                    {
                        new CompactFlyoutItem("工作", "\uE8EC", null, false),
                        new CompactFlyoutItem("重点", "\uE8EC", null, false),
                        new CompactFlyoutItem("归档", "\uE8EC", null, false)
                    });
                return;
            }

            _isTagsExpanded = !_isTagsExpanded;
            TagsItemsPanel.Visibility = _isTagsExpanded ? Visibility.Visible : Visibility.Collapsed;
            TagsChevron.Glyph = _isTagsExpanded ? "\uE70E" : "\uE70D";
        }

        private void TreeCompactBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isCompact)
            {
                return;
            }

            ShowTreeCompactFlyout(TreeCompactBorder);
        }

        private void PinnedItem_Click(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not string key)
            {
                return;
            }

            if (!_pinnedPaths.TryGetValue(key, out string? path) || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            PinnedGroupSelectionIndicator.Visibility = Visibility.Collapsed;
            PinnedGroupBorder.Background = TransparentBrush();
            SetSelectedPath(path);
            NavigateRequested?.Invoke(this, new SidebarNavigateRequestedEventArgs(path));
        }

        private void DynamicItem_Click(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not SidebarNavItemModel item || string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            SetSelectedPath(item.Path);
            NavigateRequested?.Invoke(this, new SidebarNavigateRequestedEventArgs(item.Path));
        }

        private void ClearSelection()
        {
            PinnedGroupSelectionIndicator.Visibility = Visibility.Collapsed;
            PinnedGroupBorder.Background = TransparentBrush();

            foreach (SidebarVisualItem item in _visualItems)
            {
                item.Indicator.Visibility = Visibility.Collapsed;
                item.Border.Background = TransparentBrush();
            }
        }

        private void RegisterGroupHover(Border border)
        {
            border.PointerEntered += Group_PointerEntered;
            border.PointerExited += Group_PointerExited;
        }

        private void Group_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
            {
                return;
            }

            if (IsGroupSelected(border))
            {
                return;
            }

            border.Background = HoverBackgroundBrush();
        }

        private void Group_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
            {
                return;
            }

            if (IsGroupSelected(border))
            {
                border.Background = SelectedBackgroundBrush();
                return;
            }

            border.Background = TransparentBrush();
        }

        private bool IsGroupSelected(Border border)
        {
            if (ReferenceEquals(border, PinnedGroupBorder))
            {
                return PinnedGroupSelectionIndicator.Visibility == Visibility.Visible;
            }

            return false;
        }

        private void Item_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
            {
                return;
            }

            SidebarVisualItem? item = FindVisualItem(border);
            if (item is not null && IsItemSelected(item))
            {
                return;
            }

            border.Background = HoverBackgroundBrush();
        }

        private void Item_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border)
            {
                return;
            }

            SidebarVisualItem? item = FindVisualItem(border);
            if (item is not null && IsItemSelected(item))
            {
                border.Background = SelectedBackgroundBrush();
                return;
            }

            border.Background = TransparentBrush();
        }

        private SidebarVisualItem? FindVisualItem(Border border)
        {
            foreach (SidebarVisualItem item in _visualItems)
            {
                if (ReferenceEquals(item.Border, border))
                {
                    return item;
                }
            }

            return null;
        }

        private bool IsItemSelected(SidebarVisualItem item)
        {
            if (string.IsNullOrWhiteSpace(_selectedPath) || !item.Selectable)
            {
                return false;
            }

            string? itemPath = item.Path;
            if (string.IsNullOrWhiteSpace(itemPath) && item.Section == SidebarSection.Pinned)
            {
                _pinnedPaths.TryGetValue(item.Key, out itemPath);
            }

            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return false;
            }

            string current = NormalizePath(_selectedPath);
            string candidate = NormalizePath(itemPath);
            return string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)
                || current.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return System.IO.Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return path.TrimEnd('\\');
            }
        }

        private static Brush SelectedBackgroundBrush()
        {
            return (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"];
        }

        private static Brush HoverBackgroundBrush()
        {
            if (Application.Current.Resources.TryGetValue("ListViewItemBackgroundPointerOver", out object? brush) && brush is Brush resolved)
            {
                return resolved;
            }

            return TransparentBrush();
        }

        private static Brush TransparentBrush()
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        private void ShowPinnedCompactFlyout(FrameworkElement target)
        {
            _compactMenuController.Show(target, BuildPinnedCompactItems());
        }

        private void ShowStaticItemsFlyout(FrameworkElement target, IReadOnlyList<CompactFlyoutItem> items)
        {
            _compactMenuController.Show(target, BuildStaticCompactItems(items));
        }

        private void ShowTreeCompactFlyout(FrameworkElement target)
        {
            _compactMenuController.Show(target, BuildTreeCompactItems());
        }

        private IReadOnlyList<CompactSidebarMenuItem> BuildPinnedCompactItems()
        {
            return new[]
            {
                CreatePinnedCompactItem("桌面", "\uE80F", "Desktop"),
                CreatePinnedCompactItem("文档", "\uE8A5", "Documents"),
                CreatePinnedCompactItem("下载", "\uE896", "Downloads"),
                CreatePinnedCompactItem("图片", "\uE91B", "Pictures")
            };
        }

        private CompactSidebarMenuItem CreatePinnedCompactItem(string label, string glyph, string key)
        {
            bool available = _pinnedPaths.TryGetValue(key, out string? path) && !string.IsNullOrWhiteSpace(path);
            return new CompactSidebarMenuItem(label, glyph, available ? path : null, null, available);
        }

        private IReadOnlyList<CompactSidebarMenuItem> BuildStaticCompactItems(IReadOnlyList<CompactFlyoutItem> items)
        {
            var result = new List<CompactSidebarMenuItem>(items.Count);
            foreach (CompactFlyoutItem item in items)
            {
                result.Add(new CompactSidebarMenuItem(item.Label, item.Glyph, item.Path, null, item.Selectable));
            }

            return result;
        }

        private IReadOnlyList<CompactSidebarMenuItem> BuildTreeCompactItems()
        {
            var result = new List<CompactSidebarMenuItem>();
            if (_attachedTreeView is null || _attachedTreeView.RootNodes.Count == 0)
            {
                return result;
            }

            foreach (TreeViewNode root in _attachedTreeView.RootNodes)
            {
                if (root.Content is not SidebarTreeEntry rootEntry || !string.Equals(rootEntry.FullPath, "shell:mycomputer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (TreeViewNode driveNode in root.Children)
                {
                    if (driveNode.Content is SidebarTreeEntry driveEntry)
                    {
                        result.Add(CreateTreeCompactItem(driveEntry, depth: 0));
                    }
                }
            }

            return result;
        }

        private CompactSidebarMenuItem CreateTreeCompactItem(SidebarTreeEntry entry, int depth)
        {
            bool hasChildren = _explorerService.EnumerateSidebarDirectoriesAsync(entry.FullPath, default, CompactTreeProbeLimit)
                .GetAwaiter()
                .GetResult()
                .Count > 0;
            Func<IReadOnlyList<CompactSidebarMenuItem>>? loader = hasChildren
                ? () => LoadTreeCompactChildren(entry.FullPath, depth + 1)
                : null;

            return new CompactSidebarMenuItem(entry.Name, "\uE8B7", entry.FullPath, null, true, loader);
        }

        private IReadOnlyList<CompactSidebarMenuItem> LoadTreeCompactChildren(string path, int depth)
        {
            List<SidebarTreeEntry> childEntries = _explorerService.EnumerateSidebarDirectoriesAsync(path, default, CompactTreeChildLimit)
                .GetAwaiter()
                .GetResult();
            var children = new List<CompactSidebarMenuItem>(childEntries.Count);
            foreach (SidebarTreeEntry child in childEntries)
            {
                children.Add(CreateTreeCompactItem(child, depth));
            }

            return children;
        }

        private void NavigateToPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            SetSelectedPath(path);
            NavigateRequested?.Invoke(this, new SidebarNavigateRequestedEventArgs(path));
        }

        private static void ApplyGroupHeaderLayout(Border border, Grid grid, Border indicator, FontIcon icon, bool compact)
        {
            border.Width = compact ? 32 : double.NaN;
            border.Height = 32;
            border.Padding = new Thickness(0, 4, 0, 4);
            border.Margin = compact ? new Thickness(0, 2, 0, 2) : new Thickness(0, 2, 0, 2);
            border.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;

            indicator.Visibility = compact ? Visibility.Collapsed : indicator.Visibility;
            icon.FontSize = 12;

            grid.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(10);
            grid.ColumnDefinitions[1].Width = compact ? new GridLength(32) : new GridLength(12);
            grid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[3].Width = compact ? new GridLength(0) : new GridLength(26);
        }

        private sealed record SidebarVisualItem(string Key, string? Path, Border Border, Border Indicator, TextBlock Label, SidebarSection Section, bool Selectable);
        private sealed record CompactFlyoutItem(string Label, string Glyph, string? Path, bool Selectable);

        private enum SidebarSection
        {
            Pinned,
            Extra
        }
    }

    public sealed class SidebarNavItemModel
    {
        public SidebarNavItemModel(string key, string label, string? path, string glyph, bool selectable = true)
        {
            Key = key;
            Label = label;
            Path = path;
            Glyph = glyph;
            Selectable = selectable;
        }

        public string Key { get; }
        public string Label { get; }
        public string? Path { get; }
        public string Glyph { get; }
        public bool Selectable { get; }
    }

    public sealed class SidebarNavigateRequestedEventArgs : EventArgs
    {
        public SidebarNavigateRequestedEventArgs(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
