#!/bin/bash

# FIX 1: Target the internal motherboard keyboard directly (AT Translated) instead of generic "keyboard"
DEVICE=$(grep -A 5 -B 5 "AT Translated Set 2 keyboard" /proc/bus/input/devices | grep -m 1 "event" | sed 's/.*event\([0-9]\+\).*/\/dev\/input\/event\1/')

if [ -z "$DEVICE" ]; then
    echo "Error: Could not find keyboard device."
    exit 1
fi

echo "Monitoring Nitro button (code 425) on $DEVICE..." #require sudo

# FIX 2: Run evtest with sudo, but execute GUI as normal user, added spam protection
sudo evtest "$DEVICE" | grep --line-buffered "code 425.*value 1" | while read -r line; do
    
    if ! pgrep -x "DivAcerManagerMax" > /dev/null && ! pgrep -x "DAMX" > /dev/null; then
        DAMX &
    else
        echo "Interface is already running!"
    fi
done