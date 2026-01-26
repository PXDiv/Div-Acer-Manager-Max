# ❓ Frequently Asked Questions (FAQ)

### 🔧 Driver Installation Fails

There are several common reasons for driver installation issues:

1. **Incompatible Kernel Version**
   Linuwu Sense drivers require **Linux kernel 6.13 or later**. If you're running an older kernel, the installation will fail. Please update your kernel before proceeding.

2. **Secure Boot is Enabled**
   Secure Boot prevents unsigned kernel modules from loading. This can cause errors like:

   ```
   modprobe: ERROR: could not insert 'linuwu_sense': Key was rejected by service
   make: Error 1
   ```

   **Solution**: Disable Secure Boot in your BIOS/UEFI settings and try installing again.

3. **Installation Path Contains Spaces**
   If the installation directory contains spaces (e.g., `DAMX-0.8.8 (1)/setup.sh`), the install script may fail.
   **Solution**: Move or rename the directory so it has **no spaces in the path**.

---

### 🧩 GUI is Empty or Showing "Unknown Model" (Even Though Your Model Is Supported)

This is usually caused by model detection issues:

* **Acer Firmware Quirks**: Some Acer devices behave inconsistently during hardware detection.
  **Try restarting your laptop** or **reinstalling the drivers**.

* **Distro Compatibility Issues**:
  The DAMX project is officially tested and supported on **Ubuntu only**. Other Linux distributions might introduce kernel or library incompatibilities, leading to missing features in the GUI.

  For **Arch-based distributions**, make sure the following packages are installed:

  ```bash
  sudo pacman -S linux-headers clang llvm lld
  ```
---

### 🛑 It Shows "Unknown Model" and My Model Isn’t in the Compatibility List

No worries! Your device might still be supported unofficially.

**Steps to try:**

1. Open the **Internals Manager** in the GUI.
2. Start the drivers with one of the following parameters:

   * `nitro_v4` or `predator_v4`
   * Optional: Add `enable_all` to unlock all features (RGB control, LCD override, etc.)
3. If this works, you can **make the parameter persistent** using the Internals Manager.

Want native support?

* Head over to the Div-Linuwu Sense project and **submit your device’s quirk configuration in the driver itself** to help others in the community.

---

### 🛠️ I Want to Add Support for My Own Model – How?

You have two options:

