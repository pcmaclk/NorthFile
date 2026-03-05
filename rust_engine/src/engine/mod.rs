use std::path::Path;

use crate::core::error::{FsError, FsErrorCode};
use crate::core::traits::PathEngine;
use crate::core::types::{DirPage, EnumReq, FileMeta, SearchPage, SearchReq, VolumeInfo, VolumeKind};
use crate::memory::MemoryPathEngine;
use crate::traditional::TraditionalPathEngine;

pub struct PathRouter {
    memory: MemoryPathEngine,
    traditional: TraditionalPathEngine,
}

impl PathRouter {
    pub fn new() -> Self {
        Self {
            memory: MemoryPathEngine::new(),
            traditional: TraditionalPathEngine::new(),
        }
    }

    pub fn enumerate_dir(&self, volume: &VolumeInfo, req: EnumReq) -> Result<DirPage, FsError> {
        self.select(volume).enumerate_dir(req)
    }

    pub fn search(&self, volume: &VolumeInfo, req: SearchReq) -> Result<SearchPage, FsError> {
        self.select(volume).search(req)
    }

    pub fn get_meta(&self, volume: &VolumeInfo, file_ref: u64) -> Result<FileMeta, FsError> {
        self.select(volume).get_meta(file_ref)
    }

    fn select(&self, volume: &VolumeInfo) -> &dyn PathEngine {
        match volume.kind {
            VolumeKind::NtfsLocal => &self.memory,
            VolumeKind::OtherLocal | VolumeKind::Network => &self.traditional,
        }
    }
}

impl Default for PathRouter {
    fn default() -> Self {
        Self::new()
    }
}

pub fn validate_limit(limit: usize) -> Result<(), FsError> {
    if limit == 0 || limit > 2000 {
        return Err(FsError::new(
            FsErrorCode::InvalidArgument,
            "limit must be in range 1..=2000",
        ));
    }
    Ok(())
}

pub fn classify_path(path: &Path) -> VolumeKind {
    let raw = path.to_string_lossy();

    // UNC paths are treated as network volumes.
    if raw.starts_with("\\\\") {
        return VolumeKind::Network;
    }

    #[cfg(windows)]
    {
        return classify_windows(raw.as_ref());
    }

    #[cfg(not(windows))]
    {
        VolumeKind::OtherLocal
    }
}

#[cfg(windows)]
fn classify_windows(raw: &str) -> VolumeKind {
    use windows::Win32::Storage::FileSystem::{GetDriveTypeW, GetVolumeInformationW};
    use windows::core::PCWSTR;

    let root = match drive_root(raw) {
        Some(v) => v,
        None => return VolumeKind::OtherLocal,
    };
    let root_w = to_wide_null(&root);

    // SAFETY: root_w is a valid null-terminated UTF-16 string.
    let drive_type = unsafe { GetDriveTypeW(PCWSTR(root_w.as_ptr())) };
    // Win32 DRIVE_* constants.
    const DRIVE_FIXED_VALUE: u32 = 3;
    const DRIVE_REMOTE_VALUE: u32 = 4;
    if drive_type == DRIVE_REMOTE_VALUE {
        return VolumeKind::Network;
    }
    if drive_type != DRIVE_FIXED_VALUE {
        return VolumeKind::OtherLocal;
    }

    let mut fs_name = [0u16; 64];
    // SAFETY: all buffers are valid and sized for this API call.
    let ok = unsafe {
        GetVolumeInformationW(
            PCWSTR(root_w.as_ptr()),
            None,
            None,
            None,
            None,
            Some(&mut fs_name),
        )
    };
    if ok.is_err() {
        return VolumeKind::OtherLocal;
    }

    let len = fs_name.iter().position(|&c| c == 0).unwrap_or(fs_name.len());
    let fs = String::from_utf16_lossy(&fs_name[..len]).to_uppercase();
    if fs == "NTFS" {
        VolumeKind::NtfsLocal
    } else {
        VolumeKind::OtherLocal
    }
}

#[cfg(windows)]
fn drive_root(raw: &str) -> Option<String> {
    let bytes = raw.as_bytes();
    if bytes.len() >= 3 && bytes[1] == b':' && (bytes[2] == b'\\' || bytes[2] == b'/') {
        let drive = raw.chars().next()?;
        return Some(format!("{drive}:\\"));
    }
    None
}

#[cfg(windows)]
fn to_wide_null(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}
