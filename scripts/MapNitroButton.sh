#!/bin/bash

# 1. Read conf file
if [ -f "/etc/damx/nitro.conf" ]; then
    source /etc/damx/nitro.conf
else
    # If can't read file, use default
    NITRO_KEY=425
fi

# 2. Find device
DEVICE=$(grep -A 5 -B 5 "AT Translated Set 2 keyboard" /proc/bus/input/devices | grep -m 1 "event" | sed 's/.*event\([0-9]\+\).*/\/dev\/input\/event\1/')

if [ -z "$DEVICE" ]; then
    echo "Error: Could not find keyboard device."
    exit 1
fi

echo "Monitoring Nitro button (code $NITRO_KEY) on $DEVICE..." # require sudo

# 3. Start keylog
sudo evtest "$DEVICE" | grep --line-buffered "code $NITRO_KEY.*value 1" | while read -r line; do
    if ! pgrep -x "DivAcerManagerMax" > /dev/null && ! pgrep -x "DAMX" > /dev/null; then
        DAMX &
    else
        echo "Interface is already running!"
    fi
done