# NorthFile File Management Task Board

Last updated: 2026-03-24
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

- 2026-04-22: dual-pane copy/move now routes selected/all transfers through the shared coordinator/paste path, batches path invalidation after the filesystem operation, skips expensive per-row local insertion for large paste results, and keeps pane-transfer buttons from implicitly switching the active pane
- 2026-04-22: file-operation progress now uses a pull-based snapshot model: background operations update `FileOperationProgressStore`, while the WinUI overlay periodically reads the latest snapshot; copy/compress/extract report byte progress when totals are known, and item counts remain the fallback
- 2026-04-22: ZIP compression and extraction now share the same progress overlay and cancellation path as copy/move, with stream progress reported by bytes instead of only file count
- 2026-03-24: verified and fixed a details-row recycle regression introduced during first-frame optimization; `EntryNameCell` now listens to `EntryViewModel.PropertyChanged` for `Name`, `IconGlyph`, and `IconForeground`, so recycled rows no longer show blank name cells while type/size columns still update
- 2026-03-24: confirmed that the recent details scrolling correctness regression was not caused by the earlier `readRange` / sparse-loading refactor (`263e3f5`), but by later first-frame/layout experiments around the details repeater; the stable scrolling path has been restored and re-committed before continuing performance work
- 2026-03-24: completed a first external UI-automation smoke test with `WinUI_MCP` through OpenCode; the server can now attach to the running `NorthFile` window, read window info, and capture a WinUI accessibility snapshot, which gives a second verification path besides internal perf logs
- 2026-03-24: small-directory browse performance has now split into two separate concerns:
  - data fetch is already fast enough on the current result-set/cache path
  - remaining latency is dominated by details-mode first-frame layout/measure cost, especially for compact root/system folders such as `C:\` and `C:\Windows`
- 2026-03-24: item-level perf diagnostics showed that neither `EntryNameCell` nor `EntryItemHost` is the primary first-frame hotspot; the remaining bottleneck is the batch first-layout cost of the details row template as a whole, not a single cell update path
- 2026-03-24: first-frame optimization experiments have been partially validated:
  - useful: removing `Bindings.Update()` from `EntryNameCell`, deferring non-critical UI finalize work, and narrowing initial details layout realization
  - not useful enough by itself: flattening `EntryItemHost`
  - too risky in its last form: over-aggressive layout/viewport trimming that reintroduced blank holes or partially refreshed rows
- 2026-03-22: wired the first real placeholder context-menu commands; `Open in new window` now opens a second `MainWindow` with the target folder path, and `Open with` now goes through the system `SHOpenWithDialog` path instead of staying as a disabled placeholder
- 2026-03-22: documented the next follow-up for file context actions: `Open with` may later need an app-owned recent/recommended-program list, while `Create shortcut` still remains an unimplemented placeholder and should be split into `.lnk` creation, naming, refresh, and post-create selection work
- 2026-03-23: aligned current large-directory browse work with the longer-term result-set plan; the UI browse path is starting to move from direct directory paging calls toward a `readRange(startIndex, count)`-style result-set abstraction so future local/global index backends can plug into the same scrolling host
- 2026-03-23: moved details-mode batch metadata for normal directory browsing into the Rust result payload on the `MemoryFallback/Traditional` path, so the current visible block no longer needs a separate C# `FileInfo/DirectoryInfo` hydration pass just to fill `size` and `modified`
- 2026-03-17: documented the placeholder context-menu matrix for file, folder, and background targets; first-pass compression should target ZIP, 7z remains a later follow-up, and background `New` is expected to become a submenu
- 2026-03-17: expanded the list context menu into distinct file / folder / background display groups with placeholder commands for future features, while keeping only the already-implemented actions wired
- 2026-03-17: stabilized repeated item right-click targeting so an already-established file/folder context no longer degrades into the background menu during the list-level `ContextRequested` pass
- 2026-03-17: added an explicit row-primed context-target marker so repeated right-click on the same list item can survive the separate row-event and list-level `ContextRequested` chain without degrading into a background target
- 2026-03-17: changed mouse right-click routing so list rows and list background now open the shared flyout from separate `RightTapped` paths, reducing item-vs-background ambiguity from the old shared `ContextRequested` chain
- 2026-03-17: removed the extra row-level manual flyout opening call, so list-row right-click now only sets the item context and lets the shared attached flyout open once through the normal path
- 2026-03-17: added a strict point-hit fallback to the list-level right-click handler so repeated item right-click can still resolve the correct list item even when the row-level event does not fire, without broadening blank-space clicks into item targets
- 2026-03-17: added pending-flyout reopen routing so repeated right-click while the context menu is already open can reopen on the same item without having the `Closed` handler clear the saved item context first
- 2026-03-17: moved list-item right-click routing from the item-template content surface to the `ListViewItem` container so repeated right-click can use the full realized item container hit area before falling back to background handling
- 2026-03-17: the list context menu now starts consuming the standalone file-command model for visibility decisions, so item-vs-background menu differences are beginning to come from the shared target/trait/catalog layer instead of ad hoc checks in `MainWindow`
- 2026-03-17: added a standalone file-command model layer under `FileExplorerUI/Commands`, including target kinds, file traits, capability flags, target resolution, and a provider-based command catalog so future file-type-specific context menus do not need to be designed inside `MainWindow`
- 2026-03-16: added the first shared `Can...` capability checks and started using them to drive toolbar and context-menu state, so file-management actions no longer decide enablement separately per entry point
- 2026-03-16: started the shared command-layer refactor in `MainWindow.xaml.cs`; current new / rename / delete / copy / cut / paste entry points now converge on shared `Execute...` flows instead of duplicating UI-side validation and dispatch logic
- 2026-03-16: added a dedicated file-management command-architecture document so future create / rename / delete / copy / cut / paste / shortcut work follows the same operation-layer, command-layer, and UI-entry-layer split
- 2026-03-16: wired FM-03 single-item copy/cut/paste into the toolbar and list context menu; paste now routes through the coordinator, reloads the current directory, and refreshes the expanded current tree branch when pasted folders land in the open folder
- 2026-03-16: expanded `FileManagementCoordinator` into a real clipboard/paste coordination layer with copy/cut clipboard state, same-path/conflict reporting, and paste execution results; UI commands are not wired yet
- 2026-03-16: added `FileManagementCoordinator`; create, rename, delete, and name validation now go through a shared file-ops coordination layer for future copy/cut/paste work
- 2026-03-16: create-file and create-folder window logic now share one `CreateNewEntryAsync(bool isDirectory)` path, and default naming/physical create dispatch were moved further into `ExplorerService`
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

Current implementation note:

- `FileManagementCoordinator` now already owns clipboard state and paste execution/reporting
- FM-03 now has single-item toolbar/context-menu wiring
- FM-03 still needs keyboard shortcuts, multi-select clipboard behavior, and richer conflict handling

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

## FM-07 Context Menu Follow-up Commands

### Goal

Finish the first batch of non-placeholder list context-menu actions without regressing the shared command model.

### Tasks

- Keep `Open in new window` aligned with the shared command catalog and dynamic menu wiring
- Decide whether new windows should inherit view mode, sort mode, grouping, query text, and future panel/session state
- Keep `Open with` on the system dialog for now, but document the later custom-dialog replacement path
- Design a custom `Open with` dialog that can show recent apps, recommended apps, and a browse-more entry
- Research and encapsulate candidate-app sources for a custom `Open with` dialog:
- `OpenWithList`
- `OpenWithProgids`
- per-extension user choice / recent app state
- Implement `Create shortcut` with `.lnk` creation, conflict naming, refresh, and post-create selection behavior
- Add regression coverage so newly implemented context-menu items are not left as static disabled XAML placeholders again

### Dependencies

- Shared file-command catalog and target resolution
- `FileManagementCoordinator` for refresh/reselect patterns
- Stable window creation flow for multi-window support

### Current implementation note

- `Open in new window` is complete enough for daily use, but only carries the target path into the new window
- `Open with` is intentionally still the system dialog; the resource-explorer-style app list is a later enhancement, not current behavior
- `Create shortcut` is still pending

### Definition of done

- Folder context menu can open a new window reliably
- File context menu can open the system `Open with` dialog reliably
- Remaining first-batch placeholders have explicit follow-up notes instead of implicit disabled XAML
- `Create shortcut` has either shipped or been split into a concrete follow-up checklist with owner-ready steps
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
- Context-menu routing is being hardened: repeated right-click on entries/background now suppresses the extra `ListView.RightTapped` pass after preview-driven flyout switching to avoid double background popups.
- Adjusted context-menu suppression from one-shot flags to a short preview-switch window so switching menus no longer blocks the next intentional item right-click.
- Reworked right-click routing to a strict two-path model: `ListViewItem` handler owns item menu and `ListView` handler owns background menu only (no preview takeover, no list-side item fallback).
- Removed temporary right-click diagnostics and legacy fallback handlers after routing stabilized; retained only active item/background menu paths.
- Consolidated entries context-flyout runtime state into `EntriesContextRequest`, removing unused `primedFromRow` state and split pending fields to keep reopen/retarget logic easier to maintain.
- Shifted current-folder existence probing from menu-state calculation to action-time validation for create/paste so right-click menu opening stays I/O-light.
- Hardened pointer/cursor behavior around context-menu and splitter interactions by removing global cursor forcing and keeping splitter feedback local, then adding arrow-reset fallback on activation/resize/client-pointer movement to avoid sticky busy/resize cursors.
- Added list-scroll auto-dismiss for the entries context menu so wheel/scrollbar movement closes the open flyout immediately.
- First-batch context-menu integration is now functionally complete for:
  - `Open in new window`
  - `Open with`
  - `Create shortcut`
  - `Open target`
  - `Run as administrator`
- Context-menu follow-up rule is now explicit:
  - future command integrations must update provider visibility, `MainWindow.xaml.cs` execution/can-execute, and `MainWindow.xaml` dynamic menu wiring together so shipped commands do not remain static disabled placeholders.
- Details large-directory scrolling has been moved off the old sequential paging assumptions:
  - details now routes scrolling through viewport-range reads
  - small moves prefetch near the loaded tail
  - large jumps read the current viewport block directly
- The current details-mode UX/perf state should be treated as:
  - scrolling correctness restored and no longer the active blocker
  - small-directory first-frame cost still open
  - future optimization work must not modify the stable sparse/recycle path without an explicit UI regression check
- `WinUI_MCP` is now available as an optional external verification path for WinUI UI state:
  - attachment to a running `NorthFile` window is already validated outside the codebase
  - use it for screenshot/UIA verification when layout or interaction issues are ambiguous from logs alone
## 2026-03-23 全局索引前置设计

- 新增《全局索引数据结构与演进方案》：
  - 明确主记录表、排序索引、搜索索引三层结构
  - 明确与 `IEntryResultSet` 的集成方式
  - 明确目录级缓存到全局索引的分阶段演进顺序
  - 已补边界条件：
    - `entry_id` 语义与 FRN 复用检测
    - 分层缓存一致性规则
    - 搜索冷启动策略
    - 回退粒度
    - USN 不可用降级路径
    - 性能验收方向
- 下一阶段建议：
  - 先做全局索引方案验证与基础类型定义
  - 再新开分支做全局索引第一阶段原型
