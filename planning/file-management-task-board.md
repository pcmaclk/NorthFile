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

- 2026-03-16: file rename now preselects only the base-name part in the inline editor, while folders still select the full name
- 2026-03-16: off-screen create reveal now scrolls slightly further so the created row lands with a small bottom margin instead of sticking to the viewport edge
- 2026-03-16: create-then-rename no longer preselects the newly created row before showing the rename overlay; selection is now applied only after the create rename flow finishes, reducing create-start jump
- 2026-03-16: paged row fills now mutate existing `EntryViewModel` instances instead of replacing them, so selection can survive append loads and scroll-triggered paging more reliably
- 2026-03-16: context-menu create actions now defer until the list flyout fully closes, separating right-click flyout dismissal from list insertion and rename startup
- 2026-03-16: self-created files and folders now suppress the next same-directory watcher refresh so create-then-rename is not interrupted by an immediate background reload
- 2026-03-16: added focused DEBUG tracing around list selection and create-rename startup/completion so the remaining new-item jump can be diagnosed from VS output before changing behavior again
- 2026-03-16: create flow now promotes the inserted row to the current list item immediately after insertion, then starts rename from that current item and leaves completion to the normal rename path
- 2026-03-16: create-then-rename now uses a single startup path again; off-screen targets scroll into the destination region first, then insert at the final sorted slot, then enter the existing rename overlay
- 2026-03-16: list-row pointer handling now short-circuits when the clicked row is already selected, so left-click and right-click no longer re-run the preselection path before file-management actions
- 2026-03-15: extracted a shared `ExplorerService` layer for `MainWindow` and `SidebarView`
- UI code now routes the main Rust/file-system access path through the service layer before FM-01 starts
- compact sidebar tree menu now uses the same service abstraction as the main window tree
- 2026-03-15: existing item rename now uses a stabilized name-column overlay editor instead of the old dialog flow, with overlay positioning, custom textbox chrome, Enter/Escape handling, focus recovery tuned for the list surface, in-place left-tree sync for renamed directories, fallback parent-branch refresh only when needed, left-tree-to-right-list rename sync for visible parent folders, a code-created `Canvas + Border + TextBox` rename overlay for regular folder nodes in the left tree, matching editor chrome and sizing between list and tree rename surfaces, text-anchored tree rename positioning, matching context-menu rename support on regular folder nodes in the left tree, and a more stable left-tree overlay sizing/placement pass that waits for the text anchor layout before showing the editor
- 2026-03-15: FM-01 new file completed with a toolbar entry, context-menu entry, service-backed empty-file creation, local row insertion, and immediate entry into the main list rename overlay
- 2026-03-15: FM-02 new folder completed with shared overlay naming flow, service-backed folder creation, local folder row insertion, and tree/list refresh alignment
- 2026-03-16: create-then-rename flow was tightened to avoid a redundant second list scroll before opening the rename overlay
- 2026-03-16: create flow now uses a temporary local row in the current viewport, enters rename immediately, and only commits to the final sorted slot after the user confirms the name
- 2026-03-16: list item transitions were disabled for the main list, and temporary create insertion now keys off the current visible viewport instead of the previous selection
- 2026-03-16: pending create rows now finalize by mutating the existing row model in place instead of swapping the row object, to reduce post-confirm visual jump
- 2026-03-16: create flow now explicitly reselects the new row both before and after commit so the created item remains selected
- 2026-03-16: rename overlay startup now skips redundant list reselection when the target item is already selected, so create/rename debugging can isolate overlay positioning and scroll effects from selection churn
- 2026-03-16: right-side list selection-state debugging continues with the built-in `ListView` selection visuals only; custom row-selection chrome was removed after it duplicated the native indicator
- 2026-03-16: right-side rename completion now relies on the selected-path restore already performed by the refresh pipeline, instead of applying an extra explicit reselection after refresh
- 2026-03-16: right-side rename of existing rows now stays local to the visible list row and no longer forces a full current-directory reload at rename completion
- 2026-03-16: right-side rename completion now explicitly returns focus to the file list so Enter-submit does not bounce focus into the toolbar

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

## Current Notes

- FM-01/FM-02 currently use a real-position pending row plus rename overlay.
- After create success, list selection should be applied at the end of the UI tick so post-create tree sync does not clear the visible highlight.
- Existing-item rename should mutate the current row model in place instead of replacing it, otherwise list selection is lost after commit.
- If rename selection is still unstable, prefer reloading the current directory and reselecting by final path over layering more local selection state.
- Right-side list selection should converge on a selected-path state so refresh and reorder scenarios recover selection deterministically.
- New file/new folder should ensure the final insert position is visible before showing rename, not just assume the current viewport already contains it.
- If the rename overlay cannot position because the new row starts off-screen, retry once after `ScrollIntoView` instead of silently leaving the pending item without edit mode.
- Current create flow should use a real created item plus follow-up rename, not a pending placeholder row.
- For off-screen create targets, move the viewport before insertion; for on-screen create targets, clear the old selection before insert so the new row becomes the only highlighted item.
