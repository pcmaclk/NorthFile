# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]
- Week4 in progress.
- WinUI sidebar refactor: pinned/tree/cloud/network/tags groups now use the revised shell styling with collapsible groups.
- Added "My Computer" home page with drive listing, drive-space columns, root-up navigation, and sidebar/tree selection sync.
- Refined tree navigation behavior: real expandability detection, separate expand vs navigate handling, and better collapsed-parent selection fallback.
- Added compact sidebar mode with icon-only groups, a dedicated "My Computer" compact entry, and custom multi-level popup menus for compact navigation.
- Tuned compact popup menu positioning, viewport clamping, submenu behavior, and menu-like styling to better match WinUI menu interactions.

## [0.1.0] - 2026-03-05
- Week1: browsing/navigation/paging/rename/delete stability and cache invalidation.
- Week2: real NTFS probe + boot sector params + minimal MFT/index integration.
- Week3: layered search path (prefix + ASCII fast path + UTF-16 fallback), search UI and perf metrics.
