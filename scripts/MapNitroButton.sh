#!/bin/bash

# Function to set up device permissions
setup_permissions() {
    echo "Setting up permissions..."
    
    # Add user to input group if not already
    if ! groups | grep -q '\binput\b'; then
        echo "Adding user $USER to 'input' group..."
        usermod -a -G input $USER
        echo "Note: You'll need to log out and back in for group changes to take effect."
    fi
    
    # Create udev rule if needed
    UDEV_RULE="/etc/udev/rules.d/99-input.rules"
    if [ ! -f "$UDEV_RULE" ] || ! grep -q 'MODE="0660"' "$UDEV_RULE"; then
        echo "Creating udev rules file..."
        echo 'KERNEL=="event*", SUBSYSTEM=="input", MODE="0660", GROUP="input"' > /tmp/input.rules
        mv /tmp/input.rules "$UDEV_RULE"
        chmod 644 "$UDEV_RULE"
        udevadm control --reload-rules
        udevadm trigger
        echo "Permissions rules updated."
    fi
    
    echo "Permission setup complete."
}

# Find keyboard device
DEVICE=$(grep -A 5 -B 5 "keyboard\|Keyboard" /proc/bus/input/devices | grep -m 1 "event" | sed 's/.*event\([0-9]\+\).*/\/dev\/input\/event\1/')

if [ -z "$DEVICE" ]; then
    echo "Error: Could not find keyboard device."
    exit 1
fi

# Check if we're root (sudo)
if [ "$(id -u)" -eq 0 ]; then
    # Running as root - perform setup then re-exec as normal user
    setup_permissions
    echo "Re-launching as normal user..."
    exec sudo -u $SUDO_USER "$0"
    exit 0
fi

# Check permissions
if [ ! -r "$DEVICE" ]; then
    echo "Error: Cannot read $DEVICE (permission denied)."
    echo "Please run this script with sudo to set up permissions:"
    echo "  sudo $0"
    exit 1
fi

# Main functionality
echo "Monitoring keyboard events on $DEVICE"
echo "Predator models usually use keycode 202 (KEY_PROG3). Nitro models often use 425."
echo "Run 'sudo evtest' and press your launcher key to confirm the keycode for your model."
evtest "$DEVICE" | grep --line-buffered -E "code (202|425).*value 1" | while read -r line; do
    DAMX &
done