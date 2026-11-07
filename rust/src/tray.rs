//! Minimal native Win32 shell. HID work runs off the UI thread.
use crate::watcher;
use controller_battery_native::features::{LowBatteryAlerts, TrayLayout};
use controller_battery_native::windows_probe::{self, Detection};
use std::{
    ffi::c_void,
    mem::size_of,
    ptr,
    sync::atomic::{AtomicI32, Ordering},
    sync::{Mutex, OnceLock},
    thread,
    time::Duration,
};

type Handle = *mut c_void;
type WndProc = unsafe extern "system" fn(Handle, u32, usize, isize) -> isize;
const APP_UPDATE: u32 = 0x8001;
const WM_CREATE: u32 = 1;
const WM_DESTROY: u32 = 2;
const WM_PAINT: u32 = 15;
const WM_CLOSE: u32 = 16;
const WM_RBUTTONUP: u32 = 0x205;
const WM_COMMAND: u32 = 0x111;
const WM_APP_TRAY: u32 = 0x8002;
const WM_APP_EXIT: u32 = 0x8003;
const APP_REFRESH: u32 = 0x8004;
const WM_SETICON: u32 = 0x80;
const NIF_MESSAGE: u32 = 1;
const NIF_ICON: u32 = 2;
const NIF_TIP: u32 = 4;
const NIF_INFO: u32 = 0x10;

