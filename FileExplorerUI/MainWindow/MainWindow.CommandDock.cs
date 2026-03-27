using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void DockRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string tag)
            {
                return;
            }

            _commandDockSide = tag switch
            {
                "Right" => CommandDockSide.Right,
                "Bottom" => CommandDockSide.Bottom,
                _ => CommandDockSide.Top,
            };
            ApplyCommandDockLayout();
        }

        private void CommandAutoHideSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            ApplyCommandDockLayout();
        }

        private void CommandPeekButton_Click(object sender, RoutedEventArgs e)
        {
            if (CommandDockPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            CommandDockPanel.Visibility = Visibility.Visible;
            CommandPeekButton.Visibility = Visibility.Collapsed;
        }

        private void CommandDockPanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (CommandAutoHideSwitch.IsOn)
            {
                CommandDockPanel.Visibility = Visibility.Collapsed;
                CommandPeekButton.Visibility = Visibility.Visible;
            }
        }

        private void ApplyCommandDockLayout()
        {
            if (CommandDockPanel is null || CommandPeekButton is null)
            {
                return;
            }

            if (!_showCommandDock)
            {
                CommandDockPanel.Visibility = Visibility.Collapsed;
                CommandPeekButton.Visibility = Visibility.Collapsed;
                return;
            }

            bool autoHide = CommandAutoHideSwitch?.IsOn == true;
            CommandDockPanel.Visibility = autoHide ? Visibility.Collapsed : Visibility.Visible;
            CommandPeekButton.Visibility = autoHide ? Visibility.Visible : Visibility.Collapsed;

            switch (_commandDockSide)
            {
                case CommandDockSide.Right:
                    CommandDockPanel.HorizontalAlignment = HorizontalAlignment.Right;
                    CommandDockPanel.VerticalAlignment = VerticalAlignment.Center;
                    CommandDockPanel.Margin = new Thickness(10, 0, 10, 0);
                    CommandPeekButton.HorizontalAlignment = HorizontalAlignment.Right;
                    CommandPeekButton.VerticalAlignment = VerticalAlignment.Center;
                    CommandPeekButton.Margin = new Thickness(0, 0, 10, 0);
                    break;
                case CommandDockSide.Bottom:
                    CommandDockPanel.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandDockPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    CommandDockPanel.Margin = new Thickness(10);
                    CommandPeekButton.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandPeekButton.VerticalAlignment = VerticalAlignment.Bottom;
                    CommandPeekButton.Margin = new Thickness(10);
                    break;
                default:
                    CommandDockPanel.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandDockPanel.VerticalAlignment = VerticalAlignment.Top;
                    CommandDockPanel.Margin = new Thickness(10);
                    CommandPeekButton.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandPeekButton.VerticalAlignment = VerticalAlignment.Top;
                    CommandPeekButton.Margin = new Thickness(10);
                    break;
            }
        }
    }
}
