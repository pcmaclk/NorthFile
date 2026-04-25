using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NorthFileUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Windows.Foundation;

namespace NorthFileUI
{
    public sealed partial class SidebarView : UserControl
    {
        private const int CompactTreeChildLimit = 200;
        private const double PinnedDragStartThreshold = 6;
        private const double SidebarItemHeight = 32;
        private const double SidebarItemGlyphSize = 12;
        private const double SidebarItemContentInsetLeft = 8;
        private const double SidebarExpandedScrollRightPadding = 12;
        private const double SidebarBottomPadding = 8;
        private const double SidebarFooterBottomMargin = 0;
        private const double SidebarCompactButtonSize = 32;
        private const double PinnedDragOverlayWidth = 44;
        private const double PinnedDragOverlayHeight = 44;
        private const double PinnedDragOverlayPointerInsetY = 4;
        private static string S(string key) => LocalizedStrings.Instance.Get(key);
        private readonly ExplorerService _explorerService = new();
        private readonly List<SidebarVisualItem> _visualItems = new();
        private readonly List<TextBlock> _labelBlocks = new();
        private readonly List<TextBlock> _headerBlocks = new();
        private readonly List<FrameworkElement> _compactGroupHeaders = new();
        private readonly List<FrameworkElement> _groupSeparators = new();
        private readonly List<Panel> _fullGroupPanels = new();
        private readonly List<FrameworkElement> _fullOnlySections = new();
        private readonly List<FontIcon> _groupChevrons = new();
        private readonly List<GroupHeaderLayoutParts> _groupHeaderLayouts = new();
        private bool _isPinnedExpanded = true;
        private bool _isCloudExpanded = true;
        private bool _isNetworkExpanded = true;
        private bool _isTagsExpanded = true;
        private bool _isCompact;
        private bool _showFavoritesSection = true;
        private bool _showCloudSection = true;
        private bool _showNetworkSection = true;
        private bool _showTagsSection = true;
        private string? _selectedPath;
        private string? _explicitPinnedSelectionPath;
        private bool _isSelectionActive = true;
        private TreeView? _attachedTreeView;
        private readonly CompactSidebarMenuController _compactMenuController = new();
        private readonly Dictionary<SidebarNavItemModel, FrameworkElement> _pinnedItemHosts = new();
        private bool _compactButtonsAttached;
        private SidebarNavItemModel? _pendingPinnedDragItem;
        private FrameworkElement? _pendingPinnedDragHost;
        private Point _pendingPinnedDragStartPoint;
        private bool _isPinnedDragActive;
        private bool _suppressPinnedTap;
        private SidebarNavItemModel? _activePinnedDropTarget;
        private bool _activePinnedDropInsertAfter;
        public ObservableCollection<SidebarNavItemModel> PinnedItems { get; } = new();
        public ObservableCollection<SidebarNavItemModel> CloudItems { get; } = new();
        public ObservableCollection<SidebarNavItemModel> NetworkItems { get; } = new();
        public ObservableCollection<SidebarNavItemModel> TagItems { get; } = new();

        public event EventHandler<SidebarNavigateRequestedEventArgs>? NavigateRequested;
        public event EventHandler<SidebarFavoriteActionRequestedEventArgs>? FavoriteActionRequested;
        public event EventHandler<SidebarPinnedContextRequestedEventArgs>? PinnedContextRequested;
        public event EventHandler? SettingsRequested;

