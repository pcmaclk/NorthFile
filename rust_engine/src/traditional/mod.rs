use std::cmp::Ordering;
use std::fs;
use std::path::Path;
use std::time::SystemTime;
#[cfg(windows)]
use std::os::windows::fs::MetadataExt;

use crate::core::error::{FsError, FsErrorCode};
use crate::core::traits::PathEngine;
use crate::core::types::{DirPage, EnumReq, FileMeta, SearchPage, SearchReq};
use crate::engine::validate_limit;

#[derive(Debug, Clone)]
pub struct TraditionalDirItem {
    pub name: String,
    pub is_dir: bool,
    pub is_link: bool,
    pub size_bytes: Option<u64>,
    pub modified_unix_ms: Option<i64>,
}

pub const SORT_DIRS_FIRST_NAME_ASC: u8 = 1;

pub fn normalize_sort_mode(sort_mode: u8) -> u8 {
    match sort_mode {
        SORT_DIRS_FIRST_NAME_ASC => SORT_DIRS_FIRST_NAME_ASC,
        _ => SORT_DIRS_FIRST_NAME_ASC,
    }
}

pub fn compare_directory_like(
    a_is_dir: bool,
    a_name: &str,
    b_is_dir: bool,
    b_name: &str,
) -> Ordering {
    b_is_dir
        .cmp(&a_is_dir)
        .then_with(|| a_name.to_lowercase().cmp(&b_name.to_lowercase()))
        .then_with(|| a_name.cmp(b_name))
}

fn sort_dir_items(items: &mut [TraditionalDirItem], sort_mode: u8) {
    match normalize_sort_mode(sort_mode) {
        SORT_DIRS_FIRST_NAME_ASC => items.sort_unstable_by(|a, b| {
            compare_directory_like(a.is_dir, &a.name, b.is_dir, &b.name)
        }),
        _ => unreachable!(),
    }
}

pub fn resolve_entry_is_dir(dir_entry: &fs::DirEntry, file_type: &fs::FileType) -> bool {
    if file_type.is_dir() {
        return true;
    }

    #[cfg(windows)]
    {
        const FILE_ATTRIBUTE_DIRECTORY: u32 = 0x0010;
        return fs::symlink_metadata(dir_entry.path())
            .map(|meta| (meta.file_attributes() & FILE_ATTRIBUTE_DIRECTORY) != 0)
            .unwrap_or(false);
    }

    #[cfg(not(windows))]
    {
        dir_entry.metadata().map(|meta| meta.is_dir()).unwrap_or(false)
    }
}

pub fn resolve_entry_is_link(dir_entry: &fs::DirEntry) -> bool {
    #[cfg(windows)]
    {
        const FILE_ATTRIBUTE_REPARSE_POINT: u32 = 0x0400;
        return fs::symlink_metadata(dir_entry.path())
            .map(|meta| (meta.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            .unwrap_or(false);
    }

    #[cfg(not(windows))]
    {
        let _ = dir_entry;
        false
    }
}

pub struct TraditionalPathEngine;

impl TraditionalPathEngine {
    pub fn new() -> Self {
        Self
    }
}

impl Default for TraditionalPathEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl PathEngine for TraditionalPathEngine {
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
            "TraditionalPathEngine::get_meta is not implemented yet",
        ))
    }
}

pub const ENTRY_FLAG_DIRECTORY: u16 = 0x0001;
pub const ENTRY_FLAG_LINK: u16 = 0x0002;

fn system_time_to_unix_ms(value: SystemTime) -> Option<i64> {
    value
        .duration_since(SystemTime::UNIX_EPOCH)
        .ok()
        .and_then(|duration| i64::try_from(duration.as_millis()).ok())
}

pub fn list_directory_page(
    dir_path: &Path,
    cursor: u64,
    limit: usize,
    sort_mode: u8,
) -> Result<(Vec<TraditionalDirItem>, u64, bool, usize), FsError> {
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }

    let items = list_directory_all(dir_path, sort_mode)?;

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

pub fn list_directory_all(dir_path: &Path, sort_mode: u8) -> Result<Vec<TraditionalDirItem>, FsError> {
    let mut items: Vec<TraditionalDirItem> = Vec::new();
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
        let metadata = dir_entry.metadata().ok();

        items.push(TraditionalDirItem {
            name: dir_entry.file_name().to_string_lossy().into_owned(),
            is_dir,
            is_link: resolve_entry_is_link(&dir_entry),
            size_bytes: metadata.as_ref().filter(|_| !is_dir).map(|value| value.len()),
            modified_unix_ms: metadata
                .as_ref()
                .and_then(|value| value.modified().ok())
                .and_then(system_time_to_unix_ms),
        });
    }

    sort_dir_items(&mut items, sort_mode);
    Ok(items)
}