#[repr(C)]
#[derive(Default)]
struct WndClass {
    style: u32,
    proc: Option<WndProc>,
    class_extra: i32,
    window_extra: i32,
    instance: Handle,
    icon: Handle,
    cursor: Handle,
    background: Handle,
    menu_name: *const u16,
    class_name: *const u16,
}
#[repr(C)]
#[derive(Default)]
struct Message {
    hwnd: Handle,
    message: u32,
    wparam: usize,
    lparam: isize,
    time: u32,
    point: Point,
    private: u32,
}
#[repr(C)]
#[derive(Default)]
struct Point {
    x: i32,
    y: i32,
}
#[repr(C)]
#[derive(Default)]
struct Rect {
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
}
#[repr(C)]
#[derive(Default)]
struct Paint {
    dc: Handle,
    erase: i32,
    rect: Rect,
    restore: i32,
    update: i32,
    reserved: [u8; 32],
}
#[repr(C)]
struct NotifyIcon {
    size: u32,
    hwnd: Handle,
    id: u32,
    flags: u32,
    callback: u32,
    icon: Handle,
    tip: [u16; 128],
    state: u32,
    state_mask: u32,
    info: [u16; 256],
    version: u32,
    info_title: [u16; 64],
    info_flags: u32,
    guid: [u8; 16],
    balloon_icon: Handle,
}
impl NotifyIcon {
    fn new(hwnd: Handle, id: u32) -> Self {
        Self {
            size: size_of::<Self>() as u32,
            hwnd,
            id,
            flags: NIF_MESSAGE | NIF_ICON | NIF_TIP,
            callback: WM_APP_TRAY,
            icon: ptr::null_mut(),
            tip: [0; 128],
            state: 0,
            state_mask: 0,
            info: [0; 256],
            version: 0,
            info_title: [0; 64],
            info_flags: 0,
            guid: [0; 16],
            balloon_icon: ptr::null_mut(),
        }
    }
}
#[link(name = "user32")]
unsafe extern "system" {
    fn RegisterClassW(class: *const WndClass) -> u16;
    fn FindWindowW(class: *const u16, title: *const u16) -> Handle;
    fn CreateWindowExW(
        ex: u32,
        class: *const u16,
        title: *const u16,
        style: u32,
        x: i32,
        y: i32,
        width: i32,
        height: i32,
        parent: Handle,
        menu: Handle,
        instance: Handle,
        param: Handle,
    ) -> Handle;
    fn DefWindowProcW(hwnd: Handle, msg: u32, wp: usize, lp: isize) -> isize;
    fn GetMessageW(message: *mut Message, hwnd: Handle, min: u32, max: u32) -> i32;
    fn TranslateMessage(message: *const Message) -> i32;
    fn DispatchMessageW(message: *const Message) -> isize;
    fn PostMessageW(hwnd: Handle, msg: u32, wp: usize, lp: isize) -> i32;
    fn PostQuitMessage(code: i32);
    fn DestroyWindow(hwnd: Handle) -> i32;
    fn SendMessageW(hwnd: Handle, msg: u32, wp: usize, lp: isize) -> isize;
    fn SetProcessDpiAwarenessContext(context: Handle) -> i32;
    fn GetDpiForSystem() -> u32;
    fn GetSystemMetrics(index: i32) -> i32;
    fn ShowWindow(hwnd: Handle, command: i32) -> i32;
    fn IsWindowVisible(hwnd: Handle) -> i32;
    fn SetForegroundWindow(hwnd: Handle) -> i32;
    fn InvalidateRect(hwnd: Handle, rect: *const Rect, erase: i32) -> i32;
    fn BeginPaint(hwnd: Handle, paint: *mut Paint) -> Handle;
    fn EndPaint(hwnd: Handle, paint: *const Paint) -> i32;
    fn GetClientRect(hwnd: Handle, rect: *mut Rect) -> i32;
    fn LoadImageW(
        instance: Handle,
        name: *const u16,
        kind: u32,
        width: i32,
        height: i32,
        flags: u32,
    ) -> Handle;
    fn DestroyIcon(icon: Handle) -> i32;
    fn DrawIconEx(
        dc: Handle,
        x: i32,
        y: i32,
        icon: Handle,
        width: i32,
        height: i32,
        step: u32,
        brush: Handle,
        flags: u32,
    ) -> i32;
    fn GetCursorPos(point: *mut Point) -> i32;
    fn CreatePopupMenu() -> Handle;
    fn AppendMenuW(menu: Handle, flags: u32, id: usize, text: *const u16) -> i32;
    fn TrackPopupMenu(
        menu: Handle,
        flags: u32,
        x: i32,
        y: i32,
        reserved: i32,
        owner: Handle,
        rect: *const Rect,
    ) -> i32;
    fn DestroyMenu(menu: Handle) -> i32;
    fn DrawTextW(dc: Handle, text: *const u16, len: i32, rect: *mut Rect, flags: u32) -> i32;
}
#[link(name = "shell32")]
unsafe extern "system" {
    fn Shell_NotifyIconW(command: u32, data: *const NotifyIcon) -> i32;
}
#[link(name = "gdi32")]
unsafe extern "system" {
    fn CreateSolidBrush(color: u32) -> Handle;
    fn GetStockObject(index: i32) -> Handle;
    fn SaveDC(dc: Handle) -> i32;
    fn RestoreDC(dc: Handle, saved: i32) -> i32;
    fn RoundRect(
        dc: Handle,
        left: i32,
        top: i32,
        right: i32,
        bottom: i32,
        width: i32,
        height: i32,
    ) -> i32;
    fn DeleteObject(object: Handle) -> i32;
    fn SelectObject(dc: Handle, object: Handle) -> Handle;
    fn SetTextColor(dc: Handle, color: u32) -> u32;
    fn SetBkMode(dc: Handle, mode: i32) -> i32;
    fn CreateFontW(
        height: i32,
        width: i32,
        escapement: i32,
        orientation: i32,
        weight: i32,
        italic: u32,
        underline: u32,
        strikeout: u32,
        charset: u32,
        output: u32,
        clip: u32,
        quality: u32,
        pitch: u32,
        face: *const u16,
    ) -> Handle;
}
#[link(name = "user32")]
unsafe extern "system" {
    fn FillRect(dc: Handle, rect: *const Rect, brush: Handle) -> i32;
}
#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetModuleHandleW(name: *const u16) -> Handle;
}
#[link(name = "advapi32")]
unsafe extern "system" {
    fn RegGetValueW(
        key: Handle,
        subkey: *const u16,
        value: *const u16,
        flags: u32,
        kind: *mut u32,
        data: *mut u32,
        size: *mut u32,
    ) -> i32;
}

