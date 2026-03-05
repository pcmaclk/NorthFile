use std::collections::HashMap;
use std::fs;
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
}

const MEMORY_DIR_CACHE_TTL: Duration = Duration::from_secs(3);
const NTFS_META_CACHE_TTL: Duration = Duration::from_secs(15);
const NTFS_DIR_CACHE_TTL: Duration = Duration::from_secs(2);

#[derive(Debug, Clone)]
struct CachedDirectory {
    items: Vec<MemoryDirItem>,
    cached_at: Instant,
    dir_modified: Option<SystemTime>,
}

type MemoryTable = HashMap<String, CachedDirectory>;

fn memory_table() -> &'static Mutex<MemoryTable> {
    static TABLE: OnceLock<Mutex<MemoryTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

#[derive(Debug, Clone, Copy)]
struct CachedNtfsMeta {
    meta: NtfsMeta,
    cached_at: Instant,
}

type NtfsMetaTable = HashMap<String, CachedNtfsMeta>;

fn ntfs_meta_table() -> &'static Mutex<NtfsMetaTable> {
    static TABLE: OnceLock<Mutex<NtfsMetaTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

#[derive(Debug, Clone)]
struct CachedNtfsDirectory {
    items: Vec<NtfsIndexRootEntry>,
    cached_at: Instant,
}

type NtfsDirTable = HashMap<String, CachedNtfsDirectory>;

fn ntfs_dir_table() -> &'static Mutex<NtfsDirTable> {
    static TABLE: OnceLock<Mutex<NtfsDirTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

pub fn invalidate_directory_cache(dir_path: &Path) -> Result<bool, FsError> {
    let key = dir_path.to_string_lossy().to_string();
    let mut table = memory_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock memory path table (poisoned mutex)",
        )
    })?;
    let removed_mem = table.remove(&key).is_some();

    let mut ntfs_table = ntfs_dir_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs dir table (poisoned mutex)",
        )
    })?;
    let removed_ntfs = ntfs_table.remove(&key).is_some();
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
            if cached.cached_at.elapsed() <= NTFS_META_CACHE_TTL {
                return Ok(cached.meta);
            }
        }
    }

    let meta = probe_ntfs_for_path(path)?;

    let mut table = ntfs_meta_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs meta table (poisoned mutex)",
        )
    })?;
    table.insert(
        root_key,
        CachedNtfsMeta {
            meta,
            cached_at: Instant::now(),
        },
    );
    Ok(meta)
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
) -> Result<(Vec<MemoryDirItem>, u64, bool, usize), FsError> {
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }

    let key = dir_path.to_string_lossy().to_string();
    let mut table = memory_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock memory path table (poisoned mutex)",
        )
    })?;

    let current_modified = fs::metadata(dir_path).ok().and_then(|m| m.modified().ok());
    let needs_reload = match table.get(&key) {
        Some(cached) => {
            let expired = cached.cached_at.elapsed() > MEMORY_DIR_CACHE_TTL;
            let changed = cached.dir_modified != current_modified;
            expired || changed
        }
        None => true,
    };

    let items = if needs_reload {
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
            loaded.push(MemoryDirItem {
                name: dir_entry.file_name().to_string_lossy().into_owned(),
                is_dir: file_type.is_dir(),
            });
        }
        loaded.sort_unstable_by(|a, b| a.name.cmp(&b.name));
        table.insert(
            key.clone(),
            CachedDirectory {
                items: loaded.clone(),
                cached_at: Instant::now(),
                dir_modified: current_modified,
            },
        );
        loaded
    } else {
        match table.get(&key) {
            Some(cached) => cached.items.clone(),
            None => Vec::new(),
        }
    };

    let start = cursor as usize;
    if start >= items.len() {
        return Ok((Vec::new(), cursor, false, items.len()));
    }

    let end = (start + limit).min(items.len());
    let page = items[start..end].to_vec();
    let has_more = end < items.len();
    let next_cursor = if has_more { end as u64 } else { cursor };

    Ok((page, next_cursor, has_more, items.len()))
}

pub fn list_directory_entries_ntfs_cached(
    dir_path: &Path,
    meta: NtfsMeta,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    let key = dir_path.to_string_lossy().to_string();

    {
        let table = ntfs_dir_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs dir table (poisoned mutex)",
            )
        })?;
        if let Some(cached) = table.get(&key) {
            if cached.cached_at.elapsed() <= NTFS_DIR_CACHE_TTL {
                return Ok(cached.items.clone());
            }
        }
    }

    let loaded = list_directory_index_root_entries_for_path_with_meta(dir_path, meta)?;
    let mut table = ntfs_dir_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock ntfs dir table (poisoned mutex)",
        )
    })?;
    table.insert(
        key,
        CachedNtfsDirectory {
            items: loaded.clone(),
            cached_at: Instant::now(),
        },
    );
    Ok(loaded)
}

pub fn list_directory_page_ntfs_cached(
    dir_path: &Path,
    meta: NtfsMeta,
    cursor: u64,
    limit: usize,
) -> Result<(Vec<NtfsIndexRootEntry>, u64, bool, usize), FsError> {
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }

    let key = dir_path.to_string_lossy().to_string();

    {
        let table = ntfs_dir_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs dir table (poisoned mutex)",
            )
        })?;
        if let Some(cached) = table.get(&key) {
            if cached.cached_at.elapsed() <= NTFS_DIR_CACHE_TTL {
                let total = cached.items.len();
                let start = cursor as usize;
                if start >= total {
                    return Ok((Vec::new(), cursor, false, total));
                }
                let end = (start + limit).min(total);
                let page = cached.items[start..end].to_vec();
                let has_more = end < total;
                let next_cursor = if has_more { end as u64 } else { cursor };
                return Ok((page, next_cursor, has_more, total));
            }
        }
    }

    let loaded = list_directory_index_root_entries_for_path_with_meta(dir_path, meta)?;
    let total = loaded.len();

    let start = cursor as usize;
    if start >= total {
        let mut table = ntfs_dir_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock ntfs dir table (poisoned mutex)",
            )
        })?;
        table.insert(
            key,
            CachedNtfsDirectory {
                items: loaded,
                cached_at: Instant::now(),
            },
        );
        return Ok((Vec::new(), cursor, false, total));
    }

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
    table.insert(
        key,
        CachedNtfsDirectory {
            items: loaded,
            cached_at: Instant::now(),
        },
    );

    Ok((page, next_cursor, has_more, total))
}
