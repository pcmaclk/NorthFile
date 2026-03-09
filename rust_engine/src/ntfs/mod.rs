use std::path::Path;
use std::{
    collections::HashSet,
    fs::File,
    io::{Read, Seek, SeekFrom},
};

use crate::core::error::{FsError, FsErrorCode};

#[derive(Debug, Clone, Copy)]
pub struct NtfsMeta {
    pub bytes_per_sector: u32,
    pub bytes_per_cluster: u32,
    pub bytes_per_record: u32,
    pub mft_lcn: u64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Run {
    pub lcn: i64,
    pub len_clusters: u64,
}

#[derive(Debug, Default, Clone)]
pub struct MftLayout {
    pub runs: Vec<Run>,
}

#[derive(Debug, Clone, Copy)]
pub struct MftRecordHeader {
    pub magic: [u8; 4],
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
}

#[derive(Debug, Clone, Copy)]
pub struct NtfsRecordInsights {
    pub attr_count: u32,
    pub has_file_name: bool,
    pub has_index_root: bool,
    pub has_index_allocation: bool,
    pub has_bitmap: bool,
}

#[derive(Debug, Clone)]
pub struct NtfsIndexRootEntry {
    pub file_ref: u64,
    pub name: String,
    pub flags: u16,
}

#[derive(Debug)]
struct ParsedIndexRoot {
    entries: Vec<NtfsIndexRootEntry>,
    has_children: bool,
    index_block_size: u32,
}

pub fn probe_ntfs_for_path(path: &Path) -> Result<NtfsMeta, FsError> {
    #[cfg(windows)]
    {
        return probe_ntfs_windows(path);
    }
    #[cfg(not(windows))]
    {
        let _ = path;
        Err(FsError::new(
            FsErrorCode::Unsupported,
            "NTFS probe is only supported on Windows",
        ))
    }
}

pub fn read_mft_record_header(path: &Path, record_index: u64) -> Result<MftRecordHeader, FsError> {
    let meta = probe_ntfs_for_path(path)?;
    read_mft_record_header_with_meta(path, meta, record_index)
}

pub fn read_mft_record_header_with_meta(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<MftRecordHeader, FsError> {
    #[cfg(windows)]
    {
        return read_mft_record_header_windows(path, meta, record_index);
    }
    #[cfg(not(windows))]
    {
        let _ = (path, meta, record_index);
        Err(FsError::new(
            FsErrorCode::Unsupported,
            "MFT record read is only supported on Windows",
        ))
    }
}

pub fn inspect_mft_record_with_meta(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<NtfsRecordInsights, FsError> {
    #[cfg(windows)]
    {
        return inspect_mft_record_windows(path, meta, record_index);
    }
    #[cfg(not(windows))]
    {
        let _ = (path, meta, record_index);
        Err(FsError::new(
            FsErrorCode::Unsupported,
            "MFT record inspect is only supported on Windows",
        ))
    }
}

pub fn list_index_root_entries_with_meta(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    #[cfg(windows)]
    {
        return list_index_root_entries_windows(path, meta, record_index);
    }
    #[cfg(not(windows))]
    {
        let _ = (path, meta, record_index);
        Err(FsError::new(
            FsErrorCode::Unsupported,
            "INDEX_ROOT parse is only supported on Windows",
        ))
    }
}

pub fn list_directory_index_root_entries_for_path_with_meta(
    path: &Path,
    meta: NtfsMeta,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    #[cfg(windows)]
    {
        return list_directory_index_root_entries_for_path_windows(path, meta);
    }
    #[cfg(not(windows))]
    {
        let _ = (path, meta);
        Err(FsError::new(
            FsErrorCode::Unsupported,
            "INDEX_ROOT directory path resolve is only supported on Windows",
        ))
    }
}

#[cfg(windows)]
fn probe_ntfs_windows(path: &Path) -> Result<NtfsMeta, FsError> {
    use windows::Win32::Storage::FileSystem::GetVolumeInformationW;
    use windows::core::PCWSTR;

    let root = drive_root(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path must point to a local drive root (e.g. C:\\...)",
        )
    })?;
    let root_w = to_wide_null(&root);

    let mut fs_name = [0u16; 64];
    // SAFETY: all buffers are valid and correctly sized for this API call.
    let volume_ok = unsafe {
        GetVolumeInformationW(
            PCWSTR(root_w.as_ptr()),
            None,
            None,
            None,
            None,
            Some(&mut fs_name),
        )
    };
    if volume_ok.is_err() {
        return Err(FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to query volume info for '{}'", root),
        ));
    }