struct State {
    devices: Vec<Detection>,
    icon: Handle,
    second_icon: Handle,
    window_icon: Handle,
    exit: bool,
    layout: TrayLayout,
    selected: Option<String>,
    alerts: LowBatteryAlerts,
}
unsafe impl Send for State {}
static STATE: OnceLock<Mutex<State>> = OnceLock::new();
static DPI: AtomicI32 = AtomicI32::new(96);
fn state() -> &'static Mutex<State> {
    STATE.get_or_init(|| {
        Mutex::new(State {
            devices: vec![],
            icon: ptr::null_mut(),
            second_icon: ptr::null_mut(),
            window_icon: ptr::null_mut(),
            exit: false,
            layout: load_layout(),
            selected: None,
            alerts: LowBatteryAlerts::default(),
        })
    })
}
fn wide(text: &str) -> Vec<u16> {
    text.encode_utf16().chain(Some(0)).collect()
}
fn color(r: u8, g: u8, b: u8) -> u32 {
    u32::from(r) | (u32::from(g) << 8) | (u32::from(b) << 16)
}
fn scale(value: i32) -> i32 {
    (value * DPI.load(Ordering::Relaxed) + 48) / 96
}

fn layout_path() -> Option<std::path::PathBuf> {
    std::env::var_os("LOCALAPPDATA").map(|root| {
        std::path::PathBuf::from(root)
            .join("DualSenseBatteryTray")
            .join("tray-layout.txt")
    })
}

fn load_layout() -> TrayLayout {
    layout_path()
        .and_then(|path| std::fs::read_to_string(path).ok())
        .map_or(TrayLayout::TwoIcons, |value| TrayLayout::parse(&value))
}

fn save_layout(layout: TrayLayout) -> std::io::Result<()> {
    let path = layout_path().ok_or_else(|| std::io::Error::other("LOCALAPPDATA is unavailable"))?;
    std::fs::create_dir_all(path.parent().unwrap())?;
    std::fs::write(path, layout.as_str())
}

fn selected_device(current: &State) -> Option<&Detection> {
    current
        .selected
        .as_ref()
        .and_then(|selected| {
            current
                .devices
                .iter()
                .find(|device| &device.source == selected)
        })
        .or_else(|| current.devices.first())
}

fn load_application_icon(instance: Handle, width: i32, height: i32) -> Handle {
    #[allow(clippy::manual_dangling_ptr)] // Win32 MAKEINTRESOURCEW(1), not a dereferenced pointer.
    let embedded = unsafe { LoadImageW(instance, 1usize as *const u16, 1, width, height, 0) };
    if !embedded.is_null() {
        return embedded;
    }
    let Some(path) = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|p| p.join("dualsense-disconnected.ico")))
    else {
        return ptr::null_mut();
    };
    unsafe {
        LoadImageW(
            ptr::null_mut(),
            wide(&path.to_string_lossy()).as_ptr(),
            1,
            width,
            height,
            0x10,
        )
    }
}

fn load_window_small_icon(instance: Handle, width: i32, height: i32) -> Handle {
    let custom = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|p| p.join("window-small.ico")))
        .map(|path| unsafe {
            LoadImageW(
                ptr::null_mut(),
                wide(&path.to_string_lossy()).as_ptr(),
                1,
                width,
                height,
                0x10,
            )
        })
        .unwrap_or(ptr::null_mut());
    if custom.is_null() {
        load_application_icon(instance, width, height)
    } else {
        custom
    }
}

