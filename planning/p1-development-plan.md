# NorthFile P1 Development Plan

Last updated: 2026-03-15
Target phase: post-`0.1.1`

## P1 Goal

Current decision: defer the filesystem-core work for now and focus this phase on file-management features first.

This phase should deliver:

- stronger core file-management operations
- the minimum engineering support needed to keep those operations stable
- no proactive expansion into USN / NTFS / indexed-search work unless re-prioritized later

## Workstreams

## 1. Deferred Core Capability

### 1.1 USN incremental updates

Goal:
- stop relying only on TTL and full refresh behavior
- move toward change-driven cache invalidation and refresh

Tasks:
- define USN state model per volume
- persist last processed USN per watched source
- read journal records incrementally
- translate journal changes into directory cache invalidation
- update visible folders without forcing full reloads where possible

Definition of done:
- directory changes on NTFS volumes can refresh from incremental records
- fallback path remains stable when USN is unavailable
- no UI-thread coupling inside incremental processing path

### 1.2 NTFS raw-path strengthening

Goal:
- expand the current NTFS capability path beyond probe-level support

Tasks:
- finalize real boot-sector and record-parameter usage
- extend raw enumeration path behind existing route selection
- compare raw-path output with traditional enumeration for correctness
- add guarded fallback when raw path fails or lacks privilege

Definition of done:
- NTFS path is used intentionally for supported scenarios
- fallback remains correct on access denied or unsupported volumes
- logs or diagnostics can show which route was chosen

### 1.3 Search architecture upgrade

Goal:
- move beyond current per-directory scan

Tasks:
- decide short-term path: recursive search or indexed search
- define search contract in Rust and UI
- support cancellation and page-based result return
- separate search result hydration from normal directory hydration

Definition of done:
- search works beyond current-folder flat scan
- cancellation is reliable
- large result sets do not block the UI

### 1.4 Large-directory performance

Goal:
- reduce time-to-first-usable-results and metadata churn

Tasks:
- measure first-page latency, scroll hitching, and metadata completion time
- tighten viewport-first hydration rules
- cancel outdated hydration work aggressively
- avoid duplicate refresh or redundant enumeration

Definition of done:
- first page arrives faster and more consistently in large folders
- fast scrolling does not cause stale metadata writeback
- status and logs can expose basic timing signals

Status:
- deferred for the current phase

## 2. File Management

### 2.1 Create operations

Current progress:
- shared backend access has been moved behind `ExplorerService`
- FM-01 new file is complete
- FM-02 new folder is complete
- FM-03 copy / cut / paste is the next task

Tasks:
- new file
- new folder
- immediate naming flow after create
- collision handling

Definition of done:
- create file/folder works from current directory
- duplicate names get clear handling

### 2.2 Clipboard operations

Tasks:
- copy
- cut
- paste
- overwrite / duplicate / skip decisions
- progress and failure messaging

Definition of done:
- basic file copy and move flows are complete
- long operations remain responsive

### 2.3 Multi-select and batch actions

Tasks:
- multi-select in list view
- batch delete
- batch copy/move preparation
- keyboard modifiers and clear selection rules

Definition of done:
- multiple items can be selected and acted on safely
- action scope is always visible to the user

### 2.4 Drag and drop

Tasks:
- drag from list to folder target
- drag between folders in list or tree
- copy vs move semantics

Definition of done:
- drag-and-drop works for common local scenarios
- invalid targets fail safely

## 3. Engineering Support Required For File Management

### 3.1 Diagnostics

Tasks:
- add concise route-selection logging
- add timings for enumeration, search, and metadata hydration
- add failure reasons for access, fallback, and copy/move errors

### 3.2 Regression checks

Tasks:
- define manual regression checklist for:
  - navigation
  - tree sync
  - search
  - create / rename / delete
  - copy / move
  - no-access folders
  - publish package launch

### 3.3 Release flow hardening

Tasks:
- keep Rust release build and UI publish in sync
- verify publish package contents
- keep release-notes flow separate from ignored docs

## Suggested Execution Order

1. Deliver new file / new folder
2. Deliver copy / cut / paste
3. Add multi-select and batch delete
4. Add drag-and-drop
5. Finalize diagnostics, regression, and release hardening for the phase
6. Re-evaluate whether to start deferred core capability work afterward

## Risks

- Copy/move flows can create many edge cases around conflicts and locks
- Drag-and-drop semantics can become inconsistent if list, tree, and shell behavior diverge
- Multi-select changes can easily regress existing single-item interactions
- Raw NTFS path may still fail on privilege-sensitive machines
- Search scope expansion can bloat memory or block UI if contracts are not paged
- USN incremental logic can corrupt cache state if path mapping is not precise

## Guardrails

- Keep UI-thread objects out of background filesystem models
- Preserve clear fallback from accelerated paths to traditional paths
- Do not mix P2 visual polish work into this phase unless it unblocks file-management delivery
- Every new long-running operation must support cancellation or safe staleness checks
