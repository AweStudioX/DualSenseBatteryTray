#![windows_subsystem = "windows"]

#[cfg(windows)]
mod tray;
#[cfg(windows)]
mod watcher;

fn main() {
    let arguments: Vec<String> = std::env::args().collect();
    if arguments.iter().any(|arg| arg == "--watcher") {
        watcher::run();
    } else {
        tray::run(
            arguments.iter().any(|arg| arg == "--show"),
            !arguments.iter().any(|arg| arg == "--background"),
        );
    }
}