pub fn run(show: bool, foreground_on_existing: bool) {
    let Some(_instance) = watcher::acquire(watcher::TRAY_MUTEX) else {
        if foreground_on_existing {
            let existing =
                unsafe { FindWindowW(wide("ControllerBatteryNative").as_ptr(), ptr::null()) };
            if !existing.is_null() {
                unsafe {
                    ShowWindow(existing, 5);
                    SetForegroundWindow(existing);
                }
            }
        }
        return;
    };
    let first = windows_probe::probe();
    if first.is_empty() && !show {
        return;
    }
    state().lock().unwrap().devices = first;
    unsafe {
        SetProcessDpiAwarenessContext(-2isize as Handle);
    }
    DPI.store(
        (unsafe { GetDpiForSystem() } as i32).max(120),
        Ordering::Relaxed,
    );
    let class = wide("ControllerBatteryNative");
    let title = wide("Controller Battery");
    let instance = unsafe { GetModuleHandleW(ptr::null()) };
    // WPF supplies the original high-resolution application image to Windows.
    // Keep the same source for the taskbar instead of our tray-size glyph.
    let icon = load_application_icon(instance, 256, 256);
    let wc = WndClass {
        proc: Some(window_proc),
        instance,
        class_name: class.as_ptr(),
        icon,
        ..Default::default()
    };
    if unsafe { RegisterClassW(&wc) } == 0 {
        return;
    }
    let hwnd = unsafe {
        CreateWindowExW(
            0,
            class.as_ptr(),
            title.as_ptr(),
            0x00CA_0000,
            scale(100),
            scale(100),
            scale(420),
            scale(305),
            ptr::null_mut(),
            ptr::null_mut(),
            instance,
            ptr::null_mut(),
        )
    };
    if hwnd.is_null() {
        return;
    }
    if !icon.is_null() {
        unsafe {
            SendMessageW(hwnd, WM_SETICON, 1, icon as isize);
        }
    }
    {
        let small = load_window_small_icon(instance, unsafe { GetSystemMetrics(49) }, unsafe {
            GetSystemMetrics(50)
        });
        if !small.is_null() {
            unsafe {
                SendMessageW(hwnd, WM_SETICON, 0, small as isize);
            }
            state().lock().unwrap().window_icon = small;
        }
    }
    if show {
        unsafe {
            ShowWindow(hwnd, 5);
        }
    }
    let hwnd_value = hwnd as usize;
    thread::spawn(move || {
        let mut absent = 0;
        loop {
            if state().lock().unwrap().exit {
                break;
            }
            let devices = windows_probe::probe();
            absent = if devices.is_empty() { absent + 1 } else { 0 };
            if absent == 0 || absent >= 2 {
                state().lock().unwrap().devices = devices;
                unsafe {
                    PostMessageW(hwnd_value as Handle, APP_UPDATE, 0, 0);
                }
            }
            if absent >= 2 && !show {
                unsafe {
                    PostMessageW(hwnd_value as Handle, WM_APP_EXIT, 0, 0);
                }
                break;
            }
            thread::sleep(Duration::from_secs(3));
        }
    });
    let mut message = Message::default();
    while unsafe { GetMessageW(&mut message, ptr::null_mut(), 0, 0) } > 0 {
        unsafe {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    state().lock().unwrap().exit = true;
}

unsafe extern "system" fn window_proc(hwnd: Handle, msg: u32, wp: usize, lp: isize) -> isize {
    match msg {
        WM_CREATE => {
            update_icon(hwnd);
            0
        }
        APP_UPDATE => {
            update_icon(hwnd);
            notify_low_battery(hwnd);
            unsafe {
                InvalidateRect(hwnd, ptr::null(), 1);
            }
            0
        }
        WM_APP_TRAY => {
            match lp as u32 {
                0x202 | 0x203 => {
                    if unsafe { IsWindowVisible(hwnd) } == 0 {
                        unsafe {
                            ShowWindow(hwnd, 5);
                            SetForegroundWindow(hwnd);
                        }
                    } else {
                        unsafe {
                            ShowWindow(hwnd, 0);
                        }
                    }
                }
                0x205 => show_menu(hwnd),
                _ => {}
            }
            0
        }
        WM_COMMAND => {
            match wp & 0xffff {
                1 => unsafe {
                    DestroyWindow(hwnd);
                },
                2 | 3 => {
                    let layout = if wp & 0xffff == 2 {
                        TrayLayout::Compact
                    } else {
                        TrayLayout::TwoIcons
                    };
                    if save_layout(layout).is_ok() {
                        state().lock().unwrap().layout = layout;
                        update_icon(hwnd);
                    }
                }
                4 => unsafe {
                    PostMessageW(hwnd, APP_REFRESH, 0, 0);
                },
                5 => {
                    let enabled = !watcher::startup_enabled();
                    if watcher::set_startup(enabled)
                        && enabled
                        && let Ok(exe) = std::env::current_exe()
                    {
                        let _ = std::process::Command::new(exe).arg("--watcher").spawn();
                    }
                }
                100..=199 => {
                    let mut current = state().lock().unwrap();
                    current.selected = current
                        .devices
                        .get((wp & 0xffff) - 100)
                        .map(|device| device.source.clone());
                    current.alerts = LowBatteryAlerts::default();
                    drop(current);
                    update_icon(hwnd);
                    unsafe { InvalidateRect(hwnd, ptr::null(), 1) };
                }
                _ => {}
            }
            0
        }
        APP_REFRESH => {
            let hwnd_value = hwnd as usize;
            thread::spawn(move || {
                let devices = windows_probe::probe();
                state().lock().unwrap().devices = devices;
                unsafe { PostMessageW(hwnd_value as Handle, APP_UPDATE, 0, 0) };
            });
            0
        }
        WM_APP_EXIT => {
            unsafe {
                DestroyWindow(hwnd);
            }
            0
        }
        WM_CLOSE => {
            unsafe {
                ShowWindow(hwnd, 0);
            }
            0
        }
        WM_RBUTTONUP => {
            show_menu(hwnd);
            0
        }
        WM_PAINT => {
            paint(hwnd);
            0
        }
        WM_DESTROY => {
            unsafe {
                Shell_NotifyIconW(2, &NotifyIcon::new(hwnd, 1));
                Shell_NotifyIconW(2, &NotifyIcon::new(hwnd, 2));
            }
            let mut current = state().lock().unwrap();
            current.exit = true;
            if !current.icon.is_null() {
                unsafe {
                    DestroyIcon(current.icon);
                }
                current.icon = ptr::null_mut();
            }
            if !current.second_icon.is_null() {
                unsafe { DestroyIcon(current.second_icon) };
                current.second_icon = ptr::null_mut();
            }
            if !current.window_icon.is_null() {
                unsafe {
                    DestroyIcon(current.window_icon);
                }
                current.window_icon = ptr::null_mut();
            }
            unsafe {
                PostQuitMessage(0);
            }
            0
        }
        _ => unsafe { DefWindowProcW(hwnd, msg, wp, lp) },
    }
}

fn update_icon(hwnd: Handle) {
    let current = state().lock().unwrap();
    let device = selected_device(&current);
    let glyph = controller_glyph(device.map(|d| d.source.as_str()));
    let tooltip = if let Some(device) = device {
        let level = device
            .battery
            .percentage
            .map(|v| format!("{v}%"))
            .unwrap_or("Unknown".into());
        format!("{} · {} · {:?}", device.source, level, device.battery.state)
    } else {
        "Controller Battery · Disconnected".into()
    };
    let layout = current.layout;
    let battery = device.map(|device| device.battery);
    drop(current);
    let theme = if system_uses_light_taskbar() {
        "light"
    } else {
        "dark"
    };
    let level = battery
        .and_then(|value| value.percentage)
        .map(|value| (value / 5 * 5).to_string())
        .unwrap_or_else(|| "unknown".into());
    let charging = battery
        .is_some_and(|value| value.state == controller_battery_native::ChargeState::Charging);
    let first = match (battery, layout) {
        (None, _) => format!("disconnected-{theme}.ico"),
        (Some(_), TrayLayout::Compact) => format!(
            "compact-{glyph}-{theme}-{level}{}.ico",
            if charging { "-charging" } else { "" }
        ),
        (Some(_), TrayLayout::TwoIcons) => format!(
            "{glyph}-{}-{theme}.ico",
            if charging { "charging" } else { "connected" }
        ),
    };
    set_tray_icon(hwnd, 1, &first, &tooltip);
    if battery.is_some() && layout == TrayLayout::TwoIcons {
        set_tray_icon(
            hwnd,
            2,
            &format!("percentage-{theme}-{level}.ico"),
            &tooltip,
        );
    } else {
        let mut current = state().lock().unwrap();
        if !current.second_icon.is_null() {
            unsafe {
                Shell_NotifyIconW(2, &NotifyIcon::new(hwnd, 2));
                DestroyIcon(current.second_icon);
            }
            current.second_icon = ptr::null_mut();
        }
    }
}

fn set_tray_icon(hwnd: Handle, id: u32, name: &str, tooltip: &str) {
    let Some(path) = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|directory| directory.join(name)))
    else {
        return;
    };
    let icon = unsafe {
        LoadImageW(
            ptr::null_mut(),
            wide(&path.to_string_lossy()).as_ptr(),
            1,
            GetSystemMetrics(49),
            GetSystemMetrics(50),
            0x10,
        )
    };
    if icon.is_null() {
        return;
    }
    let mut info = NotifyIcon::new(hwnd, id);
    info.icon = icon;
    for (dest, src) in info.tip.iter_mut().zip(tooltip.encode_utf16()) {
        *dest = src;
    }
    let mut current = state().lock().unwrap();
    let previous = if id == 1 {
        &mut current.icon
    } else {
        &mut current.second_icon
    };
    let command = if previous.is_null() { 0 } else { 1 };
    if unsafe { Shell_NotifyIconW(command, &info) } != 0 {
        if !previous.is_null() {
            unsafe { DestroyIcon(*previous) };
        }
        *previous = icon;
    } else {
        unsafe { DestroyIcon(icon) };
    }
}

