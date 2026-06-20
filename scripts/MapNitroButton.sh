#!/bin/bash

# --- CONFIGURATION ---
# Command set back to just DAMX as requested
APP_COMMAND="DAMX"
TARGET_KEY_CODE="425"
DEVICE_NAME_FILTER="Acer Wireless Radio Control|Acer WMI hotkeys|AT Translated Set 2 keyboard"

# --- DEVICE DETECTION ---
detect_device() {
    EVENT_ID=$(grep -E -A 4 "Name=\"($DEVICE_NAME_FILTER)\"" /proc/bus/input/devices | grep -o 'event[0-9]\+' | head -n 1)
    if [ -n "$EVENT_ID" ]; then
        echo "/dev/input/$EVENT_ID"
    else
        return 1
    fi
}

# --- MAIN LOGIC ---

DEVICE=$(detect_device)

# 1. Check if device exists
if [ -z "$DEVICE" ]; then
    echo "Error: Could not find the Nitro key device."
    exit 1
fi

# 2. Check Permissions & Apply "newgrp" Workaround
if [ ! -r "$DEVICE" ]; then
    echo "Permission denied reading $DEVICE."
    echo "Checking groups..."

    # Check if user needs to be added to input group
    if ! groups | grep -q '\binput\b'; then
        echo "Adding user $USER to 'input' group..."
        sudo usermod -a -G input $USER

        # Check for udev rules
        UDEV_RULE="/etc/udev/rules.d/99-input-group.rules"
        if [ ! -f "$UDEV_RULE" ]; then
            echo 'KERNEL=="event*", SUBSYSTEM=="input", GROUP="input", MODE="0660"' | sudo tee "$UDEV_RULE" > /dev/null
            sudo udevadm control --reload-rules
            sudo udevadm trigger
        fi

        echo "Reloading script with new group permissions..."
        # This re-runs the script with the new 'input' group active immediately
        exec sg input -c "$0 $@"
        exit 0
    fi

    # If we are already in the group but still can't read, force udev reload
    echo "User is in 'input' group but still cannot read device."
    echo "Attempting to force udev reload..."
    echo 'KERNEL=="event*", SUBSYSTEM=="input", GROUP="input", MODE="0660"' | sudo tee "/etc/udev/rules.d/99-input-group.rules" > /dev/null
    sudo udevadm control --reload-rules
    sudo udevadm trigger

    # Final check
    if [ ! -r "$DEVICE" ]; then
        echo "Still cannot read device. A reboot is recommended."
        exit 1
    fi
fi

# 3. Monitoring Loop
echo "Listening on $DEVICE for Nitro Key..."

evtest "$DEVICE" | \
grep --line-buffered "code $TARGET_KEY_CODE" | \
while read -r line; do
    if echo "$line" | grep -q "value 1"; then
        # Check if DAMX is already running (matches process name)
        if pgrep -x "DAMX" > /dev/null || pgrep -f "DivAcerManagerMax" > /dev/null; then
            echo "DAMX is already running."
        else
            echo "Launching DAMX..."
            nohup "$APP_COMMAND" >/dev/null 2>&1 &
            sleep 2
        fi
    fi
done
