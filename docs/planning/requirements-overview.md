# NorthFile Requirements Overview

Last updated: 2026-03-15
Current version baseline: `0.1.1`

## Current Baseline

NorthFile currently provides:

- My Computer home page with drive list
- Normal sidebar and compact sidebar modes
- Tree navigation with expand vs navigate separation
- Pinned section, cloud/network/tags placeholder groups
- Breadcrumb navigation
- Paged directory listing
- Async metadata hydration
- Rename, delete, double-click open
- Current-directory search
- WinUI 3 shell with Rust engine integration
- Unpackaged `win-x64` publish flow

## Product Goal

Build NorthFile into a stable Windows desktop file manager with:

- Strong large-directory performance
- Clear and modern WinUI shell
- Rust-backed filesystem access and indexing
- Progressive NTFS-native acceleration path
- Reliable release packaging and regression validation

## Remaining Work By Priority

## P1 Core Capability

- USN Journal incremental update pipeline
- Stronger NTFS raw-path implementation for browsing and indexing
- Search upgrade from directory scan to indexed or recursive model
- Large-directory performance work on first paint, scrolling, metadata hydration, and cancellation

## P1 File Management

- Create new file
- Create new folder
- Copy / cut / paste
- Drag-and-drop move or copy
- Multi-select and batch operations
- Conflict handling for overwrite / duplicate names
- Better delete and recycle-bin strategy

## P2 Explorer Experience

- More sorting, filtering, grouping options
- Multiple view modes such as details / list / icons
- User-defined pinned items
- Functional cloud / network / tags sections
- Richer context menu and shortcuts
- Optional native Windows shell context-menu integration for file, folder, and background surfaces
- Settings page for theme, startup page, behavior, and performance options

## P2 Engineering and Release

- Broader regression coverage
- Repeatable release flow for Rust build, UI publish, packaging, and release notes
- Better diagnostics and error logging
- Cleaner versioning and release automation

## P1 Scope Definition

The next implementation phase should focus on P1 only:

- filesystem capability and performance
- core file-management operations
- engineering work directly required to keep those features stable

P2 items should not interrupt P1 unless they unblock P1 delivery.