fn notify_low_battery(hwnd: Handle) {
    let mut current = state().lock().unwrap();
    let battery = selected_device(&current).map(|device| device.battery);
    let threshold = current.alerts.observe(battery);
    drop(current);
    let Some(threshold) = threshold else { return };
    let mut info = NotifyIcon::new(hwnd, 1);
    info.flags = NIF_INFO;
    let title = "Controller battery low";
    let message = format!("Battery is at or below {threshold}%. Connect a charger.");
    for (dest, src) in info.info_title.iter_mut().zip(title.encode_utf16()) {
        *dest = src;
    }
    for (dest, src) in info.info.iter_mut().zip(message.encode_utf16()) {
        *dest = src;
    }
    info.info_flags = 2;
    unsafe { Shell_NotifyIconW(1, &info) };
}

fn system_uses_light_taskbar() -> bool {
    let mut value = 0u32;
    let mut size = 4u32;
    let result = unsafe {
        RegGetValueW(
            0x8000_0001usize as Handle,
            wide("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize").as_ptr(),
            wide("SystemUsesLightTheme").as_ptr(),
            0x18,
            ptr::null_mut(),
            &mut value,
            &mut size,
        )
    };
    result == 0 && value != 0
}

fn show_menu(hwnd: Handle) {
    let menu = unsafe { CreatePopupMenu() };
    if menu.is_null() {
        return;
    }
    unsafe {
        AppendMenuW(menu, 0, 4, wide("Refresh now").as_ptr());
        AppendMenuW(
            menu,
            if watcher::startup_enabled() { 8 } else { 0 },
            5,
            wide("Listen after Windows sign-in").as_ptr(),
        );
        let current = state().lock().unwrap();
        let compact_flags = if current.layout == TrayLayout::Compact {
            8
        } else {
            0
        };
        let two_flags = if current.layout == TrayLayout::TwoIcons {
            8
        } else {
            0
        };
        AppendMenuW(menu, compact_flags, 2, wide("Compact · one icon").as_ptr());
        AppendMenuW(
            menu,
            two_flags,
            3,
            wide("Two icons · controller + percent").as_ptr(),
        );
        if current.devices.len() > 1 {
            AppendMenuW(menu, 0x800, 0, ptr::null());
            for (index, device) in current.devices.iter().take(100).enumerate() {
                let checked = if selected_device(&current)
                    .is_some_and(|selected| selected.source == device.source)
                {
                    8
                } else {
                    0
                };
                AppendMenuW(menu, checked, index + 100, wide(&device.source).as_ptr());
            }
        }
        drop(current);
        AppendMenuW(menu, 0x800, 0, ptr::null());
        AppendMenuW(menu, 0, 1, wide("Exit").as_ptr());
        let mut point = Point::default();
        GetCursorPos(&mut point);
        SetForegroundWindow(hwnd);
        let command = TrackPopupMenu(menu, 0x0102, point.x, point.y, 0, hwnd, ptr::null());
        DestroyMenu(menu);
        if command != 0 {
            PostMessageW(hwnd, WM_COMMAND, command as usize, 0);
        }
    }
}

