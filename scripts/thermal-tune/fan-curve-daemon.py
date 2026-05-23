#!/usr/bin/env python3
"""
Acer Predator/Nitro Fan Curve Daemon
-------------------------------------
Two-mode fan control for shared-heatpipe laptops:

  INDEPENDENT MODE (both temps < 82°C):
    Each fan tracks its own component's temperature independently.
    CPU fan follows CPU temp, GPU fan follows GPU temp.

  COUPLED MODE (either temp ≥ 82°C):
    Both fans lock together at max(cpu_target, gpu_target).
    Single hysteresis tracker on max temp. No split-brain possible.

Mode transitions have their own hysteresis:
  - Enter COUPLED:  max temp ≥ COUPLED_ENTER (82°C)
  - Exit COUPLED:   max temp < COUPLED_EXIT  (77°C)
  This 5°C dead zone prevents bouncing between modes.
"""

import os
import sys
import csv
import time
import signal
import logging
import logging.handlers
import subprocess
from enum import Enum
from datetime import datetime

# ─── Configuration ───────────────────────────────────────────────────────────

POLL_INTERVAL = 2  # seconds between temp checks

# Fan curve: (temp_celsius, fan_speed_percent)
# Linearly interpolated. Below first point = that speed. Above last = 100%.
FAN_CURVE = [
    (50, 0),    # Below 50°C — EC auto
    (60, 30),   # 60°C — light spin
    (65, 40),   # 65°C — gentle
    (70, 50),   # 70°C — moderate
    (75, 65),   # 75°C — getting warm
    (80, 80),   # 80°C — aggressive
    (85, 95),   # 85°C — near max
    (88, 100),  # 88°C — full blast
]

# ─── Mode Transition Thresholds ──────────────────────────────────────────────
COUPLED_ENTER = 82  # °C — enter coupled mode when max temp hits this
COUPLED_EXIT  = 77  # °C — exit coupled mode when max temp drops below this
                     #       (5°C dead zone prevents mode bouncing)
COUPLED_HOLD_SECONDS = 15  # minimum seconds to stay in coupled mode
                            # prevents rapid bouncing from turbo boost spikes

# Rolling average window for temperature smoothing (filters turbo spikes)
TEMP_SMOOTH_SAMPLES = 3

# ─── Hysteresis (within each mode) ───────────────────────────────────────────
HYSTERESIS_DOWN = 4           # °C — temp must drop this much before ramping down
MAX_RAMP_DOWN_PER_CYCLE = 10  # % — max speed reduction per poll cycle

# ─── Adaptive RAPL Power Governor (Asymmetrical P-controller) ────────
#
# UNCAPPED (115W) ──1 cycle ≥88°C──▶ ADAPTIVE (targets 84°C)
#     ▲                                   │
#     └────── 15s <78°C ──────────────────┘
#
# In ADAPTIVE mode, PL1 is continuously adjusted each cycle to hold
# the target temperature. It uses asymmetrical proportional feedback:
# drops wattage rapidly when hot, recovers wattage slowly when cool.
#
RAPL_CAP_ENTER = 88           # °C — engage adaptive mode
RAPL_CAP_DWELL = 2            # seconds hot before engaging (1 poll cycle!)
RAPL_CAP_EXIT = 78            # °C — consider releasing
RAPL_CAP_RELEASE = 15         # seconds cool before releasing

# Adaptive controller tuning
RAPL_TARGET_TEMP  = 84        # °C — temperature to converge on
RAPL_DOWN_GAIN = 5000000      # 5W per °C error above target (fast cool-down)
RAPL_UP_GAIN   = 1000000      # 1W per °C error below target (cautious recovery)

# Power limits (microwatts)
RAPL_PL1_UNCAPPED = 115000000  # 115W — factory default
RAPL_PL2_UNCAPPED = 157000000  # 157W — factory default
RAPL_PL1_MIN      = 35000000   # 35W  — absolute floor
RAPL_PL1_MAX      = 115000000  # 115W — ceiling (same as uncapped)
RAPL_PL2_HEADROOM = 15000000   # PL2 = PL1 + 15W burst headroom

RAPL_PL1_PATH = "/sys/class/powercap/intel-rapl:0/constraint_0_power_limit_uw"
RAPL_PL2_PATH = "/sys/class/powercap/intel-rapl:0/constraint_1_power_limit_uw"

