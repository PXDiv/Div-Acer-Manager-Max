# PHN16-72 Static keyboard lighting

On Predator PHN16-72 with BIOS V1.16, selecting Static could leave the
keyboard dark although the firmware reported the requested mode and colors.
A local workaround using Breathing did not produce a fixed light.

The companion Linuwu-Sense fix applies this transaction:

1. Poll Gaming WMI method 5 with an eight-byte zero scalar.
2. Enable all four keyboard zones with method 2 and scalar `0x00000f0000000008`.
3. Write each zone's RGB using method 6.
4. Commit method 20 with mode 0, the selected brightness, engine 0 and keyboard selector 1.
5. Verify the committed Static mode, brightness and engine with method 21.

Zone colors can then be read back using method 7 through `per_zone_mode`.

This sequence follows [ASense's zoned implementation](https://github.com/fladirm/asense/blob/main/kernel/asense_rgb.c)
and the ACPI methods on the tested PHN16-72. The driver uses eight-byte zone
inputs because BIOS V1.16 also reads the unused byte offsets 4 and 5.
The protocol quirk applies only to PHN16-72; animated modes retain engine 3.

DAMX uses its existing `set_per_zone_mode` command to apply a uniform Static
color. The GUI reads that color from the zone registers because the global
effect RGB registers are zero in Static. Failed writes show an error and do
not save the failed selection as a preset.

## Installation

Build and load the companion patched `linuwu_sense` module before testing
the GUI. Updating only the GUI does not fix the firmware transaction.
The existing `per_zone_mode` and `four_zone_mode` sysfs interfaces are retained;
no ACPI-call module or firmware helper is required after installation.

## Validation

Tested on Predator PHN16-72, BIOS V1.16, Linux `7.2.5-3-omarchy`:

- Independent ACPI command produced physically confirmed solid red.
- Installed DAMX GUI produced working Static lighting, confirmed by the user.
- Daemon-to-driver readback passed Neon-to-Static, color/brightness changes,
  brightness 0-to-100, four independent zone colors and direct mode 0 writes.
- The GUI Release publish completed successfully with existing warnings.
