using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FileExplorerUI.Controls;

public sealed class WorkspaceTabStripHost
{
    private readonly TabView _tabView;
    private readonly Func<IReadOnlyList<WorkspaceTabPresentation>> _presentationsProvider;
    private readonly Func<WorkspaceTabState, Task> _activateTabAsync;
    private readonly RoutedEventHandler _closeHandler;
    private readonly Func<string> _closeToolTipTextProvider;
    private readonly Func<Visibility> _tabVisibilityProvider;
    private readonly Func<Brush?> _activeBackgroundProvider;
    private readonly Func<Brush?> _activeForegroundProvider;
    private readonly Func<Brush?> _inactiveForegroundProvider;
    private readonly double _glyphSize;
    private bool _suppressSelectionChanged;

    public WorkspaceTabStripHost(
        TabView tabView,
        Func<IReadOnlyList<WorkspaceTabPresentation>> presentationsProvider,
        Func<WorkspaceTabState, Task> activateTabAsync,
        RoutedEventHandler closeHandler,
        Func<string> closeToolTipTextProvider,
        Func<Visibility> tabVisibilityProvider,
        Func<Brush?> activeBackgroundProvider,
        Func<Brush?> activeForegroundProvider,
        Func<Brush?> inactiveForegroundProvider,
        double glyphSize)
    {
        _tabView = tabView;
        _presentationsProvider = presentationsProvider;
        _activateTabAsync = activateTabAsync;
        _closeHandler = closeHandler;
        _closeToolTipTextProvider = closeToolTipTextProvider;
        _tabVisibilityProvider = tabVisibilityProvider;
        _activeBackgroundProvider = activeBackgroundProvider;
        _activeForegroundProvider = activeForegroundProvider;
        _inactiveForegroundProvider = inactiveForegroundProvider;
        _glyphSize = glyphSize;
    }

    public void Refresh()
    {
        Refresh(forceApply: false);
    }

    public void RefreshVisuals()
    {
        Refresh(forceApply: true);
    }

    private void Refresh(bool forceApply)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DisableTabListTransitions();
        WorkspaceTabViewFactory factory = CreateFactory();
        IReadOnlyList<WorkspaceTabPresentation> presentations = _presentationsProvider();
        int createdCount = 0;
        int appliedCount = 0;
        int movedCount = 0;
        int removedCount = 0;

        _suppressSelectionChanged = true;
        try
        {
            for (int i = 0; i < presentations.Count; i++)
            {
                WorkspaceTabPresentation presentation = presentations[i];
                int existingIndex = FindTabItemIndex(presentation.Tab, i);
                TabViewItem item;
                if (existingIndex >= 0)
                {
                    item = (TabViewItem)_tabView.TabItems[existingIndex];
                    if (existingIndex != i)
                    {
                        _tabView.TabItems.RemoveAt(existingIndex);
                        _tabView.TabItems.Insert(i, item);
                        movedCount++;
                    }
                }
                else
                {
                    item = factory.Create(presentation);
                    createdCount++;
                    if (i < _tabView.TabItems.Count)
                    {
                        _tabView.TabItems.Insert(i, item);
                    }
                    else
                    {
                        _tabView.TabItems.Add(item);
                    }
                }

                if (ApplyIfChanged(factory, item, presentation, forceApply))
                {
                    appliedCount++;
                }
                if (presentation.IsActive)
                {
                    _tabView.SelectedItem = item;
                }
            }

            while (_tabView.TabItems.Count > presentations.Count)
            {
                _tabView.TabItems.RemoveAt(_tabView.TabItems.Count - 1);
                removedCount++;
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
            WorkspaceTabPerf.Mark(
                "tabstrip.refresh",
                $"elapsed={stopwatch.ElapsedMilliseconds}ms count={presentations.Count} created={createdCount} applied={appliedCount} moved={movedCount} removed={removedCount} force={forceApply}");
        }
    }

    public void RefreshActiveState()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DisableTabListTransitions();
        WorkspaceTabViewFactory factory = CreateFactory();
        int appliedCount = 0;