    let fs_len = fs_name.iter().position(|&c| c == 0).unwrap_or(fs_name.len());
    let fs = String::from_utf16_lossy(&fs_name[..fs_len]).to_uppercase();
    if fs != "NTFS" {
        return Err(FsError::new(
            FsErrorCode::Unsupported,
            format!("filesystem '{}' is not NTFS", fs),
        ));
    }

    let device = format!("\\\\.\\{}", root.trim_end_matches('\\'));
    let boot = read_ntfs_boot_sector(&device)?;

    Ok(NtfsMeta {
        bytes_per_sector: boot.bytes_per_sector,
        bytes_per_cluster: boot.bytes_per_cluster,
        bytes_per_record: boot.bytes_per_record,
        mft_lcn: boot.mft_lcn,
    })
}

#[cfg(windows)]
fn read_mft_record_header_windows(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<MftRecordHeader, FsError> {
    let root = drive_root(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path must point to a local drive root (e.g. C:\\...)",
        )
    })?;
    let device = format!("\\\\.\\{}", root.trim_end_matches('\\'));

    let buf = read_mft_record_bytes_windows(&device, meta, record_index)?;
    let header = parse_mft_record_header(&buf)?;
    Ok(header)
}

#[cfg(windows)]
fn inspect_mft_record_windows(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<NtfsRecordInsights, FsError> {
    let root = drive_root(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path must point to a local drive root (e.g. C:\\...)",
        )
    })?;
    let device = format!("\\\\.\\{}", root.trim_end_matches('\\'));
    let buf = read_mft_record_bytes_windows(&device, meta, record_index)?;
    let header = parse_mft_record_header(&buf)?;
    let _ = header;
    parse_record_insights(&buf)
}

#[cfg(windows)]
fn list_index_root_entries_windows(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    let root = drive_root(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path must point to a local drive root (e.g. C:\\...)",
        )
    })?;
    let device = format!("\\\\.\\{}", root.trim_end_matches('\\'));
    let buf = read_mft_record_bytes_windows(&device, meta, record_index)?;
    let _ = parse_mft_record_header(&buf)?;
    parse_index_root_entries(&buf)
}

#[cfg(windows)]
fn list_directory_index_root_entries_for_path_windows(
    path: &Path,
    meta: NtfsMeta,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    let components = path_components_under_root(path)?;
    let mut current_record: u64 = 5; // NTFS root directory

    for part in components {
        let children = list_directory_entries_by_record_windows(path, meta, current_record)?;
        let matched = children
            .iter()
            .find(|e| e.name.eq_ignore_ascii_case(&part))
            .ok_or_else(|| {
                FsError::new(
                    FsErrorCode::NtfsParse,
                    format!("path component '{}' not found in INDEX_ROOT", part),
                )
            })?;

        if (matched.flags & 0x0001) == 0 {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                format!("path component '{}' is not a directory", part),
            ));
        }

        // FILE_REFERENCE is 6-byte entry number + 2-byte sequence.
        current_record = matched.file_ref & 0x0000_FFFF_FFFF_FFFF;
    }

    list_directory_entries_by_record_windows(path, meta, current_record)
}

#[cfg(windows)]
fn list_directory_entries_by_record_windows(
    path: &Path,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    let root = drive_root(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path must point to a local drive root (e.g. C:\\...)",
        )
    })?;
    let device = format!("\\\\.\\{}", root.trim_end_matches('\\'));

    let record_buf = read_mft_record_bytes_windows(&device, meta, record_index)?;
    let _ = parse_mft_record_header(&record_buf)?;
    let parsed_root = parse_index_root(&record_buf)?;
    if !parsed_root.has_children {
        return Ok(parsed_root.entries);
    }

    let (runs, data_size) = parse_index_allocation_runs(&record_buf)?;
    if runs.is_empty() || data_size == 0 {
        return Ok(parsed_root.entries);
    }

    let mut all = parsed_root.entries;
    let extra = read_index_allocation_entries_windows(
        &device,
        meta.bytes_per_cluster,
        &runs,
        data_size,
        parsed_root.index_block_size as usize,
        meta.bytes_per_sector,
    )?;
    let mut seen = HashSet::<u64>::new();
    for e in &all {
        seen.insert(e.file_ref);
    }
    for e in extra {
        if seen.insert(e.file_ref) {
            all.push(e);
        }
    }
    Ok(all)
}

