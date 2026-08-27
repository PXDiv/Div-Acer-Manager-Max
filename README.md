<p align="center">
  <img src="https://github.com/user-attachments/assets/6d383e82-8221-438b-9d6d-a19e998fcc59" alt="icon" width="80" style="vertical-align: middle;">
</p>

<h1 align="center">
  Div Acer Manager Max
</h1>

**Div Acer Manager Max** is a feature-rich Linux GUI utility for Acer laptops powered by the incredible [Linuwu Sense](https://github.com/0x7375646F/Linuwu-Sense) drivers. It replicates and expands on Acer’s NitroSense and PredatorSense capabilities on Linux with full fan control, performance modes, battery optimization, backlight settings, and more — all wrapped in a modern Avalonia-based UI.

> [!CAUTION]
> Project is under passive development.

![Title Image](https://github.com/user-attachments/assets/a60898a6-a2b8-432e-b5a2-8d0a45c63484)

<h4 align="center">
⭐ Please star this repository to show support. It motivates me to make the project better for everyone
</h4>  

## ✨ Features

### ✅ Fully Implemented

* 🔋 **Performance / Thermal Profiles**
  Eco, Silent, Balanced, Performance, Turbo — automatically adjusted based on AC/battery status
  (e.g., Turbo hidden when on battery or unsupported)

* 🌡 **Fan Control**
  Manual and Auto fan speed modes
  Manual disabled automatically when in Quiet profile

* 💡 **LCD Override Setting**
  Direct control over LCD power behavior

* 🎨 **Keyboard Backlight Timeout Control**
  Customize the keyboard backlight timeout

* 🔊 **Boot Animation and Sound Toggle**
  Enable/disable Acer's startup animations and sounds

* 💻 **Live System Info Display**
  Shows real-time performance profile, fan settings, calibration state, and more

* 🧠 **Smart Daemon (Low Resource Use)**

  * Auto-detects feature support per device
  * Communicates with GUI in real-time
  * Lightweight: uses \~10MB RAM
  * Can run **independently** of GUI
  * Recursive restart to fix software issues similar to those on Windows

* 🖥️ **Modern GUI**

  * Avalonia-based, clean and responsive
  * Realtime Monitoring with Dashboard and accurate Tempreature Readings
  * Dynamic UI hides unsupported features
  * Real-time feedback from daemon

## 🧭 Compatibility

Check your device's compatibility here: [Compatibility List](https://github.com/PXDiv/Div-Acer-Manager-Max/blob/main/Compatibility.md)

> Even if not here, DAMX will still work on most devices. Please file a issue on Linuwu-sense page (it will be ignored here)

## 🖥️ DAMX Installation Guide

1. Download the latest release from the **[Releases](https://github.com/PXDiv/Div-Acer-Manager-Max/releases)** section.

2. Extract the downloaded package.

3. Make the `setup.sh` script executable:

   ```bash
   chmod +x setup.sh
   ```

4. Run the script:

   * Right-click the setup file and choose **“Run in Terminal”**,
     or open a terminal in the folder and run:

     ```bash
     ./setup.sh
     ```

5. When prompted, choose an option from the menu:

   * `1` → Install
   * `2` → Install without Drivers
   * `3` → Uninstall
   * `4` → Reinstall/Update

6. Reboot your system after the installation completes.

✅ That’s it—you’re all set!

## 🔘 Nitro / PredatorSense Button

During setup you can bind your laptop's dedicated button (the **N** key on
Nitros, the **PredatorSense** key on Predators) to open DAMX. Both are the
same button as far as the EC is concerned — it sends scancode `0xf5`, which
the kernel maps to keycode `425` on most models. Some models carry a udev
hwdb quirk that remaps it to `prog1` (`148`) instead, which is why setup
captures the code from an actual press rather than assuming one.

Confirmed so far: Nitro ANV16S-41, Predator PHN16S-71 (both `425`).

**Note for keyd/kmonad users:** key remappers grab the keyboard exclusively,
so the detection service never sees the button. Bind the key in the
remapper's config instead (after keyd it typically surfaces as `f16` /
`XF86Launch7`).

## 🖥️ Troubleshooting

You can check the logs at /var/log/DAMX_Daemon_Log.log

If you get UNKNOWN as Laptop type, try restarting (it happens sometimes)
But if it still happens that might mean the Drivers Installation failed, Make sure you have the appropriate kernel headers to compile the drivers.

Also, check out the [FAQ page](https://github.com/PXDiv/Div-Acer-Manager-Max/blob/main/FAQ.md) before opening any issues.

Please open a new issue or discussion and include the logs to get support and help the project grow if you need any info, report a bug or just give ideas for the future versions of DAMX

## Screenshots

![image](https://github.com/user-attachments/assets/10d44e8c-14e4-4441-b60c-538af1840cf6)
![image](https://github.com/user-attachments/assets/89217b26-b94c-4c78-8fe8-3de2b22a7095)
![image](https://github.com/user-attachments/assets/72a7b944-5efc-4520-83b6-88069fc05723)
![image](https://github.com/user-attachments/assets/f9a9d663-70c6-482e-a0c4-15a4ea08a8d2)

## ❤️ Powered by Linuwu

The custom drivers for this project [Div-Linuwu Sense project](https://github.com/PXDiv/Div-Linuwu-Sense) is built entirely on top of the [Linuwu Sense](https://github.com/0x7375646F/Linuwu-Sense) drivers — huge thanks to their developers for enabling hardware-level access on Acer laptops.

## 🤝 Contributing

* Report bugs or request features via GitHub Issues
* Submit pull requests to improve code or UI
* Help test on different Acer laptop models

## 📄 License

This project is licensed under the **GNU General Public License v3.0**.  
See the [LICENSE](LICENSE) file for details.
