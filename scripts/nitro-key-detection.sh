#!/bin/bash

if [ -f "/etc/damx/nitro_key.conf" ]; then
    source /etc/damx/nitro_key.conf
else
    NITRO_KEY=425
fi

DEVICE=$(grep -A 5 -B 5 "AT Translated Set 2 keyboard" /proc/bus/input/devices | grep -m 1 "event" | sed 's/.*event\([0-9]\+\).*/\/dev\/input\/event\1/')

if [ -z "$DEVICE" ]; then
    echo "Error: Could not find keyboard device."
    exit 1
fi

echo "Monitoring Nitro button (code $NITRO_KEY) on $DEVICE..."

TARGET_USER="div"
USER_ID=$(id -u "$TARGET_USER")

DISPLAY=""
XAUTHORITY=""

# Wait (up to 60s) for a div-owned process with a valid DISPLAY+XAUTHORITY
for i in $(seq 1 60); do
    for pid in $(pgrep -u "$TARGET_USER"); do
        if [ -r "/proc/$pid/environ" ]; then
            ENV_DISPLAY=$(tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep '^DISPLAY=' | cut -d= -f2-)
            ENV_XAUTH=$(tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep '^XAUTHORITY=' | cut -d= -f2-)
            if [ -n "$ENV_DISPLAY" ] && [ -n "$ENV_XAUTH" ] && [ -f "$ENV_XAUTH" ]; then
                DISPLAY="$ENV_DISPLAY"
                XAUTHORITY="$ENV_XAUTH"
                break 2
            fi
        fi
    done
    sleep 1
done

# If no XAUTHORITY var was found in environ, fall back to common locations
if [ -z "$XAUTHORITY" ]; then
    for candidate in "/run/user/$USER_ID/gdm/Xauthority" "/home/$TARGET_USER/.Xauthority"; do
        if [ -f "$candidate" ]; then
            XAUTHORITY="$candidate"
            break
        fi
    done
fi

: "${DISPLAY:=:0}"

if [ -z "$XAUTHORITY" ] || [ ! -f "$XAUTHORITY" ]; then
    echo "Warning: could not find a valid XAUTHORITY file."
fi

export DISPLAY
export XAUTHORITY
export DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$USER_ID/bus"

echo "Using DISPLAY=$DISPLAY XAUTHORITY=$XAUTHORITY"

sudo evtest "$DEVICE" | grep --line-buffered "code $NITRO_KEY.*value 1" | while read -r line; do
    if ! pgrep -f "/opt/damx/gui/DivAcerManagerMax" > /dev/null; then
        sudo -u "$TARGET_USER" DISPLAY="$DISPLAY" XAUTHORITY="$XAUTHORITY" DBUS_SESSION_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS" DAMX &
    else
        echo "Interface is already running!"
    fi
done