# ─── Battery (DC) Mode Settings ──────────────────────────────────────────────
BATTERY_PL1 = 35000000   # 35W — sustained power limit on battery
BATTERY_PL2 = 45000000   # 45W — burst power limit on battery
BATTERY_EPP_P = "power"  # EPP preference for P-cores on battery
BATTERY_EPP_E = "power"  # EPP preference for E-cores on battery

# ─── Paths ───────────────────────────────────────────────────────────────────
HWMON_BASE = "/sys/devices/platform/acer-wmi/hwmon"
PREDATOR_FAN_SPEED = "/sys/module/linuwu_sense/drivers/platform:acer-wmi/acer-wmi/predator_sense/fan_speed"
NITRO_FAN_SPEED = "/sys/module/linuwu_sense/drivers/platform:acer-wmi/acer-wmi/nitro_sense/fan_speed"
LOG_PATH = "/var/log/acer_fan_curve.log"
TELEMETRY_CSV_PATH = "/var/log/acer_fan_telemetry.csv"

# ─── Logging ─────────────────────────────────────────────────────────────────

log = logging.getLogger("FanCurve")
log.setLevel(logging.INFO)
formatter = logging.Formatter('%(asctime)s - %(name)s - %(levelname)s - %(message)s')

console_handler = logging.StreamHandler()
console_handler.setFormatter(formatter)
log.addHandler(console_handler)

file_handler = logging.handlers.RotatingFileHandler(
    LOG_PATH, maxBytes=1024 * 1024 * 2, backupCount=3)
file_handler.setFormatter(formatter)
log.addHandler(file_handler)


# ─── Enums ───────────────────────────────────────────────────────────────────

class FanMode(Enum):
    INDEPENDENT = "independent"
    COUPLED     = "coupled"

class PowerState(Enum):
    UNCAPPED = "uncapped"   # Full turbo (115W)
    ADAPTIVE = "adaptive"   # PI controller actively adjusting PL1
    BATTERY  = "battery"    # Battery power caps (35W)


# ─── Helper Functions ────────────────────────────────────────────────────────

def read_fan_rpms(hwmon_path):
    """Read actual fan RPMs from hwmon. Returns (cpu_rpm, gpu_rpm)."""
    rpms = []
    for i in range(1, 3):  # fan1=CPU, fan2=GPU
        fan_file = os.path.join(hwmon_path, f"fan{i}_input")
        if os.path.exists(fan_file):
            try:
                with open(fan_file, 'r') as f:
                    rpms.append(int(f.read().strip()))
            except (ValueError, IOError):
                rpms.append(0)
        else:
            rpms.append(0)
    return rpms[0] if rpms else 0, rpms[1] if len(rpms) > 1 else 0


def read_gpu_power():
    """Read GPU power draw in watts via nvidia-smi. Returns 0.0 on failure."""
    try:
        result = subprocess.run(
            ['nvidia-smi', '--query-gpu=power.draw', '--format=csv,noheader,nounits'],
            capture_output=True, text=True, timeout=2)
        if result.returncode == 0:
            return float(result.stdout.strip())
    except (subprocess.TimeoutExpired, ValueError, FileNotFoundError):
        pass
    return 0.0


def is_ac_connected():
    """Check if AC adapter is connected. Returns True if AC is online, False if running on battery."""
    path = "/sys/class/power_supply/ACAD/online"
    if os.path.exists(path):
        try:
            with open(path, 'r') as f:
                return f.read().strip() == "1"
        except IOError:
            pass
    return True  # default to AC if check fails


