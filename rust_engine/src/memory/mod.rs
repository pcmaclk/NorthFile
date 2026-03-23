use std::collections::HashMap;
use std::fs;
use std::fs::OpenOptions;
use std::io::Write;
use std::path::Path;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant, SystemTime};

use crate::core::error::{FsError, FsErrorCode};
use crate::core::traits::PathEngine;
use crate::core::types::{DirPage, EnumReq, FileEntry, FileMeta, SearchPage, SearchReq};
use crate::engine::validate_limit;
use crate::ntfs::{
    NtfsIndexRootEntry, NtfsMeta, list_directory_index_root_entries_for_path_with_meta,
    probe_ntfs_for_path,
};
use crate::traditional::{
    SORT_DIRS_FIRST_NAME_ASC, compare_directory_like, normalize_sort_mode, resolve_entry_is_dir,
    resolve_entry_is_link,
};

pub struct StringPool {
    data: Vec<u16>,
}

impl StringPool {
    pub fn new() -> Self {
        Self { data: Vec::new() }
    }

    pub fn intern(&mut self, s: &[u16]) -> (u32, u16) {
        let off = self.data.len() as u32;
        self.data.extend_from_slice(s);
        self.data.push(0);
        (off, s.len() as u16)
    }
}

impl Default for StringPool {
    fn default() -> Self {
        Self::new()
    }
}

pub struct FileTable {
    pub entries: Vec<FileEntry>,
    pub names: StringPool,
}

impl FileTable {
    pub fn new() -> Self {
        Self {
            entries: Vec::new(),
            names: StringPool::new(),
        }
    }
}

impl Default for FileTable {
    fn default() -> Self {
        Self::new()
    }
}

pub struct MemoryPathEngine {
    _table: FileTable,
}

impl MemoryPathEngine {
    pub fn new() -> Self {
        Self {
            _table: FileTable::new(),
        }
    }
}

impl Default for MemoryPathEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl PathEngine for MemoryPathEngine {
    fn enumerate_dir(&self, req: EnumReq) -> Result<DirPage, FsError> {
        validate_limit(req.limit)?;
        let _ = req;
        Ok(DirPage {
            entries: Vec::new(),
            next_cursor: None,
            has_more: false,
        })
    }

    fn search(&self, req: SearchReq) -> Result<SearchPage, FsError> {
        validate_limit(req.limit)?;
        let _ = req;
        Ok(SearchPage {
            entries: Vec::new(),
            next_cursor: None,
            has_more: false,
        })
    }

    fn get_meta(&self, _file_ref: u64) -> Result<FileMeta, FsError> {
        Err(FsError::new(
            FsErrorCode::Unsupported,
            "MemoryPathEngine::get_meta is not implemented yet",
        ))
    }
}

#[derive(Debug, Clone)]
pub struct MemoryDirItem {
    pub name: String,
    pub is_dir: bool,
    pub is_link: bool,
    pub size_bytes: Option<u64>,
    pub modified_unix_ms: Option<i64>,
}

const MEMORY_DIR_CACHE_IDLE_TTL: Duration = Duration::from_secs(15);
const NTFS_META_CACHE_TTL: Duration = Duration::from_secs(15);
const NTFS_DIR_CACHE_IDLE_TTL: Duration = Duration::from_secs(30);
const MEMORY_DIR_CACHE_MAX_ENTRIES: usize = 48;
const NTFS_DIR_CACHE_MAX_ENTRIES: usize = 96;

#[derive(Debug, Clone)]
struct CachedDirectory {
    items: Vec<MemoryDirItem>,
    last_accessed_at: Instant,
    dir_modified: Option<SystemTime>,
}

type MemoryTable = HashMap<String, CachedDirectory>;

fn memory_table() -> &'static Mutex<MemoryTable> {
    static TABLE: OnceLock<Mutex<MemoryTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

#[derive(Debug, Clone, Copy)]
struct CachedNtfsMetaOk {
    meta: NtfsMeta,
    cached_at: Instant,
}

