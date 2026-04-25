using NorthFileUI.Workspace;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private static readonly string[] s_secondaryPaneNavigationPropertyNames =
        [
            nameof(SecondaryPaneAddressText),
            nameof(SecondaryPaneAddressEditorText),
            nameof(SecondaryPaneBreadcrumbs),
            nameof(SecondaryPaneVisibleBreadcrumbs),
            nameof(SecondaryPaneDisplayAddressText),
            nameof(SecondaryPaneAddressTextFallbackVisibility),
            nameof(SecondaryPaneSearchText),
            nameof(SecondaryPanePlaceholderText),
            nameof(SecondaryPaneEntries),
            nameof(SecondaryPaneItemsVisibility),
            nameof(SecondaryPanePlaceholderVisibility),
            nameof(SecondaryPaneCanGoBack),
            nameof(SecondaryPaneCanGoForward),
            nameof(SecondaryPaneCanGoUp)
        ];

        private static readonly string[] s_secondaryPaneDataPropertyNames =
        [
            nameof(SecondaryPaneEntries),
            nameof(SecondaryPaneItemsVisibility),
            nameof(SecondaryPanePlaceholderVisibility),
            nameof(SecondaryDetailsHeaderVisibility),
            nameof(SecondaryPanePlaceholderText)
        ];

        private static readonly string[] s_secondaryPaneAddressPropertyNames =
        [
            nameof(SecondaryPaneAddressText),
            nameof(SecondaryPaneAddressEditorText)
        ];

        private static readonly string[] s_secondaryPaneSearchPropertyNames =
        [
            nameof(SecondaryPaneSearchText)
        ];

        private static readonly string[] s_secondaryPaneBreadcrumbPresentationPropertyNames =
        [
            nameof(SecondaryPaneBreadcrumbs),
            nameof(SecondaryPaneVisibleBreadcrumbs),
            nameof(SecondaryPaneDisplayAddressText),
            nameof(SecondaryPaneAddressTextFallbackVisibility)
        ];

        private static readonly string[] s_primaryPaneBreadcrumbPresentationPropertyNames =
        [
            nameof(Breadcrumbs),
            nameof(VisibleBreadcrumbs),
            nameof(DisplayAddressText),
            nameof(AddressTextFallbackVisibility)
        ];

        private void RaisePaneAddressPropertiesChanged(WorkspacePanelId panelId)
        {
            RaisePropertyChanged(panelId == WorkspacePanelId.Secondary
                ? s_secondaryPaneAddressPropertyNames
                : s_primaryPaneBreadcrumbPresentationPropertyNames);
        }

        private void RaisePaneSearchPropertiesChanged(WorkspacePanelId panelId)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                RaisePropertyChanged(s_secondaryPaneSearchPropertyNames);
            }
        }

        private void RaisePaneBreadcrumbPresentationChanged(WorkspacePanelId panelId)
        {
            RaisePropertyChanged(panelId == WorkspacePanelId.Secondary
                ? s_secondaryPaneBreadcrumbPresentationPropertyNames
                : s_primaryPaneBreadcrumbPresentationPropertyNames);
        }

        private void RaiseSecondaryPaneNavigationPropertiesChanged()
        {
            RaisePropertyChanged(s_secondaryPaneNavigationPropertyNames);
        }

        private void RaiseSecondaryPaneDataPropertiesChanged()
        {
            RaisePropertyChanged(s_secondaryPaneDataPropertyNames);
        }
    }
}
