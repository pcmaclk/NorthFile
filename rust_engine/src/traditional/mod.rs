use std::fs;
use std::path::Path;

use crate::core::error::{FsError, FsErrorCode};
use crate::core::traits::PathEngine;
use crate::core::types::{DirPage, EnumReq, FileMeta, SearchPage, SearchReq};
use crate::engine::validate_limit;

#[derive(Debug, Clone)]
pub struct TraditionalDirItem {
    pub name: String,
    pub is_dir: bool,
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

pub fn list_directory_page(
    dir_path: &Path,
    cursor: u64,
    limit: usize,
) -> Result<(Vec<TraditionalDirItem>, u64, bool, usize), FsError> {
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }

    let items = list_directory_all(dir_path)?;

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

pub fn list_directory_all(dir_path: &Path) -> Result<Vec<TraditionalDirItem>, FsError> {
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

        items.push(TraditionalDirItem {
            name: dir_entry.file_name().to_string_lossy().into_owned(),
            is_dir: file_type.is_dir(),
        });
    }

    items.sort_unstable_by(|a, b| a.name.cmp(&b.name));
    Ok(items)
}