#[derive(Debug, Clone)]
enum CachedNtfsMeta {
    Ok(CachedNtfsMetaOk),
    Err(FsError, Instant),
}

type NtfsMetaTable = HashMap<String, CachedNtfsMeta>;

fn ntfs_meta_table() -> &'static Mutex<NtfsMetaTable> {
    static TABLE: OnceLock<Mutex<NtfsMetaTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

#[derive(Debug, Clone)]
struct CachedNtfsDirectory {
    items: Vec<NtfsIndexRootEntry>,
    last_accessed_at: Instant,
}

type NtfsDirTable = HashMap<String, CachedNtfsDirectory>;

fn ntfs_dir_table() -> &'static Mutex<NtfsDirTable> {
    static TABLE: OnceLock<Mutex<NtfsDirTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

fn append_range_perf_log(message: &str) {
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|path| path.parent().map(|parent| parent.to_path_buf()));
    let Some(dir) = exe_dir else {
        return;
    };

    let path = dir.join("rust-range-perf.log");
    if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{}", message);
    }
}

fn directory_cache_key(dir_path: &Path, sort_mode: u8) -> String {
    format!("{}|{}", dir_path.to_string_lossy(), normalize_sort_mode(sort_mode))
}

fn prune_oldest_memory_directories(table: &mut MemoryTable) {
    while table.len() > MEMORY_DIR_CACHE_MAX_ENTRIES {
        let Some(oldest_key) = table
            .iter()
            .min_by_key(|(_, value)| value.last_accessed_at)
            .map(|(key, _)| key.clone())
        else {
            break;
        };
        table.remove(&oldest_key);
    }
}

fn prune_oldest_ntfs_directories(table: &mut NtfsDirTable) {
    while table.len() > NTFS_DIR_CACHE_MAX_ENTRIES {
        let Some(oldest_key) = table
            .iter()
            .min_by_key(|(_, value)| value.last_accessed_at)
            .map(|(key, _)| key.clone())
        else {
            break;
        };
        table.remove(&oldest_key);
    }
}

fn sort_memory_items(items: &mut [MemoryDirItem], sort_mode: u8) {
    match normalize_sort_mode(sort_mode) {
        SORT_DIRS_FIRST_NAME_ASC => items.sort_unstable_by(|a, b| {
            compare_directory_like(a.is_dir, &a.name, b.is_dir, &b.name)
        }),
        _ => unreachable!(),
    }
}

fn sort_ntfs_items(items: &mut [NtfsIndexRootEntry], sort_mode: u8) {
    match normalize_sort_mode(sort_mode) {
        SORT_DIRS_FIRST_NAME_ASC => items.sort_unstable_by(|a, b| {
            compare_directory_like(
                (a.flags & 0x0001) != 0,
                &a.name,
                (b.flags & 0x0001) != 0,
                &b.name,
            )
        }),
        _ => unreachable!(),
    }
}

fn system_time_to_unix_ms(value: SystemTime) -> Option<i64> {
    value
        .duration_since(SystemTime::UNIX_EPOCH)
        .ok()
        .and_then(|duration| i64::try_from(duration.as_millis()).ok())
}

fn populate_memory_item_metadata(dir_path: &Path, item: &mut MemoryDirItem) {
    if item.modified_unix_ms.is_some() && (item.is_dir || item.size_bytes.is_some()) {
        return;
    }

    let full_path = dir_path.join(&item.name);
    let metadata = match fs::metadata(&full_path) {
        Ok(value) => value,
        Err(_) => return,
    };

    if !item.is_dir && item.size_bytes.is_none() {
        item.size_bytes = Some(metadata.len());
    }

    if item.modified_unix_ms.is_none() {
        item.modified_unix_ms = metadata.modified().ok().and_then(system_time_to_unix_ms);
    }
}

