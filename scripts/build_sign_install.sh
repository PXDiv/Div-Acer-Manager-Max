#!/bin/bash

# ==========================================
#  DAMX Driver Builder - Multi-Distro Support
# ==========================================
# Supports: Debian, Ubuntu, Fedora, RHEL, Arch, Manjaro, CachyOS

# --- 0. AUTO-ELEVATE TO ROOT ---
# If the user didn't run with sudo, ask for password and re-run as root automatically
if [ "$EUID" -ne 0 ]; then
  echo "🔒 Root privileges are required to sign drivers."
  echo "🔑 Please enter your password to continue..."
  exec sudo "$0" "$@"
  exit $?
fi

# --- 0.5. DETECT DISTRIBUTION ---
detect_distro() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        DISTRO_ID="$ID"
        DISTRO_NAME="$NAME"
    else
        echo "❌ Error: Cannot detect Linux distribution"
        exit 1
    fi
}

detect_distro

# Classify distro family
if [[ "$DISTRO_ID" =~ ^(debian|ubuntu|linuxmint|pop)$ ]]; then
    DISTRO_FAMILY="debian"
    COMPILER="gcc"
elif [[ "$DISTRO_ID" =~ ^(fedora|rhel|centos)$ ]]; then
    DISTRO_FAMILY="fedora"
    COMPILER="gcc"
elif [[ "$DISTRO_ID" =~ ^(arch|manjaro|cachyos)$ ]]; then
    DISTRO_FAMILY="arch"
    COMPILER="clang"
else
    echo "⚠️  Unknown distribution: $DISTRO_NAME"
    echo "   Attempting to proceed with GCC (may not work optimally)"
    DISTRO_FAMILY="unknown"
    COMPILER="gcc"
fi

echo "🔍 Detected Distribution: $DISTRO_NAME ($DISTRO_FAMILY family)"
echo "🔨 Using compiler: $COMPILER"

# --- 1. CONFIGURATION & CHECKS ---
KEY=$(find /var/lib /usr/share /etc -name "db.key" 2>/dev/null | head -n 1)
CERT=$(find /var/lib /usr/share /etc -name "db.pem" 2>/dev/null | head -n 1)
SIGN_TOOL="/usr/lib/modules/$(uname -r)/build/scripts/sign-file"
DRIVER_NAME="linuwu_sense"
DRIVER_FILE="./src/${DRIVER_NAME}.ko"

echo "🔧 Preparing to build driver..."

if [[ -z "$KEY" || -z "$CERT" ]]; then
    echo "❌ Error: Secure Boot keys (db.key/db.pem) not found."
    echo "   Ensure you have sbctl installed and keys generated."
    echo ""
    echo "📋 To generate Secure Boot keys, run:"
    if [[ "$DISTRO_FAMILY" == "debian" ]]; then
        echo "   sudo apt install sbsigntools mokutil"
    elif [[ "$DISTRO_FAMILY" == "fedora" ]]; then
        echo "   sudo dnf install pesign mokutil"
    elif [[ "$DISTRO_FAMILY" == "arch" ]]; then
        echo "   sudo pacman -S sbctl"
    fi
    exit 1
fi

