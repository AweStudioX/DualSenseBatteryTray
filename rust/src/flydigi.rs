//! Narrow decoder for the Flydigi Connect service response.
use crate::{BatteryState, ChargeState};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FlydigiDevice {
    pub name: String,
    pub code: String,
    pub connection_type: u64,
    pub connected: bool,
    pub level: Option<u64>,
}

pub fn normalize(device: &FlydigiDevice) -> BatteryState {
    if !device.connected {
        return BatteryState {
            percentage: None,
            state: ChargeState::Unknown,
        };
    }
    match device.level {
        Some(6) => BatteryState {
            percentage: None,
            state: ChargeState::Charging,
        },
        Some(level @ 0..=5) => BatteryState {
            percentage: Some([5, 20, 40, 60, 80, 100][level as usize]),
            state: if level == 0 {
                ChargeState::Flat
            } else {
                ChargeState::Discharging
            },
        },
        _ => BatteryState {
            percentage: None,
            state: ChargeState::Unknown,
        },
    }
}

enum Field<'a> {
    Integer(u32, u64),
    Bytes(u32, &'a [u8]),
}

fn varint(data: &[u8], offset: &mut usize) -> Option<u64> {
    let mut value = 0u64;
    for shift in (0..64).step_by(7) {
        let byte = *data.get(*offset)?;
        *offset += 1;
        if shift == 63 && byte > 1 {
            return None;
        }
        value |= u64::from(byte & 0x7f) << shift;
        if byte & 0x80 == 0 {
            return Some(value);
        }
    }
    None
}

fn fields(mut data: &[u8]) -> Option<Vec<Field<'_>>> {
    let mut result = Vec::new();
    while !data.is_empty() {
        let mut offset = 0;
        let tag = varint(data, &mut offset)?;
        let number = u32::try_from(tag >> 3).ok()?;
        if number == 0 {
            return None;
        }
        match tag & 7 {
            0 => result.push(Field::Integer(number, varint(data, &mut offset)?)),
            1 => {
                offset = offset.checked_add(8)?;
                if offset > data.len() {
                    return None;
                }
            }
            2 => {
                let length = usize::try_from(varint(data, &mut offset)?).ok()?;
                let end = offset.checked_add(length)?;
                result.push(Field::Bytes(number, data.get(offset..end)?));
                offset = end;
            }
            5 => {
                offset = offset.checked_add(4)?;
                if offset > data.len() {
                    return None;
                }
            }
            _ => return None,
        }
        data = data.get(offset..)?;
    }
    Some(result)
}

pub fn parse_response(data: &[u8]) -> Option<(u64, Vec<FlydigiDevice>)> {
    let mut command = 0;
    let mut devices = Vec::new();
    for field in fields(data)? {
        match field {
            Field::Integer(1, value) => command = value,
            Field::Bytes(5 | 6, container) => {
                for child in fields(container)? {
                    if let Field::Bytes(1, raw) = child {
                        devices.push(parse_device(raw)?);
                    }
                }
            }
            _ => {}
        }
    }
    Some((command, devices))
}

fn parse_device(data: &[u8]) -> Option<FlydigiDevice> {
    let mut device = FlydigiDevice {
        name: String::new(),
        code: String::new(),
        connection_type: 0,
        connected: false,
        level: None,
    };
    for field in fields(data)? {
        match field {
            Field::Bytes(1, value) => device.name = String::from_utf8_lossy(value).into_owned(),
            Field::Bytes(4, value) => device.code = String::from_utf8_lossy(value).into_owned(),
            Field::Integer(3, value) => device.connection_type = value,
            Field::Integer(8, value) => device.connected = value != 0,
            Field::Integer(9, value) => device.level = Some(value),
            _ => {}
        }
    }
    Some(device)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn charging_does_not_invent_percentage() {
        let device = FlydigiDevice {
            name: "APEX 4".into(),
            code: "k2".into(),
            connection_type: 1,
            connected: true,
            level: Some(6),
        };
        assert_eq!(
            normalize(&device),
            BatteryState {
                percentage: None,
                state: ChargeState::Charging
            }
        );
    }
    #[test]
    fn parses_service_fixture() {
        let device = [
            0x0a, 4, b'A', b'P', b'E', b'X', 0x20, 1, 0x22, 2, b'k', b'2', 0x40, 1, 0x48, 4,
        ];
        let mut response = vec![
            0x08,
            0x81,
            0x20,
            0x2a,
            (device.len() + 2) as u8,
            0x0a,
            device.len() as u8,
        ];
        response.extend_from_slice(&device);
        let (command, devices) = parse_response(&response).unwrap();
        assert_eq!(command, 4097);
        assert_eq!(devices[0].code, "k2");
        assert_eq!(normalize(&devices[0]).percentage, Some(80));
    }
}
