use std::collections::HashMap;

use crate::core::types::{DirPage, FileMeta};

#[derive(Default)]
pub struct DirectoryCache {
    pages: HashMap<u64, DirPage>,
}

impl DirectoryCache {
    pub fn get(&self, key: u64) -> Option<&DirPage> {
        self.pages.get(&key)
    }

    pub fn insert(&mut self, key: u64, value: DirPage) {
        self.pages.insert(key, value);
    }
}

#[derive(Default)]
pub struct FileRecordCache {
    records: HashMap<u64, FileMeta>,
}

impl FileRecordCache {
    pub fn get(&self, key: u64) -> Option<FileMeta> {
        self.records.get(&key).copied()
    }

    pub fn insert(&mut self, key: u64, value: FileMeta) {
        self.records.insert(key, value);
    }
}