#[cfg(windows)]
fn read_mft_record_bytes_windows(
    device: &str,
    meta: NtfsMeta,
    record_index: u64,
) -> Result<Vec<u8>, FsError> {
    let mut f = File::open(device).map_err(|e| {
        FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to open volume device '{}': {}", device, e),
        )
    })?;

    let mft_base = (meta.mft_lcn as u128) * (meta.bytes_per_cluster as u128);
    let record_off = (record_index as u128) * (meta.bytes_per_record as u128);
    let abs_off = mft_base
        .checked_add(record_off)
        .ok_or_else(|| FsError::new(FsErrorCode::NtfsParse, "MFT offset overflow"))?;
    let abs_off_u64 = u64::try_from(abs_off)
        .map_err(|_| FsError::new(FsErrorCode::NtfsParse, "MFT offset out of range"))?;

    f.seek(SeekFrom::Start(abs_off_u64)).map_err(|e| {
        FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to seek to MFT record {}: {}", record_index, e),
        )
    })?;

    let rec_size = meta.bytes_per_record as usize;
    if rec_size < 48 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "invalid record size (< 48 bytes)",
        ));
    }
    let mut buf = vec![0u8; rec_size];
    f.read_exact(&mut buf).map_err(|e| {
        FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to read MFT record {}: {}", record_index, e),
        )
    })?;

    apply_usa_fixup(&mut buf, meta.bytes_per_sector)?;

    Ok(buf)
}

fn parse_mft_record_header(buf: &[u8]) -> Result<MftRecordHeader, FsError> {
    if buf.len() < 48 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "record buffer too small",
        ));
    }

    let magic = [buf[0], buf[1], buf[2], buf[3]];
    if &magic != b"FILE" {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            format!(
                "invalid MFT record signature: {:02X}{:02X}{:02X}{:02X}",
                magic[0], magic[1], magic[2], magic[3]
            ),
        ));
    }

    Ok(MftRecordHeader {
        magic,
        usa_offset: u16::from_le_bytes([buf[0x04], buf[0x05]]),
        usa_count: u16::from_le_bytes([buf[0x06], buf[0x07]]),
        sequence_number: u16::from_le_bytes([buf[0x10], buf[0x11]]),
        hard_link_count: u16::from_le_bytes([buf[0x12], buf[0x13]]),
        attr_offset: u16::from_le_bytes([buf[0x14], buf[0x15]]),
        flags: u16::from_le_bytes([buf[0x16], buf[0x17]]),
        bytes_in_use: u32::from_le_bytes([buf[0x18], buf[0x19], buf[0x1A], buf[0x1B]]),
        bytes_allocated: u32::from_le_bytes([buf[0x1C], buf[0x1D], buf[0x1E], buf[0x1F]]),
        base_record_ref: u64::from_le_bytes([
            buf[0x20], buf[0x21], buf[0x22], buf[0x23], buf[0x24], buf[0x25], buf[0x26], buf[0x27],
        ]),
        record_number: u32::from_le_bytes([buf[0x2C], buf[0x2D], buf[0x2E], buf[0x2F]]),
    })
}

