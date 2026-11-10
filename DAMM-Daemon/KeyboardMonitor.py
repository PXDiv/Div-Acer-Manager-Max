#!/usr/bin/env python3
"""
KeyboardMonitor - Built-in Acer Nitro/Predator Hotkey and Turbo Button Monitor
Handles KEY_PROG1 (NitroSense Key, 148/425) and KEY_PROG2 (Gaming Turbo Key, 149/136)
directly within DAMX-Daemon.
"""

import os
import glob
import struct
import select
import subprocess
import threading
import logging
import time
from pathlib import Path

IS_64BIT = struct.calcsize("P") == 8
EVENT_SIZE = 24 if IS_64BIT else 16

# Event types
EV_KEY = 1
KEY_PRESS = 1

# Key codes
KEY_NITROSENSE = 148   # KEY_PROG1 (NitroSense 'N' button)
KEY_NITRO_ALT = 425    # Alternate vendor keycode
KEY_TURBO = 149        # KEY_PROG2 (Acer Gaming Turbo / Thermal mode button)


class KeyboardMonitor:
    def __init__(self, manager=None, logger=None):
        self.manager = manager
        self.log = logger or logging.getLogger("KeyboardMonitor")
        self.running = False
        self.monitor_thread = None
        self.lock = threading.Lock()
        self.last_press_time = {}

    def find_target_user(self):
        """Find the active logged-in desktop user."""
        try:
            # 1. Check loginctl sessions
            result = subprocess.run(
                ['loginctl', 'list-sessions', '--no-legend'],
                capture_output=True, text=True, timeout=2
            )
            for line in result.stdout.strip().splitlines():
                parts = line.split()
                if len(parts) >= 3 and parts[2] not in ('root', 'gdm', 'sddm', 'lightdm'):
                    return parts[2]
        except Exception:
            pass

        # 2. Fallback to SUDO_USER or who
        user = os.environ.get('SUDO_USER')
        if user and user != 'root':
            return user

        try:
            result = subprocess.run(['who'], capture_output=True, text=True, timeout=2)
            for line in result.stdout.splitlines():
                parts = line.split()
                if parts and parts[0] != 'root':
                    return parts[0]
        except Exception:
            pass

        return None

    def get_user_session_env(self, target_user):
        """Extract graphical environment variables for the target user."""
        env = {
            'DISPLAY': ':0',
            'WAYLAND_DISPLAY': 'wayland-0',
        }
        try:
            uid_res = subprocess.run(['id', '-u', target_user], capture_output=True, text=True)
            uid = uid_res.stdout.strip()
            env['XDG_RUNTIME_DIR'] = f"/run/user/{uid}"
            env['DBUS_SESSION_BUS_ADDRESS'] = f"unix:path=/run/user/{uid}/bus"
        except Exception:
            env['XDG_RUNTIME_DIR'] = "/run/user/1000"
            env['DBUS_SESSION_BUS_ADDRESS'] = "unix:path=/run/user/1000/bus"

        # Search /proc for running graphical user process
        try:
            pids = subprocess.run(['pgrep', '-u', target_user], capture_output=True, text=True).stdout.split()
            for pid in pids[:10]:
                environ_path = f"/proc/{pid}/environ"
                if os.path.exists(environ_path):
                    try:
                        with open(environ_path, 'rb') as f:
                            raw = f.read().split(b'\0')
                            for entry in raw:
                                if entry.startswith(b'WAYLAND_DISPLAY='):
                                    env['WAYLAND_DISPLAY'] = entry.decode('utf-8', errors='ignore').split('=', 1)[1]
                                elif entry.startswith(b'DISPLAY='):
                                    env['DISPLAY'] = entry.decode('utf-8', errors='ignore').split('=', 1)[1]
                                elif entry.startswith(b'DBUS_SESSION_BUS_ADDRESS='):
                                    env['DBUS_SESSION_BUS_ADDRESS'] = entry.decode('utf-8', errors='ignore').split('=', 1)[1]
                                elif entry.startswith(b'XDG_RUNTIME_DIR='):
                                    env['XDG_RUNTIME_DIR'] = entry.decode('utf-8', errors='ignore').split('=', 1)[1]
                    except Exception:
                        continue
        except Exception:
            pass

        return env

    def launch_or_toggle_gui(self):
        """Launch or bring DAMX GUI to foreground."""
        target_user = self.find_target_user()
        if not target_user:
            self.log.error("Could not find active desktop user to launch DAMX GUI")
            return

        env = self.get_user_session_env(target_user)
        self.log.info(f"Launching DAMX GUI for user '{target_user}'...")

        # Check if already running
        try:
            pgrep_res = subprocess.run(['pgrep', '-f', 'DivAcerManagerMax'], capture_output=True, text=True)
            if pgrep_res.returncode == 0:
                self.log.info("DivAcerManagerMax is already running in background.")
                return
        except Exception:
            pass

        cmd = [
            'systemd-run',
            f'--machine={target_user}@.host',
            '--user',
            '/usr/bin/damx'
        ]

        try:
            subprocess.Popen(
                cmd,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True
            )
            self.log.info("DAMX GUI process spawned successfully.")
        except Exception as e:
            self.log.error(f"Failed to launch DAMX GUI: {e}")

    def is_on_ac(self):
        """Check if laptop is connected to AC charger."""
        for path in glob.glob("/sys/class/power_supply/*/online"):
            if any(name in path for name in ("ACAD", "ADP", "AC0", "AC")):
                try:
                    with open(path, "r") as f:
                        if f.read().strip() == "1":
                            return True
                except Exception:
                    pass
        return False

    def cycle_thermal_profile(self):
        """Cycle thermal profiles & set fan speeds natively."""
        if not self.manager:
            self.log.error("No DAMXManager attached to KeyboardMonitor")
            return

        with self.lock:
            on_ac = self.is_on_ac()
            current = self.manager.get_thermal_profile() or "balanced"
            current = current.strip().lower()

            if not on_ac:
                # Battery rotation: low-power <-> balanced
                if current == "low-power":
                    next_info = ("balanced", 0, 0, "Dengeli Mod", "battery-charging")
                else:
                    next_info = ("low-power", 0, 0, "ECO Modu (Pil Tasarrufu)", "battery-low")
            else:
                # AC rotation: quiet -> balanced -> balanced-performance -> performance -> quiet
                rotations = {
                    "quiet": ("balanced", 0, 0, "Dengeli Mod", "system-run"),
                    "balanced": ("balanced-performance", 75, 75, "Performans Modu", "speedometer"),
                    "balanced-performance": ("performance", 100, 100, "Turbo Modu", "dialog-warning"),
                    "performance": ("quiet", 0, 0, "Sessiz Mod", "audio-volume-muted"),
                }
                next_info = rotations.get(current, ("balanced", 0, 0, "Dengeli Mod", "system-run"))

            target_profile, fan_cpu, fan_gpu, title, icon = next_info
            self.log.info(f"Cycling Thermal Profile: '{current}' -> '{target_profile}' (Fans: {fan_cpu}%)")

            # Apply profile
            self.manager.set_thermal_profile(target_profile)
            if hasattr(self.manager, 'set_fan_speed'):
                self.manager.set_fan_speed(fan_cpu, fan_gpu)

            # Send desktop notification
            self.send_desktop_notification(f"Termal Mod: {title}", f"Fanlar: %{fan_cpu if fan_cpu > 0 else 'Otomatik'}", icon)

    def toggle_touchpad(self):
        """Toggle touchpad state."""
        target_user = self.find_target_user()
        if not target_user:
            return

        cmd = [
            'systemd-run',
            f'--machine={target_user}@.host',
            '--user',
            '/usr/local/bin/toggle-touchpad.sh'
        ]
        try:
            subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except Exception as e:
            self.log.error(f"Failed to toggle touchpad: {e}")

    def send_desktop_notification(self, title, message, icon="preferences-system"):
        """Send OSD notification to desktop user."""
        target_user = self.find_target_user()
        if not target_user:
            return

        cmd = [
            'systemd-run',
            f'--machine={target_user}@.host',
            '--user',
            'notify-send',
            '-a', 'DivAcerManagerMax',
            '-u', 'normal',
            '-t', '2000',
            '-i', icon,
            title,
            message
        ]
        try:
            subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except Exception:
            pass

    def find_keyboard_devices(self):
        """Find all keyboard and hotkey input event devices."""
        devices = []
        try:
            devices_path = Path("/proc/bus/input/devices")
            if not devices_path.exists():
                return devices

            with open(devices_path, "r") as f:
                content = f.read()

            for device_block in content.split("\n\n"):
                lines = [l.strip() for l in device_block.split("\n") if l.strip()]
                name = ""
                handlers = ""
                for line in lines:
                    if line.startswith("N: Name="):
                        name = line.split("=", 1)[1].strip('"')
                    elif line.startswith("H: Handlers="):
                        handlers = line.split("=", 1)[1]

                # Match Acer WMI, AT Keyboard, or other keyboards
                if any(kw in name.lower() for kw in ("acer", "keyboard", "wmi")):
                    for token in handlers.split():
                        if token.startswith("event"):
                            dev_path = f"/dev/input/{token}"
                            if os.path.exists(dev_path) and dev_path not in devices:
                                devices.append(dev_path)
                                self.log.info(f"Found input device '{name}': {dev_path}")
        except Exception as e:
            self.log.error(f"Error finding keyboard devices: {e}")

        return devices

    def monitor_loop(self):
        """Main event loop monitoring input devices."""
        device_paths = self.find_keyboard_devices()
        if not device_paths:
            self.log.error("No input devices found for monitoring")
            return

        file_descriptors = {}
        for path in device_paths:
            try:
                fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK)
                file_descriptors[fd] = path
            except Exception as e:
                self.log.warning(f"Could not open device {path}: {e}")

        if not file_descriptors:
            self.log.error("Failed to open any input devices")
            return

        self.log.info(f"Monitoring {len(file_descriptors)} input devices for NitroSense/Turbo keys...")

        try:
            while self.running:
                rlist, _, _ = select.select(list(file_descriptors.keys()), [], [], 0.5)
                for fd in rlist:
                    try:
                        data = os.read(fd, EVENT_SIZE * 8)
                        for i in range(0, len(data), EVENT_SIZE):
                            chunk = data[i:i + EVENT_SIZE]
                            if len(chunk) != EVENT_SIZE:
                                continue

                            if IS_64BIT:
                                _, _, event_type, code, value = struct.unpack("QQHHi", chunk)
                            else:
                                _, _, event_type, code, value = struct.unpack("IIHHi", chunk)

                            if event_type == EV_KEY and value == KEY_PRESS:
                                now = time.time()
                                if now - self.last_press_time.get(code, 0) < 0.3:
                                    continue  # Debounce 300ms
                                self.last_press_time[code] = now

                                if code in (KEY_NITROSENSE, KEY_NITRO_ALT):
                                    self.log.info(f"NitroSense Key detected (code {code})!")
                                    self.launch_or_toggle_gui()
                                elif code in (KEY_TURBO, 202, 203):
                                    self.log.info(f"Gaming Turbo Key detected (code {code})!")
                                    self.cycle_thermal_profile()
                                elif code in (530, 531, 532):
                                    self.log.info(f"Touchpad Key detected (code {code})!")
                                    self.toggle_touchpad()

                    except BlockingIOError:
                        continue
                    except Exception as e:
                        self.log.error(f"Error reading from device {file_descriptors.get(fd)}: {e}")
        finally:
            for fd in file_descriptors:
                try:
                    os.close(fd)
                except Exception:
                    pass

    def start_monitoring(self):
        """Start background keyboard monitoring thread."""
        if self.running:
            return True

        self.running = True
        self.monitor_thread = threading.Thread(target=self.monitor_loop, daemon=True, name="DAMX-KeyboardMonitor")
        self.monitor_thread.start()
        self.log.info("KeyboardMonitor thread started successfully.")
        return True

    def stop_monitoring(self):
        """Stop background keyboard monitoring thread."""
        self.running = False
        if self.monitor_thread and self.monitor_thread.is_alive():
            self.monitor_thread.join(timeout=2.0)
        self.log.info("KeyboardMonitor thread stopped.")