pub fn invalidate_directory_cache(dir_path: &Path) -> Result<bool, FsError> {
    let key_prefix = format!("{}|", dir_path.to_string_lossy());
    let mut table = memory_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock memory path table (poisoned mutex)",
        )
    })?;
    let mem_before = table.len();
    table.retain(|key, _| !key.starts_with(&key_prefix));
    let removed_mem = table.len() != mem_before;

    let mut ntfs_table = ntfs_dir_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs dir table (poisoned mutex)",
        )
    })?;
    let ntfs_before = ntfs_table.len();
    ntfs_table.retain(|key, _| !key.starts_with(&key_prefix));
    let removed_ntfs = ntfs_table.len() != ntfs_before;
    Ok(removed_mem || removed_ntfs)
}

pub fn clear_memory_cache() -> Result<(), FsError> {
    let mut table = memory_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock memory path table (poisoned mutex)",
        )
    })?;
    table.clear();

    let mut meta_table = ntfs_meta_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs meta table (poisoned mutex)",
        )
    })?;
    meta_table.clear();

    let mut ntfs_dir = ntfs_dir_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs dir table (poisoned mutex)",
        )
    })?;
    ntfs_dir.clear();

    Ok(())
}

pub fn get_or_probe_ntfs_meta(path: &Path) -> Result<NtfsMeta, FsError> {
    let root_key = volume_root_key(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path does not contain a valid local volume root",
        )
    })?;

    {
        let table = ntfs_meta_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs meta table (poisoned mutex)",
            )
        })?;
        if let Some(cached) = table.get(&root_key) {
            match cached {
                CachedNtfsMeta::Ok(cached_ok) => {
                    if cached_ok.cached_at.elapsed() <= NTFS_META_CACHE_TTL {
                        return Ok(cached_ok.meta);
                    }
                }
                CachedNtfsMeta::Err(err, cached_at) => {
                    if cached_at.elapsed() <= NTFS_META_CACHE_TTL {
                        return Err(err.clone());
                    }
                }
            }
        }
    }

    let probe_result = probe_ntfs_for_path(path);

    let mut table = ntfs_meta_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs meta table (poisoned mutex)",
        )
    })?;
    let now = Instant::now();
    match probe_result {
        Ok(meta) => {
            table.insert(
                root_key,
                CachedNtfsMeta::Ok(CachedNtfsMetaOk {
                    meta,
                    cached_at: now,
                }),
            );
            Ok(meta)
        }
        Err(err) => {
            table.insert(root_key, CachedNtfsMeta::Err(err.clone(), now));
            Err(err)
        }
    }
}

fn volume_root_key(path: &Path) -> Option<String> {
    #[cfg(windows)]
    {
        let raw = path.to_string_lossy();
        let bytes = raw.as_bytes();
        if bytes.len() >= 3 && bytes[1] == b':' && (bytes[2] == b'\\' || bytes[2] == b'/') {
            let drive = raw.chars().next()?;
            return Some(format!("{}:\\", drive.to_ascii_uppercase()));
        }
        None
    }
    #[cfg(not(windows))]
    {
        let _ = path;
        None
    }
}

