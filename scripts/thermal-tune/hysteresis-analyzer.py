#!/usr/bin/env python3
import sys
import csv
import re
from datetime import datetime, timedelta
import statistics

TELEMETRY_PATH = "/var/log/acer_fan_telemetry.csv"
DAEMON_PATH = "/opt/damx/fan-curve-daemon.py"

def load_recent_telemetry(hours=2):
    logs = []
    try:
        import os
        file_size = os.path.getsize(TELEMETRY_PATH)
        with open(TELEMETRY_PATH, 'r') as f:
            # Each telemetry row is roughly 80-100 bytes.
            # 3600 rows * 100 bytes = 360,000 bytes.
            # Seek to 500,000 bytes from the end to ensure we capture more than 3600 rows.
            if file_size > 500000:
                f.seek(file_size - 500000)
                # Skip the first partial line
                f.readline()
                
            fieldnames = ['timestamp','cpu_temp','gpu_temp','max_temp','cpu_fan_pct','gpu_fan_pct','cpu_rpm','gpu_rpm','mode','power','gpu_watts','pl1_watts','epp']
            reader = csv.DictReader(f, fieldnames=fieldnames)
            for row in reader:
                try:
                    if row['timestamp'] == 'timestamp' or not row['timestamp']:
                        continue
                    ts = datetime.strptime(row['timestamp'].strip(), '%Y-%m-%d %H:%M:%S')
                    logs.append({
                        'timestamp': ts,
                        'cpu_temp': int(row['cpu_temp']),
                        'gpu_temp': int(row['gpu_temp']),
                        'max_temp': int(row['max_temp']),
                        'cpu_fan_pct': int(row['cpu_fan_pct']),
                        'gpu_fan_pct': int(row['gpu_fan_pct']),
                        'cpu_rpm': int(row['cpu_rpm']),
                        'gpu_rpm': int(row['gpu_rpm']),
                        'gpu_watts': float(row['gpu_watts']),
                        'pl1_watts': int(row['pl1_watts']),
                        'mode': row['mode']
                    })
                except Exception:
                    continue
    except FileNotFoundError:
        print(f"Error: {TELEMETRY_PATH} not found.")
        sys.exit(1)
    
    # Return the last 3600 samples (roughly 2 hours if polling interval is 2s)
    return logs[-3600:] if len(logs) > 3600 else logs