class TelemetryLogger:
    """CSV telemetry logger with automatic rotation."""
    HEADER = ["timestamp", "cpu_temp", "gpu_temp", "max_temp",
              "cpu_fan_pct", "gpu_fan_pct", "cpu_rpm", "gpu_rpm",
              "mode", "power", "gpu_watts", "pl1_watts", "epp"]

    def __init__(self, csv_path, max_bytes=10*1024*1024, backup_count=3):
        self.csv_path = csv_path
        self.max_bytes = max_bytes
        self.backup_count = backup_count
        self.file = None
        self.writer = None
        self._open()

    def _open(self):
        """Open CSV file, write header if new/empty."""
        write_header = not os.path.exists(self.csv_path) or os.path.getsize(self.csv_path) == 0
        self.file = open(self.csv_path, 'a', newline='', buffering=1)  # line-buffered
        self.writer = csv.writer(self.file)
        if write_header:
            self.writer.writerow(self.HEADER)

    def record(self, cpu_temp, gpu_temp, cpu_fan_pct, gpu_fan_pct, cpu_rpm, gpu_rpm, mode, power, gpu_watts, pl1_watts, epp):
        """Write one telemetry row."""
        try:
            self.writer.writerow([
                datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                cpu_temp, gpu_temp, max(cpu_temp, gpu_temp),
                cpu_fan_pct, gpu_fan_pct,
                cpu_rpm, gpu_rpm,
                mode, power, round(gpu_watts, 1), pl1_watts, epp
            ])
            # Rotate if too large
            if self.file.tell() > self.max_bytes:
                self._rotate()
        except Exception as e:
            log.error(f"Telemetry write error: {e}")

    def _rotate(self):
        """Rotate CSV files: .csv → .csv.1 → .csv.2 → .csv.3"""
        self.file.close()
        for i in range(self.backup_count, 0, -1):
            src = f"{self.csv_path}.{i}" if i > 1 else self.csv_path
            if i == 1:
                src = self.csv_path
            else:
                src = f"{self.csv_path}.{i-1}"
            dst = f"{self.csv_path}.{i}"
            if os.path.exists(src):
                os.rename(src, dst)
        self._open()
        log.info("Telemetry CSV rotated")

    def close(self):
        if self.file:
            self.file.close()


def find_hwmon_path():
    """Find the acer-wmi hwmon directory"""
    if not os.path.exists(HWMON_BASE):
        return None
    for entry in os.listdir(HWMON_BASE):
        path = os.path.join(HWMON_BASE, entry)
        if os.path.isdir(path):
            return path
    return None


def find_fan_speed_path():
    """Find the fan_speed sysfs file (predator or nitro)"""
    if os.path.exists(PREDATOR_FAN_SPEED):
        return PREDATOR_FAN_SPEED
    elif os.path.exists(NITRO_FAN_SPEED):
        return NITRO_FAN_SPEED
    return None


def read_temps(hwmon_path):
    """Read temperatures. Returns (cpu_temp, gpu_temp) in °C."""
    temps = []

    # Read from acer-wmi hwmon (temp1=CPU, temp2=GPU, temp3=system)
    for i in range(1, 5):
        temp_file = os.path.join(hwmon_path, f"temp{i}_input")
        if os.path.exists(temp_file):
            try:
                with open(temp_file, 'r') as f:
                    val = int(f.read().strip())
                    if val > 1000:
                        val = val // 1000
                    temps.append(val)
            except (ValueError, IOError):
                pass

    # Use coretemp for CPU package (more accurate die temp)
    cpu_pkg_temp = read_coretemp_package()

    if cpu_pkg_temp is not None:
        cpu_temp = cpu_pkg_temp
        gpu_temp = temps[1] if len(temps) > 1 else (temps[0] if temps else cpu_pkg_temp)
    elif len(temps) >= 2:
        cpu_temp, gpu_temp = temps[0], temps[1]
    elif len(temps) == 1:
        cpu_temp = gpu_temp = temps[0]
    else:
        cpu_temp = gpu_temp = 0

    return cpu_temp, gpu_temp


def read_coretemp_package():
    """Read CPU package temperature from coretemp hwmon"""
    for hwmon_dir in os.listdir("/sys/class/hwmon/"):
        name_file = os.path.join("/sys/class/hwmon", hwmon_dir, "name")
        try:
            with open(name_file, 'r') as f:
                if f.read().strip() == "coretemp":
                    temp_file = os.path.join("/sys/class/hwmon", hwmon_dir, "temp1_input")
                    if os.path.exists(temp_file):
                        with open(temp_file, 'r') as tf:
                            return int(tf.read().strip()) // 1000
        except (IOError, ValueError):
            continue
    return None


def read_current_fan_setting(fan_speed_path):
    """Read current fan speed from sysfs. Returns (cpu, gpu) as ints."""
    try:
        with open(fan_speed_path, 'r') as f:
            data = f.read().strip()
            if ',' in data:
                parts = data.split(',', 1)
                return int(parts[0].strip()), int(parts[1].strip())
    except (IOError, ValueError) as e:
        log.error(f"Failed to read fan speed: {e}")
    return 0, 0


