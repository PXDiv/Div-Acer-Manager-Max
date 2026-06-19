# RGB Daemon Feature Plan

This document tracks the daemon-side work that is intentionally not implemented in the current C#-only RGB cleanup.

## Current State

The GUI currently uses the daemon commands that already exist:

- `set_per_zone_mode` for static four-zone colors.
- `set_four_zone_mode` for animated effects.

The C# layer now routes static and dynamic modes correctly without requiring daemon protocol changes.

## Future Daemon Feature: `set_extended_rgb`

Add one high-level daemon command that accepts the complete RGB intent from the GUI:

```json
{
  "command": "set_extended_rgb",
  "params": {
    "mode": 0,
    "brightness": 100,
    "speed": 4,
    "direction": 2,
    "zones": {
      "1": { "r": 52, "g": 211, "b": 153 },
      "2": { "r": 244, "g": 63, "b": 94 },
      "3": { "r": 59, "g": 130, "b": 246 },
      "4": { "r": 168, "g": 85, "b": 247 }
    },
    "globalColor": {
      "r": 52,
      "g": 211,
      "b": 153
    }
  }
}
```

## Required Daemon Behavior

- Validate `mode`.
  - `0` means static zone color mode.
  - `1-5` means dynamic effect mode.
- Validate `brightness` as `0-100`.
- Validate `speed` as `0-9`.
- Validate `direction` as `1-2`.
  - `1` means right-to-left.
  - `2` means left-to-right.
- Validate every RGB component as `0-255`.
- If `mode == 0`:
  - Convert `zones.1` through `zones.4` to six-character RGB hex strings.
  - Route to the existing `set_per_zone_mode(...)` daemon method.
- If `mode > 0`:
  - Use `globalColor`.
  - Route to the existing `set_four_zone_mode(...)` daemon method.

## C# Follow-Up After Daemon Support Lands

- Add `SetExtendedRgbAsync(...)` to `DAMXClient`.
- Update the GUI to call `set_extended_rgb` instead of choosing between `set_per_zone_mode` and `set_four_zone_mode`.
- Keep the existing C# mapper as the UI-side validation/preparation layer.
- Keep the old daemon commands for compatibility unless there is a deliberate protocol version bump.

## Out Of Scope For This Step

- Alternate driver abstraction, including `facer.ko`.
- Hardware verification on real `/sys/module/linuwu_sense/...` nodes.