        public SidebarView()
        {
            InitializeComponent();
            ActualThemeChanged += SidebarView_ActualThemeChanged;
            _compactMenuController.NavigateRequested += (_, path) => NavigateToPath(path);

            _headerBlocks.Add(PinnedGroupTextBlock);
            _headerBlocks.Add(CloudHeaderTextBlock);
            _headerBlocks.Add(NetworkHeaderTextBlock);
            _headerBlocks.Add(TagsHeaderTextBlock);
            _compactGroupHeaders.AddRange(new FrameworkElement[] { PinnedGroupBorder, TreeCompactBorder, CloudGroupBorder, NetworkGroupBorder, TagsGroupBorder });
            _groupSeparators.AddRange(new FrameworkElement[] { PinnedTreeSeparator, TreeCloudSeparator, CloudNetworkSeparator, NetworkTagsSeparator });
            _fullGroupPanels.AddRange(new Panel[] { PinnedSectionPanel, TreeSectionPanel, CloudSectionPanel, NetworkSectionPanel, TagsSectionPanel });
            _fullOnlySections.AddRange(new FrameworkElement[] { PinnedSectionPanel, TreeSectionPanel, CloudSectionPanel, NetworkSectionPanel, TagsSectionPanel, TreeHostBorder });
            _groupChevrons.AddRange(new[] { PinnedChevron, CloudChevron, NetworkChevron, TagsChevron });
            TryRegisterGroupHeaderLayout(PinnedGroupBorder, PinnedGroupGrid, PinnedGroupSelectionIndicator, PinnedGroupIcon);
            TryRegisterGroupHeaderLayout(TreeCompactBorder, TreeCompactGrid, TreeCompactSelectionIndicator, TreeCompactIcon);
            TryRegisterGroupHeaderLayout(CloudGroupBorder, CloudGroupGrid, CloudGroupSelectionIndicator, CloudGroupIcon);
            TryRegisterGroupHeaderLayout(NetworkGroupBorder, NetworkGroupGrid, NetworkGroupSelectionIndicator, NetworkGroupIcon);
            TryRegisterGroupHeaderLayout(TagsGroupBorder, TagsGroupGrid, TagsGroupSelectionIndicator, TagsGroupIcon);
            RefreshAuxiliaryItems();

            RegisterGroupHover(PinnedGroupBorder);
            RegisterGroupHover(TreeCompactBorder);
            RegisterGroupHover(CloudGroupBorder);
            RegisterGroupHover(NetworkGroupBorder);
            RegisterGroupHover(TagsGroupBorder);

            ToolTipService.SetToolTip(PinnedGroupBorder, S("SidebarPinned"));
            ToolTipService.SetToolTip(CloudGroupBorder, S("SidebarCloud"));
            ToolTipService.SetToolTip(NetworkGroupBorder, S("SidebarNetwork"));
            ToolTipService.SetToolTip(TagsGroupBorder, S("SidebarTags"));
            ToolTipService.SetToolTip(TreeCompactBorder, S("SidebarMyComputer"));

            UpdatePinnedDragPreviewTheme();
        }

        public void SetPinnedItems(IEnumerable<SidebarNavItemModel> items, bool refreshSelection = true)
        {
            ResetStaticItems(PinnedItems, items.ToArray());
            ApplyPinnedSelection(null);
            if (refreshSelection)
            {
                SetSelectedPath(_selectedPath);
            }
        }

        public void SetExtraItems(IEnumerable<SidebarNavItemModel> items)
        {
            // The full sidebar now statically composes Cloud/Network/Tags sections.
            // The incoming items are only used to preserve future extensibility for Network children.
            ResetStaticItems(NetworkItems, Array.Empty<SidebarNavItemModel>());
            UpdateExpandedSectionVisibility(_isCompact);
        }

        public void AttachTreeView(TreeView treeView)
        {
            _attachedTreeView = treeView;
            if (treeView.Parent is Panel currentParent)
            {
                currentParent.Children.Remove(treeView);
            }

            treeView.Margin = new Thickness(0);
            treeView.Padding = new Thickness(0, 0, SidebarBottomPadding, 0);
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

            UpdateCompactButtonPlacement(compact);
            ApplyCompactVisibility(compact);
            ApplyHeaderLayout(compact);
            UpdateExpandedSectionVisibility(compact);
            SetSelectedPath(_selectedPath);
        }

        public void SetSectionVisibility(bool showFavorites, bool showCloud, bool showNetwork, bool showTags)
        {
            _showFavoritesSection = showFavorites;
            _showCloudSection = showCloud;
            _showNetworkSection = showNetwork;
            _showTagsSection = showTags;

            ApplyCompactVisibility(_isCompact);
            UpdateExpandedSectionVisibility(_isCompact);
        }

        public void RefreshLocalizedText()
        {
            PinnedGroupTextBlock.Text = S("SidebarPinned");
            CloudHeaderTextBlock.Text = S("SidebarCloud");
            NetworkHeaderTextBlock.Text = S("SidebarNetwork");
            TagsHeaderTextBlock.Text = S("SidebarTags");
            RefreshAuxiliaryItems();

            ToolTipService.SetToolTip(PinnedGroupBorder, S("SidebarPinned"));
            ToolTipService.SetToolTip(CloudGroupBorder, S("SidebarCloud"));
            ToolTipService.SetToolTip(NetworkGroupBorder, S("SidebarNetwork"));
            ToolTipService.SetToolTip(TagsGroupBorder, S("SidebarTags"));
            ToolTipService.SetToolTip(TreeCompactBorder, S("SidebarMyComputer"));
            if (ToolTipService.GetToolTip(SidebarSettingsButton) is ToolTip toolTip)
            {
                toolTip.Content = S("SidebarSettingsButtonToolTip.Content");
            }
        }

