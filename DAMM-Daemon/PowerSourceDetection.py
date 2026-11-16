#!/usr/bin/env python3
# DAMX Power Source Detection - Monitors power source and adjusts thermal profiles accordingly

import os
import logging
import subprocess
from threading import Timer
from typing import Optional

# Get logger from main daemon
log = logging.getLogger("DAMXDaemon")

class PowerSourceDetector:
    """Detects power source and manages automatic mode switching"""

    def __init__(self, manager):
        self.manager = manager
        self.current_source = None
        self.check_interval = 5  # seconds
        self.timer = None
        
        log.info("PowerSourceDetector initialized")
        
        self.possible_power_supply_paths = [
            "/sys/class/power_supply/AC/online",
            "/sys/class/power_supply/ACAD/online",
            "/sys/class/power_supply/ADP1/online",
            "/sys/class/power_supply/AC0/online"
        ]

    def start_monitoring(self):
        """Start periodic power source checking"""
        self.check_power_source()
        log.info("Monitoring power source started")

    def stop_monitoring(self):
        """Stop periodic power source checking"""
        if self.timer:
            self.timer.cancel()

    def check_power_source(self):
        """Check current power source and adjust settings if needed"""
        is_plugged_in = self._is_ac_connected()

        # Only take action if power state changed
        if is_plugged_in is not None and is_plugged_in != self.current_source:
            self.current_source = is_plugged_in
            self._handle_power_change(is_plugged_in)

        # Schedule next check
        self.timer = Timer(self.check_interval, self.check_power_source)
        self.timer.daemon = True
        self.timer.start()

    def _is_ac_connected(self) -> Optional[bool]:
        """Check if AC power is connected"""
        online_statuses = []

        try:
            # Discover AC adapters by their sysfs type rather than their name.
            for entry in os.scandir("/sys/class/power_supply"):
                try:
                    supply_type = self._read_status(os.path.join(entry.path, "type"))
                    if supply_type != "Mains":
                        continue

                    status = self._read_status(os.path.join(entry.path, "online"))
                    if status in ("0", "1"):
                        online_statuses.append(status == "1")
                except OSError as e:
                    log.warning(f"Could not read power supply status at {entry.path}: {e}")
        except OSError as e:
            log.debug(f"Could not scan power supplies: {e}")

        if online_statuses:
            return any(online_statuses)

        try:
            # Keep compatibility with systems that expose online without type.
            for path in self.possible_power_supply_paths:
                if os.path.exists(path):
                    status = self._read_status(path)
                    if status in ("0", "1"):
                        return status == "1"

            # If no power supply file is found, try command-line tools
            upower_status = self._check_using_upower()
            return upower_status if upower_status is not None else self._check_using_acpi()

        except Exception as e:
            log.error(f"Error checking power status: {e}")
            return None

    @staticmethod
    def _read_status(path: str) -> str:
        with open(path, "r") as status_file:
            return status_file.read().strip()

    def _check_using_upower(self) -> Optional[bool]:
        """Check power status using upower"""
        try:
            result = subprocess.run(
                ["upower", "-i", "/org/freedesktop/UPower/devices/line_power_AC"],
                capture_output=True,
                text=True,
                check=True
            )
            output = result.stdout.lower()
            if "online: yes" in output:
                return True
            if "online: no" in output:
                return False
            return None
        except (subprocess.CalledProcessError, FileNotFoundError) as e:
            log.warning(f"upower check failed: {e}")
            return None

    def _check_using_acpi(self) -> Optional[bool]:
        """Check power status using acpi"""
        try:
            result = subprocess.run(
                ["acpi", "-a"],
                capture_output=True,
                text=True,
                check=True
            )
            output = result.stdout.lower()
            if "on-line" in output:
                return True
            if "off-line" in output:
                return False
            return None
        except (subprocess.CalledProcessError, FileNotFoundError) as e:
            log.warning(f"acpi check failed: {e}")
            return None

    def _handle_power_change(self, is_plugged_in: bool):
        """Handle power source changes"""
        if not hasattr(self.manager, 'available_features') or "thermal_profile" not in self.manager.available_features:
            return

        current_profile = self.manager.get_thermal_profile()
        available_profiles = self.manager.get_thermal_profile_choices()

        if is_plugged_in:
            # On AC power - no restrictions
            log.info("Switched to AC power")
        else:
            # On battery power - enforce balanced or eco mode
            log.info("Switched to battery power")

            if current_profile not in ["balanced", "quiet", "power-saver"]:
                # If current profile isn't battery-friendly, switch to balanced
                if "balanced" in available_profiles:
                    log.info("Auto-switching to balanced mode for battery power")
                    self.manager.set_thermal_profile("balanced")
                elif "quiet" in available_profiles:
                    log.info("Auto-switching to quiet mode for battery power")
                    self.manager.set_thermal_profile("quiet")
                elif "power-saver" in available_profiles:
                    log.info("Auto-switching to power-saver mode for battery power")
                    self.manager.set_thermal_profile("power-saver")
                else:
                    log.warning("No battery-friendly thermal profile available")