def analyze_telemetry_and_rapl(logs):
    if not logs:
        print("No telemetry data to analyze.")
        return None
    
    times = [log['timestamp'] for log in logs]
    max_temps = [log['max_temp'] for log in logs]
    
    # Count times spent above critical RAPL thresholds (88°C is where governor kicks in)
    high_temp_count = sum(1 for r in logs if r['max_temp'] >= 88)
    high_temp_pct = (high_temp_count / len(logs)) * 100.0 if logs else 0.0
    
    # 1. Idle temp hover
    idle_cycling_events = 0
    last_fan = 0
    for i in range(1, len(logs)):
        row = logs[i]
        if row['gpu_watts'] < 5 and row['max_temp'] < 60:
            if row['cpu_fan_pct'] != last_fan:
                if (last_fan == 0 and row['cpu_fan_pct'] >= 30) or (last_fan >= 30 and row['cpu_fan_pct'] == 0):
                    idle_cycling_events += 1
                last_fan = row['cpu_fan_pct']
                
    # 2. Fan speed oscillations under load
    active_oscillations = 0
    direction = 0
    last_active_fan = 0
    for row in logs:
        if row['max_temp'] >= 65:
            fan = row['cpu_fan_pct']
            if fan > last_active_fan and direction != 1:
                active_oscillations += 1
                direction = 1
            elif fan < last_active_fan and direction != -1:
                active_oscillations += 1
                direction = -1
            last_active_fan = fan
            
    duration = (times[-1] - times[0]).total_seconds() / 60.0 if times else 0
    oscillations_per_hour = (active_oscillations / duration) * 60.0 if duration > 0 else 0
    
    # 3. Cooling rate (slope)
    cooling_rates = []
    temp_drop = 0
    drop_duration = 0
    for i in range(1, len(logs)):
        if logs[i]['max_temp'] < logs[i-1]['max_temp'] and logs[i-1]['cpu_fan_pct'] >= 50:
            temp_drop += (logs[i-1]['max_temp'] - logs[i]['max_temp'])
            drop_duration += 2
        else:
            if drop_duration >= 10:
                cooling_rates.append(temp_drop / drop_duration)
            temp_drop = 0
            drop_duration = 0
            
    avg_cooling_rate = statistics.mean(cooling_rates) if cooling_rates else 0.0
    
    # --- RAPL floor & ceiling adjustments ---
    recs = {}
    
    # Highly efficient thermal system (clean fans)
    if avg_cooling_rate > 1.2 and high_temp_pct < 1.0:
        # Base TDP of 13700HX is 55W. We can safely set floor (RAPL_PL1_MIN) to 55W
        # as the clean heatsink can dissipate it sustained. Ceiling remains 115W.
        recs['RAPL_PL1_MIN'] = 55000000   # 55W Floor
        recs['RAPL_PL1_MAX'] = 115000000  # 115W Ceiling
        recs['RAPL_PL2_HEADROOM'] = 15000000 # 15W PL2 headroom
        recs['RAPL_REASON'] = (
            f"Outstanding cooling velocity ({avg_cooling_rate:.2f}°C/s) and minimal thermal stress "
            f"({high_temp_pct:.2f}% of time >= 88°C) detected. "
            f"Floor elevated to 55W (CPU Base TDP) to prevent FPS drops under sustained load."
        )
    # Moderately efficient thermal system
    elif avg_cooling_rate > 0.6 and high_temp_pct < 3.0:
        recs['RAPL_PL1_MIN'] = 45000000   # 45W Floor
        recs['RAPL_PL1_MAX'] = 100000000  # 100W Ceiling
        recs['RAPL_PL2_HEADROOM'] = 15000000
        recs['RAPL_REASON'] = (
            f"Decent cooling velocity ({avg_cooling_rate:.2f}°C/s) with manageable thermal stress "
            f"({high_temp_pct:.2f}% >= 88°C). "
            f"Floor set to 45W and Ceiling set to 100W to balance performance and thermals."
        )
    # Poor thermal dissipation (dirty fans or extreme environment)
    else:
        recs['RAPL_PL1_MIN'] = 35000000   # 35W Floor
        recs['RAPL_PL1_MAX'] = 85000000   # 85W Ceiling (down from 115W to prevent thermal spikes)
        recs['RAPL_PL2_HEADROOM'] = 15000000
        recs['RAPL_REASON'] = (
            f"Sub-optimal cooling velocity ({avg_cooling_rate:.2f}°C/s) or high thermal stress "
            f"({high_temp_pct:.2f}% >= 88°C). "
            f"Floor dropped to 35W and Ceiling throttled to 85W to prevent thermal saturation."
        )
        
    # Standard fan hysteresis
    avg_idle_temp = statistics.mean([r['cpu_temp'] for r in logs if r['cpu_fan_pct'] == 0]) if any(r['cpu_fan_pct'] == 0 for r in logs) else 0.0
    if idle_cycling_events > 15 and avg_idle_temp > 50:
        recs['SILENT_TEMP'] = 55
        recs['SILENT_REASON'] = f"Excessive idle fan bouncing ({idle_cycling_events} events). Idle temp hovers at {avg_idle_temp:.1f}°C."
    else:
        recs['SILENT_TEMP'] = 50
        recs['SILENT_REASON'] = "Idle behavior is stable."

    if oscillations_per_hour > 10:
        recs['HYSTERESIS_DOWN'] = 5
        recs['HYSTERESIS_REASON'] = f"High fan speed oscillation rate ({oscillations_per_hour:.1f}/hr). Damping increased."
    else:
        recs['HYSTERESIS_DOWN'] = 4
        recs['HYSTERESIS_REASON'] = "Oscillation rate is optimal."
        
    return {
        'duration_min': duration,
        'idle_cycling_events': idle_cycling_events,
        'oscillations_per_hour': oscillations_per_hour,
        'avg_cooling_rate': avg_cooling_rate,
        'avg_idle_temp': avg_idle_temp,
        'high_temp_pct': high_temp_pct,
        'recommendations': recs
    }

