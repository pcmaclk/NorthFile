use std::fmt::{Display, Formatter};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FsErrorCode {
    InvalidArgument = 1001,
    VolumeAccess = 2001,
    NtfsParse = 3001,
    Cancelled = 4001,
    Internal = 5001,
    Unsupported = 5002,
}

#[derive(Debug, Clone)]
pub struct FsError {
    pub code: FsErrorCode,
    pub message: String,
}

impl FsError {
    pub fn new(code: FsErrorCode, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
        }
    }
}

impl Display for FsError {
    fn fmt(&self, f: &mut Formatter<'_>) -> std::fmt::Result {
        write!(f, "[{:?}] {}", self.code, self.message)
    }
}

impl std::error::Error for FsError {}