1. **Fork and Modify the Original Project**

   * Clone the original [linuwu-sense](#) repository
   * Add your model-specific quirks/configuration
   * Build and test

1. **Use the Div-Linuwu Sense Fork (Recommended)**

   * This is a more stable, community-friendly fork tailored for **DAMX project integration**
   * Submit your config via a pull request
   * Active and regularly updated, unlike the upstream project
  
- The original developer seems to be busy and has very less time to review or update the code let alone take pull requests. thats why i've forked the project to make sure updates and pull requests get pushed fast.

---

### 🛠️ How do I write the quirk configuration in the driver itself to get native support?
Here's a user-friendly process to add support for a new Acer laptop model to the `linuwu_sense.c` driver:

### Step-by-Step Guide to Add a New Model Quirk

1. **Identify Your Laptop Model**
   - Run `sudo dmidecode -s system-product-name` in terminal to get your exact model name
   - Note down any special features your model has (RGB keyboard, special cooling system, etc.)

2. **Check Existing Quirks**
   - Look through the existing quirk entries in the file (search for `quirk_acer_` to find them)
   - Find one that matches your laptop's capabilities as closely as possible

3. **Create Your Quirk Entry**
   - Add a new entry in the quirks section following this format:
     ```c
     static struct quirk_entry quirk_acer_YOUR_MODEL = {
        .predator_v4 = 1,             // If uses Predator Sense v4
        .nitro_v4 = 1,                // If uses Nitro Sense v4
        .nitro_sense = 1,             // If it does'nt support LCD Override and Boot Animation Sound use nitro_sense
        .four_zone_kb = 1             // If has 4-zone RGB keyboard
     
        //special quirks (only add these if you know what you are doing and your hardware supports it)
        .wireless = 1,                // If has special wireless handling
        .brightness = -1,              // If has brightness control
        .turbo = 1,                   // If has turbo mode (turbo mode is detected by driver itself, you don't explicitly need to enable it, is only needed in specific scenarios and models)
        .cpu_fans = 1,                // Number of CPU fans
        .gpu_fans = 1,                // Number of GPU fans
     };
     ```

4. **Add DMI Match Entry**
   - Add your model to the `acer_quirks` array:
     ```c
     {
         .callback = dmi_matched,
         .ident = "Acer Your Model Name",
         .matches = {
             DMI_MATCH(DMI_SYS_VENDOR, "Acer"),
             DMI_MATCH(DMI_PRODUCT_NAME, "YOUR EXACT MODEL NAME"),
         },
         .driver_data = &quirk_acer_YOUR_MODEL,
     },
     ```

5. **Test Your Changes**
   - Save the driver and its changes in the DAMX's Linuwu Sense Directory

   - Reinstall DAMX(will automatically reinstall the driver):

   - Check dmesg for errors:
     ```bash
     dmesg
     ```
   - Check DAMX for all the features you wanted:
     

6. **Submit Your Changes**
   - Fork the GitHub repository
   - Create a branch for your changes
   - Commit your changes with a descriptive message
   - Create a pull request to the original repository

### Example for a New Model 

*If your model does not require much config and does not have special features like Four zone RGB kayboard you can just add:*
```c
/* Add to acer_quirks array (Starting around line 570): */

   {
         .callback = dmi_matched,
         .ident = "Acer Nitro ANV15-51",
         .matches = {
             DMI_MATCH(DMI_SYS_VENDOR, "Acer"),
             DMI_MATCH(DMI_PRODUCT_NAME, "Nitro ANV15-51"),
         },
            .driver_data = &quirk_acer_nitro, //This line here tells which quirk list (struct) to use, since we want the default nitro_sense quirk, it'll initialize with just the default config 
     },
```
This will just enable the base quirk with defaults of nitro_sense

Here are the default structs:
- For predator_v4 use `.driver_data =  &quirk_acer_predator_v4` // Enables Base Predator_v4, use in predator models. <br>
- For nitro_v4 use `.driver_data =  &quirk_acer_nitro_v4` // Enables Base Nitro_v4, use in nitro models. <br>
- For nitro_sense use `.driver_data = &quirk_acer_nitro` //Used in nitro models without special features like lcd override and boot sound (like ANV15-51). <br>


*For an "Acer Predator AN515-58" with all features:*

```c
 static struct quirk_entry quirk_acer_nitro_an515_58 = {
    .nitro_v4 = 1,
    .four_zone_kb = 1,
 };

/* Then add to acer_quirks array: */
   {
        .callback = dmi_matched,
        .ident = "Acer Nitro AN515-58",
        .matches = {
            DMI_MATCH(DMI_SYS_VENDOR, "Acer"),
            DMI_MATCH(DMI_PRODUCT_NAME, "Nitro AN515-58"),
        },
        .driver_data = &quirk_acer_nitro_an515_58,
    },
```

*For an "Acer Predator PHN16-73" with all special features:*

```c
static struct quirk_entry quirk_acer_predator_phn16_73 = {
    .turbo = 1,
    .cpu_fans = 1,
    .gpu_fans = 1,
    .predator_v4 = 1,
    .four_zone_kb = 1
};

/* Then add to acer_quirks array: */
{
    .callback = dmi_matched,
    .ident = "Acer Predator PHN16-73",
    .matches = {
        DMI_MATCH(DMI_SYS_VENDOR, "Acer"),
        DMI_MATCH(DMI_PRODUCT_NAME, "Predator PHN16-73"),
    },
    .driver_data = &quirk_acer_predator_phn16_73,
},
```

### Troubleshooting Tips

1. If features don't work:
   - Try enabling `enable_all=1` parameter when loading the module to test all capabilities
   - Check kernel logs with `dmesg` for clues

2. If your model isn't detected:
   - Double-check the exact product name in DMI
   - Try wildcards if the name varies slightly between regions

3. For RGB keyboard issues:
   - Ensure `four_zone_kb = 1` is set
   - Check if your keyboard responds to the existing RGB controls

Remember to always keep a backup of your working kernel/driver before making changes!

---

Have more questions or need help?
👉 Feel free to [open an issue](#issues) or join the discussions.