        private void RefreshAuxiliaryItems()
        {
            ResetStaticItems(
                CloudItems,
                new[]
                {
                    new SidebarNavItemModel("OneDrive", S("SidebarOneDrive"), null, "\uE753", selectable: false)
                });
            ResetStaticItems(NetworkItems, Array.Empty<SidebarNavItemModel>());
            ResetStaticItems(
                TagItems,
                new[]
                {
                    new SidebarNavItemModel("TagWork", S("SidebarTagWork"), null, "\uE8EC", selectable: false),
                    new SidebarNavItemModel("TagFocus", S("SidebarTagFocus"), null, "\uE8EC", selectable: false),
                    new SidebarNavItemModel("TagArchive", S("SidebarTagArchive"), null, "\uE8EC", selectable: false)
                });
            UpdateExpandedSectionVisibility(_isCompact);
        }

        private static void ResetStaticItems(ObservableCollection<SidebarNavItemModel> target, IReadOnlyList<SidebarNavItemModel> items)
        {
            target.Clear();
            foreach (SidebarNavItemModel item in items)
            {
                target.Add(item);
            }
        }

        private void UpdateCompactButtonPlacement(bool compact)
        {
            if (compact)
            {
                AttachCompactButtons();
            }
            else
            {
                DetachCompactButtons();
            }
        }

        private void AttachCompactButtons()
        {
            if (_compactButtonsAttached)
            {
                return;
            }

            RemoveCompactHeadersFromParents();
            CompactButtonsPanel.Children.Clear();
            foreach (FrameworkElement header in _compactGroupHeaders)
            {
                CompactButtonsPanel.Children.Add(header);
            }

            _compactButtonsAttached = true;
        }

        private void DetachCompactButtons()
        {
            if (!_compactButtonsAttached)
            {
                return;
            }

            RemoveCompactHeadersFromParents();
            for (int i = 0; i < _compactGroupHeaders.Count; i++)
            {
                _fullGroupPanels[i].Children.Insert(0, _compactGroupHeaders[i]);
            }

            _compactButtonsAttached = false;
        }

        private void RemoveCompactHeadersFromParents()
        {
            foreach (FrameworkElement header in _compactGroupHeaders)
            {
                RemoveFromParent(header);
            }
        }

        private void ApplyCompactVisibility(bool compact)
        {
            SidebarScrollViewer.Padding = compact
                ? new Thickness(0, 0, 0, SidebarBottomPadding)
                : new Thickness(0, 0, SidebarExpandedScrollRightPadding, SidebarBottomPadding);
            UpdateSettingsButtonLayout(compact);
            CompactButtonsPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            SetVisibility(_fullOnlySections, compact ? Visibility.Collapsed : Visibility.Visible);
            SetVisibility(_groupSeparators, compact ? Visibility.Collapsed : Visibility.Visible);
            TreeCompactBorder.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            PinnedGroupBorder.Visibility = _showFavoritesSection ? (compact ? Visibility.Visible : Visibility.Visible) : Visibility.Collapsed;
            PinnedSectionPanel.Visibility = _showFavoritesSection && !compact ? Visibility.Visible : Visibility.Collapsed;
            CloudGroupBorder.Visibility = _showCloudSection ? (compact ? Visibility.Visible : Visibility.Visible) : Visibility.Collapsed;
            CloudSectionPanel.Visibility = _showCloudSection && !compact ? Visibility.Visible : Visibility.Collapsed;
            NetworkGroupBorder.Visibility = _showNetworkSection ? (compact ? Visibility.Visible : Visibility.Visible) : Visibility.Collapsed;
            NetworkSectionPanel.Visibility = _showNetworkSection && !compact ? Visibility.Visible : Visibility.Collapsed;
            TagsGroupBorder.Visibility = _showTagsSection ? (compact ? Visibility.Visible : Visibility.Visible) : Visibility.Collapsed;
            TagsSectionPanel.Visibility = _showTagsSection && !compact ? Visibility.Visible : Visibility.Collapsed;
            SetTextVisibility(_labelBlocks, compact);
            SetTextVisibility(_headerBlocks, compact);
            SetVisibility(_groupChevrons, compact ? Visibility.Collapsed : Visibility.Visible);
        }

