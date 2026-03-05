use crate::core::error::FsError;
use crate::core::types::{DirPage, EnumReq, FileMeta, SearchPage, SearchReq};

pub trait PathEngine: Send + Sync {
    fn enumerate_dir(&self, req: EnumReq) -> Result<DirPage, FsError>;
    fn search(&self, req: SearchReq) -> Result<SearchPage, FsError>;
    fn get_meta(&self, file_ref: u64) -> Result<FileMeta, FsError>;
}
