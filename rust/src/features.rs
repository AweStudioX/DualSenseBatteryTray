//! Small, platform-independent rules for the native shell.

use crate::{BatteryState, ChargeState};

#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub enum TrayLayout {
    Compact,
    #[default]
    TwoIcons,
}

impl TrayLayout {
    pub fn parse(value: &str) -> Self {
        if value.trim() == "Compact" {
            Self::Compact
        } else {
            Self::TwoIcons
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Compact => "Compact",
            Self::TwoIcons => "TwoIcons",
        }
    }
}

/// Matches the released app: one alert on crossing each threshold while discharging.
#[derive(Default)]
pub struct LowBatteryAlerts {
    last_percentage: Option<u8>,
    alerted_20: bool,
    alerted_10: bool,
}

impl LowBatteryAlerts {
    pub fn observe(&mut self, battery: Option<BatteryState>) -> Option<u8> {
        let Some(BatteryState {
            percentage: Some(level),
            state: ChargeState::Discharging,
        }) = battery
        else {
            *self = Self::default();
            return None;
        };
        if self
            .last_percentage
            .is_some_and(|previous| level > previous)
        {
            if level > 20 {
                self.alerted_20 = false;
            }
            if level > 10 {
                self.alerted_10 = false;
            }
        }
        self.last_percentage = Some(level);
        if level <= 10 && !self.alerted_10 {
            self.alerted_10 = true;
            self.alerted_20 = true;
            return Some(10);
        }
        if level <= 20 && !self.alerted_20 {
            self.alerted_20 = true;
            return Some(20);
        }
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn battery(level: u8, state: ChargeState) -> BatteryState {
        BatteryState {
            percentage: Some(level),
            state,
        }
    }

    #[test]
    fn layout_defaults_to_two_icons_for_unknown_data() {
        assert_eq!(TrayLayout::parse("Compact\n"), TrayLayout::Compact);
        assert_eq!(TrayLayout::parse("corrupt"), TrayLayout::TwoIcons);
    }

    #[test]
    fn alerts_once_per_threshold_and_resets_after_charge() {
        let mut alerts = LowBatteryAlerts::default();
        assert_eq!(
            alerts.observe(Some(battery(25, ChargeState::Discharging))),
            None
        );
        assert_eq!(
            alerts.observe(Some(battery(20, ChargeState::Discharging))),
            Some(20)
        );
        assert_eq!(
            alerts.observe(Some(battery(20, ChargeState::Discharging))),
            None
        );
        assert_eq!(
            alerts.observe(Some(battery(10, ChargeState::Discharging))),
            Some(10)
        );
        assert_eq!(
            alerts.observe(Some(battery(9, ChargeState::Charging))),
            None
        );
        assert_eq!(
            alerts.observe(Some(battery(10, ChargeState::Discharging))),
            Some(10)
        );
    }
}
