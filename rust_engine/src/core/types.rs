#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum VolumeKind {
    NtfsLocal,
    OtherLocal,
    Network,
}

#[derive(Debug, Clone)]
pub struct VolumeInfo {
    pub id: String,
    pub kind: VolumeKind,
}

#[derive(Debug, Clone)]
pub struct EnumReq {
    pub volume_id: String,
    pub dir_ref: u64,
    pub cursor: Option<u64>,
    pub limit: usize,
}

#[derive(Debug, Clone)]
pub struct SearchReq {
    pub volume_id: String,
    pub query: String,
    pub cursor: Option<u64>,
    pub limit: usize,
}

#[derive(Debug, Clone, Copy)]
#[repr(C)]
pub struct FileEntry {
    pub parent_id: u32,
    pub name_off: u32,
    pub name_len: u16,
    pub flags: u16,
    pub mft_ref: u64,
}

#[derive(Debug, Clone)]
pub struct DirPage {
    pub entries: Vec<FileEntry>,
    pub next_cursor: Option<u64>,
    pub has_more: bool,
}

#[derive(Debug, Clone)]
pub struct SearchPage {
    pub entries: Vec<FileEntry>,
    pub next_cursor: Option<u64>,
    pub has_more: bool,
}

#[derive(Debug, Clone, Copy)]
pub struct FileMeta {
    pub file_ref: u64,
    pub size: u64,
    pub created_ts: i64,
    pub modified_ts: i64,
    pub attrs: u32,
}
