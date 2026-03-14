# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]
- Week4 in progress.
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
