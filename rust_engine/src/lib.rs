pub mod cache;
pub mod core;
pub mod engine;
pub mod ffi;
pub mod memory;
pub mod ntfs;
pub mod search;
pub mod traditional;

#[unsafe(no_mangle)]
pub extern "C" fn test_connection() -> i32 {
    42
}
