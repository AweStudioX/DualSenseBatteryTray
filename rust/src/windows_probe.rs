//! Read-only, shared Windows HID/XInput probe. It never writes controller output.
use crate::{
    BatteryState, flydigi, parse_dualsense_report, parse_xinput_battery, reports_progressed,
};
use std::{ffi::c_void, mem::size_of, os::windows::ffi::OsStrExt, ptr};

type Handle = *mut c_void;
const INVALID: Handle = -1isize as Handle;

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct Guid {
    a: u32,
    b: u16,
    c: u16,
    d: [u8; 8],
}
#[repr(C)]
struct InterfaceData {
    size: u32,
    guid: Guid,
    flags: u32,
    reserved: usize,
}
#[repr(C)]
struct Attributes {
    size: u32,
    vendor: u16,
    product: u16,
    version: u16,
}
#[repr(C)]
#[derive(Default)]
struct Caps {
    usage: u16,
    usage_page: u16,
    input_len: u16,
    output_len: u16,
    feature_len: u16,
    reserved: [u16; 17],
    nodes: u16,
    buttons: [u16; 3],
    output: [u16; 3],
    feature: [u16; 3],
}
#[repr(C)]
#[derive(Default)]
struct Overlapped {
    internal: usize,
    internal_high: usize,
    offset: u32,
    offset_high: u32,
    event: Handle,
}

#[link(name = "hid")]
unsafe extern "system" {
    fn HidD_GetHidGuid(guid: *mut Guid);
    fn HidD_GetAttributes(handle: Handle, attributes: *mut Attributes) -> u8;
    fn HidD_GetPreparsedData(handle: Handle, data: *mut Handle) -> u8;
    fn HidD_FreePreparsedData(data: Handle) -> u8;
    fn HidD_GetFeature(handle: Handle, report: *mut u8, length: u32) -> u8;
    fn HidP_GetCaps(data: Handle, caps: *mut Caps) -> i32;
}
#[link(name = "setupapi")]
unsafe extern "system" {
    fn SetupDiGetClassDevsW(
        guid: *const Guid,
        enumerator: *const u16,
        parent: Handle,
        flags: u32,
    ) -> Handle;
    fn SetupDiEnumDeviceInterfaces(
        set: Handle,
        device: Handle,
        guid: *const Guid,
        index: u32,
        data: *mut InterfaceData,
    ) -> i32;
    fn SetupDiGetDeviceInterfaceDetailW(
        set: Handle,
        data: *mut InterfaceData,
        detail: *mut u8,
        length: u32,
        required: *mut u32,
        device: Handle,
    ) -> i32;
    fn SetupDiDestroyDeviceInfoList(set: Handle) -> i32;
}
#[link(name = "kernel32")]
unsafe extern "system" {
    fn CreateFileW(
        path: *const u16,
        access: u32,
        share: u32,
        security: Handle,
        disposition: u32,
        flags: u32,
        template: Handle,
    ) -> Handle;
    fn CloseHandle(handle: Handle) -> i32;
    fn CreateEventW(security: Handle, manual: i32, initial: i32, name: *const u16) -> Handle;
    fn ResetEvent(event: Handle) -> i32;
    fn ReadFile(
        handle: Handle,
        buffer: *mut u8,
        size: u32,
        read: *mut u32,
        overlapped: *mut Overlapped,
    ) -> i32;
    fn WriteFile(
        handle: Handle,
        buffer: *const u8,
        size: u32,
        written: *mut u32,
        overlapped: *mut Overlapped,
    ) -> i32;
    fn WaitNamedPipeW(path: *const u16, timeout: u32) -> i32;
    fn GetOverlappedResult(
        handle: Handle,
        overlapped: *mut Overlapped,
        read: *mut u32,
        wait: i32,
    ) -> i32;
    fn WaitForSingleObject(handle: Handle, timeout: u32) -> u32;
    fn CancelIoEx(handle: Handle, overlapped: *mut Overlapped) -> i32;
    fn GetLastError() -> u32;
    fn LoadLibraryExW(path: *const u16, file: Handle, flags: u32) -> Handle;
    fn GetProcAddress(module: Handle, name: *const u8) -> *mut c_void;
    fn FreeLibrary(module: Handle) -> i32;
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
    fn RegEnumKeyExW(
        key: Handle,
        index: u32,
        name: *mut u16,
        length: *mut u32,
        reserved: *mut u32,
        class: *mut u16,
        class_length: *mut u32,
        modified: *mut c_void,
    ) -> i32;
    fn RegCloseKey(key: Handle) -> i32;
}

