using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private static readonly string[] s_primaryPaneShellPropertyNames =
        [
            nameof(PrimaryPaneToolbarBackground),
            nameof(PrimaryPaneBodyBackground),
            nameof(PrimaryPaneToolbarBorderBrush),
            nameof(PrimaryPaneBodyBorderBrush),
            nameof(PrimaryPaneToolbarBorderThickness),
            nameof(PrimaryPaneToolbarTranslation),
            nameof(PrimaryPaneBodyBorderThickness),
            nameof(PrimaryPaneBodyTranslation)
        ];

        private static readonly string[] s_secondaryPaneShellPropertyNames =
        [
            nameof(SecondaryPaneToolbarBackground),
            nameof(SecondaryPaneBodyBackground),
            nameof(SecondaryPaneToolbarBorderBrush),
            nameof(SecondaryPaneBodyBorderBrush),
            nameof(SecondaryPaneToolbarBorderThickness),
            nameof(SecondaryPaneToolbarTranslation),
            nameof(SecondaryPaneBodyBorderThickness),
            nameof(SecondaryPaneBodyTranslation)
        ];

        private bool IsWorkspacePanelActive(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Primary
                ? !_isDualPaneEnabled || _workspaceLayoutHost.ActivePanel == WorkspacePanelId.Primary
                : _isDualPaneEnabled && _workspaceLayoutHost.ActivePanel == WorkspacePanelId.Secondary;
        }

        private Brush? GetPanelToolbarBackgroundBrush(WorkspacePanelId panelId)
        {
            return GetPanelToolbarBackgroundBrush(IsWorkspacePanelActive(panelId));
        }

        private Brush? GetPanelBodyBackgroundBrush(WorkspacePanelId panelId)
        {
            return GetPanelBodyBackgroundBrush(IsWorkspacePanelActive(panelId));
        }

        private Brush? GetPanelToolbarBorderBrush(WorkspacePanelId panelId)
        {
            return GetPanelToolbarBorderBrush(IsWorkspacePanelActive(panelId));
        }

        private Brush? GetPanelBodyBorderBrush(WorkspacePanelId panelId)
        {
            return GetPanelBodyBorderBrush(IsWorkspacePanelActive(panelId));
        }

        private Thickness GetPanelToolbarBorderThickness(WorkspacePanelId panelId)
        {
            return GetPanelToolbarBorderThickness(IsWorkspacePanelActive(panelId));
        }

        private Thickness GetPanelBodyBorderThickness(WorkspacePanelId panelId)
        {
            return GetPanelBodyBorderThickness(IsWorkspacePanelActive(panelId));
        }

        private Vector3 GetPanelToolbarTranslation(WorkspacePanelId panelId)
        {
            return IsWorkspacePanelActive(panelId)
                ? new Vector3(0, 0, 6)
                : Vector3.Zero;
        }

        private Vector3 GetPanelBodyTranslation(WorkspacePanelId panelId)
        {
            return IsWorkspacePanelActive(panelId)
                ? new Vector3(0, 0, 8)
                : Vector3.Zero;
        }

        private void RaiseWorkspacePanelShellPropertiesChanged()
        {
            RaisePropertyChanged(s_primaryPaneShellPropertyNames);
            RaisePropertyChanged(s_secondaryPaneShellPropertyNames);
        }

        private void RaiseWorkspacePanelShellPropertiesChanged(WorkspacePanelId panelId)
        {
            RaisePropertyChanged(panelId == WorkspacePanelId.Secondary
                ? s_secondaryPaneShellPropertyNames
                : s_primaryPaneShellPropertyNames);
        }
    }
}