        private void UpdateSettingsButtonLayout(bool compact)
        {
            SettingsFooterHost.Margin = new Thickness(0);
            SidebarSettingsButton.Width = compact ? SidebarCompactButtonSize : double.NaN;
            SidebarSettingsButton.Padding = compact ? new Thickness(0) : new Thickness(9, 0, 0, 0);
            SidebarSettingsButton.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            SidebarSettingsButton.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }

        private void ApplyHeaderLayout(bool compact)
        {
            foreach (GroupHeaderLayoutParts layout in _groupHeaderLayouts)
            {
                ApplyGroupHeaderLayout(layout.Border, layout.Grid, layout.Indicator, layout.Icon, layout.ExpandedGridPadding, compact);
            }
        }

        private void UpdateExpandedSectionVisibility(bool compact)
        {
            PinnedItemsPanel.Visibility = compact || !_showFavoritesSection ? Visibility.Collapsed : (_isPinnedExpanded ? Visibility.Visible : Visibility.Collapsed);
            CloudItemsPanel.Visibility = compact || !_showCloudSection ? Visibility.Collapsed : (_isCloudExpanded ? Visibility.Visible : Visibility.Collapsed);
            NetworkItemsPanel.Visibility = compact
                || !_showNetworkSection
                ? Visibility.Collapsed
                : (_isNetworkExpanded && NetworkItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed);
            TagsItemsPanel.Visibility = compact || !_showTagsSection ? Visibility.Collapsed : (_isTagsExpanded ? Visibility.Visible : Visibility.Collapsed);
        }

        private static void SetTextVisibility(IEnumerable<TextBlock> blocks, bool compact)
        {
            Visibility visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            foreach (TextBlock block in blocks)
            {
                block.Visibility = visibility;
            }
        }

        private static void SetVisibility<T>(IEnumerable<T> elements, Visibility visibility) where T : UIElement
        {
            foreach (T element in elements)
            {
                element.Visibility = visibility;
            }
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

            string current = NormalizePath(path);
            if (!string.IsNullOrWhiteSpace(_explicitPinnedSelectionPath)
                && !string.Equals(_explicitPinnedSelectionPath, current, StringComparison.OrdinalIgnoreCase))
            {
                _explicitPinnedSelectionPath = null;
            }

            SidebarNavItemModel? selectedPinnedItem = FindExplicitPinnedItem(current);
            if (selectedPinnedItem is not null)
            {
                if (!_isPinnedExpanded)
                {
                    PinnedGroupSelectionIndicator.Visibility = Visibility.Visible;
                    PinnedGroupBorder.Background = SelectedBackgroundBrush();
                    return;
                }

                ApplyPinnedSelection(selectedPinnedItem);
                return;
            }

            SidebarVisualItem? selected = null;
            int bestLength = -1;

            foreach (SidebarVisualItem item in _visualItems)
            {
                if (item.Section == SidebarSection.Pinned)
                {
                    continue;
                }

                if (!item.Selectable)
                {
                    continue;
                }

                string? itemPath = item.Path;

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

        public void SetSelectionActive(bool isActive)
        {
            if (_isSelectionActive == isActive)
            {
                return;
            }

            _isSelectionActive = isActive;
            SetSelectedPath(_selectedPath);
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
                ColumnSpacing = 0,
                Margin = new Thickness(SidebarItemContentInsetLeft, 0, 0, 0)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarItemGlyphSize) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            indicator = new Border
            {
                Width = 3,
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                CornerRadius = (CornerRadius)Application.Current.Resources["ListViewItemSelectionIndicatorCornerRadius"],
                Background = SelectionIndicatorBrush(),
                Visibility = Visibility.Collapsed
            };

            var icon = new FontIcon
            {
                Width = SidebarItemGlyphSize,
                Height = SidebarItemGlyphSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = SidebarItemGlyphSize,
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
                Height = SidebarItemHeight,
                Margin = new Thickness(0),
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

        private SidebarNavItemModel? FindBestPinnedItem(string currentPath)
        {
            SidebarNavItemModel? selected = null;
            int bestLength = -1;

            foreach (SidebarNavItemModel item in PinnedItems)
            {
                if (string.IsNullOrWhiteSpace(item.Path))
                {
                    continue;
                }

                string candidate = NormalizePath(item.Path);
                bool matched = string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase)
                    || currentPath.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase);
                if (!matched || candidate.Length <= bestLength)
                {
                    continue;
                }

                bestLength = candidate.Length;
                selected = item;
            }

            return selected;
        }

        private void ApplyPinnedSelection(SidebarNavItemModel? item)
        {
            foreach (SidebarNavItemModel pinnedItem in PinnedItems)
            {
                pinnedItem.IsSelected = ReferenceEquals(pinnedItem, item);
            }
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
                        new CompactFlyoutItem(S("SidebarOneDrive"), "\uE753", null, false)
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
                        new CompactFlyoutItem(S("SidebarNetworkEmpty"), "\uE774", null, false)
                    });
                return;
            }

