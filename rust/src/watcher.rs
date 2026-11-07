//! Tiny sign-in watcher. It starts the tray only for a live supported controller.
use controller_battery_native::windows_probe;
use std::{ffi::c_void, os::windows::ffi::OsStrExt, ptr, thread, time::Duration};
type Handle = *mut c_void;
#[link(name = "kernel32")]
unsafe extern "system" {
    fn CreateMutexW(attributes: Handle, initial_owner: i32, name: *const u16) -> Handle;
    fn OpenMutexW(access: u32, inherit: i32, name: *const u16) -> Handle;
    fn GetLastError() -> u32;
    fn CloseHandle(handle: Handle) -> i32;
}
#[link(name = "advapi32")]
unsafe extern "system" {
    fn RegOpenKeyExW(
        root: Handle,
        subkey: *const u16,
        options: u32,
        access: u32,
        key: *mut Handle,
    ) -> i32;
    fn RegQueryValueExW(
        key: Handle,
        name: *const u16,
        reserved: *mut u32,
        kind: *mut u32,
        data: *mut u8,
        size: *mut u32,
    ) -> i32;
    fn RegSetValueExW(
        key: Handle,
        name: *const u16,
        reserved: u32,
        kind: u32,
        data: *const u8,
        size: u32,
    ) -> i32;
    fn RegDeleteValueW(key: Handle, name: *const u16) -> i32;
    fn RegCloseKey(key: Handle) -> i32;
}
pub(crate) const TRAY_MUTEX: &str = "Local\\ControllerBatteryNative.Tray";
const WATCHER_MUTEX: &str = "Local\\ControllerBatteryNative.Watcher";
const RUN_KEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
const RUN_VALUE: &str = "ControllerBatteryNative";
pub(crate) fn wide(text: &str) -> Vec<u16> {
    std::ffi::OsStr::new(text)
        .encode_wide()
        .chain(Some(0))
        .collect()
}

pub(crate) struct Instance(Handle);
impl Drop for Instance {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}
pub(crate) fn acquire(name: &str) -> Option<Instance> {
    let handle = unsafe { CreateMutexW(ptr::null_mut(), 0, wide(name).as_ptr()) };
    if handle.is_null() {
        return None;
    }
    if unsafe { GetLastError() } == 183 {
        unsafe {
            CloseHandle(handle);
        }
        return None;
    }
    Some(Instance(handle))
}
fn tray_running() -> bool {
    let handle = unsafe { OpenMutexW(0x0010_0000, 0, wide(TRAY_MUTEX).as_ptr()) };
    if handle.is_null() {
        false
    } else {
        unsafe {
            CloseHandle(handle);
        }
        true
    }
}

fn watcher_command() -> Option<String> {
    let exe = std::env::current_exe().ok()?;
    Some(format!("\"{}\" --watcher", exe.display()))
}

fn open_run_key(access: u32) -> Option<Handle> {
    let mut key = ptr::null_mut();
    let status = unsafe {
        RegOpenKeyExW(
            0x8000_0001usize as Handle,
            wide(RUN_KEY).as_ptr(),
            0,
            access,
            &mut key,
        )
    };
    if status == 0 { Some(key) } else { None }
}

pub(crate) fn startup_enabled() -> bool {
    let Some(expected) = watcher_command() else {
        return false;
    };
    let Some(key) = open_run_key(0x0001) else {
        return false;
    };
    let mut kind = 0u32;
    let mut size = 0u32;
    let name = wide(RUN_VALUE);
    let first = unsafe {
        RegQueryValueExW(
            key,
            name.as_ptr(),
            ptr::null_mut(),
            &mut kind,
            ptr::null_mut(),
            &mut size,
        )
    };
    if first != 0 || kind != 1 || size > 4096 {
        unsafe { RegCloseKey(key) };
        return false;
    }
    let mut data = vec![0u8; size as usize];
    let status = if size > 0 {
        unsafe {
            RegQueryValueExW(
                key,
                name.as_ptr(),
                ptr::null_mut(),
                &mut kind,
                data.as_mut_ptr(),
                &mut size,
            )
        }
    } else {
        -1
    };
    unsafe { RegCloseKey(key) };
    if status != 0 {
        return false;
    }
    let value = String::from_utf16_lossy(
        &data[..size as usize]
            .as_chunks::<2>()
            .0
            .iter()
            .map(|pair| u16::from_le_bytes(*pair))
            .take_while(|unit| *unit != 0)
            .collect::<Vec<_>>(),
    );
    value.eq_ignore_ascii_case(&expected)
}

pub(crate) fn set_startup(enabled: bool) -> bool {
    let Some(key) = open_run_key(0x0002 | 0x0001) else {
        return false;
    };
    let name = wide(RUN_VALUE);
    let result = if enabled {
        let Some(command) = watcher_command() else {
            unsafe { RegCloseKey(key) };
            return false;
        };
        let encoded = wide(&command);
        (unsafe {
            RegSetValueExW(
                key,
                name.as_ptr(),
                0,
                1,
                encoded.as_ptr().cast(),
                (encoded.len() * 2) as u32,
            )
        }) == 0
    } else if startup_enabled() {
        (unsafe { RegDeleteValueW(key, name.as_ptr()) }) == 0
    } else {
        false
    };
    unsafe { RegCloseKey(key) };
    result
}
pub fn run() {
    let Some(_instance) = acquire(WATCHER_MUTEX) else {
        return;
    };
    loop {
        if !startup_enabled() {
            return;
        }
        if !tray_running()
            && !windows_probe::probe().is_empty()
            && let Ok(exe) = std::env::current_exe()
        {
            let _ = std::process::Command::new(exe).arg("--background").spawn();
        }
        thread::sleep(Duration::from_secs(2));
    }
}