fn parse_record_insights(buf: &[u8]) -> Result<NtfsRecordInsights, FsError> {
    let attr_offset = u16::from_le_bytes([buf[0x14], buf[0x15]]) as usize;
    if attr_offset >= buf.len() {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "attribute offset is out of record range",
        ));
    }

    let mut p = attr_offset;
    let mut attr_count = 0u32;
    let mut has_file_name = false;
    let mut has_index_root = false;
    let mut has_index_allocation = false;
    let mut has_bitmap = false;

    // Minimal attribute walker: header-only scanning.
    while p + 8 <= buf.len() {
        let attr_type = u32::from_le_bytes([buf[p], buf[p + 1], buf[p + 2], buf[p + 3]]);
        if attr_type == 0xFFFF_FFFF {
            break;
        }
        let attr_len = u32::from_le_bytes([buf[p + 4], buf[p + 5], buf[p + 6], buf[p + 7]]) as usize;
        if attr_len == 0 || p + attr_len > buf.len() {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                format!("invalid attribute length at offset {}", p),
            ));
        }

        attr_count = attr_count.saturating_add(1);
        match attr_type {
            0x30 => has_file_name = true,
            0x90 => has_index_root = true,
            0xA0 => has_index_allocation = true,
            0xB0 => has_bitmap = true,
            _ => {}
        }

        p += attr_len;
    }

    Ok(NtfsRecordInsights {
        attr_count,
        has_file_name,
        has_index_root,
        has_index_allocation,
        has_bitmap,
    })
}

fn parse_index_root_entries(buf: &[u8]) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    Ok(parse_index_root(buf)?.entries)
}

fn parse_index_root(buf: &[u8]) -> Result<ParsedIndexRoot, FsError> {
    let attr_offset = u16::from_le_bytes([buf[0x14], buf[0x15]]) as usize;
    if attr_offset >= buf.len() {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "attribute offset is out of record range",
        ));
    }

    let mut p = attr_offset;
    while p + 24 <= buf.len() {
        let attr_type = u32::from_le_bytes([buf[p], buf[p + 1], buf[p + 2], buf[p + 3]]);
        if attr_type == 0xFFFF_FFFF {
            break;
        }
        let attr_len = u32::from_le_bytes([buf[p + 4], buf[p + 5], buf[p + 6], buf[p + 7]]) as usize;
        if attr_len == 0 || p + attr_len > buf.len() {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                format!("invalid attribute length at offset {}", p),
            ));
        }

        if attr_type == 0x90 {
            let non_resident = buf[p + 8];
            if non_resident != 0 {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "INDEX_ROOT attribute is unexpectedly non-resident",
                ));
            }
            let value_len = u32::from_le_bytes([buf[p + 16], buf[p + 17], buf[p + 18], buf[p + 19]]) as usize;
            let value_off = u16::from_le_bytes([buf[p + 20], buf[p + 21]]) as usize;
            let value_start = p + value_off;
            let value_end = value_start + value_len;
            if value_end > p + attr_len || value_end > buf.len() || value_len < 32 {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "invalid INDEX_ROOT value range",
                ));
            }

            // INDEX_ROOT value:
            // [0..16] INDEX_ROOT header, [16..] INDEX_HEADER then entries.
            let ih = value_start + 16;
            if ih + 16 > value_end {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "INDEX_ROOT missing INDEX_HEADER",
                ));
            }
            let index_block_size =
                u32::from_le_bytes([buf[value_start + 8], buf[value_start + 9], buf[value_start + 10], buf[value_start + 11]]);
            if index_block_size == 0 {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "invalid INDEX_ROOT index block size",
                ));
            }
            let entries_off = u32::from_le_bytes([buf[ih], buf[ih + 1], buf[ih + 2], buf[ih + 3]]) as usize;
            let entries_total =
                u32::from_le_bytes([buf[ih + 4], buf[ih + 5], buf[ih + 6], buf[ih + 7]]) as usize;
            let index_header_flags = buf[ih + 12];
            let entries_start = ih + entries_off;
            let entries_end = entries_start + entries_total;
            if entries_start > value_end || entries_end > value_end {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "INDEX_ROOT entries range out of bounds",
                ));
            }

            let mut out = Vec::new();
            let mut e = entries_start;
            while e + 16 <= entries_end {
                let entry_len = u16::from_le_bytes([buf[e + 8], buf[e + 9]]) as usize;
                let flags = u16::from_le_bytes([buf[e + 12], buf[e + 13]]);
                if entry_len == 0 || e + entry_len > entries_end {
                    break;
                }

                // Last entry marker, no key payload.
                if (flags & 0x0002) != 0 {
                    break;
                }
                if let Some(parsed) = parse_index_entry(buf, e, entry_len) {
                    out.push(parsed);
                }

                e += entry_len;
            }

            return Ok(ParsedIndexRoot {
                entries: out,
                has_children: (index_header_flags & 0x01) != 0,
                index_block_size,
            });
        }

        p += attr_len;
    }

    Ok(ParsedIndexRoot {
        entries: Vec::new(),
        has_children: false,
        index_block_size: 0,
    })
}