def write_fan_speed(fan_speed_path, cpu_speed, gpu_speed):
    """Write fan speeds to sysfs"""
    try:
        with open(fan_speed_path, 'w') as f:
            f.write(f"{cpu_speed},{gpu_speed}")
        return True
    except IOError as e:
        log.error(f"Failed to write fan speed: {e}")
        return False


def interpolate_fan_curve(temp):
    """Linearly interpolate the fan curve for a given temperature."""
    if temp <= FAN_CURVE[0][0]:
        return FAN_CURVE[0][1]
    if temp >= FAN_CURVE[-1][0]:
        return FAN_CURVE[-1][1]

    for i in range(len(FAN_CURVE) - 1):
        t_low, s_low = FAN_CURVE[i]
        t_high, s_high = FAN_CURVE[i + 1]
        if t_low <= temp <= t_high:
            ratio = (temp - t_low) / (t_high - t_low)
            return int(s_low + ratio * (s_high - s_low))

    return 100


def apply_hysteresis(target, current, temp, last_ramp_temp):
    """Apply hysteresis + rate-limiting for ramp-down.
    Returns (new_speed, new_last_ramp_temp).
    """
    if target < current:
        if last_ramp_temp is not None and temp > (last_ramp_temp - HYSTERESIS_DOWN):
            return current, last_ramp_temp  # Hold — not enough drop yet
        else:
            clamped = max(target, current - MAX_RAMP_DOWN_PER_CYCLE)
            return clamped, temp  # Allow ramp-down, update tracker
    elif target > current:
        return target, temp  # Ramp up immediately
    else:
        return current, last_ramp_temp  # No change


# ─── Main Daemon ─────────────────────────────────────────────────────────────