def apply_recommendations(recs):
    try:
        with open(DAEMON_PATH, 'r') as f:
            content = f.read()
        
        # 1. Update Silent Fan Curve
        if recs['SILENT_TEMP'] == 55:
            new_curve = "FAN_CURVE = [\n    (55, 0),    # Below 55°C — EC auto\n    (62, 30),   # 62°C — light spin"
            content = re.sub(r'FAN_CURVE\s*=\s*\[\s*\(50,\s*0\),\s*#.*?\n\s*\(60,\s*30\),\s*#.*?\n', new_curve + '\n', content)
            print("✔ Adjusted FAN_CURVE to start silent mode up to 55°C.")
            
        # 2. Update Hysteresis
        current_hyst = int(re.search(r'HYSTERESIS_DOWN\s*=\s*(\d+)', content).group(1))
        if current_hyst != recs['HYSTERESIS_DOWN']:
            content = re.sub(r'HYSTERESIS_DOWN\s*=\s*\d+', f"HYSTERESIS_DOWN = {recs['HYSTERESIS_DOWN']}", content)
            print(f"✔ Adjusted HYSTERESIS_DOWN from {current_hyst}°C to {recs['HYSTERESIS_DOWN']}°C.")
            
        # 3. Update RAPL PL1 / PL2 Floor and Ceiling
        curr_min = int(re.search(r'RAPL_PL1_MIN\s*=\s*(\d+)', content).group(1))
        curr_max = int(re.search(r'RAPL_PL1_MAX\s*=\s*(\d+)', content).group(1))
        curr_head = int(re.search(r'RAPL_PL2_HEADROOM\s*=\s*(\d+)', content).group(1))
        
        if curr_min != recs['RAPL_PL1_MIN']:
            content = re.sub(r'RAPL_PL1_MIN\s*=\s*\d+', f"RAPL_PL1_MIN      = {recs['RAPL_PL1_MIN']}", content)
            print(f"✔ Adjusted RAPL PL1 Floor (RAPL_PL1_MIN) from {curr_min//1000000}W to {recs['RAPL_PL1_MIN']//1000000}W.")
            
        if curr_max != recs['RAPL_PL1_MAX']:
            content = re.sub(r'RAPL_PL1_MAX\s*=\s*\d+', f"RAPL_PL1_MAX      = {recs['RAPL_PL1_MAX']}", content)
            print(f"✔ Adjusted RAPL PL1 Ceiling (RAPL_PL1_MAX) from {curr_max//1000000}W to {recs['RAPL_PL1_MAX']//1000000}W.")
            
        if curr_head != recs['RAPL_PL2_HEADROOM']:
            content = re.sub(r'RAPL_PL2_HEADROOM\s*=\s*\d+', f"RAPL_PL2_HEADROOM = {recs['RAPL_PL2_HEADROOM']}", content)
            print(f"✔ Adjusted RAPL PL2 Headroom from {curr_head//1000000}W to {recs['RAPL_PL2_HEADROOM']//1000000}W.")
            
        with open(DAEMON_PATH, 'w') as f:
            f.write(content)
            
        print("✔ Daemon configuration updated successfully. Please restart the daemon.")
    except Exception as e:
        print(f"Failed to apply modifications: {e}")

def main():
    print("========================================")
    print("      ACER THERMAL HYSTERESIS ANALYZER  ")
    print("========================================\n")
    
    logs = load_recent_telemetry()
    res = analyze_telemetry_and_rapl(logs)
    
    if not res:
        return
        
    print(f"Parsed {len(logs)} samples over {res['duration_min']:.1f} minutes of active telemetry.")
    print(f"• Average Idle Temperature: {res['avg_idle_temp']:.1f}°C")
    print(f"• Idle Fan Bouncing Count:  {res['idle_cycling_events']} events")
    print(f"• Active Fan Oscillation:   {res['oscillations_per_hour']:.1f} transitions/hour")
    print(f"• Average Cooling Velocity:  {res['avg_cooling_rate']:.3f}°C/second")
    print(f"• Dwell Time >= 88°C:        {res['high_temp_pct']:.2f}%\n")
    
    print("--- Diagnostics & Recommendations ---")
    recs = res['recommendations']
    print(f"1. Adaptive RAPL Power Limits:")
    print(f"   - Target Floor (PL1 Min):   {recs['RAPL_PL1_MIN'] // 1000000}W")
    print(f"   - Target Ceiling (PL1 Max): {recs['RAPL_PL1_MAX'] // 1000000}W")
    print(f"   - Analysis:                 {recs['RAPL_REASON']}")
    
    print(f"\n2. Zero-RPM Silent Zone:")
    print(f"   - Target Temp: {recs['SILENT_TEMP']}°C")
    print(f"   - Rationale:   {recs['SILENT_REASON']}")
    
    print(f"\n3. Fan Ramping Hysteresis:")
    print(f"   - Target Hysteresis: {recs['HYSTERESIS_DOWN']}°C")
    print(f"   - Rationale:         {recs['HYSTERESIS_REASON']}")
    
    if len(sys.argv) > 1 and sys.argv[1] == '--apply':
        print("\nApplying optimized parameters to the daemon...")
        apply_recommendations(recs)
    else:
        print("\nTo apply these adjustments automatically, run:")
        print("  sudo python3 /opt/damx/hysteresis-analyzer.py --apply")

if __name__ == '__main__':
    main()
