fn main() {
    for detection in controller_battery_native::windows_probe::probe() {
        println!("{}: {:?}", detection.source, detection.battery);
    }
}