fn parse_index_entry(
    buf: &[u8],
    entry_start: usize,
    entry_len: usize,
) -> Option<NtfsIndexRootEntry> {
    let file_ref = u64::from_le_bytes([
        buf[entry_start],
        buf[entry_start + 1],
        buf[entry_start + 2],
        buf[entry_start + 3],
        buf[entry_start + 4],
        buf[entry_start + 5],
        buf[entry_start + 6],
        buf[entry_start + 7],
    ]);
    let key_len = u16::from_le_bytes([buf[entry_start + 10], buf[entry_start + 11]]) as usize;
    let key_start = entry_start + 16;
    if key_len < 66 || key_start + key_len > entry_start + entry_len {
        return None;
    }

    let name_len = buf[key_start + 64] as usize;
    let name_start = key_start + 66;
    let name_bytes = name_len.saturating_mul(2);
    if name_start + name_bytes > key_start + key_len {
        return None;
    }

    let mut utf16 = Vec::with_capacity(name_len);
    let mut i = 0usize;
    while i < name_bytes {
        let lo = buf[name_start + i];
        let hi = buf[name_start + i + 1];
        utf16.push(u16::from_le_bytes([lo, hi]));
        i += 2;
    }

    // FILE_NAME attribute: file attributes at offset 0x38 (56) from key start.
    let file_attrs = if key_start + 60 <= key_start + key_len {
        u32::from_le_bytes([
            buf[key_start + 56],
            buf[key_start + 57],
            buf[key_start + 58],
            buf[key_start + 59],
        ])
    } else {
        0
    };
    let mut flags = 0u16;
    if (file_attrs & 0x0000_0010) != 0 {
        flags |= 0x0001;
    }
    if (file_attrs & 0x0000_0400) != 0 {
        flags |= 0x0002;
    }

    Some(NtfsIndexRootEntry {
        file_ref,
        name: String::from_utf16_lossy(&utf16),
        flags,
    })
}

fn parse_index_allocation_runs(buf: &[u8]) -> Result<(Vec<Run>, u64), FsError> {
    let attr_offset = u16::from_le_bytes([buf[0x14], buf[0x15]]) as usize;
    if attr_offset >= buf.len() {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "attribute offset is out of record range",
        ));
    }

    let mut p = attr_offset;
    while p + 24 <= buf.len() {
        let attr_type = u32::from_le_bytes([buf[p], buf[p + 1], buf[p + 2], buf[p + 3]]);
        if attr_type == 0xFFFF_FFFF {
            break;
        }
        let attr_len = u32::from_le_bytes([buf[p + 4], buf[p + 5], buf[p + 6], buf[p + 7]]) as usize;
        if attr_len == 0 || p + attr_len > buf.len() {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                format!("invalid attribute length at offset {}", p),
            ));
        }

        if attr_type == 0xA0 {
            if buf[p + 8] == 0 {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "INDEX_ALLOCATION should be non-resident",
                ));
            }
            if p + 74 > p + attr_len {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "INDEX_ALLOCATION header too short",
                ));
            }
            let run_off = u16::from_le_bytes([buf[p + 32], buf[p + 33]]) as usize;
            let data_size = u64::from_le_bytes([
                buf[p + 48],
                buf[p + 49],
                buf[p + 50],
                buf[p + 51],
                buf[p + 52],
                buf[p + 53],
                buf[p + 54],
                buf[p + 55],
            ]);
            let run_start = p + run_off;
            if run_start >= p + attr_len {
                return Err(FsError::new(
                    FsErrorCode::NtfsParse,
                    "invalid INDEX_ALLOCATION run list offset",
                ));
            }
            let runs = parse_run_list(&buf[run_start..p + attr_len])?;
            return Ok((runs, data_size));
        }

        p += attr_len;
    }

    Ok((Vec::new(), 0))
}

