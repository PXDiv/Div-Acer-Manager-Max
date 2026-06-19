#!/usr/bin/env python3
"""
KeyboardMonitor - Simple keyboard monitor that launches GUI as regular user
"""

import os
import struct
import select
import subprocess
import threading
import logging
import time
import pwd
from pathlib import Path

# Determine if we're on 64-bit system
import platform
IS_64BIT = platform.machine().endswith('64')
EVENT_SIZE = 24 if IS_64BIT else 16

# Event types and codes
EV_KEY = 1
KEY_PRESS = 1
TARGET_KEYCODE = 425
ACER_WMI_DEVICE_NAME = "acer wmi hotkeys"

class KeyboardMonitor:
    def __init__(self, target_keycode=TARGET_KEYCODE, command_to_run="/opt/damx/gui/DivAcerManagerMax", logger=None):
        self.target_keycode = target_keycode
        self.command_to_run = command_to_run
        self.running = False
        self.device_path = None
        self.monitor_thread = None
        self.log = logger or logging.getLogger("KeyboardMonitor")
        
    def find_keyboard_device(self):
        """Find the input device that emits the Nitro/PredatorSense key."""
        try:
            devices_path = Path("/proc/bus/input/devices")
            if not devices_path.exists():
                self.log.error("Cannot access /proc/bus/input/devices")
                return None
                
            with open(devices_path, 'r') as f:
                content = f.read()
                
            devices = content.split('\n\n')
            fallback_device = None
            
            for device in devices:
                lines = device.strip().split('\n')
                if not lines:
                    continue
                    
                is_keyboard = False
                is_acer_wmi = False
                event_num = None
                
                for line in lines:
                    line = line.strip()
                    line_lower = line.lower()
                    if ACER_WMI_DEVICE_NAME in line_lower:
                        is_acer_wmi = True
                    if 'keyboard' in line_lower:
                        is_keyboard = True
                    elif line.startswith('H:') and 'event' in line:
                        import re
                        match = re.search(r'event(\d+)', line)
                        if match:
                            event_num = match.group(1)
                
                if event_num:
                    device_path = f"/dev/input/event{event_num}"
                    if is_acer_wmi and os.path.exists(device_path):
                        self.log.info(f"Found Acer WMI hotkeys device: {device_path}")
                        return device_path

                    if is_keyboard and os.path.exists(device_path) and fallback_device is None:
                        fallback_device = device_path

            if fallback_device:
                self.log.warning(f"Acer WMI hotkeys device not found, falling back to keyboard device: {fallback_device}")
                return fallback_device
                        
        except Exception as e:
            self.log.error(f"Error finding keyboard device: {e}")
            
        return None
    
    def execute_command(self):
        """Execute the GUI command using systemd-run for proper user context"""
        try:
            # Get the current desktop user (most reliable method)
            user = os.environ.get('SUDO_USER') or self.get_console_user()
            if not user:
                self.log.error("Could not determine user to run command")
                return False

            user_info = pwd.getpwnam(user)
            runtime_dir = f"/run/user/{user_info.pw_uid}"

            # Get the user's environment
            env = os.environ.copy()
            
            # Add essential environment variables
            env.update({
                'DISPLAY': ':0',
                'XAUTHORITY': f'/home/{user}/.Xauthority',
                'DBUS_SESSION_BUS_ADDRESS': f'unix:path={runtime_dir}/bus'
            })

            # Try running as the user with proper environment
            cmd = [
                'sudo', '-u', user,
                'env',
                f'DISPLAY={env["DISPLAY"]}',
                f'XAUTHORITY={env["XAUTHORITY"]}',
                f'DBUS_SESSION_BUS_ADDRESS={env["DBUS_SESSION_BUS_ADDRESS"]}',
                self.command_to_run
            ]
            
            subprocess.Popen(cmd, 
                            stdout=subprocess.DEVNULL, 
                            stderr=subprocess.DEVNULL,
                            start_new_session=True)
            self.log.info(f"Executed as user {user}: {self.command_to_run}")
            return True
            
        except Exception as e:
            self.log.error(f"Failed to execute command: {e}")
            return False
    
    def get_console_user(self):
        """Get the user currently logged into the console"""
        try:
            # Check who is on the console
            result = subprocess.run(['who'], capture_output=True, text=True)
            for line in result.stdout.splitlines():
                if 'console' in line or ':0' in line:
                    return line.split()[0]
            
            # Fallback to first user in who output
            if result.stdout.strip():
                return result.stdout.splitlines()[0].split()[0]
                
        except:
            pass
        return None
    
    def monitor_events(self):
        """Monitor keyboard events"""
        if not self.device_path:
            self.log.error("No device path set")
            return
            
        try:
            with open(self.device_path, 'rb') as device:
                self.log.info(f"Monitoring {self.device_path} for keycode {self.target_keycode}")
                
                while self.running:
                    ready, _, _ = select.select([device], [], [], 1.0)
                    
                    if not ready:
                        continue
                        
                    data = device.read(EVENT_SIZE)
                    if len(data) != EVENT_SIZE:
                        continue
                    
                    if IS_64BIT:
                        _, _, event_type, code, value = struct.unpack('QQHHi', data)
                    else:
                        _, _, event_type, code, value = struct.unpack('IIHHi', data)
                    
                    if (event_type == EV_KEY and 
                        code == self.target_keycode and 
                        value == KEY_PRESS):
                        
                        self.log.info(f"Target keycode {self.target_keycode} pressed!")
                        self.execute_command()
                        
        except PermissionError:
            self.log.error(f"Permission denied accessing {self.device_path}. Run as root.")
        except Exception as e:
            self.log.error(f"Error monitoring events: {e}")
    
    def start_monitoring(self):
        """Start monitoring"""
        if self.running:
            return False
            
        self.device_path = self.find_keyboard_device()
        if not self.device_path:
            return False
            
        self.running = True
        self.monitor_thread = threading.Thread(target=self.monitor_events, daemon=True)
        self.monitor_thread.start()
        
        self.log.info("Keyboard monitoring started")
        return True
    
    def stop_monitoring(self):
        """Stop monitoring"""
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