# --- 2. INSTALL BUILD DEPENDENCIES ---
install_dependencies() {
    if [[ "$DISTRO_FAMILY" == "debian" ]]; then
        echo "📦 Checking build dependencies for Debian/Ubuntu..."
        MISSING_PACKAGES=()

        for pkg in build-essential libelf-dev sbsigntools mokutil; do
            if ! dpkg -l | grep -q "^ii  $pkg"; then
                MISSING_PACKAGES+=("$pkg")
            fi
        done

        # Check for linux-headers
        if ! dpkg -l | grep -q "^ii  linux-headers"; then
            MISSING_PACKAGES+=("linux-headers-$(uname -r)")
        fi

        if [ ${#MISSING_PACKAGES[@]} -gt 0 ]; then
            echo "   Installing missing: ${MISSING_PACKAGES[*]}"
            apt update
            apt install -y "${MISSING_PACKAGES[@]}"
        else
            echo "   ✅ All dependencies already installed."
        fi

    elif [[ "$DISTRO_FAMILY" == "fedora" ]]; then
        echo "📦 Checking build dependencies for Fedora/RHEL..."
        MISSING_PACKAGES=()

        for pkg in gcc kernel-devel kernel-headers elfutils-devel pesign mokutil; do
            if ! rpm -q "$pkg" &>/dev/null; then
                MISSING_PACKAGES+=("$pkg")
            fi
        done

        if [ ${#MISSING_PACKAGES[@]} -gt 0 ]; then
            echo "   Installing missing: ${MISSING_PACKAGES[*]}"
            dnf install -y "${MISSING_PACKAGES[@]}"
        else
            echo "   ✅ All dependencies already installed."
        fi

    elif [[ "$DISTRO_FAMILY" == "arch" ]]; then
        echo "📦 Checking build dependencies for Arch/Manjaro/CachyOS..."
        MISSING_PACKAGES=()

        for pkg in base-devel linux-headers sbctl; do
            if ! pacman -Q "$pkg" &>/dev/null; then
                MISSING_PACKAGES+=("$pkg")
            fi
        done

        # For CachyOS with clang
        if [[ "$DISTRO_ID" == "cachyos" ]]; then
            for pkg in clang llvm lld; do
                if ! pacman -Q "$pkg" &>/dev/null; then
                    MISSING_PACKAGES+=("$pkg")
                fi
            done
        fi

        if [ ${#MISSING_PACKAGES[@]} -gt 0 ]; then
            echo "   Installing missing: ${MISSING_PACKAGES[*]}"
            pacman -Sy --noconfirm "${MISSING_PACKAGES[@]}"
        else
            echo "   ✅ All dependencies already installed."
        fi
    fi
}

install_dependencies

# --- 2.5. DETECT LLVM KERNEL ---
# Check if the kernel was compiled with LLVM/Clang
is_llvm_kernel() {
    local kernel_version=$(uname -r)

    # Method 1: Check kernel build info from modinfo (most reliable)
    if modinfo -F vermagic 2>/dev/null | grep -qi "gcc\|clang"; then
        modinfo -F vermagic 2>/dev/null | grep -qi "clang" && return 0
    fi

    # Method 2: Check /proc/version (fallback, works with CachyOS)
    if grep -qi "clang" /proc/version 2>/dev/null; then
        return 0
    fi

    # Method 3: Try to find and read kernel config
    local kernel_config
    if [[ -f "/boot/config-${kernel_version}" ]]; then
        kernel_config="/boot/config-${kernel_version}"
    elif [[ -f "/proc/config.gz" ]]; then
        if zgrep -q "CONFIG_CC_IS_CLANG=y" /proc/config.gz 2>/dev/null; then
            return 0
        fi
        return 1
    else
        # Last resort: check /sys/kernel/config if accessible
        if [[ -f "/sys/kernel/config/CC_IS_CLANG" ]] && grep -q "y" /sys/kernel/config/CC_IS_CLANG 2>/dev/null; then
            return 0
        fi
        # Default to non-LLVM if unable to determine
        return 1
    fi

    # Check the config file
    if [[ -f "$kernel_config" ]] && grep -q "CONFIG_CC_IS_CLANG=y" "$kernel_config" 2>/dev/null; then
        return 0
    fi

    return 1
}

# --- 3. CLEAN & BUILD ---
echo "🧹 Cleaning previous builds..."
make clean >/dev/null 2>&1

# Build with appropriate compiler for the kernel
echo "🔨 Building driver..."

# Additional check: scan kernel messages for compiler info
check_kernel_compiler() {
    # Check if kernel reports being built with clang
    if strings /lib/modules/$(uname -r)/build/vmlinux 2>/dev/null | grep -qi "clang version"; then
        return 0
    fi
    # Check kernel version string more carefully
    if uname -v 2>/dev/null | grep -qi "clang"; then
        return 0
    fi
    return 1
}

if is_llvm_kernel || check_kernel_compiler; then
    echo "   Detected LLVM-compiled kernel, using Clang..."
    install_dependencies  # Ensure clang is installed
    make clean LLVM=1 CC=clang
    make LLVM=1 CC=clang
else
    echo "   Using GCC (default)..."
    make clean
    make
fi

if [[ ! -f "$DRIVER_FILE" ]]; then
    echo "❌ Build failed. $DRIVER_FILE not found."
    # Fallback check
    if [[ -f "./${DRIVER_NAME}.ko" ]]; then
        DRIVER_FILE="./${DRIVER_NAME}.ko"
        echo "⚠️  Found driver in root folder instead. Proceeding..."
    else
        echo "   (Checked both ./src/ and ./ for .ko file)"
        exit 1
    fi
fi

# --- 4. SIGN DRIVER FILE ---
echo "🔐 Signing driver file: $DRIVER_FILE..."

# Different signing methods based on distro
sign_driver() {
    # All distros use the kernel's sign-file script for module signing
    if [[ -f "$SIGN_TOOL" ]]; then
        if [[ -z "$KEY" || -z "$CERT" ]]; then
            echo "⚠️  Warning: Secure Boot keys not found, skipping signature..."
            return 0
        fi

        echo "   Using sign-file script (kernel native)..."
        if "$SIGN_TOOL" sha256 "$KEY" "$CERT" "$DRIVER_FILE" 2>&1; then
            return 0
        else
            echo "⚠️  Warning: Signing attempt had issues, proceeding anyway..."
            return 0
        fi
    else
        echo "⚠️  Warning: sign-file tool not found at $SIGN_TOOL"
        echo "   This usually means kernel headers may not be properly installed."
        echo "   Proceeding without signing..."
        return 0
    fi
}

# Call the signing function
sign_driver
echo "✅ Driver preparation complete."

sign_driver
# --- 5. INSTALL DRIVER ---
echo "📦 Installing driver..."
TARGET_DIR="/usr/lib/modules/$(uname -r)/extra"
mkdir -p "$TARGET_DIR"
cp "$DRIVER_FILE" "$TARGET_DIR/"
depmod -a

# --- 6. RELOAD MODULE ---
echo "♻️  Reloading module..."
modprobe -r "$DRIVER_NAME" 2>/dev/null
modprobe "$DRIVER_NAME" 2>/dev/null

# --- 7. FINAL STATUS & RESTART NOTICE ---
INSTALLED_PATH=$(modinfo -n "$DRIVER_NAME" 2>/dev/null)

echo ""
echo "============================================"
if modinfo "$INSTALLED_PATH" 2>/dev/null | grep -q "signer"; then
    echo "✅ SUCCESS: Driver installed and SIGNED."
    SIGNED=1
else
    echo "⚠️  WARNING: Driver installed but signature verification pending."
    SIGNED=0
fi
echo "============================================"
echo ""
echo "📋 DISTRIBUTION INFO:"
echo "   Distribution: $DISTRO_NAME"
echo "   Family: $DISTRO_FAMILY"
echo "   Compiler: $COMPILER"
echo "   Kernel: $(uname -r)"
echo ""
echo "📢 IMPORTANT NEXT STEPS:"
echo "1. 🔄 YOU MUST RESTART YOUR MACHINE NOW for the signature to fully take effect."
if [[ "$SIGNED" == 1 ]]; then
    echo "2. After rebooting, go back to the main folder and run './setup.sh'"
    echo "   Choose Option 2 (Install without Drivers)."
else
    echo "2. After rebooting, enroll the Secure Boot key if prompted."
    echo "3. Then run './setup.sh' and choose Option 2 (Install without Drivers)."
fi
echo ""