fn parse_run_list(runlist: &[u8]) -> Result<Vec<Run>, FsError> {
    let mut out = Vec::new();
    let mut p = 0usize;
    let mut cur_lcn = 0i64;
    while p < runlist.len() {
        let hdr = runlist[p];
        p += 1;
        if hdr == 0 {
            break;
        }
        let len_sz = (hdr & 0x0F) as usize;
        let off_sz = (hdr >> 4) as usize;
        if len_sz == 0 || off_sz == 0 || p + len_sz + off_sz > runlist.len() {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                "invalid data run header",
            ));
        }

        let mut run_len = 0u64;
        for i in 0..len_sz {
            run_len |= (runlist[p + i] as u64) << (i * 8);
        }
        p += len_sz;

        let mut off_raw = 0i64;
        for i in 0..off_sz {
            off_raw |= (runlist[p + i] as i64) << (i * 8);
        }
        // Sign-extend the variable-width offset.
        if (runlist[p + off_sz - 1] & 0x80) != 0 {
            off_raw |= -1i64 << (off_sz * 8);
        }
        p += off_sz;

        cur_lcn = cur_lcn.saturating_add(off_raw);
        out.push(Run {
            lcn: cur_lcn,
            len_clusters: run_len,
        });
    }

    Ok(out)
}

#[cfg(windows)]
fn read_index_allocation_entries_windows(
    device: &str,
    bytes_per_cluster: u32,
    runs: &[Run],
    data_size: u64,
    index_block_size: usize,
    bytes_per_sector: u32,
) -> Result<Vec<NtfsIndexRootEntry>, FsError> {
    if index_block_size < 0x30 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "invalid index block size",
        ));
    }

    let mut f = File::open(device).map_err(|e| {
        FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to open volume device '{}': {}", device, e),
        )
    })?;
    let segments = build_stream_segments(runs, bytes_per_cluster)?;

    let mut out = Vec::new();
    let block_size_u64 = index_block_size as u64;
    let mut logical_off = 0u64;
    while logical_off + block_size_u64 <= data_size {
        let mut block = vec![0u8; index_block_size];
        if read_stream_slice_windows(&mut f, &segments, logical_off, &mut block).is_err() {
            logical_off += block_size_u64;
            continue;
        }

        if &block[0..4] != b"INDX" {
            logical_off += block_size_u64;
            continue;
        }
        if apply_usa_fixup(&mut block, bytes_per_sector).is_err() {
            logical_off += block_size_u64;
            continue;
        }

        let ih = 0x18usize;
        let entries_off = u32::from_le_bytes([block[ih], block[ih + 1], block[ih + 2], block[ih + 3]]) as usize;
        let entries_total =
            u32::from_le_bytes([block[ih + 4], block[ih + 5], block[ih + 6], block[ih + 7]]) as usize;
        let entries_start = ih + entries_off;
        let entries_end = entries_start.saturating_add(entries_total).min(block.len());
        if entries_start >= entries_end || entries_end > block.len() {
            continue;
        }

        let mut e = entries_start;
        while e + 16 <= entries_end {
            let entry_len = u16::from_le_bytes([block[e + 8], block[e + 9]]) as usize;
            let flags = u16::from_le_bytes([block[e + 12], block[e + 13]]);
            if entry_len == 0 || e + entry_len > entries_end {
                break;
            }
            if (flags & 0x0002) != 0 {
                break;
            }
            if let Some(parsed) = parse_index_entry(&block, e, entry_len) {
                out.push(parsed);
            }
            e += entry_len;
        }

        logical_off += block_size_u64;
    }
    Ok(out)
}

#[cfg(windows)]
#[derive(Debug, Clone, Copy)]
struct StreamSegment {
    logical_start: u64,
    logical_end: u64,
    disk_start: u64,
}

