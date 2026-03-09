use std::ffi::{CStr, CString, c_char};
use std::fs;
use std::io::ErrorKind;
use std::path::Path;

use crate::core::types::FileEntry;
use crate::engine::classify_path;
use crate::core::types::VolumeKind;
use crate::memory::{
    clear_memory_cache, get_or_probe_ntfs_meta, invalidate_directory_cache,
    list_directory_entries_ntfs_cached, list_directory_page_memory, list_directory_page_ntfs_cached,
};
use crate::ntfs::{
    inspect_mft_record_with_meta, list_index_root_entries_with_meta, read_mft_record_header_with_meta,
};
use crate::search::Matcher;
use crate::traditional::{
    ENTRY_FLAG_DIRECTORY, ENTRY_FLAG_LINK, SORT_DIRS_FIRST_NAME_ASC, compare_directory_like,
    list_directory_all, list_directory_page, normalize_sort_mode,
};
use crate::usn::{clear_usn_capability_cache, probe_usn_capability_cached};

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FfiBatchResult {
    pub entries: *mut FileEntry,
    pub entries_len: u32,
    pub names_utf16: *mut u16,
    pub names_len: u32,
    pub total_entries: u32,
    pub scanned_entries: u32,
    pub matched_entries: u32,
    pub next_cursor: u64,
    pub suggested_next_limit: u32,
    pub source_kind: u8,
    pub error_code: i32,
    pub has_more: u8,
    pub error_message: *mut c_char,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FfiNtfsRecordHeader {
    pub magic0: u8,
    pub magic1: u8,
    pub magic2: u8,
    pub magic3: u8,
    pub usa_offset: u16,
    pub usa_count: u16,
    pub sequence_number: u16,
    pub hard_link_count: u16,
    pub attr_offset: u16,
    pub flags: u16,
    pub bytes_in_use: u32,
    pub bytes_allocated: u32,
    pub base_record_ref: u64,
    pub record_number: u32,
    pub error_code: i32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FfiNtfsRecordInsights {
    pub attr_count: u32,
    pub has_file_name: u8,
    pub has_index_root: u8,
    pub has_index_allocation: u8,
    pub has_bitmap: u8,
    pub error_code: i32,
}

#[repr(C)]
#[derive(Clone, Copy)]
pub struct FfiNtfsVolumeMeta {
    pub bytes_per_sector: u32,
    pub bytes_per_cluster: u32,
    pub bytes_per_record: u32,
    pub mft_lcn: u64,
    pub error_code: i32,
}

impl FfiNtfsVolumeMeta {
    fn ok(v: crate::ntfs::NtfsMeta) -> Self {
        Self {
            bytes_per_sector: v.bytes_per_sector,
            bytes_per_cluster: v.bytes_per_cluster,
            bytes_per_record: v.bytes_per_record,
            mft_lcn: v.mft_lcn,
            error_code: 0,
        }
    }

    fn err(code: i32) -> Self {
        Self {
            bytes_per_sector: 0,
            bytes_per_cluster: 0,
            bytes_per_record: 0,
            mft_lcn: 0,
            error_code: code,
        }
    }
}

impl FfiNtfsRecordInsights {
    fn ok(v: crate::ntfs::NtfsRecordInsights) -> Self {
        Self {
            attr_count: v.attr_count,
            has_file_name: if v.has_file_name { 1 } else { 0 },
            has_index_root: if v.has_index_root { 1 } else { 0 },
            has_index_allocation: if v.has_index_allocation { 1 } else { 0 },
            has_bitmap: if v.has_bitmap { 1 } else { 0 },
            error_code: 0,
        }
    }

    fn err(code: i32) -> Self {
        Self {
            attr_count: 0,
            has_file_name: 0,
            has_index_root: 0,
            has_index_allocation: 0,
            has_bitmap: 0,
            error_code: code,
        }
    }
}

impl FfiNtfsRecordHeader {
    fn ok(h: crate::ntfs::MftRecordHeader) -> Self {
        Self {
            magic0: h.magic[0],
            magic1: h.magic[1],
            magic2: h.magic[2],
            magic3: h.magic[3],
            usa_offset: h.usa_offset,
            usa_count: h.usa_count,
            sequence_number: h.sequence_number,
            hard_link_count: h.hard_link_count,
            attr_offset: h.attr_offset,
            flags: h.flags,
            bytes_in_use: h.bytes_in_use,
            bytes_allocated: h.bytes_allocated,
            base_record_ref: h.base_record_ref,
            record_number: h.record_number,
            error_code: 0,
        }
    }

    fn err(code: i32) -> Self {
        Self {
            magic0: 0,
            magic1: 0,
            magic2: 0,
            magic3: 0,
            usa_offset: 0,
            usa_count: 0,
            sequence_number: 0,
            hard_link_count: 0,
            attr_offset: 0,
            flags: 0,
            bytes_in_use: 0,
            bytes_allocated: 0,
            base_record_ref: 0,
            record_number: 0,
            error_code: code,
        }
    }
}

impl FfiBatchResult {
    fn ok(
        mut entries: Vec<FileEntry>,
        mut names_utf16: Vec<u16>,
        total_entries: u32,
        scanned_entries: u32,
        matched_entries: u32,
        next_cursor: u64,
        has_more: bool,
        suggested_next_limit: u32,
        source_kind: u8,
    ) -> Self {
        let entries_len = entries.len() as u32;
        let names_len = names_utf16.len() as u32;
        let entries_ptr = entries.as_mut_ptr();
        let names_ptr = names_utf16.as_mut_ptr();

        std::mem::forget(entries);
        std::mem::forget(names_utf16);

        Self {
            entries: entries_ptr,
            entries_len,
            names_utf16: names_ptr,
            names_len,
            total_entries,
            scanned_entries,
            matched_entries,
            next_cursor,
            suggested_next_limit,
            source_kind,
            error_code: 0,
            has_more: if has_more { 1 } else { 0 },
            error_message: std::ptr::null_mut(),
        }
    }

    fn err(code: i32, message: &str) -> Self {
        let safe_message = message.replace('\0', " ");
        let message_ptr = CString::new(safe_message)
            .ok()
            .map_or(std::ptr::null_mut(), CString::into_raw);

        Self {
            entries: std::ptr::null_mut(),
            entries_len: 0,
            names_utf16: std::ptr::null_mut(),
            names_len: 0,
            total_entries: 0,
            scanned_entries: 0,
            matched_entries: 0,
            next_cursor: 0,
            suggested_next_limit: 0,
            source_kind: 0,
            error_code: code,
            has_more: 0,
            error_message: message_ptr,
        }
    }
}

const SOURCE_UNKNOWN: u8 = 0;
const SOURCE_TRADITIONAL: u8 = 1;
const SOURCE_MEMORY_FALLBACK: u8 = 2;
const SOURCE_NTFS_INDEX_ROOT: u8 = 3;
const SOURCE_SEARCH: u8 = 4;

const MIN_LIMIT: u32 = 64;
const MAX_LIMIT: u32 = 1000;
const TARGET_FETCH_MS: u32 = 40;

const OP_NOT_FOUND: i32 = 2101;
const OP_PERMISSION_DENIED: i32 = 2102;
const OP_ALREADY_EXISTS: i32 = 2103;
const OP_DIR_NOT_EMPTY: i32 = 2104;
const OP_BUSY: i32 = 2105;
const OP_INVALID_INPUT: i32 = 2106;
const OP_UNSUPPORTED: i32 = 2107;
const OP_IO_GENERIC: i32 = 2199;

#[unsafe(no_mangle)]
pub extern "C" fn fe_get_engine_version() -> *const c_char {
    concat!(env!("CARGO_PKG_VERSION"), "\0").as_ptr() as *const c_char
}

fn clamp_limit(limit: u32) -> u32 {
    limit.clamp(MIN_LIMIT, MAX_LIMIT)
}

fn suggest_next_limit(requested: u32, returned: usize, has_more: bool, last_fetch_ms: u32) -> u32 {
    let mut next = clamp_limit(requested);
    if !has_more {
        return next;
    }
    if (returned as u32) < requested {
        return next;
    }
    if last_fetch_ms <= TARGET_FETCH_MS / 2 {
        next = (next.saturating_mul(2)).min(MAX_LIMIT);
    } else if last_fetch_ms >= TARGET_FETCH_MS * 2 {
        next = (next / 2).max(MIN_LIMIT);
    }
    next
}

fn map_io_error_code(kind: ErrorKind) -> i32 {
    match kind {
        ErrorKind::NotFound => OP_NOT_FOUND,
        ErrorKind::PermissionDenied => OP_PERMISSION_DENIED,
        ErrorKind::AlreadyExists => OP_ALREADY_EXISTS,
        ErrorKind::DirectoryNotEmpty => OP_DIR_NOT_EMPTY,
        ErrorKind::InvalidInput => OP_INVALID_INPUT,
        ErrorKind::Unsupported => OP_UNSUPPORTED,
        ErrorKind::WouldBlock | ErrorKind::TimedOut | ErrorKind::Interrupted => OP_BUSY,
        _ => OP_IO_GENERIC,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_ntfs_probe_volume(path_utf8: *const c_char) -> FfiNtfsVolumeMeta {
    if path_utf8.is_null() {
        return FfiNtfsVolumeMeta::err(1001);
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiNtfsVolumeMeta::err(1001),
    };
    let path = Path::new(path_str);

    match get_or_probe_ntfs_meta(path) {
        Ok(v) => FfiNtfsVolumeMeta::ok(v),
        Err(e) => FfiNtfsVolumeMeta::err(e.code as i32),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_get_demo_batch(limit: u32) -> FfiBatchResult {
    if limit == 0 || limit > 2000 {
        return FfiBatchResult::err(1001, "limit must be in range 1..=2000");
    }

    let sample_names = ["Windows", "Program Files", "Users"];
    let take = (limit as usize).min(sample_names.len());

    let mut names_utf16: Vec<u16> = Vec::new();
    let mut entries: Vec<FileEntry> = Vec::with_capacity(take);

    for (idx, name) in sample_names.iter().take(take).enumerate() {
        let off = names_utf16.len() as u32;
        let encoded: Vec<u16> = name.encode_utf16().collect();
        let len = encoded.len() as u16;

        names_utf16.extend_from_slice(&encoded);
        names_utf16.push(0);

        entries.push(FileEntry {
            parent_id: 0,
            name_off: off,
            name_len: len,
            flags: if idx == 0 { 1 } else { 0 },
            mft_ref: (idx as u64) + 1,
        });
    }

    FfiBatchResult::ok(
        entries,
        names_utf16,
        take as u32,
        take as u32,
        take as u32,
        0,
        false,
        clamp_limit(limit),
        SOURCE_UNKNOWN,
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_list_dir_batch(
    path_utf8: *const c_char,
    cursor: u64,
    limit: u32,
    last_fetch_ms: u32,
    sort_mode: u8,
) -> FfiBatchResult {
    if path_utf8.is_null() {
        return FfiBatchResult::err(1001, "path is null");
    }
    if limit == 0 || limit > 2000 {
        return FfiBatchResult::err(1001, "limit must be in range 1..=2000");
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiBatchResult::err(1001, "path is not valid utf-8"),
    };

    let sort_mode = normalize_sort_mode(sort_mode);
    let (items, next_cursor, has_more, total_entries) =
        match list_directory_page(Path::new(path_str), cursor, limit as usize, sort_mode) {
            Ok(v) => v,
            Err(e) => return FfiBatchResult::err(e.code as i32, &e.message),
        };

    let mut names_utf16: Vec<u16> = Vec::new();
    let mut entries: Vec<FileEntry> = Vec::with_capacity(items.len());

    for (idx, item) in items.iter().enumerate() {
        let off = names_utf16.len() as u32;
        let encoded: Vec<u16> = item.name.encode_utf16().collect();
        let len = encoded.len() as u16;

        names_utf16.extend_from_slice(&encoded);
        names_utf16.push(0);

        entries.push(FileEntry {
            parent_id: 0,
            name_off: off,
            name_len: len,
            flags: (if item.is_dir { ENTRY_FLAG_DIRECTORY } else { 0 })
                | (if item.is_link { ENTRY_FLAG_LINK } else { 0 }),
            mft_ref: cursor + (idx as u64) + 1,
        });
    }

    let suggested_next_limit = suggest_next_limit(limit, items.len(), has_more, last_fetch_ms);
    FfiBatchResult::ok(
        entries,
        names_utf16,
        total_entries as u32,
        total_entries as u32,
        total_entries as u32,
        next_cursor,
        has_more,
        suggested_next_limit,
        SOURCE_TRADITIONAL,
    )
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_list_dir_batch_memory(
    path_utf8: *const c_char,
    cursor: u64,
    limit: u32,
    last_fetch_ms: u32,
    sort_mode: u8,
) -> FfiBatchResult {
    if path_utf8.is_null() {
        return FfiBatchResult::err(1001, "path is null");
    }
    if limit == 0 || limit > 2000 {
        return FfiBatchResult::err(1001, "limit must be in range 1..=2000");
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiBatchResult::err(1001, "path is not valid utf-8"),
    };
    let sort_mode = normalize_sort_mode(sort_mode);
    let path = Path::new(path_str);
    let meta = match get_or_probe_ntfs_meta(path) {
        Ok(v) => v,
        Err(e) => return FfiBatchResult::err(e.code as i32, &e.message),
    };

    // Switchable source:
    // 1) Prefer NTFS INDEX_ROOT traversal for current path.
    // 2) Fall back to simulated memory directory listing for now.
    if let Some(index_root_batch) =
        try_index_root_batch(meta, path, cursor, limit, last_fetch_ms, sort_mode)
    {
        return index_root_batch;
    }

    let (items, next_cursor, has_more, total_entries) =
        match list_directory_page_memory(path, cursor, limit as usize, sort_mode) {
            Ok(v) => v,
            Err(e) => return FfiBatchResult::err(e.code as i32, &e.message),
        };

    let mut names_utf16: Vec<u16> = Vec::new();
    let mut entries: Vec<FileEntry> = Vec::with_capacity(items.len());

    for (idx, item) in items.iter().enumerate() {
        let off = names_utf16.len() as u32;
        let encoded: Vec<u16> = item.name.encode_utf16().collect();
        let len = encoded.len() as u16;

        names_utf16.extend_from_slice(&encoded);
        names_utf16.push(0);

        entries.push(FileEntry {
            parent_id: 0,
            name_off: off,
            name_len: len,
            flags: (if item.is_dir { ENTRY_FLAG_DIRECTORY } else { 0 })
                | (if item.is_link { ENTRY_FLAG_LINK } else { 0 }),
            mft_ref: cursor + (idx as u64) + 1,
        });
    }

    let suggested_next_limit = suggest_next_limit(limit, items.len(), has_more, last_fetch_ms);
    FfiBatchResult::ok(
        entries,
        names_utf16,
        total_entries as u32,
        total_entries as u32,
        total_entries as u32,
        next_cursor,
        has_more,
        suggested_next_limit,
        SOURCE_MEMORY_FALLBACK,
    )
}

fn try_index_root_batch(
    meta: crate::ntfs::NtfsMeta,
    path: &Path,
    cursor: u64,
    limit: u32,
    last_fetch_ms: u32,
    sort_mode: u8,
) -> Option<FfiBatchResult> {
    let (page, next_cursor, has_more, total_entries) = match list_directory_page_ntfs_cached(
        path,
        meta,
        cursor,
        limit as usize,
        sort_mode,
    ) {
        Ok(v) => v,
        Err(_) => return None,
    };

    // Some volume roots still hit parser gaps in the raw NTFS path and can
    // incorrectly look empty. If the NTFS path returns no rows for the first
    // page but the directory is actually non-empty, fall back to the safer path.
    if cursor == 0 && total_entries == 0 && directory_appears_non_empty(path) {
        return None;
    }

    let mut names_utf16: Vec<u16> = Vec::new();
    let mut entries: Vec<FileEntry> = Vec::with_capacity(page.len());
    for item in &page {
        let off = names_utf16.len() as u32;
        let encoded: Vec<u16> = item.name.encode_utf16().collect();
        let len = encoded.len() as u16;
        names_utf16.extend_from_slice(&encoded);
        names_utf16.push(0);
        entries.push(FileEntry {
            parent_id: 0,
            name_off: off,
            name_len: len,
            flags: item.flags,
            mft_ref: item.file_ref,
        });
    }
    let suggested_next_limit = suggest_next_limit(limit, page.len(), has_more, last_fetch_ms);

    Some(FfiBatchResult::ok(
        entries,
        names_utf16,
        total_entries as u32,
        total_entries as u32,
        total_entries as u32,
        next_cursor,
        has_more,
        suggested_next_limit,
        SOURCE_NTFS_INDEX_ROOT,
    ))
}

fn directory_appears_non_empty(path: &Path) -> bool {
    match fs::read_dir(path) {
        Ok(mut entries) => entries.next().is_some(),
        Err(_) => false,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_list_dir_batch_auto(
    path_utf8: *const c_char,
    cursor: u64,
    limit: u32,
    last_fetch_ms: u32,
    sort_mode: u8,
) -> FfiBatchResult {
    if path_utf8.is_null() {
        return FfiBatchResult::err(1001, "path is null");
    }
    if limit == 0 || limit > 2000 {
        return FfiBatchResult::err(1001, "limit must be in range 1..=2000");
    }

    let sort_mode = normalize_sort_mode(sort_mode);
    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiBatchResult::err(1001, "path is not valid utf-8"),
    };
    let path = Path::new(path_str);

    match classify_path(path) {
        VolumeKind::NtfsLocal => {
            let first_try =
                fe_list_dir_batch_memory(path_utf8, cursor, limit, last_fetch_ms, sort_mode);
            if first_try.error_code == 0 {
                first_try
            } else {
                fe_list_dir_batch(path_utf8, cursor, limit, last_fetch_ms, sort_mode)
            }
        }
        VolumeKind::OtherLocal | VolumeKind::Network => {
            fe_list_dir_batch(path_utf8, cursor, limit, last_fetch_ms, sort_mode)
        }
    }
}

#[derive(Debug, Clone)]
struct SearchItem {
    name: String,
    is_dir: bool,
    is_link: bool,
    mft_ref: u64,
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_search_dir_batch_auto(
    path_utf8: *const c_char,
    query_utf8: *const c_char,
    cursor: u64,
    limit: u32,
    last_fetch_ms: u32,
    sort_mode: u8,
) -> FfiBatchResult {
    if path_utf8.is_null() || query_utf8.is_null() {
        return FfiBatchResult::err(1001, "path/query is null");
    }
    if limit == 0 || limit > 2000 {
        return FfiBatchResult::err(1001, "limit must be in range 1..=2000");
    }

    // SAFETY: pointers must be valid null-terminated C strings from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    // SAFETY: pointers must be valid null-terminated C strings from caller.
    let c_query = unsafe { CStr::from_ptr(query_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiBatchResult::err(1001, "path is not valid utf-8"),
    };
    let query = match c_query.to_str() {
        Ok(v) => v.trim(),
        Err(_) => return FfiBatchResult::err(1001, "query is not valid utf-8"),
    };
    let path = Path::new(path_str);
    let sort_mode = normalize_sort_mode(sort_mode);
    let matcher = Matcher::new(query);

    let all_items = match collect_search_items(path, sort_mode) {
        Ok(v) => v,
        Err(e) => return FfiBatchResult::err(e.code as i32, &e.message),
    };
    let scanned_entries = all_items.len() as u32;
    let filtered: Vec<SearchItem> = if matcher.is_empty() {
        all_items
    } else {
        all_items
            .into_iter()
            .filter(|item| matcher.is_match(&item.name))
            .collect()
    };
    let mut filtered = filtered;
    sort_search_items(&mut filtered, sort_mode);
    let matched_entries = filtered.len() as u32;

    let start = cursor as usize;
    if start >= filtered.len() {
        return FfiBatchResult::ok(
            Vec::new(),
            Vec::new(),
            filtered.len() as u32,
            scanned_entries,
            matched_entries,
            cursor,
            false,
            clamp_limit(limit),
            SOURCE_SEARCH,
        );
    }
    let end = (start + limit as usize).min(filtered.len());
    let page = &filtered[start..end];
    let has_more = end < filtered.len();
    let next_cursor = if has_more { end as u64 } else { cursor };

    let mut names_utf16: Vec<u16> = Vec::new();
    let mut entries: Vec<FileEntry> = Vec::with_capacity(page.len());
    for (idx, item) in page.iter().enumerate() {
        let off = names_utf16.len() as u32;
        let encoded: Vec<u16> = item.name.encode_utf16().collect();
        let len = encoded.len() as u16;
        names_utf16.extend_from_slice(&encoded);
        names_utf16.push(0);
        entries.push(FileEntry {
            parent_id: 0,
            name_off: off,
            name_len: len,
            flags: (if item.is_dir { ENTRY_FLAG_DIRECTORY } else { 0 })
                | (if item.is_link { ENTRY_FLAG_LINK } else { 0 }),
            mft_ref: if item.mft_ref == 0 {
                cursor + (idx as u64) + 1
            } else {
                item.mft_ref
            },
        });
    }

    let suggested_next_limit = suggest_next_limit(limit, page.len(), has_more, last_fetch_ms);
    FfiBatchResult::ok(
        entries,
        names_utf16,
        filtered.len() as u32,
        scanned_entries,
        matched_entries,
        next_cursor,
        has_more,
        suggested_next_limit,
        SOURCE_SEARCH,
    )
}

fn sort_search_items(items: &mut [SearchItem], sort_mode: u8) {
    match normalize_sort_mode(sort_mode) {
        SORT_DIRS_FIRST_NAME_ASC => items.sort_unstable_by(|a, b| {
            compare_directory_like(a.is_dir, &a.name, b.is_dir, &b.name)
        }),
        _ => unreachable!(),
    }
}

fn collect_search_items(
    path: &Path,
    sort_mode: u8,
) -> Result<Vec<SearchItem>, crate::core::error::FsError> {
    match classify_path(path) {
        VolumeKind::NtfsLocal => {
            // If raw NTFS volume access is denied (common without elevated privileges),
            // gracefully fall back to traditional directory enumeration.
            if let Ok(meta) = get_or_probe_ntfs_meta(path) {
                if let Ok(items) = list_directory_entries_ntfs_cached(path, meta, sort_mode) {
                    return Ok(items
                        .into_iter()
                        .map(|v| SearchItem {
                            name: v.name,
                            is_dir: (v.flags & 0x0001) != 0,
                            is_link: (v.flags & 0x0002) != 0,
                            mft_ref: v.file_ref,
                        })
                        .collect());
                }
            }

            let items = list_directory_all(path, sort_mode)?;
            Ok(items
                .into_iter()
                .map(|v| SearchItem {
                    name: v.name,
                    is_dir: v.is_dir,
                    is_link: v.is_link,
                    mft_ref: 0,
                })
                .collect())
        }
        VolumeKind::OtherLocal | VolumeKind::Network => {
            let items = list_directory_all(path, sort_mode)?;
            Ok(items
                .into_iter()
                .map(|v| SearchItem {
                    name: v.name,
                    is_dir: v.is_dir,
                    is_link: v.is_link,
                    mft_ref: 0,
                })
                .collect())
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_free_batch_result(result: FfiBatchResult) {
    if !result.entries.is_null() && result.entries_len > 0 {
        let len = result.entries_len as usize;
        // SAFETY: The buffer was allocated by Rust in fe_get_demo_batch and ownership is
        // transferred back exactly once via this function.
        unsafe {
            let _ = Vec::from_raw_parts(result.entries, len, len);
        }
    }

    if !result.names_utf16.is_null() && result.names_len > 0 {
        let len = result.names_len as usize;
        // SAFETY: The buffer was allocated by Rust in fe_get_demo_batch and ownership is
        // transferred back exactly once via this function.
        unsafe {
            let _ = Vec::from_raw_parts(result.names_utf16, len, len);
        }
    }

    if !result.error_message.is_null() {
        // SAFETY: error_message is created with CString::into_raw in this module.
        unsafe {
            let _ = CString::from_raw(result.error_message);
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_memory_invalidate_dir(path_utf8: *const c_char) -> i32 {
    if path_utf8.is_null() {
        return 1001;
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return 1001,
    };

    match invalidate_directory_cache(Path::new(path_str)) {
        Ok(_) => 0,
        Err(e) => e.code as i32,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_memory_clear_cache() -> i32 {
    let mem = clear_memory_cache();
    let usn = clear_usn_capability_cache();
    match (mem, usn) {
        (Ok(_), Ok(_)) => 0,
        (Err(e), _) => e.code as i32,
        (_, Err(e)) => e.code as i32,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_usn_mark_path_changed(path_utf8: *const c_char) -> i32 {
    if path_utf8.is_null() {
        return 1001;
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return 1001,
    };

    match invalidate_directory_cache(Path::new(path_str)) {
        Ok(_) => 0,
        Err(e) => e.code as i32,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_delete_path(path_utf8: *const c_char, recursive: u8) -> i32 {
    if path_utf8.is_null() {
        return 1001;
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return 1001,
    };

    let path = Path::new(path_str);
    let metadata = match fs::metadata(path) {
        Ok(v) => v,
        Err(e) => return map_io_error_code(e.kind()),
    };

    let result = if metadata.is_dir() {
        if recursive != 0 {
            fs::remove_dir_all(path)
        } else {
            fs::remove_dir(path)
        }
    } else {
        fs::remove_file(path)
    };

    match result {
        Ok(_) => 0,
        Err(e) => map_io_error_code(e.kind()),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_rename_path(src_utf8: *const c_char, dst_utf8: *const c_char) -> i32 {
    if src_utf8.is_null() || dst_utf8.is_null() {
        return 1001;
    }

    // SAFETY: pointers must be valid null-terminated C strings from caller.
    let src_c = unsafe { CStr::from_ptr(src_utf8) };
    // SAFETY: pointers must be valid null-terminated C strings from caller.
    let dst_c = unsafe { CStr::from_ptr(dst_utf8) };
    let src = match src_c.to_str() {
        Ok(v) => v,
        Err(_) => return 1001,
    };
    let dst = match dst_c.to_str() {
        Ok(v) => v,
        Err(_) => return 1001,
    };

    match fs::rename(src, dst) {
        Ok(_) => 0,
        Err(e) => map_io_error_code(e.kind()),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_ntfs_read_record_header(
    path_utf8: *const c_char,
    record_index: u64,
) -> FfiNtfsRecordHeader {
    if path_utf8.is_null() {
        return FfiNtfsRecordHeader::err(1001);
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiNtfsRecordHeader::err(1001),
    };
    let path = Path::new(path_str);

    let meta = match get_or_probe_ntfs_meta(path) {
        Ok(v) => v,
        Err(e) => return FfiNtfsRecordHeader::err(e.code as i32),
    };

    match read_mft_record_header_with_meta(path, meta, record_index) {
        Ok(h) => FfiNtfsRecordHeader::ok(h),
        Err(e) => FfiNtfsRecordHeader::err(e.code as i32),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_ntfs_inspect_record(
    path_utf8: *const c_char,
    record_index: u64,
) -> FfiNtfsRecordInsights {
    if path_utf8.is_null() {
        return FfiNtfsRecordInsights::err(1001);
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiNtfsRecordInsights::err(1001),
    };
    let path = Path::new(path_str);

    let meta = match get_or_probe_ntfs_meta(path) {
        Ok(v) => v,
        Err(e) => return FfiNtfsRecordInsights::err(e.code as i32),
    };

    match inspect_mft_record_with_meta(path, meta, record_index) {
        Ok(v) => FfiNtfsRecordInsights::ok(v),
        Err(e) => FfiNtfsRecordInsights::err(e.code as i32),
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_ntfs_list_index_root(
    path_utf8: *const c_char,
    record_index: u64,
    cursor: u64,
    limit: u32,
    sort_mode: u8,
) -> FfiBatchResult {
    if path_utf8.is_null() {
        return FfiBatchResult::err(1001, "path is null");
    }
    if limit == 0 || limit > 2000 {
        return FfiBatchResult::err(1001, "limit must be in range 1..=2000");
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiBatchResult::err(1001, "path is not valid utf-8"),
    };
    let path = Path::new(path_str);

    let meta = match get_or_probe_ntfs_meta(path) {
        Ok(v) => v,
        Err(e) => return FfiBatchResult::err(e.code as i32, &e.message),
    };

    let all = match list_index_root_entries_with_meta(path, meta, record_index) {
        Ok(v) => v,
        Err(e) => return FfiBatchResult::err(e.code as i32, &e.message),
    };
    let mut all = all;
    match normalize_sort_mode(sort_mode) {
        SORT_DIRS_FIRST_NAME_ASC => all.sort_unstable_by(|a, b| {
            compare_directory_like(
                (a.flags & 0x0001) != 0,
                &a.name,
                (b.flags & 0x0001) != 0,
                &b.name,
            )
        }),
        _ => unreachable!(),
    }

    let start = cursor as usize;
    if start >= all.len() {
        return FfiBatchResult::ok(
            Vec::new(),
            Vec::new(),
            all.len() as u32,
            all.len() as u32,
            all.len() as u32,
            cursor,
            false,
            clamp_limit(limit),
            SOURCE_NTFS_INDEX_ROOT,
        );
    }
    let end = (start + limit as usize).min(all.len());
    let page = &all[start..end];
    let has_more = end < all.len();
    let next_cursor = if has_more { end as u64 } else { cursor };

    let mut names_utf16: Vec<u16> = Vec::new();
    let mut entries: Vec<FileEntry> = Vec::with_capacity(page.len());
    for item in page {
        let off = names_utf16.len() as u32;
        let encoded: Vec<u16> = item.name.encode_utf16().collect();
        let len = encoded.len() as u16;
        names_utf16.extend_from_slice(&encoded);
        names_utf16.push(0);
        entries.push(FileEntry {
            parent_id: 0,
            name_off: off,
            name_len: len,
            flags: item.flags,
            mft_ref: item.file_ref,
        });
    }

    FfiBatchResult::ok(
        entries,
        names_utf16,
        all.len() as u32,
        all.len() as u32,
        all.len() as u32,
        next_cursor,
        has_more,
        clamp_limit(limit),
        SOURCE_NTFS_INDEX_ROOT,
    )
}


#[repr(C)]
#[derive(Clone, Copy)]
pub struct FfiUsnCapability {
    pub is_ntfs_local: u8,
    pub can_open_volume: u8,
    pub access_denied: u8,
    pub available: u8,
    pub error_code: i32,
}

impl FfiUsnCapability {
    fn ok(v: crate::usn::UsnCapability) -> Self {
        Self {
            is_ntfs_local: if v.is_ntfs_local { 1 } else { 0 },
            can_open_volume: if v.can_open_volume { 1 } else { 0 },
            access_denied: if v.access_denied { 1 } else { 0 },
            available: if v.available { 1 } else { 0 },
            error_code: 0,
        }
    }

    fn err(code: i32) -> Self {
        Self {
            is_ntfs_local: 0,
            can_open_volume: 0,
            access_denied: 0,
            available: 0,
            error_code: code,
        }
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn fe_usn_probe_volume(path_utf8: *const c_char) -> FfiUsnCapability {
    if path_utf8.is_null() {
        return FfiUsnCapability::err(1001);
    }

    // SAFETY: path_utf8 must be a valid null-terminated C string from caller.
    let c_path = unsafe { CStr::from_ptr(path_utf8) };
    let path_str = match c_path.to_str() {
        Ok(v) => v,
        Err(_) => return FfiUsnCapability::err(1001),
    };

    match probe_usn_capability_cached(Path::new(path_str)) {
        Ok(v) => FfiUsnCapability::ok(v),
        Err(e) => FfiUsnCapability::err(e.code as i32),
    }
}