            if (NetworkItems.Count == 0)
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
                        new CompactFlyoutItem(S("SidebarTagWork"), "\uE8EC", null, false),
                        new CompactFlyoutItem(S("SidebarTagFocus"), "\uE8EC", null, false),
                        new CompactFlyoutItem(S("SidebarTagArchive"), "\uE8EC", null, false)
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

        private void PinnedItemHost_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SidebarNavItemModel item)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            PointerPointProperties properties = e.GetCurrentPoint(element).Properties;
            if (properties.IsRightButtonPressed)
            {
                CancelPinnedDrag();
                return;
            }

            if (!properties.IsLeftButtonPressed)
            {
                return;
            }

            _pendingPinnedDragItem = item;
            _pendingPinnedDragHost = element;
            _pendingPinnedDragStartPoint = e.GetCurrentPoint(RootGrid).Position;
            _suppressPinnedTap = false;
            element.CapturePointer(e.Pointer);
        }

        private void PinnedItemHost_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SidebarNavItemModel item)
            {
                return;
            }

            PointerPoint point = e.GetCurrentPoint(RootGrid);
            if (!_isPinnedDragActive)
            {
                if (!ReferenceEquals(_pendingPinnedDragItem, item) || !point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                if (Math.Abs(point.Position.X - _pendingPinnedDragStartPoint.X) < PinnedDragStartThreshold &&
                    Math.Abs(point.Position.Y - _pendingPinnedDragStartPoint.Y) < PinnedDragStartThreshold)
                {
                    return;
                }

                _isPinnedDragActive = true;
                _suppressPinnedTap = true;
            }

            UpdatePinnedDragTarget(point.Position);
            e.Handled = true;
        }

        private void PinnedItemHost_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            element.ReleasePointerCaptures();
            if (!_isPinnedDragActive || _pendingPinnedDragItem is null)
            {
                _pendingPinnedDragItem = null;
                _pendingPinnedDragHost = null;
                return;
            }

            CompletePinnedDrag();
            e.Handled = true;
        }

        private void PinnedItemHost_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_isPinnedDragActive)
            {
                CompletePinnedDrag();
                return;
            }

            _pendingPinnedDragItem = null;
            _pendingPinnedDragHost = null;
        }

        private void PinnedItemHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_suppressPinnedTap)
            {
                _suppressPinnedTap = false;
                e.Handled = true;
                return;
            }

            if (sender is not FrameworkElement element || element.DataContext is not SidebarNavItemModel item || string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            PinnedGroupSelectionIndicator.Visibility = Visibility.Collapsed;
            PinnedGroupBorder.Background = TransparentBrush();
            SetSelectedPath(item.Path);
            NavigateRequested?.Invoke(this, new SidebarNavigateRequestedEventArgs(item.Path));
            e.Handled = true;
        }

        private void PinnedItemHost_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SidebarNavItemModel item)
            {
                return;
            }

            Point position = default;
            bool hasPosition = args.TryGetPosition(element, out Point requestedPoint);
            if (hasPosition)
            {
                position = requestedPoint;
            }

            RaisePinnedContextRequested(element, item, hasPosition ? position : null);
            args.Handled = true;
        }

        private void SidebarView_ActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdatePinnedDragPreviewTheme();
            RefreshSelectionIndicatorTheme();
        }

        private void UpdatePinnedDragPreviewTheme()
        {
            if (PinnedDragPreviewHost is null)
            {
                return;
            }

            if (ActualTheme == ElementTheme.Dark)
            {
                PinnedDragPreviewHost.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x57, 0x57, 0x57));
                return;
            }

            if (Resources.TryGetValue("ControlStrokeColorDefaultBrush", out object resource) && resource is Brush brush)
            {
                PinnedDragPreviewHost.BorderBrush = brush;
                return;
            }

            PinnedDragPreviewHost.BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xD0, 0xD0, 0xD0));
        }

        private void PinnedItemHost_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SidebarNavItemModel item)
            {
                _pinnedItemHosts[item] = element;
            }
        }

        private void PinnedItemHost_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            KeyValuePair<SidebarNavItemModel, FrameworkElement> match =
                _pinnedItemHosts.FirstOrDefault(pair => ReferenceEquals(pair.Value, element));
            if (match.Key is not null)
            {
                _pinnedItemHosts.Remove(match.Key);
            }
        }

        private void ClearPinnedDropIndicators()
        {
            foreach (SidebarNavItemModel item in PinnedItems)
            {
                item.IsDragInsertBefore = false;
                item.IsDragInsertAfter = false;
            }

            _activePinnedDropTarget = null;
            _activePinnedDropInsertAfter = false;
        }

        private void SetPinnedDropIndicator(SidebarNavItemModel targetItem, bool insertAfter)
        {
            foreach (SidebarNavItemModel item in PinnedItems)
            {
                bool isTarget = ReferenceEquals(item, targetItem);
                item.IsDragInsertBefore = isTarget && !insertAfter;
                item.IsDragInsertAfter = isTarget && insertAfter;
            }

            _activePinnedDropTarget = targetItem;
            _activePinnedDropInsertAfter = insertAfter;
        }

        private void ShowPinnedDragOverlay(FrameworkElement relativeTo, Point relativePoint)
        {
            try
            {
                GeneralTransform transform = relativeTo.TransformToVisual(RootGrid);
                Point anchor = transform.TransformPoint(relativePoint);
                ShowPinnedDragOverlayAtRootPoint(anchor);
            }
            catch
            {
            }
        }

        private void ShowPinnedDragOverlayAtRootPoint(Point rootPoint)
        {
            if (PinnedDragPreviewHost.Visibility != Visibility.Visible)
            {
                PinnedDragPreviewHost.Visibility = Visibility.Visible;
            }

            Canvas.SetLeft(PinnedDragPreviewHost, rootPoint.X - (PinnedDragOverlayWidth / 2));
            Canvas.SetTop(PinnedDragPreviewHost, rootPoint.Y - PinnedDragOverlayHeight + PinnedDragOverlayPointerInsetY);
        }

        private void HidePinnedDragOverlay()
        {
            PinnedDragPreviewHost.Visibility = Visibility.Collapsed;
        }

        private void UpdatePinnedDragTarget(Point rootPoint)
        {
            if (_pendingPinnedDragItem is null)
            {
                return;
            }

            FrameworkElement? matchedHost = null;
            SidebarNavItemModel? matchedItem = null;
            Rect matchedBounds = default;

            foreach ((SidebarNavItemModel item, FrameworkElement host) in _pinnedItemHosts)
            {
                if (ReferenceEquals(item, _pendingPinnedDragItem))
                {
                    continue;
                }

                try
                {
                    GeneralTransform transform = host.TransformToVisual(RootGrid);
                    Rect bounds = transform.TransformBounds(new Rect(0, 0, host.ActualWidth, host.ActualHeight));
                    if (!bounds.Contains(rootPoint))
                    {
                        continue;
                    }

                    matchedHost = host;
                    matchedItem = item;
                    matchedBounds = bounds;
                    break;
                }
                catch
                {
                }
            }

            if (matchedHost is null || matchedItem is null)
            {
                ClearPinnedDropIndicators();
                ShowPinnedDragOverlayAtRootPoint(rootPoint);
                return;
            }

            bool insertAfter = rootPoint.Y >= (matchedBounds.Top + (matchedBounds.Height / 2));
            SetPinnedDropIndicator(matchedItem, insertAfter);
            ShowPinnedDragOverlay(matchedHost, new Point(rootPoint.X - matchedBounds.Left, rootPoint.Y - matchedBounds.Top));
        }

        private void CompletePinnedDrag()
        {
            SidebarNavItemModel? sourceItem = _pendingPinnedDragItem;
            SidebarNavItemModel? targetItem = _activePinnedDropTarget;
            bool insertAfter = _activePinnedDropInsertAfter;

            CancelPinnedDrag(clearTapSuppression: false);

            if (sourceItem is null || targetItem is null || string.IsNullOrWhiteSpace(sourceItem.Path) || string.IsNullOrWhiteSpace(targetItem.Path))
            {
                return;
            }

            FavoriteActionRequested?.Invoke(
                this,
                new SidebarFavoriteActionRequestedEventArgs(
                    SidebarFavoriteAction.Reorder,
                    sourceItem.Path!,
                    targetItem.Label,
                    targetPath: targetItem.Path,
                    insertAfter: insertAfter));
        }

        private void CancelPinnedDrag(bool clearTapSuppression = true)
        {
            _pendingPinnedDragHost?.ReleasePointerCaptures();
            _pendingPinnedDragItem = null;
            _pendingPinnedDragHost = null;
            _isPinnedDragActive = false;
            if (clearTapSuppression)
            {
                _suppressPinnedTap = false;
            }

            ClearPinnedDropIndicators();
            HidePinnedDragOverlay();
        }

        private void RaisePinnedContextRequested(FrameworkElement anchor, SidebarNavItemModel item, Point? position)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            Point resolvedPosition = position ?? new Point(Math.Max(0, anchor.ActualWidth / 2), Math.Max(0, anchor.ActualHeight / 2));
            PinnedContextRequested?.Invoke(
                this,
                new SidebarPinnedContextRequestedEventArgs(item.Path!, item.Label, anchor, resolvedPosition));
        }

        private void DynamicItem_Click(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not SidebarNavItemModel item || string.IsNullOrWhiteSpace(item.Path))
            {
                return;
            }

            _explicitPinnedSelectionPath = NormalizePath(item.Path);
            SetSelectedPath(item.Path);
            NavigateRequested?.Invoke(this, new SidebarNavigateRequestedEventArgs(item.Path));
        }

        private void SidebarSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ClearSelection()
        {
            PinnedGroupSelectionIndicator.Visibility = Visibility.Collapsed;
            PinnedGroupBorder.Background = TransparentBrush();
            PinnedGroupSelectionIndicator.Background = SelectionIndicatorBrush();
            ApplyPinnedSelection(null);

            foreach (SidebarVisualItem item in _visualItems)
            {
                item.Indicator.Visibility = Visibility.Collapsed;
                item.Indicator.Background = SelectionIndicatorBrush();
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

            if (string.IsNullOrWhiteSpace(itemPath))
            {
                return false;
            }

            if (item.Section == SidebarSection.Pinned)
            {
                if (string.IsNullOrWhiteSpace(_explicitPinnedSelectionPath))
                {
                    return false;
                }

                string pinnedPath = NormalizePath(itemPath);
                return string.Equals(pinnedPath, _explicitPinnedSelectionPath, StringComparison.OrdinalIgnoreCase);
            }

            string current = NormalizePath(_selectedPath);
            string candidate = NormalizePath(itemPath);
            return string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)
                || current.StartsWith(candidate + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private SidebarNavItemModel? FindExplicitPinnedItem(string currentPath)
        {
            if (string.IsNullOrWhiteSpace(_explicitPinnedSelectionPath))
            {
                return null;
            }

            if (!string.Equals(_explicitPinnedSelectionPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (SidebarNavItemModel item in PinnedItems)
            {
                if (string.IsNullOrWhiteSpace(item.Path))
                {
                    continue;
                }

                string candidate = NormalizePath(item.Path);
                if (string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
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

        private Brush SelectionIndicatorBrush()
        {
            string resourceKey = _isSelectionActive ? "TreeViewItemSelectionIndicatorForeground" : "TextFillColorDisabledBrush";
            if (Application.Current.Resources.TryGetValue(resourceKey, out object? value) && value is Brush brush)
            {
                return brush;
            }

            return new SolidColorBrush(_isSelectionActive
                ? ColorHelper.FromArgb(0xFF, 0x00, 0x78, 0xD4)
                : ColorHelper.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
        }

        private void RefreshSelectionIndicatorTheme()
        {
            Brush indicatorBrush = SelectionIndicatorBrush();
            PinnedGroupSelectionIndicator.Background = indicatorBrush;
            TreeCompactSelectionIndicator.Background = indicatorBrush;
            CloudGroupSelectionIndicator.Background = indicatorBrush;
            NetworkGroupSelectionIndicator.Background = indicatorBrush;
            TagsGroupSelectionIndicator.Background = indicatorBrush;

            foreach (SidebarVisualItem item in _visualItems)
            {
                item.Indicator.Background = indicatorBrush;
            }
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

        private void TryRegisterGroupHeaderLayout(
            Border? border,
            Grid? grid,
            Border? indicator,
            FontIcon? icon)
        {
            if (border is null || grid is null || indicator is null || icon is null)
            {
                return;
            }

            _groupHeaderLayouts.Add(new GroupHeaderLayoutParts(border, grid, indicator, icon, grid.Padding));
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
            return PinnedItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new CompactSidebarMenuItem(item.Label, item.Glyph, item.Path, null, item.Selectable))
                .ToArray();
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
            bool hasChildren = _explorerService.DirectoryHasChildDirectories(entry.FullPath);
            Func<IReadOnlyList<CompactSidebarMenuItem>>? loader = hasChildren
                ? () => LoadTreeCompactChildren(entry.FullPath, depth + 1)
                : null;

            return new CompactSidebarMenuItem(entry.Name, "\uE8B7", entry.FullPath, null, true, loader);
        }

        private IReadOnlyList<CompactSidebarMenuItem> LoadTreeCompactChildren(string path, int depth)
        {
            List<SidebarTreeEntry> childEntries = _explorerService.EnumerateSidebarDirectories(path, CompactTreeChildLimit);
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

        private static void ApplyGroupHeaderLayout(Border border, Grid grid, Border indicator, FontIcon icon, Thickness expandedGridPadding, bool compact)
        {
            border.Width = compact ? SidebarCompactButtonSize : double.NaN;
            border.Height = SidebarItemHeight;
            border.Padding = new Thickness(0, 4, 0, 4);
            border.Margin = new Thickness(0);
            border.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            grid.Padding = compact ? new Thickness(0) : expandedGridPadding;

            indicator.Visibility = compact ? Visibility.Collapsed : indicator.Visibility;
            icon.FontSize = SidebarItemGlyphSize;
            icon.Margin = new Thickness(0);

            grid.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(10);
            grid.ColumnDefinitions[1].Width = compact ? new GridLength(SidebarCompactButtonSize) : new GridLength(SidebarItemGlyphSize);
            grid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[3].Width = compact ? new GridLength(0) : new GridLength(26);
        }

        private sealed record SidebarVisualItem(string Key, string? Path, Border Border, Border Indicator, TextBlock Label, SidebarSection Section, bool Selectable);
        private sealed record CompactFlyoutItem(string Label, string Glyph, string? Path, bool Selectable);
        private sealed record GroupHeaderLayoutParts(Border Border, Grid Grid, Border Indicator, FontIcon Icon, Thickness ExpandedGridPadding);

        private enum SidebarSection
        {
            Pinned,
            Extra
        }
    }

    public sealed class SidebarNavItemModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _label;
        private bool _isDragInsertBefore;
        private bool _isDragInsertAfter;

        public SidebarNavItemModel(string key, string label, string? path, string glyph, bool selectable = true)
        {
            Key = key;
            _label = label;
            Path = path;
            Glyph = glyph;
            Selectable = selectable;
        }

        public string Key { get; }
        public string Label
        {
            get => _label;
            set
            {
                if (string.Equals(_label, value, StringComparison.Ordinal))
                {
                    return;
                }

                _label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            }
        }
        public string? Path { get; }
        public string Glyph { get; }
        public bool Selectable { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionIndicatorVisibility)));
            }
        }

        public Visibility SelectionIndicatorVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

        public bool IsDragInsertBefore
        {
            get => _isDragInsertBefore;
            set
            {
                if (_isDragInsertBefore == value)
                {
                    return;
                }

                _isDragInsertBefore = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragInsertBefore)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DragInsertBeforeVisibility)));
            }
        }

        public bool IsDragInsertAfter
        {
            get => _isDragInsertAfter;
            set
            {
                if (_isDragInsertAfter == value)
                {
                    return;
                }

                _isDragInsertAfter = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDragInsertAfter)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DragInsertAfterVisibility)));
            }
        }

        public Visibility DragInsertBeforeVisibility => _isDragInsertBefore ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DragInsertAfterVisibility => _isDragInsertAfter ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed class SidebarNavigateRequestedEventArgs : EventArgs
    {
        public SidebarNavigateRequestedEventArgs(string path)
        {
            Path = path;
        }

        public string Path { get; }
    }

    public enum SidebarFavoriteAction
    {
        Remove,
        MoveUp,
        MoveDown,
        Reorder
    }

    public sealed class SidebarFavoriteActionRequestedEventArgs : EventArgs
    {
        public SidebarFavoriteActionRequestedEventArgs(
            SidebarFavoriteAction action,
            string path,
            string label,
            string? targetPath = null,
            bool insertAfter = false)
        {
            Action = action;
            Path = path;
            Label = label;
            TargetPath = targetPath;
            InsertAfter = insertAfter;
        }

        public SidebarFavoriteAction Action { get; }
        public string Path { get; }
        public string Label { get; }
        public string? TargetPath { get; }
        public bool InsertAfter { get; }
    }

    public sealed class SidebarPinnedContextRequestedEventArgs : EventArgs
    {
        public SidebarPinnedContextRequestedEventArgs(string path, string label, UIElement anchor, Point position)
        {
            Path = path;
            Label = label;
            Anchor = anchor;
            Position = position;
        }

        public string Path { get; }
        public string Label { get; }
        public UIElement Anchor { get; }
        public Point Position { get; }
    }
}