#[cfg(windows)]
fn build_stream_segments(runs: &[Run], bytes_per_cluster: u32) -> Result<Vec<StreamSegment>, FsError> {
    let mut out = Vec::new();
    let mut logical = 0u64;
    for r in runs {
        if r.lcn < 0 {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                "sparse/compressed run is not supported in MVP",
            ));
        }
        let run_bytes_u128 = (r.len_clusters as u128) * (bytes_per_cluster as u128);
        let run_bytes =
            u64::try_from(run_bytes_u128).map_err(|_| FsError::new(FsErrorCode::NtfsParse, "run byte length overflow"))?;
        if run_bytes == 0 {
            continue;
        }
        let disk_start_u128 = (r.lcn as u128) * (bytes_per_cluster as u128);
        let disk_start =
            u64::try_from(disk_start_u128).map_err(|_| FsError::new(FsErrorCode::NtfsParse, "run offset out of range"))?;
        let logical_end = logical
            .checked_add(run_bytes)
            .ok_or_else(|| FsError::new(FsErrorCode::NtfsParse, "stream logical range overflow"))?;
        out.push(StreamSegment {
            logical_start: logical,
            logical_end,
            disk_start,
        });
        logical = logical_end;
    }
    Ok(out)
}

#[cfg(windows)]
fn read_stream_slice_windows(
    f: &mut File,
    segments: &[StreamSegment],
    logical_off: u64,
    out: &mut [u8],
) -> Result<(), FsError> {
    let mut remain = out.len();
    let mut dst_off = 0usize;
    let mut cur_logical = logical_off;

    while remain > 0 {
        let seg = segments
            .iter()
            .find(|s| cur_logical >= s.logical_start && cur_logical < s.logical_end)
            .ok_or_else(|| FsError::new(FsErrorCode::NtfsParse, "stream logical offset out of run range"))?;
        let seg_avail = usize::try_from(seg.logical_end - cur_logical)
            .map_err(|_| FsError::new(FsErrorCode::NtfsParse, "segment size overflow"))?;
        let n = seg_avail.min(remain);
        let disk_off = seg
            .disk_start
            .checked_add(cur_logical - seg.logical_start)
            .ok_or_else(|| FsError::new(FsErrorCode::NtfsParse, "disk offset overflow"))?;

        f.seek(SeekFrom::Start(disk_off)).map_err(|e| {
            FsError::new(
                FsErrorCode::VolumeAccess,
                format!("failed to seek volume stream at {}: {}", disk_off, e),
            )
        })?;
        f.read_exact(&mut out[dst_off..dst_off + n]).map_err(|e| {
            FsError::new(
                FsErrorCode::VolumeAccess,
                format!("failed to read volume stream at {}: {}", disk_off, e),
            )
        })?;

        dst_off += n;
        remain -= n;
        cur_logical = cur_logical.saturating_add(n as u64);
    }

    Ok(())
}

#[cfg(windows)]
fn path_components_under_root(path: &Path) -> Result<Vec<String>, FsError> {
    let raw = path.to_string_lossy();
    let root = drive_root(path).ok_or_else(|| {
        FsError::new(
            FsErrorCode::InvalidArgument,
            "path must point to a local drive root (e.g. C:\\...)",
        )
    })?;

    let mut remain = raw[root.len()..].to_string();
    while remain.ends_with('\\') || remain.ends_with('/') {
        remain.pop();
    }
    if remain.is_empty() {
        return Ok(Vec::new());
    }

    Ok(remain
        .split(['\\', '/'])
        .filter(|s| !s.is_empty())
        .map(|s| s.to_string())
        .collect())
}

#[derive(Debug, Clone, Copy)]
struct NtfsBootInfo {
    bytes_per_sector: u32,
    bytes_per_cluster: u32,
    bytes_per_record: u32,
    mft_lcn: u64,
}

