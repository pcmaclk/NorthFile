# FileExplorerUI Project Layout

This document records the intended folder boundaries for the WinUI frontend.

## Root

- `App.xaml` / `App.xaml.cs`: application entry and app-wide resource merge point.
- `MainWindow.xaml` / `MainWindow.xaml.cs`: main shell XAML and primary partial class anchor.
- `FileExplorerUI.csproj`: project build configuration.
- `Package.appxmanifest` / `app.manifest`: app and process manifests.

## Resources

Shared XAML resources live under `Resources`.

- `AppThemeResources.xaml`: app-wide light/dark theme tokens used by pane, tab, sidebar, and shell surfaces.
- `EntryItemHostResources.xaml`: default `EntryItemHost` template and visual-state brushes.
- `MenuFlyoutResources.xaml`: shared menu flyout presenter and context-menu item styles.
- `MainWindowResources.xaml`: index resource dictionary for `MainWindow`-local resources.
- `MainWindowTabResources.xaml`: title-bar tab styles and tab-specific theme resources.
- `MainWindowDetailsHeaderResources.xaml`: details header hit target, text, and sort glyph styles.
- `MainWindowPaneChromeResources.xaml`: pane address/search chrome and breadcrumb button styles.
- `MainWindowEntryListResources.xaml`: entry list scroll, row host, and details-cell text styles.
- `MainWindowOverlayResources.xaml`: rename overlay border and text box styles.
- `MainWindowStatusResources.xaml`: pane status bar text style.
- `MainWindowCommandDockResources.xaml`: command dock and peek button styles.

Resource dictionaries should hold stable styling, templates, and theme-aware brushes. Do not move XAML blocks that depend on `x:Name`, `x:Bind`, or code-behind event handlers into detached dictionaries unless they are extracted as a proper control with its own code-behind. This keeps event resolution and typed bindings predictable.

## MainWindow

`MainWindow` partial files are grouped by feature area:

- `Core`: startup, fields, shared support types, localization helpers, path helpers, and visual-tree helpers.
- `Navigation`: address bar, breadcrumbs, directory refresh, navigation commands, loading, watcher, and navigation diagnostics.
- `Panels`: primary/secondary pane shell, pane navigation, pane viewport/data coordination, and workspace shell behavior.
- `FileOps`: file commands, command targets, file operation dialogs, pane file commands, and operation execution.
- `Entries`: entry context menus, entry interactions, metadata formatting, and inline rename behavior.
- `Presentation`: list/grid presentation, command dock, viewport loading, metadata, and scheduling.
- `Selection`: selection state and selection surface handling.
- `Settings`: settings page shell and import/export hooks.
- `Sidebar`: favorites and sidebar tree coordination from the main window.
- `Overlays`: window-level dialog and overlay attachment helpers.
- `Windowing`: window chrome, placement, and tray behavior.
- `Tabs`: workspace tab handling.
- `Input`: keyboard handling.

## Controls

Reusable controls are grouped by UI role:

- `Entries`: entry rows, entry name cells, group headers, density metrics.
- `Panes`: pane toolbar, pane navigation bar, pane host, splitters.
- `Dialogs`: modal action and file-operation progress overlays.
- `Menus`: command menu flyout wrappers.
- `Tabs`: workspace tab strip and tab view factory.
- `Settings`: settings view.
- `Icons`: custom composed icons.
- `Triggers`: XAML state triggers.

## Other Areas

- `Sidebar`: sidebar view and sidebar-specific helper controls.
- `Workspace`: tab, panel, and session state models.
- `Services`: filesystem, file-management, settings, and progress services.
- `Controllers`: command controllers and handlers.
- `Commands`: command target and catalog model.
- `Models`: view models and small UI data models.
- `Interop`: native and Rust interop.
- `Localization`: app localization helper types.
- `Strings`: localized resource files.
- `Assets`: app image assets.