        _suppressSelectionChanged = true;
        try
        {
            IReadOnlyList<WorkspaceTabPresentation> currentPresentations = _presentationsProvider();
            if (_tabView.TabItems.Count != currentPresentations.Count)
            {
                Refresh();
                return;
            }

            TabViewItem? activeItem = null;
            int previousActiveIndex = -1;
            int activeIndex = -1;
            for (int i = 0; i < currentPresentations.Count; i++)
            {
                if (_tabView.TabItems[i] is not TabViewItem item)
                {
                    Refresh();
                    return;
                }

                WorkspaceTabPresentation presentation = currentPresentations[i];
                if (item.DataContext is WorkspaceTabPresentation previousPresentation &&
                    previousPresentation.IsActive)
                {
                    previousActiveIndex = i;
                }

                if (presentation.IsActive)
                {
                    activeIndex = i;
                    activeItem = item;
                }
            }

            appliedCount += ApplyIfValid(factory, currentPresentations, activeIndex);
            appliedCount += ApplyIfValid(factory, currentPresentations, previousActiveIndex);
            appliedCount += ApplyIfValid(factory, currentPresentations, activeIndex - 1);
            appliedCount += ApplyIfValid(factory, currentPresentations, previousActiveIndex - 1);
            _tabView.SelectedItem = activeItem;
        }
        finally
        {
            _suppressSelectionChanged = false;
            WorkspaceTabPerf.Mark(
                "tabstrip.refresh-active",
                $"elapsed={stopwatch.ElapsedMilliseconds}ms applied={appliedCount}");
        }
    }

    public void RefreshActivePresentation()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        DisableTabListTransitions();
        WorkspaceTabViewFactory factory = CreateFactory();
        IReadOnlyList<WorkspaceTabPresentation> currentPresentations = _presentationsProvider();
        for (int i = 0; i < currentPresentations.Count; i++)
        {
            WorkspaceTabPresentation presentation = currentPresentations[i];
            if (!presentation.IsActive)
            {
                continue;
            }

            int itemIndex = FindTabItemIndex(presentation.Tab, 0);
            if (itemIndex >= 0 && _tabView.TabItems[itemIndex] is TabViewItem item)
            {
                bool wasSuppressing = _suppressSelectionChanged;
                _suppressSelectionChanged = true;
                try
                {
                    bool applied = ApplyIfChanged(factory, item, presentation);
                    _tabView.SelectedItem = item;
                    WorkspaceTabPerf.Mark(
                        "tabstrip.refresh-active-presentation",
                        $"elapsed={stopwatch.ElapsedMilliseconds}ms applied={applied}");
                }
                finally
                {
                    _suppressSelectionChanged = wasSuppressing;
                }
            }

            return;
        }

        WorkspaceTabPerf.Mark(
            "tabstrip.refresh-active-presentation",
            $"elapsed={stopwatch.ElapsedMilliseconds}ms applied=False reason=not-found");
    }

    public async Task HandleSelectionChangedAsync(SelectionChangedEventArgs e)
    {
        WorkspaceTabPerf.Mark("tabstrip.selection-changed", $"added={e.AddedItems.Count} suppress={_suppressSelectionChanged}");
        if (_suppressSelectionChanged ||
            e.AddedItems.Count == 0 ||
            e.AddedItems[0] is not TabViewItem tabViewItem ||
            tabViewItem.Tag is not WorkspaceTabState tab)
        {
            WorkspaceTabPerf.Mark("tabstrip.selection-skipped");
            return;
        }

        await _activateTabAsync(tab);
        WorkspaceTabPerf.Mark("tabstrip.selection-activated");
    }

    private WorkspaceTabViewFactory CreateFactory()
    {
        return new WorkspaceTabViewFactory(
            _tabView,
            _glyphSize,
            _closeHandler,
            _closeToolTipTextProvider(),
            _tabVisibilityProvider(),
            _activeBackgroundProvider,
            _activeForegroundProvider,
            _inactiveForegroundProvider);
    }

    private void DisableTabListTransitions()
    {
        if (FindDescendantByName<ListViewBase>(_tabView, "TabListView") is ListViewBase tabListView)
        {
            tabListView.ItemContainerTransitions = new TransitionCollection();
        }
    }

    private int FindTabItemIndex(WorkspaceTabState tab, int startIndex)
    {
        for (int i = Math.Max(0, startIndex); i < _tabView.TabItems.Count; i++)
        {
            if (_tabView.TabItems[i] is TabViewItem item &&
                ReferenceEquals(item.Tag, tab))
            {
                return i;
            }
        }

        return -1;
    }

    private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
            {
                return element;
            }

            T? nested = FindDescendantByName<T>(child, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private int ApplyIfValid(
        WorkspaceTabViewFactory factory,
        IReadOnlyList<WorkspaceTabPresentation> presentations,
        int index)
    {
        if (index < 0 ||
            index >= presentations.Count ||
            index >= _tabView.TabItems.Count ||
            _tabView.TabItems[index] is not TabViewItem item)
        {
            return 0;
        }

        return ApplyIfChanged(factory, item, presentations[index]) ? 1 : 0;
    }

    private static bool ApplyIfChanged(
        WorkspaceTabViewFactory factory,
        TabViewItem item,
        WorkspaceTabPresentation presentation,
        bool forceApply = false)
    {
        if (!forceApply && PresentationMatches(item, presentation))
        {
            return false;
        }

        factory.Apply(item, presentation);
        return true;
    }

    private static bool PresentationMatches(TabViewItem item, WorkspaceTabPresentation presentation)
    {
        return item.DataContext is WorkspaceTabPresentation previous &&
            ReferenceEquals(previous.Tab, presentation.Tab) &&
            string.Equals(previous.Title, presentation.Title, StringComparison.Ordinal) &&
            string.Equals(previous.Glyph, presentation.Glyph, StringComparison.Ordinal) &&
            previous.IsActive == presentation.IsActive &&
            previous.CanClose == presentation.CanClose &&
            previous.ShowTrailingSeparator == presentation.ShowTrailingSeparator &&
            item.Tag is WorkspaceTabState tag &&
            ReferenceEquals(tag, presentation.Tab);
    }
}