#[cfg(windows)]
fn read_ntfs_boot_sector(device_path: &str) -> Result<NtfsBootInfo, FsError> {
    let mut f = File::open(device_path).map_err(|e| {
        FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to open volume device '{}': {}", device_path, e),
        )
    })?;

    let mut buf = [0u8; 512];
    f.read_exact(&mut buf).map_err(|e| {
        FsError::new(
            FsErrorCode::VolumeAccess,
            format!("failed to read NTFS boot sector '{}': {}", device_path, e),
        )
    })?;

    let bytes_per_sector = u16::from_le_bytes([buf[0x0B], buf[0x0C]]) as u32;
    let sectors_per_cluster = buf[0x0D] as u32;
    if bytes_per_sector == 0 || sectors_per_cluster == 0 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "invalid NTFS BPB: bytes_per_sector or sectors_per_cluster is zero",
        ));
    }
    let bytes_per_cluster = bytes_per_sector.saturating_mul(sectors_per_cluster);

    let mft_lcn = u64::from_le_bytes([
        buf[0x30], buf[0x31], buf[0x32], buf[0x33], buf[0x34], buf[0x35], buf[0x36], buf[0x37],
    ]);

    let clusters_per_record_raw = buf[0x40] as i8;
    let bytes_per_record = if clusters_per_record_raw > 0 {
        bytes_per_cluster.saturating_mul(clusters_per_record_raw as u32)
    } else {
        // NTFS encodes negative value as 2^abs(n) bytes (e.g. -10 => 1024).
        let shift = (-clusters_per_record_raw) as u32;
        1u32.checked_shl(shift).unwrap_or(0)
    };
    if bytes_per_record == 0 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "invalid NTFS BPB: computed bytes_per_record is zero",
        ));
    }

    Ok(NtfsBootInfo {
        bytes_per_sector,
        bytes_per_cluster,
        bytes_per_record,
        mft_lcn,
    })
}

fn apply_usa_fixup(record: &mut [u8], bytes_per_sector: u32) -> Result<(), FsError> {
    if bytes_per_sector < 2 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "invalid bytes_per_sector for USA fixup",
        ));
    }
    let sector_size = bytes_per_sector as usize;
    if sector_size == 0 || record.len() < sector_size {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "record too small for USA fixup",
        ));
    }

    let usa_off = u16::from_le_bytes([record[0x04], record[0x05]]) as usize;
    let usa_count = u16::from_le_bytes([record[0x06], record[0x07]]) as usize;
    if usa_count < 2 {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "invalid USA count (< 2)",
        ));
    }
    let usa_bytes = usa_count
        .checked_mul(2)
        .ok_or_else(|| FsError::new(FsErrorCode::NtfsParse, "USA size overflow"))?;
    if usa_off + usa_bytes > record.len() {
        return Err(FsError::new(
            FsErrorCode::NtfsParse,
            "USA array exceeds record boundary",
        ));
    }

    let seq = [record[usa_off], record[usa_off + 1]];
    let sectors = usa_count - 1;
    for i in 0..sectors {
        let end = (i + 1)
            .checked_mul(sector_size)
            .ok_or_else(|| FsError::new(FsErrorCode::NtfsParse, "sector index overflow"))?;
        if end < 2 || end > record.len() {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                "sector end out of record boundary during USA fixup",
            ));
        }
        let fixup_pos = end - 2;
        if record[fixup_pos] != seq[0] || record[fixup_pos + 1] != seq[1] {
            return Err(FsError::new(
                FsErrorCode::NtfsParse,
                "USA sequence mismatch",
            ));
        }

        let src = usa_off + 2 + i * 2;
        record[fixup_pos] = record[src];
        record[fixup_pos + 1] = record[src + 1];
    }

    Ok(())
}

#[cfg(windows)]
fn drive_root(path: &Path) -> Option<String> {
    let raw = path.to_string_lossy();
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

#[cfg(test)]
mod tests {
    use super::{parse_run_list, Run};

    #[test]
    fn parse_run_list_handles_positive_and_negative_delta() {
        // Run #1: len=3, off=+0x20 => LCN 0x20
        // Run #2: len=2, off=-1 => LCN 0x1F
        let runlist = [0x11u8, 0x03, 0x20, 0x11, 0x02, 0xFF, 0x00];
        let runs = parse_run_list(&runlist).expect("parse runlist");
        assert_eq!(
            runs,
            vec![
                Run {
                    lcn: 0x20,
                    len_clusters: 3
                },
                Run {
                    lcn: 0x1F,
                    len_clusters: 2
                }
            ]
        );
    }

    #[test]
    fn parse_run_list_rejects_invalid_header() {
        let bad = [0x10u8, 0x01, 0x00];
        let err = parse_run_list(&bad).expect_err("must fail");
        assert!(err.message.contains("invalid data run header"));
    }
}
