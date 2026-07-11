#!/usr/bin/env python3
"""
KeyboardMonitor - Launches DAMX GUI when configured special keys are pressed.
"""

import os
import pwd
import re
import struct
import select
import subprocess
import threading
import logging
import time
from pathlib import Path

import platform

IS_64BIT = platform.machine().endswith('64')
EVENT_SIZE = 24 if IS_64BIT else 16

EV_KEY = 1
KEY_PRESS = 1

DEFAULT_KEYCODES = [202, 425]
DEFAULT_COMMAND = "/opt/damx/gui/DivAcerManagerMax"


class KeyboardMonitor:
    def __init__(
        self,
        target_keycodes=None,
        command_to_run=DEFAULT_COMMAND,
        device_path=None,
        logger=None,
    ):
        self.target_keycodes = set(target_keycodes or DEFAULT_KEYCODES)
        self.command_to_run = command_to_run
        self.configured_device_path = device_path
        self.running = False
        self.device_paths = []
        self.monitor_thread = None
        self.log = logger or logging.getLogger("KeyboardMonitor")
        self._last_launch = 0.0
        self._launch_cooldown = 1.0

    def find_input_devices(self):
        """Find input devices that may emit Acer special keys."""
        if self.configured_device_path:
            path = self.configured_device_path
            if os.path.exists(path):
                return [path]
            self.log.error(f"Configured input device not found: {path}")
            return []

        devices_path = Path("/proc/bus/input/devices")
        if not devices_path.exists():
            self.log.error("Cannot access /proc/bus/input/devices")
            return []

        try:
            blocks = devices_path.read_text().split('\n\n')
        except OSError as exc:
            self.log.error(f"Error reading input devices: {exc}")
            return []

        acer_devices = []
        keyboard_devices = []
        skip_names = (
            'sleep button',
            'power button',
            'lid switch',
            'video bus',
            'pc speaker',
        )

        for block in blocks:
            if not block.strip():
                continue

            name = ""
            handlers = ""
            has_key_events = False

            for line in block.strip().split('\n'):
                line = line.strip()
                if line.startswith('N:'):
                    name = line[2:].strip().strip('"')
                elif line.startswith('H:'):
                    handlers = line
                elif line.startswith('B: EV=') and '3' in line.split('=')[1]:
                    has_key_events = True

            if 'kbd' not in handlers or not has_key_events:
                continue

            if any(skip in name.lower() for skip in skip_names):
                continue

            match = re.search(r'event(\d+)', handlers)
            if not match:
                continue

            device_path = f"/dev/input/event{match.group(1)}"
            if not os.path.exists(device_path):
                continue

            if 'acer' in name.lower():
                acer_devices.append(device_path)
            else:
                keyboard_devices.append(device_path)

        devices = []
        for device_path in acer_devices + keyboard_devices:
            if device_path not in devices:
                devices.append(device_path)

        return devices

    def get_console_user(self):
        """Get the user currently logged into the graphical session."""
        try:
            result = subprocess.run(['loginctl', 'list-sessions', '--no-legend'],
                                    capture_output=True, text=True, check=False)
            for line in result.stdout.splitlines():
                parts = line.split()
                if len(parts) < 3:
                    continue
                session_id, _uid, user = parts[0], parts[1], parts[2]
                session_info = subprocess.run(
                    ['loginctl', 'show-session', session_id, '-p', 'Active', '-p', 'Type'],
                    capture_output=True, text=True, check=False
                )
                if 'Active=yes' in session_info.stdout and 'Type=wayland' in session_info.stdout:
                    return user
                if 'Active=yes' in session_info.stdout and 'Type=x11' in session_info.stdout:
                    return user
        except Exception:
            pass

        try:
            result = subprocess.run(['who'], capture_output=True, text=True, check=False)
            for line in result.stdout.splitlines():
                if '(:' in line or 'tty' in line:
                    return line.split()[0]
        except Exception:
            pass

        return os.environ.get('SUDO_USER')

    def build_user_env(self, user):
        """Build a desktop session environment for the target user."""
        env = {'HOME': pwd.getpwnam(user).pw_dir}
        uid = pwd.getpwnam(user).pw_uid
        runtime_dir = Path(f'/run/user/{uid}')

        if runtime_dir.exists():
            env['XDG_RUNTIME_DIR'] = str(runtime_dir)
            bus = runtime_dir / 'bus'
            if bus.exists():
                env['DBUS_SESSION_BUS_ADDRESS'] = f'unix:path={bus}'

            for wayland_socket in sorted(runtime_dir.glob('wayland-*')):
                env['WAYLAND_DISPLAY'] = wayland_socket.name
                break

        env.setdefault('DISPLAY', ':0')
        return env

    def is_gui_running(self):
        try:
            result = subprocess.run(
                ['pgrep', '-f', 'DivAcerManagerMax'],
                capture_output=True,
                check=False,
            )
            return result.returncode == 0
        except Exception:
            return False

    def execute_command(self):
        """Execute the GUI command in the active desktop session."""
        now = time.time()
        if now - self._last_launch < self._launch_cooldown:
            return False

        if self.is_gui_running():
            self.log.info("DAMX GUI is already running")
            return False

        user = os.environ.get('SUDO_USER') or self.get_console_user()
        if not user:
            self.log.error("Could not determine user to run command")
            return False

        try:
            env = self.build_user_env(user)
            cmd = ['runuser', '-u', user, '--']
            for key, value in env.items():
                cmd.extend([f'{key}={value}'])
            cmd.append(self.command_to_run)

            subprocess.Popen(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True,
            )
            self._last_launch = now
            self.log.info(f"Launched DAMX for user {user}: {self.command_to_run}")
            return True
        except Exception as exc:
            self.log.error(f"Failed to execute command: {exc}")
            return False

    def monitor_events(self):
        """Monitor keyboard events from one or more input devices."""
        if not self.device_paths:
            self.log.error("No input devices to monitor")
            return

        fds = {}
        try:
            for device_path in self.device_paths:
                fds[open(device_path, 'rb')] = device_path

            keycodes = sorted(self.target_keycodes)
            paths = ', '.join(self.device_paths)
            self.log.info(f"Monitoring {paths} for keycodes {keycodes}")

            while self.running:
                readable, _, _ = select.select(list(fds.keys()), [], [], 1.0)
                if not readable:
                    continue

                for device in readable:
                    data = device.read(EVENT_SIZE)
                    if len(data) != EVENT_SIZE:
                        continue

                    if IS_64BIT:
                        _, _, event_type, code, value = struct.unpack('QQHHi', data)
                    else:
                        _, _, event_type, code, value = struct.unpack('IIHHi', data)

                    if (event_type == EV_KEY and
                            code in self.target_keycodes and
                            value == KEY_PRESS):
                        self.log.info(
                            f"Keycode {code} pressed on {fds[device]}, launching DAMX"
                        )
                        self.execute_command()
        except PermissionError:
            self.log.error("Permission denied accessing input devices. Run daemon as root.")
        except Exception as exc:
            self.log.error(f"Error monitoring events: {exc}")
        finally:
            for device in fds:
                try:
                    device.close()
                except Exception:
                    pass

    def start_monitoring(self):
        """Start monitoring."""
        if self.running:
            return False

        self.device_paths = self.find_input_devices()
        if not self.device_paths:
            return False

        self.running = True
        self.monitor_thread = threading.Thread(target=self.monitor_events, daemon=True)
        self.monitor_thread.start()
        self.log.info("Keyboard monitoring started")
        return True

    def stop_monitoring(self):
        """Stop monitoring."""
        self.running = False
        if self.monitor_thread:
            self.monitor_thread.join(timeout=2.0)


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO,
                        format='%(asctime)s - %(levelname)s - %(message)s')

    monitor = KeyboardMonitor()
    if monitor.start_monitoring():
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            monitor.stop_monitoring()
    else:
        print("Failed to start monitoring")