fn paint(hwnd: Handle) {
    let mut ps = Paint::default();
    let dc = unsafe { BeginPaint(hwnd, &mut ps) };
    if dc.is_null() {
        return;
    }
    let mut client = Rect::default();
    unsafe {
        GetClientRect(hwnd, &mut client);
    }
    let background = unsafe { CreateSolidBrush(color(250, 251, 251)) };
    unsafe {
        FillRect(dc, &client, background);
        DeleteObject(background);
    }
    unsafe {
        SetBkMode(dc, 1);
    }
    let current = {
        let state = state().lock().unwrap();
        selected_device(&state).map(|device| (device.source.clone(), device.battery))
    };
    draw_controller_icon(dc, current.as_ref().map(|(source, _)| source.as_str()));
    let connected = current.is_some();
    let pill_left = if connected { 317 } else { 298 };
    let status_background = unsafe {
        CreateSolidBrush(if connected {
            color(229, 242, 241)
        } else {
            color(237, 241, 241)
        })
    };
    let status_ink = unsafe {
        CreateSolidBrush(if connected {
            color(64, 132, 131)
        } else {
            color(91, 105, 107)
        })
    };
    unsafe {
        let saved = SaveDC(dc);
        SelectObject(dc, status_background);
        SelectObject(dc, GetStockObject(8));
        RoundRect(
            dc,
            scale(pill_left),
            scale(22),
            scale(392),
            scale(50),
            scale(11),
            scale(11),
        );
        SelectObject(dc, status_ink);
        let dot_x = if connected { 328 } else { 309 };
        RoundRect(
            dc,
            scale(dot_x),
            scale(32),
            scale(dot_x + 7),
            scale(39),
            scale(7),
            scale(7),
        );
        if !connected {
            SelectObject(dc, status_background);
            RoundRect(
                dc,
                scale(dot_x + 2),
                scale(34),
                scale(dot_x + 5),
                scale(37),
                scale(3),
                scale(3),
            );
        }
        RestoreDC(dc, saved);
        DeleteObject(status_ink);
        DeleteObject(status_background);
    }
    draw_text(
        dc,
        "DEVICE STATUS",
        30,
        25,
        280,
        54,
        15,
        600,
        color(50, 60, 62),
    );
    draw_text_center(
        dc,
        if connected { "LIVE" } else { "OFFLINE" },
        if connected { 339 } else { 321 },
        22,
        382,
        50,
        12,
        600,
        if connected {
            color(64, 132, 131)
        } else {
            color(91, 105, 107)
        },
    );

    if let Some((source, battery)) = current {
        draw_text(dc, &source, 125, 85, 392, 119, 20, 600, color(48, 55, 57));
        let headline = battery
            .percentage
            .map(|level| format!("{level}%"))
            .unwrap_or_else(|| {
                if battery.state == controller_battery_native::ChargeState::Charging {
                    "Charging".into()
                } else {
                    "Battery unavailable".into()
                }
            });
        draw_text(
            dc,
            &headline,
            125,
            120,
            392,
            184,
            if battery.percentage.is_some() { 48 } else { 24 },
            600,
            color(39, 47, 49),
        );
        if let Some(level) = battery.percentage {
            let track = unsafe { CreateSolidBrush(color(226, 234, 233)) };
            let line = Rect {
                left: scale(30),
                top: scale(195),
                right: client.right - scale(30),
                bottom: scale(199),
            };
            unsafe {
                FillRect(dc, &line, track);
                DeleteObject(track);
            }
            let fill = unsafe { CreateSolidBrush(color(89, 158, 156)) };
            let width = (client.right - scale(60)) * i32::from(level) / 100;
            let line = Rect {
                left: scale(30),
                top: scale(195),
                right: scale(30) + width,
                bottom: scale(199),
            };
            unsafe {
                FillRect(dc, &line, fill);
                DeleteObject(fill);
            }
        }
        draw_battery_status_icon(dc, battery.percentage, battery.state);
        let state_label = if source.starts_with("Flydigi")
            && battery.state == controller_battery_native::ChargeState::Unknown
        {
            "Check Flydigi Space Station battery data"
        } else {
            match battery.state {
                controller_battery_native::ChargeState::Charging => "Charging",
                controller_battery_native::ChargeState::Discharging => "On battery",
                controller_battery_native::ChargeState::Full => "Full",
                controller_battery_native::ChargeState::Flat => "Empty",
                controller_battery_native::ChargeState::Unknown => "Battery status unknown",
            }
        };
        draw_text(
            dc,
            state_label,
            65,
            218,
            392,
            249,
            15,
            500,
            color(79, 108, 110),
        );
    } else {
        draw_text(
            dc,
            "Disconnected",
            125,
            91,
            392,
            132,
            25,
            600,
            color(48, 55, 57),
        );
        draw_text(
            dc,
            "No controller detected",
            125,
            136,
            392,
            180,
            15,
            400,
            color(103, 116, 118),
        );
        draw_text(
            dc,
            "Connect a controller to view its battery",
            30,
            220,
            392,
            250,
            14,
            400,
            color(100, 113, 115),
        );
    }
    unsafe {
        EndPaint(hwnd, &ps);
    }
}