pub fn list_directory_page_memory(
    dir_path: &Path,
    cursor: u64,
    limit: usize,
    sort_mode: u8,
) -> Result<(Vec<MemoryDirItem>, u64, bool, usize), FsError> {
    let total_sw = Instant::now();
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }

    let key = directory_cache_key(dir_path, sort_mode);
    let mut table = memory_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock memory path table (poisoned mutex)",
        )
    })?;

    let metadata_sw = Instant::now();
    let current_modified = fs::metadata(dir_path).ok().and_then(|m| m.modified().ok());
    let metadata_ms = metadata_sw.elapsed().as_millis();
    let needs_reload = match table.get(&key) {
        Some(cached) => {
            let expired = cached.last_accessed_at.elapsed() > MEMORY_DIR_CACHE_IDLE_TTL;
            let changed = cached.dir_modified != current_modified;
            expired || changed
        }
        None => true,
    };

    let cache_mode;
    if needs_reload {
        cache_mode = "reload";
        let read_sw = Instant::now();
        let mut loaded: Vec<MemoryDirItem> = Vec::new();
        let entries = fs::read_dir(dir_path).map_err(|e| {
            FsError::new(
                FsErrorCode::VolumeAccess,
                format!("failed to read directory '{}': {}", dir_path.display(), e),
            )
        })?;

        for dir_entry in entries.flatten() {
            let file_type = match dir_entry.file_type() {
                Ok(v) => v,
                Err(_) => continue,
            };
            let is_dir = resolve_entry_is_dir(&dir_entry, &file_type);
            loaded.push(MemoryDirItem {
                name: dir_entry.file_name().to_string_lossy().into_owned(),
                is_dir,
                is_link: resolve_entry_is_link(&dir_entry),
                size_bytes: None,
                modified_unix_ms: None,
            });
        }
        let read_ms = read_sw.elapsed().as_millis();
        let sort_sw = Instant::now();
        sort_memory_items(&mut loaded, sort_mode);
        let sort_ms = sort_sw.elapsed().as_millis();
        let now = Instant::now();
        table.insert(
            key.clone(),
            CachedDirectory {
                items: loaded.clone(),
                last_accessed_at: now,
                dir_modified: current_modified,
            },
        );
        prune_oldest_memory_directories(&mut table);
        append_range_perf_log(&format!(
            "[RUST-RANGE] kind=memory path=\"{}\" cursor={} limit={} mode={} metadata={}ms read={}ms sort={}ms total_items={}",
            dir_path.display(),
            cursor,
            limit,
            cache_mode,
            metadata_ms,
            read_ms,
            sort_ms,
            loaded.len()
        ));
    } else {
        cache_mode = "hit";
        match table.get_mut(&key) {
            Some(cached) => {
                cached.last_accessed_at = Instant::now();
                append_range_perf_log(&format!(
                    "[RUST-RANGE] kind=memory path=\"{}\" cursor={} limit={} mode={} metadata={}ms total_items={}",
                    dir_path.display(),
                    cursor,
                    limit,
                    cache_mode,
                    metadata_ms,
                    cached.items.len()
                ));
            }
            None => {}
        }
    }

    let cached = table.get_mut(&key).ok_or_else(|| {
        FsError::new(
            FsErrorCode::Internal,
            "memory directory cache entry was not available after load",
        )
    })?;
    cached.last_accessed_at = Instant::now();

    let total_items = cached.items.len();
    let start = cursor as usize;
    if start >= total_items {
        return Ok((Vec::new(), cursor, false, total_items));
    }

    let end = (start + limit).min(total_items);
    let hydrate_sw = Instant::now();
    let mut hydrated = 0usize;
    for item in &mut cached.items[start..end] {
        let had_size = item.size_bytes.is_some();
        let had_modified = item.modified_unix_ms.is_some();
        populate_memory_item_metadata(dir_path, item);
        if (!had_size && item.size_bytes.is_some()) || (!had_modified && item.modified_unix_ms.is_some()) {
            hydrated += 1;
        }
    }

    let slice_sw = Instant::now();
    let page = cached.items[start..end].to_vec();
    let has_more = end < total_items;
    let next_cursor = if has_more { end as u64 } else { cursor };
    append_range_perf_log(&format!(
        "[RUST-RANGE] kind=memory-slice path=\"{}\" cursor={} limit={} mode={} hydrate={}ms hydrated={} slice={}ms page_rows={} total_items={} total={}ms",
        dir_path.display(),
        cursor,
        limit,
        cache_mode,
        hydrate_sw.elapsed().as_millis(),
        hydrated,
        slice_sw.elapsed().as_millis(),
        page.len(),
        total_items,
        total_sw.elapsed().as_millis()
    ));

    Ok((page, next_cursor, has_more, total_items))
}

pub fn list_directory_entries_ntfs_cached(
    dir_path: &Path,
    meta: NtfsMeta,
    sort_mode: u8,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    let key = directory_cache_key(dir_path, sort_mode);

    {
        let mut table = ntfs_dir_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs dir table (poisoned mutex)",
            )
        })?;
        if let Some(cached) = table.get_mut(&key) {
            if cached.last_accessed_at.elapsed() <= NTFS_DIR_CACHE_IDLE_TTL {
                cached.last_accessed_at = Instant::now();
                return Ok(cached.items.clone());
            }
        }
    }

    let mut loaded = list_directory_index_root_entries_for_path_with_meta(dir_path, meta)?;
    sort_ntfs_items(&mut loaded, sort_mode);
    let mut table = ntfs_dir_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs dir table (poisoned mutex)",
        )
    })?;
    let now = Instant::now();
    table.insert(
        key,
        CachedNtfsDirectory {
            items: loaded.clone(),
            last_accessed_at: now,
        },
    );
    prune_oldest_ntfs_directories(&mut table);
    Ok(loaded)
}

