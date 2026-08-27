#!/bin/bash
set -euo pipefail

if [ -f "/etc/damx/nitro_key.conf" ]; then
    # shellcheck disable=SC1091
    source /etc/damx/nitro_key.conf
else
    NITRO_KEY=425
fi

find_target_user() {
    if [ -n "${DAMX_TARGET_USER:-}" ] && id -u "$DAMX_TARGET_USER" >/dev/null 2>&1; then
        echo "$DAMX_TARGET_USER"
        return 0
    fi

    if [ -n "${SUDO_USER:-}" ] && [ "$SUDO_USER" != "root" ] && id -u "$SUDO_USER" >/dev/null 2>&1; then
        echo "$SUDO_USER"
        return 0
    fi

    if command -v loginctl >/dev/null 2>&1; then
        loginctl list-sessions --no-legend 2>/dev/null | awk '$3 != "root" && $3 != "gdm" { print $3; exit }'
        return 0
    fi

    awk -F: '$3 >= 1000 && $3 < 60000 && $1 != "nobody" { print $1; exit }' /etc/passwd
}

DEVICE=$(grep -A 5 -B 5 "AT Translated Set 2 keyboard" /proc/bus/input/devices | grep -m 1 "event" | sed 's/.*event\([0-9]\+\).*/\/dev\/input\/event\1/')

if [ -z "$DEVICE" ]; then
    echo "Error: Could not find keyboard device."
    exit 1
fi

if ! command -v evtest >/dev/null 2>&1; then
    echo "Error: evtest is required for Nitro key detection. Re-run setup or install evtest."
    exit 1
fi

TARGET_USER=$(find_target_user)
if [ -z "$TARGET_USER" ] || ! id -u "$TARGET_USER" >/dev/null 2>&1; then
    echo "Error: could not determine target desktop user for launching DAMX."
    exit 1
fi

USER_ID=$(id -u "$TARGET_USER")

DISPLAY=""
XAUTHORITY=""
WAYLAND_DISPLAY=""

# Wait (up to 60s) for a target-user process with useful graphical session env.
for _ in $(seq 1 60); do
    while IFS= read -r pid; do
        if [ -r "/proc/$pid/environ" ]; then
            ENV_DISPLAY=$(tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep '^DISPLAY=' | cut -d= -f2- || true)
            ENV_WAYLAND=$(tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep '^WAYLAND_DISPLAY=' | cut -d= -f2- || true)
            ENV_XAUTH=$(tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep '^XAUTHORITY=' | cut -d= -f2- || true)

            if [ -n "$ENV_DISPLAY" ]; then
                DISPLAY="$ENV_DISPLAY"
            fi
            if [ -n "$ENV_WAYLAND" ]; then
                WAYLAND_DISPLAY="$ENV_WAYLAND"
            fi
            if [ -n "$ENV_XAUTH" ] && [ -f "$ENV_XAUTH" ]; then
                XAUTHORITY="$ENV_XAUTH"
            fi

            if [ -n "$DISPLAY" ] || [ -n "$WAYLAND_DISPLAY" ]; then
                break 2
            fi
        fi
    done < <(pgrep -u "$TARGET_USER" || true)
    sleep 1
done

# If no XAUTHORITY var was found in environ, fall back to common locations.
if [ -z "$XAUTHORITY" ]; then
    for candidate in "/run/user/$USER_ID/gdm/Xauthority" "/home/$TARGET_USER/.Xauthority" /run/user/$USER_ID/.mutter-Xwaylandauth.*; do
        if [ -f "$candidate" ]; then
            XAUTHORITY="$candidate"
            break
        fi
    done
fi

: "${DISPLAY:=:0}"
: "${WAYLAND_DISPLAY:=wayland-0}"

if [ -z "$XAUTHORITY" ] || [ ! -f "$XAUTHORITY" ]; then
    echo "Warning: could not find a valid XAUTHORITY file."
fi

export DISPLAY
export WAYLAND_DISPLAY
export XAUTHORITY
export XDG_RUNTIME_DIR="/run/user/$USER_ID"
export DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$USER_ID/bus"

echo "Monitoring Nitro/PredatorSense button (code $NITRO_KEY) on $DEVICE for user $TARGET_USER..."
echo "Using DISPLAY=$DISPLAY WAYLAND_DISPLAY=$WAYLAND_DISPLAY XAUTHORITY=$XAUTHORITY"

evtest "$DEVICE" | grep --line-buffered "code $NITRO_KEY.*value 1" | while read -r _line; do
    if ! pgrep -f "/opt/damx/gui/DivAcerManagerMax" > /dev/null; then
        sudo -u "$TARGET_USER" \
            DISPLAY="$DISPLAY" \
            WAYLAND_DISPLAY="$WAYLAND_DISPLAY" \
            XAUTHORITY="$XAUTHORITY" \
            XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" \
            DBUS_SESSION_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS" \
            DAMX &
    else
        echo "Interface is already running!"
    fi
done
