# MainWindow Refactor Plan

## Goals

- Split `MainWindow.xaml.cs` into feature-focused modules.
- Keep behavior unchanged during migration.
- Make each step buildable and reversible.

## Strategy

1. Split by `partial class` first, no behavior changes.
2. Move feature clusters one by one.
3. After boundaries are stable, extract controllers/services.

## Current Status (2026-03-27)

- `MainWindow.xaml.cs` has been reduced to constructor-centric shell.
- Feature logic has been migrated into partial modules and compiles cleanly.
- Settings logic has been split further:
  - `SettingsController` owns default sort/language/diff decisions.
  - `MainWindow.SettingsImportExport.cs` isolates settings import/export + startup-path browse handlers.
- Watcher refresh guard logic has been moved to `WatcherController`.
- Navigation/UI interaction logic has been split into focused modules.
- Windowing / presentation / viewport / entries-context heavy blocks were further decomposed:
  - `MainWindow.WindowingTray.cs`
  - `MainWindow.PresentationProjection.cs`
  - `MainWindow.ViewportScheduling.cs`
  - `MainWindow.ViewportMetadata.cs`
  - `MainWindow.EntriesContextCommands.cs`
  - `MainWindow.SelectionState.cs`
  - `MainWindow.SelectionSurface.cs`
  - `MainWindow.CommandDock.cs`
  - `MainWindow.PresentationMenus.cs`
  - `MainWindow.FileCommandsTargets.cs`
- Project structure was normalized:
  - `FileExplorerUI/MainWindow/` now holds all `MainWindow.*.cs` partials.
  - `FileExplorerUI/Controllers/` holds controller classes.
  - `FileExplorerUI/Models/` holds shared view-model/domain model classes.
- Each migration batch was validated with:
  - `dotnet build`
  - MCP smoke navigation to `D:\New Folder`

## XAML Resource Split (completed)

- App-level theme, entry host, and menu flyout resources now live under `FileExplorerUI/Resources`.
- `MainWindow.xaml` uses `Resources/MainWindowResources.xaml` as a single local resource index.
- Stable visual styles were split out for tabs, details headers, pane address/search chrome, entry lists, overlays, status text, and command dock surfaces.
- The split intentionally stops before moving blocks with `x:Name`, `x:Bind`, or code-behind event handlers. Those areas should only move later as proper controls, not as loose resource dictionaries.

## Phase 1: Partial split (completed)

- `MainWindow.Favorites.cs`
- `MainWindow.FileOps.cs`
- `MainWindow.Watcher.cs`
- `MainWindow.Settings.cs`
- `MainWindow.Navigation.cs`
- `MainWindow.Presentation.cs`
- `MainWindow.EntriesContext.cs`
- `MainWindow.SidebarTreeInteractions.cs`
- `MainWindow.InlineRename.cs`
- `MainWindow.FileCommands.cs`
- `MainWindow.Localization.cs`
- `MainWindow.Windowing.cs`
- `MainWindow.Keyboard.cs`
- `MainWindow.Pathing.cs`
- `MainWindow.ViewportData.cs`
- `MainWindow.VisualTree.cs`
- `MainWindow.Core.cs`
- `MainWindow.Startup.cs`
- `MainWindow.Declarations.cs`
- `MainWindow.Fields.cs`
- `MainWindow.LayoutProperties.cs`
- `MainWindow.EntryMetadataFormatting.cs`
- `MainWindow.CommandDock.cs`
- `MainWindow.PresentationMenus.cs`
- `MainWindow.SelectionSurface.cs`
- `MainWindow.SelectionState.cs`
- `MainWindow.ViewportScheduling.cs`
- `MainWindow.ViewportMetadata.cs`
- `MainWindow.PresentationProjection.cs`
- `MainWindow.FileCommandsTargets.cs`
- `MainWindow.WindowingTray.cs`
- `MainWindow.EntriesContextCommands.cs`

## Shared model/type extraction (completed)

- `Models/EntryViewModel.cs`
- `Models/BreadcrumbItemViewModel.cs`
- `Models/SidebarTreeEntry.cs`
- `NativeMethods.cs`
- `MainWindow.SupportTypes.cs`
- `MainWindow.InternalModels.cs`

## Phase 2: Controller extraction (completed)

- `Controllers/FavoritesController.cs`
- `Controllers/DirectorySessionController.cs`
- `Controllers/FileOperationsController.cs`
- `Controllers/SettingsController.cs`
- `Controllers/WatcherController.cs`

## Phase 3: Dependency cleanup (deferred)

- Reduce shared mutable state between modules.
- Replace implicit cross-calls with explicit interfaces.
- Add targeted regression checks for each migrated area.

## Stop Criteria (for this refactor batch)

- No single `MainWindow` partial remains at “mega-file” scale.
- Domain boundaries are explicit (MainWindow partials / Controllers / Models).
- Build and MCP smoke checks are green.
- Further splitting is optional optimization, not required stabilization work.
