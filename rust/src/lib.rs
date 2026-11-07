//! Pure report decoding shared by the native Windows tray implementation.
//! Device handles remain read-only and shared; this module never sends input.

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ChargeState {
    Discharging,
    Charging,
    Full,
    Flat,
    Unknown,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BatteryState {
    pub percentage: Option<u8>,
    pub state: ChargeState,
}

pub fn parse_dualsense_report(report: &[u8]) -> Option<BatteryState> {
    let offset = match (report.len(), report.first().copied()) {
        (64, Some(0x01)) => 1,
        (78, Some(0x31))
            if bluetooth_crc(&report[..74])
                == u32::from_le_bytes(report[74..78].try_into().ok()?) =>
        {
            2
        }
        _ => return None,
    };
    let value = *report.get(offset + 52)?;
    let charge = value & 0x0f;
    let percentage = (charge.saturating_mul(10).saturating_add(5)).min(100);
    Some(match value >> 4 {
        0 => BatteryState {
            percentage: Some(percentage),
            state: ChargeState::Discharging,
        },
        1 => BatteryState {
            percentage: Some(percentage),
            state: ChargeState::Charging,
        },
        2 => BatteryState {
            percentage: Some(100),
            state: ChargeState::Full,
        },
        15 => BatteryState {
            percentage: Some(0),
            state: ChargeState::Flat,
        },
        _ => BatteryState {
            percentage: None,
            state: ChargeState::Unknown,
        },
    })
}

pub fn bluetooth_crc(data: &[u8]) -> u32 {
    let mut crc = update_crc(u32::MAX, 0xa1);
    for &byte in data {
        crc = update_crc(crc, byte);
    }
    !crc
}

pub fn reports_progressed(first: &[u8], second: &[u8]) -> bool {
    let offset = match (
        first.len(),
        first.first().copied(),
        second.len(),
        second.first().copied(),
    ) {
        (64, Some(1), 64, Some(1)) => 1,
        (78, Some(0x31), 78, Some(0x31)) => 2,
        _ => return false,
    };
    if parse_dualsense_report(first).is_none() || parse_dualsense_report(second).is_none() {
        return false;
    }
    let sequence = offset + 6;
    let timestamp = offset + 27;
    first[sequence] != second[sequence]
        || first[timestamp..timestamp + 4] != second[timestamp..timestamp + 4]
}

fn update_crc(mut crc: u32, byte: u8) -> u32 {
    crc ^= u32::from(byte);
    for _ in 0..8 {
        crc = (crc >> 1) ^ if crc & 1 != 0 { 0xedb8_8320 } else { 0 };
    }
    crc
}

pub fn parse_xinput_battery(kind: u8, level: u8) -> Option<BatteryState> {
    match kind {
        0 => None,
        1 => Some(BatteryState {
            percentage: None,
            state: ChargeState::Unknown,
        }),
        _ => Some(BatteryState {
            percentage: match level {
                0 => Some(0),
                1 => Some(25),
                2 => Some(50),
                3 => Some(100),
                _ => None,
            },
            state: match level {
                0 => ChargeState::Flat,
                1..=3 => ChargeState::Discharging,
                _ => ChargeState::Unknown,
            },
        }),
    }
}

pub mod features;
pub mod flydigi;
#[cfg(windows)]
pub mod windows_probe;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn usb_charge_is_not_reported_as_full() {
        let mut report = [0_u8; 64];
        report[0] = 1;
        report[53] = 0x14;
        assert_eq!(
            parse_dualsense_report(&report),
            Some(BatteryState {
                percentage: Some(45),
                state: ChargeState::Charging,
            })
        );
    }

    #[test]
    fn bluetooth_rejects_bad_crc() {
        let mut report = [0_u8; 78];
        report[0] = 0x31;
        report[54] = 0x06;
        assert_eq!(parse_dualsense_report(&report), None);
        let crc = bluetooth_crc(&report[..74]);
        report[74..].copy_from_slice(&crc.to_le_bytes());
        assert_eq!(
            parse_dualsense_report(&report).unwrap().percentage,
            Some(65)
        );
    }

    #[test]
    fn wired_xinput_does_not_invent_full_charge() {
        assert_eq!(parse_xinput_battery(1, 3).unwrap().percentage, None);
    }

    #[test]
    fn unchanged_reports_are_not_live() {
        let mut first = [0u8; 64];
        first[0] = 1;
        assert!(!reports_progressed(&first, &first));
        let mut next = first;
        next[7] = 1;
        assert!(reports_progressed(&first, &next));
    }
}