class FanCurveDaemon:
    def __init__(self):
        self.running = True

        # Fan state
        self.cpu_speed = 0
        self.gpu_speed = 0

        # Per-mode hysteresis trackers
        # Independent mode: per-component
        self.last_ramp_cpu = None
        self.last_ramp_gpu = None
        # Coupled mode: single unified tracker
        self.last_ramp_coupled = None

        # Current mode
        self.mode = FanMode.INDEPENDENT
        self.coupled_enter_time = 0  # timestamp when we entered coupled

        # Temperature smoothing ring buffers
        self.cpu_temp_history = []
        self.gpu_temp_history = []

        # Adaptive RAPL power governor (asymmetrical)
        self.power_state = PowerState.UNCAPPED
        self.cap_dwell_start = None      # when we started seeing temps above CAP_ENTER
        self.release_dwell_start = None  # when we started seeing temps below CAP_EXIT
        self.current_pl1 = RAPL_PL1_UNCAPPED  # current PL1 setting in microwatts

        # Dynamic Energy Performance Preference governor
        self.idle_start_time = None
        self.current_epp = "performance"

        # DAMX interaction
        self.damx_override = False
        self.we_are_controlling = False

    def signal_handler(self, sig, frame):
        log.info(f"Received signal {sig}, shutting down...")
        self.running = False

    def check_damx_override(self, fan_speed_path):
        """Back off if DAMX GUI sets manual fan speeds.
        Re-engage when DAMX returns to auto (0,0).
        """
        cpu, gpu = read_current_fan_setting(fan_speed_path)

        if self.we_are_controlling:
            # We are controlling: if sysfs differs from our last written speeds
            # and is not (0,0) (which would mean someone set it back to auto),
            # then someone else has overridden us.
            if (cpu != self.cpu_speed or gpu != self.gpu_speed) and (cpu > 0 or gpu > 0):
                log.info(f"DAMX manual override detected while controlling ({cpu},{gpu}), backing off")
                self.damx_override = True
                self.we_are_controlling = False
                return True
            return False
        else:
            # We are NOT controlling (either backed off, or in EC auto):
            # If we see any non-zero manual speed in sysfs, it's an override.
            if cpu > 0 or gpu > 0:
                if not self.damx_override:
                    log.info(f"DAMX manual override detected ({cpu},{gpu}), backing off")
                    self.damx_override = True
                return True
            else:
                if self.damx_override:
                    log.info("DAMX returned to auto, re-engaging fan curve")
                    self.damx_override = False
                    # Reset hysteresis on re-engage so we don't inherit stale state
                    self.last_ramp_cpu = None
                    self.last_ramp_gpu = None
                    self.last_ramp_coupled = None
                return False

    def smooth_temp(self, history, new_temp):
        """Rolling average temperature to filter turbo boost spikes."""
        history.append(new_temp)
        if len(history) > TEMP_SMOOTH_SAMPLES:
            history.pop(0)
        return sum(history) // len(history)

    def update_mode(self, max_temp):
        """State machine for mode transitions with hysteresis + hold timer."""
        old_mode = self.mode

        if self.mode == FanMode.INDEPENDENT:
            if max_temp >= COUPLED_ENTER:
                self.mode = FanMode.COUPLED
                self.coupled_enter_time = time.time()
                self.last_ramp_coupled = None
        elif self.mode == FanMode.COUPLED:
            # Don't exit coupled until hold timer expires AND temp is low enough
            held_long_enough = (time.time() - self.coupled_enter_time) >= COUPLED_HOLD_SECONDS
            if held_long_enough and max_temp < COUPLED_EXIT:
                self.mode = FanMode.INDEPENDENT
                self.last_ramp_cpu = None
                self.last_ramp_gpu = None

        if self.mode != old_mode:
            log.info(f"Mode transition: {old_mode.value} → {self.mode.value} (max_temp={max_temp}°C)")

    def update_power_state(self, max_temp, gpu_watts):
        """Adaptive RAPL governor with Asymmetrical P-controller + Battery management.

        AC Connected:
            UNCAPPED ──hot (1 cycle ≥88°C)──▶ ADAPTIVE (targets 84°C)
                ▲                                   │
                └────── cool 15s <78°C ─────────────┘

        Battery Connected:
            Locks power state to BATTERY mode with low PL1/PL2 limits to conserve battery
            and prevent voltage sag.
        """
        old_state = self.power_state
        now = time.time()
        ac_online = is_ac_connected()

        if not ac_online:
            # Force Battery Mode immediately if unplugged
            if self.power_state != PowerState.BATTERY:
                self.power_state = PowerState.BATTERY
                self.current_pl1 = BATTERY_PL1
                self._write_rapl(BATTERY_PL1, BATTERY_PL2)
                self._write_epp_battery()
                self.cap_dwell_start = None
                self.release_dwell_start = None
        else:
            # AC online: restore full power if transitioning back from Battery Mode
            if self.power_state == PowerState.BATTERY:
                log.info("🔌 AC plugged in, restoring full power limits")
                self.power_state = PowerState.UNCAPPED
                self.current_pl1 = RAPL_PL1_UNCAPPED
                self._write_rapl(RAPL_PL1_UNCAPPED, RAPL_PL2_UNCAPPED)
                self._write_epp_asymmetric(is_idle=False)
                self.cap_dwell_start = None
                self.release_dwell_start = None

            # Normal AC power state machine
            if self.power_state == PowerState.UNCAPPED:
                if max_temp >= RAPL_CAP_ENTER:
                    if self.cap_dwell_start is None:
                        self.cap_dwell_start = now
                    elif (now - self.cap_dwell_start) >= RAPL_CAP_DWELL:
                        # Engage adaptive mode immediately, start at 65W
                        self.power_state = PowerState.ADAPTIVE
                        self.current_pl1 = 65000000
                        self._write_rapl(self.current_pl1, self.current_pl1 + RAPL_PL2_HEADROOM)
                        self.cap_dwell_start = None
                        self.release_dwell_start = None
                else:
                    self.cap_dwell_start = None

            elif self.power_state == PowerState.ADAPTIVE:
                # Proportional step: adjust PL1 relative to CURRENT limit based on error from 84°C
                error = max_temp - RAPL_TARGET_TEMP  # positive = too hot

                if error > 0:
                    # Hot: drop limit rapidly (5W per °C above target)
                    adjustment = -int(error * RAPL_DOWN_GAIN)
                else:
                    # Cool: recover limit slowly (1W per °C below target)
                    adjustment = -int(error * RAPL_UP_GAIN)

                target_pl1 = self.current_pl1 + adjustment
                new_pl1 = max(RAPL_PL1_MIN, min(RAPL_PL1_MAX, target_pl1))

                if new_pl1 != self.current_pl1:
                    self.current_pl1 = new_pl1
                    self._write_rapl(new_pl1, min(new_pl1 + RAPL_PL2_HEADROOM, RAPL_PL2_UNCAPPED))

                # Check if we can release back to uncapped
                if max_temp < RAPL_CAP_EXIT:
                    if self.release_dwell_start is None:
                        self.release_dwell_start = now
                    elif (now - self.release_dwell_start) >= RAPL_CAP_RELEASE:
                        self.power_state = PowerState.UNCAPPED
                        self.current_pl1 = RAPL_PL1_UNCAPPED
                        self._write_rapl(RAPL_PL1_UNCAPPED, RAPL_PL2_UNCAPPED)
                        self.cap_dwell_start = None
                        self.release_dwell_start = None
                else:
                    self.release_dwell_start = None

        if self.power_state != old_state:
            pl1_w = self.current_pl1 // 1000000
            log.info(f"⚡ Power: {old_state.value} → {self.power_state.value} (PL1={pl1_w}W, GPU={gpu_watts:.0f}W, max_temp={max_temp}°C)")

    def _write_rapl(self, pl1, pl2):
        """Write RAPL power limits."""
        try:
            with open(RAPL_PL1_PATH, 'w') as f:
                f.write(str(pl1))
            with open(RAPL_PL2_PATH, 'w') as f:
                f.write(str(pl2))
        except IOError as e:
            log.error(f"Failed to write RAPL: {e}")

    def update_epp_state(self, gpu_watts):
        """Dynamic Energy Performance Preference governor.
        
        Monitors CPU and GPU utilization to toggle EPP:
        - Active/Heavy utilization: "performance" on all cores for low latency.
        - Idle (CPU < 12%, GPU power < 12W for 15s): EPP asymmetry is engaged.
          - P-cores (0-15) set to "power" (aggressive sleep).
          - E-cores (16-23) set to "balance_power" (takes background load).
          This naturally guides Linux scheduler to place idle background tasks onto E-cores first.
        """
        if self.power_state == PowerState.BATTERY:
            return  # Managed by battery power state EPP settings

        try:
            import psutil
            cpu_usage = psutil.cpu_percent()
        except Exception:
            cpu_usage = 10.0 # safe fallback
            
        now = time.time()
        
        # System is active if CPU > 12% or GPU drawing real power > 12W
        is_active = (cpu_usage > 12.0) or (gpu_watts > 12.0)
        
        if is_active:
            self.idle_start_time = None
            if self.current_epp != "performance":
                self._write_epp_asymmetric(is_idle=False)
        else:
            if self.idle_start_time is None:
                self.idle_start_time = now
            elif (now - self.idle_start_time) >= 15: # 15 seconds of consecutive idle
                if self.current_epp != "asymmetric_idle":
                    self._write_epp_asymmetric(is_idle=True)

    def _write_epp_asymmetric(self, is_idle):
        """Helper to write energy performance preference using EPP asymmetry."""
        try:
            # 13700HX Core Layout:
            # P-cores: 0-15 (16 logical threads)
            # E-cores: 16-23 (8 logical threads)
            p_pref = "power" if is_idle else "performance"
            e_pref = "balance_power" if is_idle else "performance"
            
            # Write to P-cores (0-15)
            p_count = 0
            for i in range(16):
                path = f"/sys/devices/system/cpu/cpu{i}/cpufreq/energy_performance_preference"
                try:
                    with open(path, 'w') as f:
                        f.write(p_pref)
                    p_count += 1
                except IOError:
                    pass
            
            # Write to E-cores (16-23)
            e_count = 0
            for i in range(16, 24):
                path = f"/sys/devices/system/cpu/cpu{i}/cpufreq/energy_performance_preference"
                try:
                    with open(path, 'w') as f:
                        f.write(e_pref)
                    e_count += 1
                except IOError:
                    pass
            
            self.current_epp = "asymmetric_idle" if is_idle else "performance"
            state_str = "Idle (E-cores prioritized)" if is_idle else "Active (Full power)"
            log.info(f"🌿 EPP Asymmetry: {state_str} -> P-cores={p_pref} ({p_count} threads), E-cores={e_pref} ({e_count} threads)")
        except Exception as e:
            log.error(f"Failed to set asymmetric EPP: {e}")

    def _write_epp_battery(self):
        """Write power-saving EPP to all cores to maximize battery life."""
        try:
            p_pref = BATTERY_EPP_P
            e_pref = BATTERY_EPP_E
            
            p_count = 0
            for i in range(16):
                path = f"/sys/devices/system/cpu/cpu{i}/cpufreq/energy_performance_preference"
                try:
                    with open(path, 'w') as f:
                        f.write(p_pref)
                    p_count += 1
                except IOError:
                    pass
            
            e_count = 0
            for i in range(16, 24):
                path = f"/sys/devices/system/cpu/cpu{i}/cpufreq/energy_performance_preference"
                try:
                    with open(path, 'w') as f:
                        f.write(e_pref)
                    e_count += 1
                except IOError:
                    pass
            
            self.current_epp = "battery"
            log.info(f"🔋 Battery EPP: P-cores={p_pref} ({p_count} threads), E-cores={e_pref} ({e_count} threads)")
        except Exception as e:
            log.error(f"Failed to set battery EPP: {e}")

    def compute_independent(self, cpu_temp, gpu_temp):
        """Independent mode: each fan follows its own component.
        Uses simple rate-limited ramp-down (no temp hysteresis) since
        the rolling average already smooths out transient spikes.
        """
        target_cpu = interpolate_fan_curve(cpu_temp)
        target_gpu = interpolate_fan_curve(gpu_temp)

        # Rate-limit ramp-down only (ramp-up is instant)
        if target_cpu < self.cpu_speed:
            target_cpu = max(target_cpu, self.cpu_speed - MAX_RAMP_DOWN_PER_CYCLE)
        if target_gpu < self.gpu_speed:
            target_gpu = max(target_gpu, self.gpu_speed - MAX_RAMP_DOWN_PER_CYCLE)

        return target_cpu, target_gpu

    def compute_coupled(self, cpu_temp, gpu_temp):
        """Coupled mode: both fans locked to max temp."""
        max_temp = max(cpu_temp, gpu_temp)
        target = interpolate_fan_curve(max_temp)

        target, self.last_ramp_coupled = apply_hysteresis(
            target, max(self.cpu_speed, self.gpu_speed), max_temp, self.last_ramp_coupled)

        return target, target

    def run(self):
        log.info("Starting Acer Fan Curve Daemon (two-mode heatpipe)")

        if os.geteuid() != 0:
            log.error("Must run as root")
            sys.exit(1)

        hwmon_path = find_hwmon_path()
        if not hwmon_path:
            log.error("Could not find acer-wmi hwmon path")
            sys.exit(1)
        log.info(f"hwmon: {hwmon_path}")

        fan_speed_path = find_fan_speed_path()
        if not fan_speed_path:
            log.error("Could not find fan_speed sysfs path")
            sys.exit(1)
        log.info(f"fan_speed: {fan_speed_path}")

        signal.signal(signal.SIGTERM, self.signal_handler)
        signal.signal(signal.SIGINT, self.signal_handler)

        log.info(f"Curve: {FAN_CURVE}")
        log.info(f"Coupled enter: ≥{COUPLED_ENTER}°C, exit: <{COUPLED_EXIT}°C")
        log.info(f"Hysteresis: {HYSTERESIS_DOWN}°C, ramp-down max: {MAX_RAMP_DOWN_PER_CYCLE}%/cycle")

        # Start telemetry
        telemetry = TelemetryLogger(TELEMETRY_CSV_PATH)
        log.info(f"Telemetry CSV: {TELEMETRY_CSV_PATH}")

        # Initialize fan speed to auto (0,0) to clear boot-time BIOS defaults
        log.info("Initializing fan speeds to auto (0,0) on startup")
        write_fan_speed(fan_speed_path, 0, 0)

        last_log_time = 0
        LOG_INTERVAL = 60
        last_poll_time = time.time()

        while self.running:
            try:
                now = time.time()
                # Check for suspend/resume (time jump)
                if now - last_poll_time > POLL_INTERVAL * 3:
                    log.info(f"System resume detected (elapsed time: {now - last_poll_time:.1f}s). Re-initializing fans & power states.")
                    write_fan_speed(fan_speed_path, 0, 0)
                    self.we_are_controlling = False
                    self.damx_override = False
                    self.last_ramp_cpu = None
                    self.last_ramp_gpu = None
                    self.last_ramp_coupled = None
                    
                    # Force re-write of RAPL limits and EPP preference
                    self._write_rapl(self.current_pl1, self.current_pl1 + RAPL_PL2_HEADROOM if self.power_state == PowerState.ADAPTIVE else RAPL_PL2_UNCAPPED)
                    self._write_epp_asymmetric(is_idle=(self.current_epp == "asymmetric_idle"))
                
                last_poll_time = now

                if self.check_damx_override(fan_speed_path):
                    self.we_are_controlling = False
                    time.sleep(POLL_INTERVAL)
                    continue

                cpu_temp_raw, gpu_temp_raw = read_temps(hwmon_path)

                # Smooth temps to filter turbo boost spikes
                cpu_temp = self.smooth_temp(self.cpu_temp_history, cpu_temp_raw)
                gpu_temp = self.smooth_temp(self.gpu_temp_history, gpu_temp_raw)
                max_temp = max(cpu_temp, gpu_temp)

                # Read GPU power draw for RAPL governor decisions
                gpu_watts = read_gpu_power()

                # ─── State machine: update mode ──────────────────────────
                self.update_mode(max_temp)

                # ─── State machine: dynamic RAPL governor ───────────
                self.update_power_state(max_temp, gpu_watts)

                # ─── State machine: dynamic EPP scaling ─────────────
                self.update_epp_state(gpu_watts)

                # ─── Compute fan speeds based on current mode ────────────
                if self.power_state == PowerState.BATTERY:
                    # On battery: if moderate (<75°C), release to EC auto (0,0) to save battery power.
                    # If hot (≥75°C), run our independent fan curve to protect components.
                    if max_temp < 75:
                        target_cpu, target_gpu = 0, 0
                    else:
                        target_cpu, target_gpu = self.compute_independent(cpu_temp, gpu_temp)
                elif self.power_state != PowerState.UNCAPPED:
                    # Power caps active — force fans to max to recover temps
                    target_cpu, target_gpu = 100, 100
                elif self.mode == FanMode.COUPLED:
                    target_cpu, target_gpu = self.compute_coupled(cpu_temp, gpu_temp)
                else:
                    target_cpu, target_gpu = self.compute_independent(cpu_temp, gpu_temp)

                # ─── Apply to hardware ───────────────────────────────────
                if target_cpu == 0 and target_gpu == 0:
                    if self.we_are_controlling:
                        log.info(f"Temps low (CPU={cpu_temp}°C GPU={gpu_temp}°C), releasing to EC auto")
                        write_fan_speed(fan_speed_path, 0, 0)
                        self.cpu_speed = 0
                        self.gpu_speed = 0
                        self.we_are_controlling = False
                    time.sleep(POLL_INTERVAL)
                    continue

                if target_cpu != self.cpu_speed or target_gpu != self.gpu_speed:
                    mode_tag = "🔗" if self.mode == FanMode.COUPLED else "🔀"
                    log.info(f"{mode_tag} CPU={cpu_temp}°C GPU={gpu_temp}°C → cpu_fan={target_cpu}% gpu_fan={target_gpu}%")
                    self.we_are_controlling = True
                    write_fan_speed(fan_speed_path, target_cpu, target_gpu)
                    self.cpu_speed = target_cpu
                    self.gpu_speed = target_gpu

                # Record telemetry every cycle
                cpu_rpm, gpu_rpm = read_fan_rpms(hwmon_path)
                pl1_w = self.current_pl1 // 1000000
                telemetry.record(cpu_temp, gpu_temp, self.cpu_speed, self.gpu_speed,
                                 cpu_rpm, gpu_rpm, self.mode.value, self.power_state.value,
                                 gpu_watts, pl1_w, self.current_epp)

                # Periodic status
                now = time.time()
                if now - last_log_time > LOG_INTERVAL:
                    log.info(f"[{self.mode.value}|{self.power_state.value}|EPP={self.current_epp}] CPU={cpu_temp}°C GPU={gpu_temp}°C PL1={pl1_w}W GPU={gpu_watts:.0f}W RPM=({cpu_rpm},{gpu_rpm}) fans=({self.cpu_speed}%,{self.gpu_speed}%)")
                    last_log_time = now

            except Exception as e:
                log.error(f"Error in main loop: {e}")

            time.sleep(POLL_INTERVAL)

        # Clean shutdown — restore full power and release fans
        self._write_rapl(RAPL_PL1_UNCAPPED, RAPL_PL2_UNCAPPED)
        self._write_epp_asymmetric(is_idle=False)
        telemetry.close()
        log.info("Shutting down, restoring full power + EPP + releasing fans to EC auto")
        if fan_speed_path:
            write_fan_speed(fan_speed_path, 0, 0)
        log.info("Fan curve daemon stopped")


if __name__ == "__main__":
    daemon = FanCurveDaemon()
    daemon.run()