pub fn list_directory_page_ntfs_cached(
    dir_path: &Path,
    meta: NtfsMeta,
    cursor: u64,
    limit: usize,
    sort_mode: u8,
) -> Result<(Vec<NtfsIndexRootEntry>, u64, bool, usize), FsError> {
    let total_sw = Instant::now();
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }

    let key = directory_cache_key(dir_path, sort_mode);

    {
        let mut table = ntfs_dir_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs dir table (poisoned mutex)",
            )
        })?;
        if let Some(cached) = table.get_mut(&key) {
            if cached.last_accessed_at.elapsed() <= NTFS_DIR_CACHE_IDLE_TTL {
                let clone_sw = Instant::now();
                cached.last_accessed_at = Instant::now();
                let items = cached.items.clone();
                let clone_ms = clone_sw.elapsed().as_millis();
                let total = items.len();
                let start = cursor as usize;
                if start >= total {
                    return Ok((Vec::new(), cursor, false, total));
                }
                let slice_sw = Instant::now();
                let end = (start + limit).min(total);
                let page = items[start..end].to_vec();
                let has_more = end < total;
                let next_cursor = if has_more { end as u64 } else { cursor };
                append_range_perf_log(&format!(
                    "[RUST-RANGE] kind=ntfs path=\"{}\" cursor={} limit={} mode=hit clone={}ms slice={}ms page_rows={} total_items={} total={}ms",
                    dir_path.display(),
                    cursor,
                    limit,
                    clone_ms,
                    slice_sw.elapsed().as_millis(),
                    page.len(),
                    total,
                    total_sw.elapsed().as_millis()
                ));
                return Ok((page, next_cursor, has_more, total));
            }
        }
    }

    let load_sw = Instant::now();
    let mut loaded = list_directory_index_root_entries_for_path_with_meta(dir_path, meta)?;
    let load_ms = load_sw.elapsed().as_millis();
    let sort_sw = Instant::now();
    sort_ntfs_items(&mut loaded, sort_mode);
    let sort_ms = sort_sw.elapsed().as_millis();
    let total = loaded.len();

    let start = cursor as usize;
    if start >= total {
        let mut table = ntfs_dir_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs dir table (poisoned mutex)",
            )
        })?;
        let now = Instant::now();
        table.insert(
            key,
            CachedNtfsDirectory {
                items: loaded,
                last_accessed_at: now,
            },
        );
        prune_oldest_ntfs_directories(&mut table);
        return Ok((Vec::new(), cursor, false, total));
    }

    let slice_sw = Instant::now();
    let end = (start + limit).min(total);
    let page = loaded[start..end].to_vec();
    let has_more = end < total;
    let next_cursor = if has_more { end as u64 } else { cursor };

    let mut table = ntfs_dir_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs dir table (poisoned mutex)",
        )
    })?;
    let now = Instant::now();
    table.insert(
        key,
        CachedNtfsDirectory {
            items: loaded,
            last_accessed_at: now,
        },
    );
    prune_oldest_ntfs_directories(&mut table);
    append_range_perf_log(&format!(
        "[RUST-RANGE] kind=ntfs path=\"{}\" cursor={} limit={} mode=reload load={}ms sort={}ms slice={}ms page_rows={} total_items={} total={}ms",
        dir_path.display(),
        cursor,
        limit,
        load_ms,
        sort_ms,
        slice_sw.elapsed().as_millis(),
        page.len(),
        total,
        total_sw.elapsed().as_millis()
    ));

    Ok((page, next_cursor, has_more, total))
}
