using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FileExplorerUI.Controls
{
    public sealed class CommandMenuFlyout : MenuFlyout
    {
        public static readonly DependencyProperty CommandsProperty =
            DependencyProperty.Register(
                nameof(Commands),
                typeof(ObservableCollection<CommandMenuFlyoutItem>),
                typeof(CommandMenuFlyout),
                new PropertyMetadata(null, OnCommandsChanged));

        private MenuFlyoutPresenter? presenter;
        private ScrollViewer? menuScrollViewer;
        private ItemsControl? commandBar;
        private FrameworkElement? commandSeparator;
        private UIElement? invocationAnchor;
        private Point? invocationPosition;

        public CommandMenuFlyout()
        {
            Commands = [];
            Opened += CommandMenuFlyout_Opened;
            Closed += CommandMenuFlyout_Closed;
        }

        public ObservableCollection<CommandMenuFlyoutItem> Commands
        {
            get => (ObservableCollection<CommandMenuFlyoutItem>)GetValue(CommandsProperty);
            set => SetValue(CommandsProperty, value);
        }

        protected override Control CreatePresenter()
        {
            Control presenterControl = base.CreatePresenter();
            presenter = presenterControl as MenuFlyoutPresenter;
            if (presenter is not null)
            {
                presenter.ApplyTemplate();
                menuScrollViewer = FindDescendantByName<ScrollViewer>(presenter, "MenuFlyoutPresenterScrollViewer");
                commandBar = FindDescendantByName<ItemsControl>(presenter, "PART_CommandBar");
                commandSeparator = FindDescendantByName<FrameworkElement>(presenter, "PART_CommandSeparator");
            }
            ApplyCommandBarPlacement(openUpward: false);
            SyncPresenterState();
            return presenterControl;
        }

        public void SetInvocationContext(UIElement anchor, Point position)
        {
            invocationAnchor = anchor;
            invocationPosition = position;
        }

        private static void OnCommandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var flyout = (CommandMenuFlyout)d;

            if (e.OldValue is ObservableCollection<CommandMenuFlyoutItem> oldCollection)
            {
                oldCollection.CollectionChanged -= flyout.Commands_CollectionChanged;
            }

            if (e.NewValue is ObservableCollection<CommandMenuFlyoutItem> newCollection)
            {
                newCollection.CollectionChanged += flyout.Commands_CollectionChanged;
            }

            flyout.SyncPresenterState();
        }

        private void Commands_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SyncPresenterState();
        }

        private void SyncPresenterState()
        {
            if (presenter is null || commandBar is null)
            {
                return;
            }

            bool hasCommands = Commands.Count > 0;
            for (int i = 0; i < Commands.Count; i++)
            {
                Commands[i].SeparatorVisibility = i == Commands.Count - 1
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            commandBar.ItemsSource = Commands;
            commandBar.Visibility = hasCommands ? Visibility.Visible : Visibility.Collapsed;
            if (commandSeparator is not null)
            {
                commandSeparator.Visibility = hasCommands ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CommandMenuFlyout_Opened(object? sender, object e)
        {
            if (presenter is null)
            {
                return;
            }

            presenter.DispatcherQueue.TryEnqueue(UpdateCommandBarPlacement);
        }

        private void CommandMenuFlyout_Closed(object? sender, object e)
        {
            invocationAnchor = null;
            invocationPosition = null;
        }

        private void UpdateCommandBarPlacement()
        {
            if (presenter is null || menuScrollViewer is null || invocationAnchor is null || invocationPosition is null)
            {
                ApplyCommandBarPlacement(openUpward: false);
                return;
            }

            if (presenter.XamlRoot?.Content is not UIElement rootVisual)
            {
                ApplyCommandBarPlacement(openUpward: false);
                return;
            }

            GeneralTransform anchorTransform = invocationAnchor.TransformToVisual(rootVisual);
            Point anchorPoint = anchorTransform.TransformPoint(invocationPosition.Value);

            GeneralTransform presenterTransform = presenter.TransformToVisual(rootVisual);
            Point presenterOrigin = presenterTransform.TransformPoint(new Point(0, 0));
            double distanceToTop = Math.Abs(anchorPoint.Y - presenterOrigin.Y);
            double distanceToBottom = Math.Abs((presenterOrigin.Y + presenter.ActualHeight) - anchorPoint.Y);

            ApplyCommandBarPlacement(openUpward: distanceToBottom > distanceToTop);
        }

        private void ApplyCommandBarPlacement(bool openUpward)
        {
            if (menuScrollViewer is null || commandSeparator is null || commandBar is null)
            {
                return;
            }

            Grid.SetRow(commandBar, openUpward ? 0 : 2);
            Grid.SetRow(commandSeparator, 1);
            Grid.SetRow(menuScrollViewer, openUpward ? 2 : 0);
        }

        private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root is T element && element.Name == name)
            {
                return element;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T? nested = FindDescendantByName<T>(child, name);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