struct Owned(Handle);
impl Drop for Owned {
    fn drop(&mut self) {
        if !self.0.is_null() && self.0 != INVALID {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }
}

#[derive(Debug)]
pub struct Detection {
    pub source: String,
    pub battery: BatteryState,
}

pub fn probe() -> Vec<Detection> {
    let mut found = probe_dualsense();
    let mut xinput = Vec::new();
    if let Some(xinput_api) = XInputApi::load() {
        for index in 0..4 {
            let mut raw = [0u8; 2];
            if unsafe { (xinput_api.get_battery)(index, 0, &mut raw) } == 0
                && let Some(battery) = parse_xinput_battery(raw[0], raw[1])
            {
                xinput.push(Detection {
                    source: format!("XInput {index}"),
                    battery,
                });
            }
        }
    }
    if xinput.len() == 1 {
        let service = query_flydigi().filter(|device| device.connected);
        if let Some(device) = service {
            let source = if device.code.eq_ignore_ascii_case("k2")
                || device.name.to_ascii_uppercase().contains("APEX 4")
            {
                "Flydigi APEX 4".to_owned()
            } else {
                device.name.clone()
            };
            found.push(Detection {
                source,
                battery: flydigi::normalize(&device),
            });
            return found;
        }
        if flydigi_apex_receiver_present() {
            let xinput_battery = xinput.remove(0).battery;
            found.push(Detection {
                source: "Flydigi APEX 4 (XInput)".to_owned(),
                battery: xinput_battery,
            });
            return found;
        }
    }
    found.extend(xinput);
    found
}

type GetXInputBattery = unsafe extern "system" fn(u32, u8, *mut [u8; 2]) -> u32;

struct XInputApi {
    module: Handle,
    get_battery: GetXInputBattery,
}

impl XInputApi {
    fn load() -> Option<Self> {
        let name: Vec<u16> = std::ffi::OsStr::new("xinput1_4.dll")
            .encode_wide()
            .chain(Some(0))
            .collect();
        // LOAD_LIBRARY_SEARCH_SYSTEM32 avoids loading an application-local DLL.
        let module = unsafe { LoadLibraryExW(name.as_ptr(), ptr::null_mut(), 0x800) };
        if module.is_null() {
            return None;
        }
        let proc =
            unsafe { GetProcAddress(module, c"XInputGetBatteryInformation".as_ptr().cast()) };
        if proc.is_null() {
            unsafe { FreeLibrary(module) };
            return None;
        }
        Some(Self {
            module,
            get_battery: unsafe { std::mem::transmute::<*mut c_void, GetXInputBattery>(proc) },
        })
    }
}

impl Drop for XInputApi {
    fn drop(&mut self) {
        unsafe { FreeLibrary(self.module) };
    }
}

fn flydigi_apex_receiver_present() -> bool {
    const KEY: &str = "SYSTEM\\CurrentControlSet\\Enum\\USB\\VID_045E&PID_028E";
    let mut key = ptr::null_mut();
    let path: Vec<u16> = std::ffi::OsStr::new(KEY)
        .encode_wide()
        .chain(Some(0))
        .collect();
    if unsafe {
        RegOpenKeyExW(
            0x8000_0002usize as Handle,
            path.as_ptr(),
            0,
            0x20019,
            &mut key,
        )
    } != 0
    {
        return false;
    }
    let mut found = false;
    for index in 0..64 {
        let mut name = [0u16; 256];
        let mut length = name.len() as u32;
        if unsafe {
            RegEnumKeyExW(
                key,
                index,
                name.as_mut_ptr(),
                &mut length,
                ptr::null_mut(),
                ptr::null_mut(),
                ptr::null_mut(),
                ptr::null_mut(),
            )
        } != 0
        {
            break;
        }
        if is_apex_instance(&String::from_utf16_lossy(&name[..length as usize])) {
            found = true;
            break;
        }
    }
    unsafe { RegCloseKey(key) };
    found
}

fn is_apex_instance(name: &str) -> bool {
    name.to_ascii_uppercase().starts_with("FLYDIGI_APEX")
}

fn query_flydigi() -> Option<flydigi::FlydigiDevice> {
    let path: Vec<u16> = std::ffi::OsStr::new(r"\\.\pipe\fcs.sock")
        .encode_wide()
        .chain(Some(0))
        .collect();
    if unsafe { WaitNamedPipeW(path.as_ptr(), 100) } == 0 {
        return None;
    }
    let pipe = Owned(unsafe {
        CreateFileW(
            path.as_ptr(),
            0xc000_0000,
            3,
            ptr::null_mut(),
            3,
            0x4000_0000,
            ptr::null_mut(),
        )
    });
    if pipe.0 == INVALID {
        return None;
    }
    const HEADER: &[u8] = b"FDG_PROTOCOL\n";
    let mut request = HEADER.to_vec();
    request.extend_from_slice(&[5, 0, 0, 0, 0x08, 0x81, 0x20, 0x1a, 0]);
    write_exact(pipe.0, &request)?;
    for _ in 0..4 {
        let mut header = [0u8; 13];
        read_exact(pipe.0, &mut header)?;
        if header != HEADER {
            return None;
        }
        let mut length = [0u8; 4];
        read_exact(pipe.0, &mut length)?;
        let length = u32::from_le_bytes(length) as usize;
        if length > 1024 * 1024 {
            return None;
        }
        let mut payload = vec![0u8; length];
        read_exact(pipe.0, &mut payload)?;
        if let Some((4097, devices)) = flydigi::parse_response(&payload) {
            return devices.into_iter().find(|device| device.connected);
        }
    }
    None
}

fn write_exact(handle: Handle, data: &[u8]) -> Option<()> {
    let mut offset = 0;
    while offset < data.len() {
        let event = Owned(unsafe { CreateEventW(ptr::null_mut(), 1, 0, ptr::null()) });
        if event.0.is_null() {
            return None;
        }
        let mut overlap = Overlapped {
            event: event.0,
            ..Default::default()
        };
        let mut written = 0;
        let result = unsafe {
            WriteFile(
                handle,
                data[offset..].as_ptr(),
                (data.len() - offset) as u32,
                &mut written,
                &mut overlap,
            )
        };
        if result == 0 {
            if unsafe { GetLastError() } != 997 || unsafe { WaitForSingleObject(event.0, 500) } != 0
            {
                unsafe {
                    CancelIoEx(handle, &mut overlap);
                    GetOverlappedResult(handle, &mut overlap, &mut written, 1);
                }
                return None;
            }
            if unsafe { GetOverlappedResult(handle, &mut overlap, &mut written, 0) } == 0 {
                return None;
            }
        }
        if written == 0 {
            return None;
        }
        offset += written as usize;
    }
    Some(())
}

fn read_exact(handle: Handle, data: &mut [u8]) -> Option<()> {
    let mut offset = 0;
    while offset < data.len() {
        let event = Owned(unsafe { CreateEventW(ptr::null_mut(), 1, 0, ptr::null()) });
        if event.0.is_null() {
            return None;
        }
        let mut overlap = Overlapped {
            event: event.0,
            ..Default::default()
        };
        let mut read = 0;
        let result = unsafe {
            ReadFile(
                handle,
                data[offset..].as_mut_ptr(),
                (data.len() - offset) as u32,
                &mut read,
                &mut overlap,
            )
        };
        if result == 0 {
            if unsafe { GetLastError() } != 997 || unsafe { WaitForSingleObject(event.0, 500) } != 0
            {
                unsafe {
                    CancelIoEx(handle, &mut overlap);
                    GetOverlappedResult(handle, &mut overlap, &mut read, 1);
                }
                return None;
            }
            if unsafe { GetOverlappedResult(handle, &mut overlap, &mut read, 0) } == 0 {
                return None;
            }
        }
        if read == 0 {
            return None;
        }
        offset += read as usize;
    }
    Some(())
}

fn probe_dualsense() -> Vec<Detection> {
    let mut found = Vec::new();
    let mut guid = Guid::default();
    unsafe {
        HidD_GetHidGuid(&mut guid);
    }
    let set = unsafe { SetupDiGetClassDevsW(&guid, ptr::null(), ptr::null_mut(), 0x12) };
    if set == INVALID {
        return found;
    }
    let mut index = 0;
    loop {
        let mut item = InterfaceData {
            size: size_of::<InterfaceData>() as u32,
            guid: Guid::default(),
            flags: 0,
            reserved: 0,
        };
        if unsafe { SetupDiEnumDeviceInterfaces(set, ptr::null_mut(), &guid, index, &mut item) }
            == 0
        {
            break;
        }
        index += 1;
        let mut needed = 0;
        unsafe {
            SetupDiGetDeviceInterfaceDetailW(
                set,
                &mut item,
                ptr::null_mut(),
                0,
                &mut needed,
                ptr::null_mut(),
            );
        }
        if !(8..=4096).contains(&needed) {
            continue;
        }
        let mut detail = vec![0u8; needed as usize];
        detail[..4]
            .copy_from_slice(&(if size_of::<usize>() == 8 { 8u32 } else { 6u32 }).to_le_bytes());
        if unsafe {
            SetupDiGetDeviceInterfaceDetailW(
                set,
                &mut item,
                detail.as_mut_ptr(),
                needed,
                &mut needed,
                ptr::null_mut(),
            )
        } == 0
        {
            continue;
        }
        let units: Vec<u16> = detail[4..]
            .as_chunks::<2>()
            .0
            .iter()
            .map(|c| u16::from_le_bytes(*c))
            .take_while(|v| *v != 0)
            .collect();
        let path = String::from_utf16_lossy(&units);
        if let Some((battery, bluetooth)) = probe_hid_path(&path) {
            found.push(Detection {
                source: if bluetooth {
                    "DualSense Bluetooth"
                } else {
                    "DualSense USB"
                }
                .into(),
                battery,
            });
        }
    }
    unsafe {
        SetupDiDestroyDeviceInfoList(set);
    }
    found
}

fn probe_hid_path(path: &str) -> Option<(BatteryState, bool)> {
    let wide: Vec<u16> = std::ffi::OsStr::new(path)
        .encode_wide()
        .chain(Some(0))
        .collect();
    let attrs_handle =
        Owned(unsafe { CreateFileW(wide.as_ptr(), 0, 3, ptr::null_mut(), 3, 0, ptr::null_mut()) });
    if attrs_handle.0 == INVALID {
        return None;
    }
    let mut attrs = Attributes {
        size: size_of::<Attributes>() as u32,
        vendor: 0,
        product: 0,
        version: 0,
    };
    if unsafe { HidD_GetAttributes(attrs_handle.0, &mut attrs) } == 0
        || (attrs.vendor, attrs.product) != (0x054c, 0x0ce6)
    {
        return None;
    }
    let mut preparsed = ptr::null_mut();
    if unsafe { HidD_GetPreparsedData(attrs_handle.0, &mut preparsed) } == 0 {
        return None;
    }
    let mut caps = Caps::default();
    let status = unsafe { HidP_GetCaps(preparsed, &mut caps) };
    unsafe {
        HidD_FreePreparsedData(preparsed);
    }
    if status != 0x110000 || !matches!(caps.input_len, 64 | 78) {
        return None;
    }
    drop(attrs_handle);
    let handle = Owned(unsafe {
        CreateFileW(
            wide.as_ptr(),
            0x8000_0000,
            3,
            ptr::null_mut(),
            3,
            0x4000_0000,
            ptr::null_mut(),
        )
    });
    if handle.0 == INVALID {
        return None;
    }
    if caps.input_len == 78 {
        let mut feature = vec![0u8; (caps.feature_len as usize).max(41)];
        feature[0] = 5;
        unsafe {
            HidD_GetFeature(handle.0, feature.as_mut_ptr(), feature.len() as u32);
        }
    }
    let event = Owned(unsafe { CreateEventW(ptr::null_mut(), 1, 0, ptr::null()) });
    if event.0.is_null() {
        return None;
    }
    let first = read_report(handle.0, event.0, caps.input_len as usize)?;
    let second = read_report(handle.0, event.0, caps.input_len as usize)?;
    if !reports_progressed(&first, &second) {
        return None;
    }
    Some((parse_dualsense_report(&second)?, caps.input_len == 78))
}

fn read_report(handle: Handle, event: Handle, length: usize) -> Option<Vec<u8>> {
    unsafe {
        ResetEvent(event);
    }
    let mut overlap = Overlapped {
        event,
        ..Default::default()
    };
    let mut report = vec![0u8; length];
    let mut read = 0;
    let result = unsafe {
        ReadFile(
            handle,
            report.as_mut_ptr(),
            report.len() as u32,
            &mut read,
            &mut overlap,
        )
    };
    if result == 0 {
        if unsafe { GetLastError() } != 997 || unsafe { WaitForSingleObject(event, 1200) } != 0 {
            unsafe {
                CancelIoEx(handle, &mut overlap);
                GetOverlappedResult(handle, &mut overlap, &mut read, 1);
            }
            return None;
        }
        if unsafe { GetOverlappedResult(handle, &mut overlap, &mut read, 0) } == 0 {
            return None;
        }
    }
    if read as usize != length {
        return None;
    }
    Some(report)
}

#[cfg(test)]
mod registry_tests {
    use super::is_apex_instance;

    #[test]
    fn only_apex_receiver_identity_is_apex() {
        assert!(is_apex_instance("FLYDIGI_APEX_4"));
        assert!(!is_apex_instance("FLYDIGI_VADER_4"));
    }
}
