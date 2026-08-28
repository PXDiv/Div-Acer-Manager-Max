# PHN16-72 Back Logo / Lightbar Integration Notes

## Root cause

DAMX itself is a user-space GUI + daemon. The rear logo cannot be controlled unless the kernel driver exposes a sysfs node for it.

The driver dependency is implemented by [PXDiv/Div-Linuwu-Sense#20](https://github.com/PXDiv/Div-Linuwu-Sense/pull/20). That PR was tested on a Predator PHN16-72 / EQE_RTX and exposes the sysfs API consumed here.

## Driver API dependency

Expose this sysfs file from the patched `linuwu_sense` driver:

```text
/sys/module/linuwu_sense/drivers/platform:acer-wmi/acer-wmi/back_logo/color
```

Read/write format:

```text
RRGGBB,brightness,enable
```

Example:

```bash
echo 'FFFFFF,100,1' | sudo tee /sys/module/linuwu_sense/drivers/platform:acer-wmi/acer-wmi/back_logo/color
```

When disabled, the driver may normalize the readback to `RRGGBB,0,0`. The GUI therefore retains its last useful brightness value so that enabling the logo does not send an unintended 0% brightness.

## Driver-side implementation

The linked driver PR implements the following pieces:

1. Add a capability flag such as `ACER_CAP_BACK_LOGO`.
2. Add a `back_logo` quirk field and enable it for `Predator PHN16-72` / `EQE_RTX`.
3. Add WMI methods for PHN16-72 logo/lightbar:
   - WMID GUID: `7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56`
   - preferred set path: method/Arg1 `0x0C`
   - preferred get path: method/Arg1 `0x0D`
   - some firmware also gates power via `0x14` selector `2`, so a robust setter should drive both paths.
4. Add a sysfs attribute group:
   - group name: `back_logo`
   - file: `color`
   - write parser: `RRGGBB,brightness,enable`
   - strict validation: RGB = 6 hex chars, brightness = 0..100, enable = 0/1.
5. Register the `back_logo` group only when the capability is present.
6. Keep existing `predator_sense` and `four_zoned_kb` groups unchanged so DAMX fan/profile/keyboard features keep working.

## DAMX-side work included in this patch

This repository patch adds:

- daemon feature detection for `back_logo`
- daemon command `set_back_logo_color`
- daemon `get_all_settings` field `back_logo_color`
- C# client method `SetBackLogoColorAsync`
- GUI card under Keyboard Lighting: color, brightness, enable, apply

The GUI intentionally performs static writes only. Continuous software animations would repeatedly invoke the firmware WMI setter and are outside this integration's safety scope.

## Important safety note

Do not load two Acer WMI drivers at the same time. Patch and use one kernel module path: preferably `linuwu_sense` for DAMX compatibility.
