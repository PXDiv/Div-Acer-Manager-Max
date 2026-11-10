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

# systemctl show-environment emits plain values or $'...' strings. Decode
# only that data format; never evaluate shell syntax from a user's environment.
decode_environment_value() {
    local encoded="$1" char escape
    local -n decoded_value="$2"
    decoded_value=""
    if [[ "$encoded" != \$\'* ]]; then
        decoded_value="$encoded"
        return 0
    fi
    [[ "$encoded" == *\' && ${#encoded} -ge 3 ]] || return 1
    encoded="${encoded:2:-1}"
    while [ -n "$encoded" ]; do
        char="${encoded:0:1}"
        encoded="${encoded:1}"
        if [ "$char" = "\\" ]; then
            escape="${encoded:0:1}"
            encoded="${encoded:1}"
            case "$escape" in
                "'"|'"'|"\\") char="$escape" ;;
                a|b|f|n|r|t|v) printf -v char '%b' "\\$escape" ;;
                [0-3])
                    encoded="$escape$encoded"
                    [[ "$encoded" =~ ^([0-3][0-7]{2}) ]] || return 1
                    [ "${BASH_REMATCH[1]}" != 000 ] || return 1
                    printf -v char '%b' "\\0${BASH_REMATCH[1]}"
                    encoded="${encoded:3}"
                    ;;
                *) return 1 ;;
            esac
        fi
        decoded_value+="$char"
    done
}

# Resolve credentials for each key press: the service can start before login,
# and a later login can replace the display and its authorization file.
load_session_environment() {
    local session_id session_info session_type session_active
    local session_ids session_env key value graphical_session=false
    DISPLAY=""
    WAYLAND_DISPLAY=""
    XAUTHORITY=""
    XDG_RUNTIME_DIR="/run/user/$USER_ID"
    DBUS_SESSION_BUS_ADDRESS="unix:path=$XDG_RUNTIME_DIR/bus"

    if ! session_ids=$(loginctl list-sessions --no-legend 2>/dev/null); then
        echo "Skipping launch: could not query login sessions."
        return 1
    fi
    while read -r session_id; do
        [ -n "$session_id" ] || continue
        session_type=""
        session_active=""
        if ! session_info=$(loginctl show-session "$session_id" \
            --property=Type --property=Active 2>/dev/null); then
            continue
        fi
        while IFS='=' read -r key value; do
            case "$key" in
                Type) session_type="$value" ;;
                Active) session_active="$value" ;;
            esac
        done <<< "$session_info"
        if [ "$session_active" = "yes" ] &&
            { [ "$session_type" = "wayland" ] || [ "$session_type" = "x11" ]; }; then
            graphical_session=true
            break
        fi
    done < <(awk -v uid="$USER_ID" '$2 == uid { print $1 }' <<< "$session_ids")

    if [ "$graphical_session" != true ]; then
        echo "Skipping launch: no active graphical session for $TARGET_USER."
        return 1
    fi

    # Query the user's manager, never source/eval the returned environment.
    if ! session_env=$(sudo -u "$TARGET_USER" env \
        XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" \
        DBUS_SESSION_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS" \
        timeout 5s systemctl --user show-environment 2>/dev/null); then
        echo "Skipping launch: could not read the user-session environment."
        return 1
    fi
    while IFS='=' read -r key value; do
        case "$key" in
            DISPLAY|WAYLAND_DISPLAY|XAUTHORITY)
                if ! decode_environment_value "$value" value; then
                    echo "Skipping launch: invalid encoding for $key."
                    return 1
                fi
                ;;
            *) continue ;;
        esac
        case "$key" in
            DISPLAY) DISPLAY="$value" ;;
            WAYLAND_DISPLAY) WAYLAND_DISPLAY="$value" ;;
            XAUTHORITY) XAUTHORITY="$value" ;;
        esac
    done <<< "$session_env"

    # This DAMX build uses X11, including XWayland on a Wayland desktop.
    if [ -z "$DISPLAY" ] || [ -z "$XAUTHORITY" ] ||
        ! sudo -u "$TARGET_USER" test -r "$XAUTHORITY"; then
        echo "Skipping launch: current X11 display credentials are unavailable."
        return 1
    fi
    return 0
}

echo "Monitoring Nitro/PredatorSense button (code $NITRO_KEY) on $DEVICE for user $TARGET_USER..."

evtest "$DEVICE" | grep --line-buffered "code $NITRO_KEY.*value 1" | while read -r _line; do
    if ! pgrep -u "$USER_ID" -f "/opt/damx/gui/DivAcerManagerMax" > /dev/null; then
        if ! load_session_environment; then
            continue
        fi
        sudo -u "$TARGET_USER" \
            DISPLAY="$DISPLAY" \
            WAYLAND_DISPLAY="$WAYLAND_DISPLAY" \
            XAUTHORITY="$XAUTHORITY" \
            XDG_RUNTIME_DIR="$XDG_RUNTIME_DIR" \
            DBUS_SESSION_BUS_ADDRESS="$DBUS_SESSION_BUS_ADDRESS" \
            /opt/damx/gui/DivAcerManagerMax &
    else
        echo "Interface is already running!"
    fi
done
