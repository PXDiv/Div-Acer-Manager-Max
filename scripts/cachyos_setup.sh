#!/bin/bash

# ==========================================
#  DAMX Driver Builder for CachyOS (Clang)
# ==========================================

# --- 0. AUTO-ELEVATE TO ROOT ---
if [ "$EUID" -ne 0 ]; then
  echo "🔒 Root privileges are required to sign drivers."
  echo "🔑 Please enter your password to continue..."
  exec sudo "$0" "$@"
  exit $?
fi

# --- 1. CONFIGURATION & CHECKS ---
KEY=$(find /var/lib /usr/share /etc -name "db.key" 2>/dev/null | head -n 1)
CERT=$(find /var/lib /usr/share /etc -name "db.pem" 2>/dev/null | head -n 1)
SIGN_TOOL="/usr/lib/modules/$(uname -r)/build/scripts/sign-file"
DRIVER_NAME="linuwu_sense"
# Assuming script is run from inside Linuwu-Sense folder or we navigate there.
# But if this is in scripts/ folder, we might need to adjust. 
# Ideally, this script should be moved to Linuwu-Sense folder by the user or run from there.
# For safety, let's assume the user runs it inside the driver folder as per instructions.
DRIVER_FILE="./src/${DRIVER_NAME}.ko"

echo "🔧 Preparing to build for CachyOS..."

if [[ -z "$KEY" || -z "$CERT" ]]; then
    echo "❌ Error: Secure Boot keys (db.key/db.pem) not found."
    echo "   Ensure you have sbctl installed and keys generated."
    exit 1
fi

# --- 2. CLEAN & BUILD (Using Clang/LLD) ---
echo "🧹 Cleaning previous builds..."
make clean >/dev/null 2>&1

echo "🔨 Building driver with Clang..."
make CC=clang LD=ld.lld

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

# --- 3. SIGN LOCAL FILE ---
echo "🔐 Signing driver file: $DRIVER_FILE..."
"$SIGN_TOOL" sha256 "$KEY" "$CERT" "$DRIVER_FILE"

if modinfo "$DRIVER_FILE" | grep -q "signer"; then
    echo "✅ Signature applied successfully."
else
    echo "❌ Error: Failed to sign the driver."
    exit 1
fi

# --- 4. INSTALL ---
echo "📦 Installing driver..."
TARGET_DIR="/usr/lib/modules/$(uname -r)/extra"
mkdir -p "$TARGET_DIR"
cp "$DRIVER_FILE" "$TARGET_DIR/"
depmod -a

# --- 5. RELOAD ---
echo "♻️  Reloading module..."
modprobe -r "$DRIVER_NAME" 2>/dev/null
modprobe "$DRIVER_NAME" 2>/dev/null

# --- 6. FINAL STATUS & NEXT STEPS ---
INSTALLED_PATH=$(modinfo -n "$DRIVER_NAME" 2>/dev/null)
echo ""
echo "============================================"
if modinfo "$INSTALLED_PATH" 2>/dev/null | grep -q "signer"; then
    echo "✅ SUCCESS: Driver installed and SIGNED."
else
    echo "⚠️  WARNING: Driver installed but signature verification failed."
fi
echo "============================================"
echo ""
echo "📢 IMPORTANT NEXT STEPS:"
echo "--------------------------------------------------------"
echo "👉 SCENARIO A: NEW INSTALLATION (First time setup)"
echo "   1. Go back to the main folder:  cd .."
echo "   2. Run the installer:           ./setup.sh"
echo "   3. CRITICAL: Choose Option 2 (Install without Drivers)"
echo "      (This prevents overwriting your signed driver)"
echo ""
echo "👉 SCENARIO B: UPDATING EXISTING DRIVER"
echo "   1. You are done! Just RESTART your computer now."
echo "--------------------------------------------------------"
echo ""
