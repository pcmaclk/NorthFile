# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]
- Fixed a WinUI XAML compile regression in the rename overlay editor by simplifying the overlay host back to a minimal safe shape.
- Added a shared `ExplorerService` layer so the main window now calls a dedicated service for directory reads, search reads, tree loading, breadcrumb directory lookup, rename/delete, and USN/cache operations instead of calling Rust/file-system APIs inline.
- Moved compact sidebar tree flyout directory enumeration onto the same `ExplorerService` abstraction so sidebar navigation and the main explorer surface now share one backend access path.
- Changed existing item rename from dialog-based flow to inline list editing, including Enter/Escape/LostFocus handling, selection retention, and reuse of the existing rename backend path.
- Refined rename again to use a dedicated name-column overlay editor:
  - the editor is positioned over the filename cell instead of reshaping the row template
  - context-menu rename now opens in one step after the flyout closes
  - the overlay editor now uses a custom lightweight textbox template and overlay container chrome instead of the default row layout
  - focus handling now keeps pointer interaction inside the rename editor stable and returns Enter-submit focus to the file list instead of the toolbar
  - renaming a directory from the right-hand list now refreshes the matching expanded branch in the left tree immediately
  - the left tree now supports context-menu rename for regular folders and refreshes the parent branch immediately after success
  - finishing a left-tree rename now restores a pointer-style tree focus instead of leaving a keyboard focus rectangle on the selected node
  - renaming a directory from the right-hand list now updates the matching left-tree node in place and falls back to refreshing only the expanded parent branch when needed
  - renaming a directory from the left tree now updates the matching row in the right-hand list when the current folder is its parent
  - left-tree rename dialog exit now returns focus to the sidebar surface instead of the tree item itself to reduce keyboard-focus flashing
  - left-tree rename no longer uses a modal dialog and now edits through a code-created `Canvas + Border + TextBox` overlay anchored to the tree item text
  - left-tree rename overlay now uses the same white-box, thin-border editor chrome as the main list rename overlay
  - left-tree rename editor dimensions and positioning now follow the tree item's text block instead of the whole node container
  - left-tree rename overlay now waits briefly for tree-item layout, reuses the same lightweight textbox template as the list rename overlay, and clamps editor width to the visible sidebar surface so the inline editor opens reliably
- Added FM-01 new-file support with:
  - a top toolbar entry and list context-menu entry
  - service-backed empty-file creation
  - local list insertion that keeps the created item selected and visible
  - immediate reuse of the existing rename prompt after creation
  - readable validation for empty, invalid, and duplicate names during the immediate rename step
- Added FM-02 new-folder support with:
  - a top toolbar entry and list context-menu entry
  - service-backed folder creation
  - reuse of the same immediate rename prompt introduced for new files
  - local folder row insertion with selection retention
  - sidebar tree refresh alignment after folder create/rename

## [0.1.1] - 2026-03-15
- WinUI sidebar refactor: pinned/tree/cloud/network/tags groups now use the revised shell styling with collapsible groups.
- Added "My Computer" home page with drive listing, drive-space columns, root-up navigation, and sidebar/tree selection sync.
- Refined tree navigation behavior: real expandability detection, separate expand vs navigate handling, and better collapsed-parent selection fallback.
- Added compact sidebar mode with icon-only groups and a dedicated "My Computer" compact entry.
- Reworked compact tree navigation from the earlier custom popup path back onto native `MenuFlyout` / `MenuFlyoutSubItem` so submenu background, shadow, hover states, and open-state visuals match WinUI more closely.
- Compact tree flyout now supports:
  - hover to open child directories
  - click on a parent item to navigate directly
  - lazy child loading at hover time instead of recursively building the full directory tree upfront
  - unlimited submenu depth at the data level
- Removed the earlier hidden compact-tree limits:
  - removed the hard-coded subtree depth cap
  - removed the hard-coded `24` items-per-level cap
- Removed the redundant "open current item" entry from compact submenus now that parent items navigate directly.
- Added and then removed temporary debug output used to validate compact hover counts and normal tree child enumeration; the final code does not ship with those traces.
- Updated compact menu presenter styling so the root flyout keeps a minimum width and relies on native `MenuFlyoutPresenter` scroll behavior rather than a custom popup scroll host.
- Updated "My Computer" icon usage:
  - compact sidebar entry now uses a display icon
  - normal sidebar tree root node now uses a display icon
- Updated the main window shell layout:
  - toolbar/address row stays on top
  - sidebar spans the lower-left column to the bottom
  - content area and status bar sit on the lower-right side
  - sidebar bottom row now includes a fixed settings button
- Added title-bar theme synchronization through `AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode` so Mica-backed title bars follow app light/dark mode correctly.
- Restored normal-mode pinned header width behavior by returning group headers to stretch alignment outside compact mode.
- Fixed a regression introduced during the icon refactor:
  - root cause: `SidebarTreeEntry` was temporarily extended to hold a `SolidColorBrush`
  - the normal tree enumerates directories on a background thread via `Task.Run`
  - creating `SolidColorBrush` on that background thread caused a WinRT COM exception
  - the enumeration code swallowed that exception and returned an empty list
  - user-visible symptom: clicking a drive arrow made the chevron disappear, but no children appeared
  - final fix: `SidebarTreeEntry` now only carries plain data needed by the template (`Name`, `FullPath`, `IconGlyph`) and no UI-thread-bound objects
- Documentation updated to explicitly record the current sidebar architecture and the rule that limits or fallback caps must not be added silently.

## [0.1.0] - 2026-03-05
- Week1: browsing/navigation/paging/rename/delete stability and cache invalidation.
- Week2: real NTFS probe + boot sector params + minimal MFT/index integration.
- Week3: layered search path (prefix + ASCII fast path + UTF-16 fallback), search UI and perf metrics.