fn draw_controller_icon(dc: Handle, source: Option<&str>) {
    let file = if source.is_some() {
        match controller_glyph(source) {
            "flydigi-apex" => "window-flydigi-apex-connected-light.ico",
            "dualsense" => "window-dualsense-connected-light.ico",
            _ => "window-generic-connected-light.ico",
        }
    } else {
        "window-dualsense-disconnected-light.ico"
    };
    let Some(path) = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|directory| directory.join(file)))
    else {
        return;
    };
    let icon = unsafe {
        LoadImageW(
            ptr::null_mut(),
            wide(&path.to_string_lossy()).as_ptr(),
            1,
            scale(96),
            scale(96),
            0x10,
        )
    };
    if icon.is_null() {
        return;
    }
    unsafe {
        DrawIconEx(
            dc,
            scale(13),
            scale(89),
            icon,
            scale(96),
            scale(96),
            0,
            ptr::null_mut(),
            3,
        );
        DestroyIcon(icon);
    }
}

fn draw_battery_status_icon(
    dc: Handle,
    percentage: Option<u8>,
    state: controller_battery_native::ChargeState,
) {
    let name = if state == controller_battery_native::ChargeState::Charging {
        "battery-charging"
    } else {
        match percentage {
            Some(0) | None => "battery",
            Some(1..=20) => "battery-low",
            Some(21..=70) => "battery-medium",
            Some(_) => "battery-full",
        }
    };
    let Some(path) = std::env::current_exe().ok().and_then(|exe| {
        exe.parent()
            .map(|directory| directory.join(format!("lucide-{name}.ico")))
    }) else {
        return;
    };
    let icon = unsafe {
        LoadImageW(
            ptr::null_mut(),
            wide(&path.to_string_lossy()).as_ptr(),
            1,
            scale(24),
            scale(24),
            0x10,
        )
    };
    if icon.is_null() {
        return;
    }
    unsafe {
        DrawIconEx(
            dc,
            scale(30),
            scale(222),
            icon,
            scale(24),
            scale(24),
            0,
            ptr::null_mut(),
            3,
        );
        DestroyIcon(icon);
    }
}

