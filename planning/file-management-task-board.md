# NorthFile File Management Task Board

Last updated: 2026-03-15
Phase: post-`0.1.1`

## Scope

This board covers the current implementation phase only:

- new file
- new folder
- copy / cut / paste
- multi-select and batch delete
- drag and drop
- the minimum diagnostics and regression work needed to keep those stable

Deferred:

- USN incremental updates
- NTFS raw-path expansion
- search architecture expansion
- broader sidebar / shell redesign

## Current Progress

- 2026-03-15: extracted a shared `ExplorerService` layer for `MainWindow` and `SidebarView`
- UI code now routes the main Rust/file-system access path through the service layer before FM-01 starts
- compact sidebar tree menu now uses the same service abstraction as the main window tree
- 2026-03-15: existing item rename now uses a stabilized name-column overlay editor instead of the old dialog flow, with overlay positioning, custom textbox chrome, Enter/Escape handling, focus recovery tuned for the list surface, in-place left-tree sync for renamed directories, fallback parent-branch refresh only when needed, left-tree-to-right-list rename sync for visible parent folders, a code-created `Canvas + Border + TextBox` rename overlay for regular folder nodes in the left tree, matching editor chrome and sizing between list and tree rename surfaces, text-anchored tree rename positioning, and matching context-menu rename support on regular folder nodes in the left tree
- 2026-03-15: FM-01 new file completed with a toolbar entry, context-menu entry, service-backed empty-file creation, local row insertion, and inline rename
- 2026-03-15: FM-02 new folder completed with shared inline naming flow, service-backed folder creation, local folder row insertion, and tree/list refresh alignment

## Execution Order

1. FM-01 New File
2. FM-02 New Folder
3. FM-03 Copy / Cut / Paste
4. FM-04 Multi-select and Batch Delete
5. FM-05 Drag and Drop
6. FM-06 Diagnostics and Regression

## FM-01 New File

Status:
- completed on 2026-03-15

### Goal

Create an empty file in the current directory with an immediate naming flow.

### Tasks

- Define UI entry point for "new file"
- Add Rust or C# operation for empty file creation
- Insert the new row locally and immediately enter the existing rename flow
- Handle duplicate name, invalid name, and permission failure
- Refresh list and keep selection on the created item

### Dependencies

- Current-directory path resolution
- Existing rename flow

### Definition of done

- User can create a new empty file from the current folder
- Naming is editable immediately after creation
- Duplicate and invalid names show readable errors
- Success keeps the new item selected and visible

### Validation

- Create in writable folder
- Try duplicate name
- Try invalid name
- Try read-only or denied folder

## FM-02 New Folder

Status:
- completed on 2026-03-15

### Goal

Create a new folder in the current directory with the same immediate naming experience.

### Tasks

- Reuse the create-then-rename flow from FM-01
- Add folder creation operation
- Keep naming and error handling consistent with new file
- Refresh tree and list state after success

### Dependencies

- FM-01 interaction model

### Definition of done

- User can create a folder from the current folder
- New folder enters rename immediately
- Tree and list both reflect the new folder correctly

### Validation

- Create in writable folder
- Duplicate folder name
- Invalid name
- Denied folder

## FM-03 Copy / Cut / Paste

### Goal

Support standard clipboard-style file operations inside NorthFile.

### Tasks

- Define internal clipboard model for copy and cut
- Add command entry points: toolbar, shortcut, or context menu
- Implement paste into current directory
- Handle overwrite, duplicate, skip, and denied cases
- Decide initial scope: same-volume move, cross-volume copy, folder support
- Keep UI responsive during long operations

### Dependencies

- Stable file and folder identity in the list
- Clear current directory target

### Definition of done

- Copy and cut both work for selected items
- Paste creates correct results in the target directory
- Conflict handling is explicit
- Failure does not corrupt list state

### Validation

- File copy in same folder to duplicate name
- Copy across directories
- Cut/move across directories
- Folder copy
- Denied destination

## FM-04 Multi-select and Batch Delete

### Goal

Allow selecting multiple items and applying delete safely.

### Tasks

- Enable list multi-select
- Define ctrl/shift selection behavior
- Add batch delete flow and confirmation copy
- Keep current single-item interactions intact
- Refresh selection after delete

### Dependencies

- Existing delete flow

### Definition of done

- User can select multiple items reliably
- Batch delete works and gives clear confirmation
- Mixed file/folder selection is handled correctly

### Validation

- Ctrl multi-select
- Shift range select
- Batch delete with files only
- Batch delete with files and folders
- Delete failure on locked item

## FM-05 Drag and Drop

### Goal

Move or copy items via drag interaction.

### Tasks

- Define initial drag sources and valid targets
- Support drag from list to folder target
- Support modifier-based copy vs move if practical in phase one
- Prevent invalid targets and self-drop
- Provide visible drag feedback

### Dependencies

- Copy/move implementation from FM-03
- Multi-select model from FM-04

### Definition of done

- Dragging selected items onto a folder target performs the expected action
- Invalid drops fail safely
- Drop result refreshes list and tree state correctly

### Validation

- Drag single file to folder
- Drag multi-selection to folder
- Copy vs move behavior
- Self-drop and invalid target

## FM-06 Diagnostics and Regression

### Goal

Add the minimum safety net around file-management work.

### Tasks

- Add concise operation logging for create/copy/move/delete failure reasons
- Record a manual regression checklist for file-management flows
- Verify release publish still runs after file-management changes

### Definition of done

- File-management failures are diagnosable
- Regression checklist exists and is usable
- Release publish remains runnable

## Suggested First Slice

Start with:

1. FM-01 New File
2. FM-02 New Folder

Reason:

- smallest surface area
- reuses current rename flow
- gives a reusable inline creation pattern for later operations
