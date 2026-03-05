use std::path::Path;
use std::collections::HashMap;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use crate::core::error::{FsError, FsErrorCode};
use crate::core::types::VolumeKind;
use crate::engine::classify_path;

#[derive(Debug, Clone)]
pub struct UsnCapability {
    pub is_ntfs_local: bool,
    pub can_open_volume: bool,
    pub access_denied: bool,
    pub available: bool,
    pub message: String,
}

const USN_CAPABILITY_TTL: Duration = Duration::from_secs(10);

#[derive(Debug, Clone)]
struct CachedUsnCapability {
    value: UsnCapability,
    cached_at: Instant,
}

type UsnCapTable = HashMap<String, CachedUsnCapability>;

fn usn_cap_table() -> &'static Mutex<UsnCapTable> {
    static TABLE: OnceLock<Mutex<UsnCapTable>> = OnceLock::new();
    TABLE.get_or_init(|| Mutex::new(HashMap::new()))
}

pub fn probe_usn_capability(path: &Path) -> Result<UsnCapability, FsError> {
    let kind = classify_path(path);
    if kind != VolumeKind::NtfsLocal {
        return Ok(UsnCapability {
            is_ntfs_local: false,
            can_open_volume: false,
            access_denied: false,
            available: false,
            message: "volume is not local NTFS".to_string(),
        });
    }

    #[cfg(windows)]
    {
        let device = volume_device_path(path).ok_or_else(|| {
            FsError::new(
                FsErrorCode::InvalidArgument,
                "path must point to a local drive path (e.g. C:\\...)",
            )
        })?;
        match std::fs::File::open(&device) {
            Ok(_) => Ok(UsnCapability {
                is_ntfs_local: true,
                can_open_volume: true,
                access_denied: false,
                available: true,
                message: "USN entry is available (volume device open succeeded)".to_string(),
            }),
            Err(e) => {
                let denied = e.kind() == std::io::ErrorKind::PermissionDenied;
                Ok(UsnCapability {
                    is_ntfs_local: true,
                    can_open_volume: false,
                    access_denied: denied,
                    available: false,
                    message: format!("volume device open failed: {}", e),
                })
            }
        }
    }

    #[cfg(not(windows))]
    {
        let _ = path;
        Ok(UsnCapability {
            is_ntfs_local: false,
            can_open_volume: false,
            access_denied: false,
            available: false,
            message: "USN probe is only supported on Windows".to_string(),
        })
    }
}

pub fn probe_usn_capability_cached(path: &Path) -> Result<UsnCapability, FsError> {
    let root = volume_root_key(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path does not contain a valid local volume root",
        )
    })?;

    {
        let table = usn_cap_table().lock().map_err(|_| {
            FsError::new(
                FsErrorCode::Internal,
                "failed to lock usn capability table (poisoned mutex)",
            )
        })?;
        if let Some(cached) = table.get(&root) {
            if cached.cached_at.elapsed() <= USN_CAPABILITY_TTL {
                return Ok(cached.value.clone());
            }
        }
    }

    let value = probe_usn_capability(path)?;
    let mut table = usn_cap_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock usn capability table (poisoned mutex)",
        )
    })?;
    table.insert(
        root,
        CachedUsnCapability {
            value: value.clone(),
            cached_at: Instant::now(),
        },
    );
    Ok(value)
}

pub fn clear_usn_capability_cache() -> Result<(), FsError> {
    let mut table = usn_cap_table().lock().map_err(|_| {
        FsError::new(
            FsErrorCode::Internal,
            "failed to lock usn capability table (poisoned mutex)",
        )
    })?;
    table.clear();
    Ok(())
}

#[cfg(windows)]
fn volume_device_path(path: &Path) -> Option<String> {
    let raw = path.to_string_lossy();
    let bytes = raw.as_bytes();
    if bytes.len() >= 2 && bytes[1] == b':' {
        let drive = raw.chars().next()?;
        return Some(format!("\\\\.\\{}:", drive.to_ascii_uppercase()));
    }
    None
}

fn volume_root_key(path: &Path) -> Option<String> {
    #[cfg(windows)]
    {
        let raw = path.to_string_lossy();
        let bytes = raw.as_bytes();
        if bytes.len() >= 2 && bytes[1] == b':' {
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
