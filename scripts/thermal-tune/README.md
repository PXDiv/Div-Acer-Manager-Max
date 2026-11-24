# Acer Predator/Nitro Thermal Tuning & Fan Control Suite

An advanced, hybrid-architecture thermal optimization and dynamic fan control daemon for Acer Predator and Nitro laptops running Linux.

This suite integrates with [Div-Acer-Manager-Max (DAMX)](https://github.com/PXDiv/Div-Acer-Manager-Max) and optimized system utilities like `Ananicy` and `irqbalance` to unlock maximum gaming performance, lower latency, and quiet idle operation on hybrid Intel Core (P/E-core) systems (such as the i7-13700HX).

---

## Features

### 1. Dual-Mode Intelligent Fan Control
The `fan-curve-daemon.py` operates a sophisticated two-mode control scheme:
* **Independent Mode** (Temps < 82°C): The CPU and GPU fans track their respective component temperatures independently for quiet, focused cooling.
* **Coupled Mode** (Temps ≥ 82°C): Both fans lock together at the `max(CPU_temp, GPU_temp)` curve to balance the thermal load across the shared copper heatpipe system.
* **Zero-Bounce Hysteresis**: Features a 5°C dead zone (`82°C` enter / `77°C` exit) and a minimum hold timer to prevent fan speeds from oscillating rapidly during transient turbo boost spikes.

### 2. Asymmetrical RAPL Power Governor
Instead of letting the CPU bounce off its TJMax throttling point:
* When the CPU hits `89°C`, a proportional feedback controller immediately takes over.
* It dynamically scales down the CPU's short-term PL1 limit (from 115W down to a 35W floor) to crush the thermal spike.
* Once temperatures stabilize below `84°C`, it gradually and safely scales power back up to maximize performance without inducing further temperature spikes.

### 3. Dynamic EPP Scheduler Asymmetry
Maintains quiet, energy-efficient idle operation without sacrificing gaming responsiveness:
* **Active Mode**: Sets all cores to `performance`.
* **Idle Mode** (CPU < 12%, GPU < 12W for 15s): prioritizes E-cores by applying EPP asymmetry:
  * **P-cores (0-15)**: Placed in aggressive `power` savings (P-cores sleep deeply and stay cold).
  * **E-cores (16-23)**: Placed in `balance_power` (handles background tasks).
  * Guides the Linux scheduler to run background tasks on E-cores first.

### 4. Interrupt Affinity Isolation (`irqbalance` integration)
Eliminates latency spikes and frame drops during intense gaming sessions:
* Banned P-cores (`0-15`) from handling hardware interrupts.
* Forces your network, GPU, and USB interrupts to be distributed strictly across E-cores (`16-23`), ensuring P-cores are dedicated entirely to game engines and physics threads.

---

## Repository Structure

```text
├── fan-curve-daemon.py         # Main fan control & thermal governor daemon
├── acer-thermal-tune.sh        # Startup script to apply initial PL1/PL2 limits
├── acer-thermal-tune.service   # Systemd unit for the thermal script
├── acer-fan-curve.service      # Hardened systemd unit for the fan daemon
├── fan-dashboard.html          # HTML/JS web panel for real-time telemetry
├── 99-custom.rules             # Ananicy rules for game/launcher core affinity
└── gaming-thermal.conf         # Vulkan/DXVK environment performance variables
```

---

## Installation & Setup

### 1. Copy Files to System Directories
Make the scripts executable and copy them to `/opt/damx/`:
```bash
sudo mkdir -p /opt/damx
sudo cp fan-curve-daemon.py /opt/damx/
sudo cp acer-thermal-tune.sh /opt/damx/
sudo chmod +x /opt/damx/fan-curve-daemon.py
sudo chmod +x /opt/damx/acer-thermal-tune.sh
```

### 2. Install Systemd Services
Copy the unit files to `/etc/systemd/system/` and enable them:
```bash
sudo cp *.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now acer-thermal-tune.service
sudo systemctl enable --now acer-fan-curve.service
```

### 3. Configure `Ananicy` (Auto-Nicer)
Copy the custom rules file to your Ananicy configuration folder:
```bash
sudo cp 99-custom.rules /etc/ananicy/rules.d/
sudo systemctl restart ananicy.service
```

### 4. Configure `irqbalance`
Install `sys-apps/irqbalance` via your package manager (e.g., `emerge sys-apps/irqbalance` on Gentoo) and add the P-core ban list to its environment file:
```env
# /etc/default/irqbalance.env
IRQBALANCE_BANNED_CPULIST="0-15"
```
Enable and start the service:
```bash
sudo systemctl enable --now irqbalance.service
```

---

## Telemetry Web Dashboard
The `fan-dashboard.html` file provides a stunning, high-performance web panel built with Chart.js to visualize your laptop's real-time telemetry.

To use it:
1. Open `fan-dashboard.html` in your web browser.
2. Load `/var/log/acer_fan_telemetry.csv` (requires root read access or copy it to your home directory).
3. Toggle **Auto-Reload** to watch the real-time thermal balancing act, dynamic power capping, and core routing.
