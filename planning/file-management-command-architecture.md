# NorthFile File Management Command Architecture

Last updated: 2026-03-16
Applies to: post-`0.1.1` file-management phase

## Goal

Keep file-management features scalable as more entry points are added.

The main risk in this phase is not the backend operation itself, but duplicated UI logic across:

- toolbar buttons
- list context menus
- future keyboard shortcuts
- future top-level menus
- future tree context menus

This document defines the command architecture that should be used for all remaining file-management work.

## Core Rule

Do not implement a feature separately for each UI entry point.

Instead, every file-management feature should be split into three layers:

1. operation layer
2. command layer
3. UI entry layer

## Layer 1: Operation Layer

Owned by:

- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\Services\ExplorerService.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Services/ExplorerService.cs)
- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\Services\FileManagementCoordinator.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Services/FileManagementCoordinator.cs)

Responsibilities:

- filesystem access
- Rust interop
- name generation
- clipboard state
- copy / cut / paste execution
- create / rename / delete execution
- conflict detection
- same-path detection
- low-level result reporting

Rules:

- no WinUI control access
- no direct `ListView`, `TreeView`, `MenuFlyout`, or focus logic
- return data/result objects instead of directly touching UI state

Examples:

- `CreateEntryAsync(...)`
- `RenameEntryAsync(...)`
- `DeleteEntryAsync(...)`
- `SetClipboard(...)`
- `PasteAsync(...)`

## Layer 2: Command Layer

Owned by:

- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\MainWindow.xaml.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/MainWindow.xaml.cs)

Responsibilities:

- resolve the current UI target
- call the coordinator
- update list state
- update tree state
- restore or move selection
- manage focus
- update status text
- decide whether fallback refresh is needed

This layer should expose one unified command function per user action.

Recommended shape:

- `ExecuteNewFileAsync()`
- `ExecuteNewFolderAsync()`
- `ExecuteRenameAsync()`
- `ExecuteDeleteAsync()`
- `ExecuteCopyAsync()`
- `ExecuteCutAsync()`
- `ExecutePasteAsync()`

Rules:

- this layer is the only place where UI state and coordinator results meet
- one user action should have one main command path
- do not duplicate the same workflow in toolbar, context menu, and shortcut handlers

## Layer 3: UI Entry Layer

Owned by:

- toolbar button handlers
- context-menu handlers
- future keyboard shortcut handlers
- future menu-bar handlers

Responsibilities:

- call the matching command-layer function
- do nothing else

Rules:

- no filesystem logic
- no direct list/tree mutation
- no duplicate validation logic
- no duplicate status strings if the command layer already provides them

Example:

- `CopyButton_Click(...)` should call `ExecuteCopyAsync()` or `ExecuteCopy()`
- `ContextCopy_Click(...)` should call the same command
- future `Ctrl+C` should also call the same command

## Capability State

All entry points should be enabled or disabled from one shared capability decision model.

Recommended capability functions in the window layer:

- `CanCreateInCurrentDirectory()`
- `CanRenameSelectedEntry()`
- `CanDeleteSelectedEntry()`
- `CanCopySelectedEntry()`
- `CanCutSelectedEntry()`
- `CanPasteIntoCurrentDirectory()`

Rules:

- toolbar enabled state should use these functions
- context-menu visibility/enabled state should use these functions
- shortcut execution should check these functions before running

This avoids each entry point inventing its own rules.

## Context Model

The command layer should work from a small set of explicit targets.

Implemented structure:

- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\Commands\FileCommandModels.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Commands/FileCommandModels.cs)
- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\Commands\FileCommandTargetResolver.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Commands/FileCommandTargetResolver.cs)
- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\Commands\FileCommandCatalog.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Commands/FileCommandCatalog.cs)
- [D:\Develop\Workspace\Rust\FileExplorer\FileExplorerUI\Commands\FileCommandProviders.cs](D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Commands/FileCommandProviders.cs)

Current target kinds:

- `ListBackground`
- `CurrentDirectory`
- `FileEntry`
- `DirectoryEntry`
- `DriveRoot`
- `TreeDirectoryNode`
- `VirtualNode`

This keeps command logic clear when the same menu surface can mean different things.

## File Traits

Do not explode the target-kind model for every file extension.

Instead:

- target kind answers "what did the user click"
- traits answer "what sort of file is this"

Current traits model:

- `Shortcut`
- `Executable`
- `Archive`
- `Image`
- `Video`
- `Audio`
- `Text`
- `Script`

This lets command availability evolve without rewriting the whole target model.

## Command Catalog

Command definitions should be composed through providers, not large `if/else` blocks inside the window layer.

Current structure:

- `BaseFileCommandProvider`
- `ShortcutFileCommandProvider`
- `ExecutableFileCommandProvider`
- `ArchiveFileCommandProvider`
- `PreviewFileCommandProvider`

Current catalog responsibilities:

- merge command contributions
- deduplicate by command id
- keep file-type-specific command growth outside `MainWindow`

## Placeholder Context Menu Matrix

This matrix is the current target design only.

It is a placeholder for gradual implementation, not a commitment to ship all commands at once.

### File

- Open
- Open with
- Share
- Compress
  - first implementation should target ZIP
  - 7z can be added later as an extension
- Create shortcut
- Copy file path
- Set tag
- Cut
- Copy
- Rename
- Delete
- Properties

### Folder

- Open
- Open in new window
- Pin to sidebar / Unpin from sidebar
- Compress
  - first implementation should target ZIP
  - 7z can be added later as an extension
- Copy folder path
- Set tag
- Open in terminal
- Cut
- Copy
- Paste
- Rename
- Delete
- Properties

### Background

- View
- Sort by
- Group by
- New
  - should be a submenu so more create options can be added later
- Open in terminal
- Paste
- Refresh
- Properties

### Notes

- `Open with` is file-only; folders and background do not need a separate command for it.
- `New` in the background menu should be implemented as a submenu so more create actions can be added later.
- ZIP should be the first compression format that becomes functional; 7z should remain a later follow-up.
- This matrix should be implemented incrementally.
- Each command should be added through the shared target/trait/catalog model first, then wired to UI entry points later.

## Refresh and Selection Rules

These rules came from the create/rename debugging work and should be preserved.

1. Do not reload the full list if a local in-place update is sufficient.
2. Do not replace `EntryViewModel` objects during page fill if they can be updated in place.
3. Do not reselect the same item multiple times in one command flow.
4. Prefer restoring selection only at the final stable point of a command.
5. For file-system changes triggered by the app itself, suppress the next same-directory watcher refresh when the local UI already has the change.

## Recommended Implementation Order

When adding a new file-management feature:

1. add operation-layer support in `ExplorerService` or `FileManagementCoordinator`
2. add one command-layer function in `MainWindow.xaml.cs`
3. add one capability function if needed
4. wire toolbar
5. wire context menu
6. wire keyboard shortcut
7. verify list/tree sync and status text

## Immediate Next Use

This architecture should be used next for:

- list context-menu visibility decisions driven by `FileCommandTargetResolver` + `FileCommandCatalog`
- FM-03 keyboard shortcut wiring for copy / cut / paste
- FM-03 conflict presentation cleanup
- FM-04 multi-select and batch actions
- FM-05 drag-and-drop command routing

## Non-Goals

This document does not define:

- the final context-menu contents
- the final shortcut map
- native Windows shell context-menu integration
- multi-select UX details

Those should be decided later on top of this structure.