fn controller_glyph(source: Option<&str>) -> &'static str {
    match source {
        Some(name) if name.starts_with("Flydigi APEX") => "flydigi-apex",
        Some(name) if name.starts_with("DualSense") => "dualsense",
        Some(_) => "generic",
        None => "dualsense",
    }
}

#[allow(clippy::too_many_arguments)] // GDI helper keeps each call site legible as coordinates.
fn draw_text(
    dc: Handle,
    text: &str,
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
    size: i32,
    weight: i32,
    ink: u32,
) {
    draw_text_flags(dc, text, left, top, right, bottom, size, weight, ink, 0x24);
}

#[allow(clippy::too_many_arguments)]
fn draw_text_center(
    dc: Handle,
    text: &str,
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
    size: i32,
    weight: i32,
    ink: u32,
) {
    draw_text_flags(dc, text, left, top, right, bottom, size, weight, ink, 0x25);
}

#[allow(clippy::too_many_arguments)]
fn draw_text_flags(
    dc: Handle,
    text: &str,
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
    size: i32,
    weight: i32,
    ink: u32,
    flags: u32,
) {
    let font = unsafe {
        CreateFontW(
            -scale(size),
            0,
            0,
            0,
            weight,
            0,
            0,
            0,
            1,
            0,
            0,
            5,
            0,
            wide("Segoe UI").as_ptr(),
        )
    };
    let old = unsafe { SelectObject(dc, font) };
    unsafe {
        SetTextColor(dc, ink);
    }
    let mut rect = Rect {
        left: scale(left),
        top: scale(top),
        right: scale(right),
        bottom: scale(bottom),
    };
    let text = wide(text);
    unsafe {
        DrawTextW(dc, text.as_ptr(), -1, &mut rect, flags);
        SelectObject(dc, old);
        DeleteObject(font);
    }
}

#[cfg(test)]
mod tests {
    use super::controller_glyph;

    #[test]
    fn connected_controllers_have_distinct_glyphs() {
        assert_eq!(controller_glyph(Some("DualSense USB")), "dualsense");
        assert_eq!(controller_glyph(Some("Flydigi APEX 4")), "flydigi-apex");
        assert_eq!(controller_glyph(Some("XInput 0")), "generic");
    }
}
