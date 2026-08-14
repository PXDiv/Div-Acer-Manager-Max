#!/bin/bash

# DAMX Remote Installer Script
# This script downloads and installs the latest DAMX Suite for Acer laptops on Linux
# Usage: curl -sSL https://raw.githubusercontent.com/PXDiv/Div-Acer-Manager-Max/main/remote-setup.sh | bash

# Constants
SCRIPT_VERSION="1.0.0"
GITHUB_REPO="PXDiv/Div-Acer-Manager-Max"
INSTALL_DIR="/opt/damx"
BIN_DIR="/usr/local/bin"
SYSTEMD_DIR="/etc/systemd/system"
DAEMON_SERVICE_NAME="damx-daemon.service"
DESKTOP_FILE_DIR="/usr/share/applications"
ICON_DIR="/usr/share/icons/hicolor/256x256/apps"
TEMP_DIR="/tmp/damx-install-$$"
MODULE_SIGNING_DIR="/var/lib/damx/secureboot"
MODULE_SIGNING_KEY="${MODULE_SIGNING_DIR}/DAMX-MOK.priv"
MODULE_SIGNING_CERT="${MODULE_SIGNING_DIR}/DAMX-MOK.der"
MODULE_SIGNING_REQUIRED=false

# Legacy paths for cleanup (uppercase naming convention)
LEGACY_INSTALL_DIR="/opt/DAMX"
LEGACY_DAEMON_SERVICE_NAME="DAMX-Daemon.service"

# Colors for terminal output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to pause script execution
pause() {
  echo -e "${BLUE}Press any key to continue...${NC}"
  read -n 1 -s -r
}

# Function to check and elevate privileges
check_root() {
  if [ "$EUID" -ne 0 ]; then
    echo -e "${YELLOW}This script requires root privileges.${NC}"

    # Check if sudo is available
    if command -v sudo &> /dev/null; then
      echo -e "${BLUE}Attempting to run with sudo...${NC}"
      exec sudo "$0" "$@"
      exit $?
    else
      echo -e "${RED}Error: sudo not found. Please run this script as root.${NC}"
      pause
      exit 1
    fi
  fi
}

print_banner() {
  clear
  echo -e "${BLUE}==========================================${NC}"
  echo -e "${BLUE}    DAMX Remote Installer v${SCRIPT_VERSION}     ${NC}"
  echo -e "${BLUE}    Acer Laptop WMI Controls for Linux  ${NC}"
  echo -e "${BLUE}==========================================${NC}"
  echo ""
}

# Function to check required tools
check_dependencies() {
  echo -e "${YELLOW}Checking dependencies...${NC}"
  
  local missing_deps=()
  
  # Check for required tools
  if ! command -v curl &> /dev/null; then
    missing_deps+=("curl")
  fi
  
  if ! command -v tar &> /dev/null; then
    missing_deps+=("tar")
  fi
  
  if ! command -v jq &> /dev/null; then
    missing_deps+=("jq")
  fi
  
  # Install missing dependencies
  if [ ${#missing_deps[@]} -gt 0 ]; then
    echo -e "${YELLOW}Installing missing dependencies: ${missing_deps[*]}${NC}"
    
    # Detect package manager and install
    if command -v apt-get &> /dev/null; then
      apt-get update && apt-get install -y "${missing_deps[@]}"
    elif command -v yum &> /dev/null; then
      yum install -y "${missing_deps[@]}"
    elif command -v dnf &> /dev/null; then
      dnf install -y "${missing_deps[@]}"
    elif command -v pacman &> /dev/null; then
      pacman -S --noconfirm "${missing_deps[@]}"
    elif command -v zypper &> /dev/null; then
      zypper install -y "${missing_deps[@]}"
    else
      echo -e "${RED}Error: Cannot install dependencies automatically. Please install: ${missing_deps[*]}${NC}"
      exit 1
    fi
  fi
  
  echo -e "${GREEN}Dependencies check completed.${NC}"
}

# Function to get latest release info
get_latest_release() {
  echo -e "${YELLOW}Fetching latest release information...${NC}"
  
  local api_url="https://api.github.com/repos/${GITHUB_REPO}/releases/latest"
  local release_info
  
  release_info=$(curl -s "$api_url")
  
  if [ $? -ne 0 ] || [ -z "$release_info" ]; then
    echo -e "${RED}Error: Failed to fetch release information from GitHub API${NC}"
    return 1
  fi
  
  # Check if the response contains an error
  if echo "$release_info" | jq -e '.message' &> /dev/null; then
    local error_msg=$(echo "$release_info" | jq -r '.message')
    echo -e "${RED}Error: GitHub API returned: $error_msg${NC}"
    return 1
  fi
  
  # Extract release information
  RELEASE_TAG=$(echo "$release_info" | jq -r '.tag_name')
  RELEASE_NAME=$(echo "$release_info" | jq -r '.name')
  DOWNLOAD_URL=$(echo "$release_info" | jq -r '.assets[] | select(.name | endswith(".tar.xz")) | .browser_download_url')
  CHECKSUM_URL=$(echo "$release_info" | jq -r '.assets[] | select(.name | endswith(".tar.xz.sha256")) | .browser_download_url')
  
  if [ -z "$DOWNLOAD_URL" ] || [ "$DOWNLOAD_URL" = "null" ]; then
    echo -e "${RED}Error: No suitable package found in the latest release${NC}"
    return 1
  fi
  
  echo -e "${GREEN}Latest release found: $RELEASE_NAME${NC}"
  echo -e "Download URL: $DOWNLOAD_URL"
  
  return 0
}

# Function to download and verify package
download_package() {
  echo -e "${YELLOW}Downloading DAMX package...${NC}"
  
  # Create temporary directory
  mkdir -p "$TEMP_DIR"
  cd "$TEMP_DIR"
  
  # Extract filename from URL
  local package_file=$(basename "$DOWNLOAD_URL")
  local checksum_file="${package_file}.sha256"
  
  # Download package
  echo "Downloading $package_file..."
  if ! curl -L -o "$package_file" "$DOWNLOAD_URL"; then
    echo -e "${RED}Error: Failed to download package${NC}"
    return 1
  fi
  
  # Download and verify checksum if available
  if [ -n "$CHECKSUM_URL" ] && [ "$CHECKSUM_URL" != "null" ]; then
    echo "Downloading checksum file..."
    if curl -L -o "$checksum_file" "$CHECKSUM_URL"; then
      echo "Verifying package integrity..."
      if sha256sum -c "$checksum_file"; then
        echo -e "${GREEN}Package integrity verified successfully.${NC}"
      else
        echo -e "${RED}Error: Package integrity check failed${NC}"
        return 1
      fi
    else
      echo -e "${YELLOW}Warning: Could not download checksum file, skipping verification${NC}"
    fi
  else
    echo -e "${YELLOW}Warning: No checksum available, skipping verification${NC}"
  fi
  
  # Extract package
  echo "Extracting package..."
  if ! tar -xJf "$package_file"; then
    echo -e "${RED}Error: Failed to extract package${NC}"
    return 1
  fi
  
  # Find extracted directory
  EXTRACTED_DIR=$(find . -maxdepth 1 -type d -name "DAMX-*" | head -1)
  if [ -z "$EXTRACTED_DIR" ]; then
    echo -e "${RED}Error: Could not find extracted DAMX directory${NC}"
    return 1
  fi
  
  echo -e "${GREEN}Package downloaded and extracted successfully.${NC}"
  return 0
}

# Function to detect and clean up legacy installations
cleanup_legacy_installation() {
  echo -e "${YELLOW}Checking for legacy installations...${NC}"
  local cleanup_performed=false

  # Check for legacy service file (uppercase naming)
  if [ -f "${SYSTEMD_DIR}/${LEGACY_DAEMON_SERVICE_NAME}" ]; then
    echo -e "${BLUE}Found legacy service file: ${LEGACY_DAEMON_SERVICE_NAME}${NC}"

    # Stop the legacy service if it's running
    if systemctl is-active --quiet ${LEGACY_DAEMON_SERVICE_NAME} 2>/dev/null; then
      echo "Stopping legacy service..."
      systemctl stop ${LEGACY_DAEMON_SERVICE_NAME}
    fi

    # Disable the legacy service if it's enabled
    if systemctl is-enabled --quiet ${LEGACY_DAEMON_SERVICE_NAME} 2>/dev/null; then
      echo "Disabling legacy service..."
      systemctl disable ${LEGACY_DAEMON_SERVICE_NAME}
    fi

    # Remove the legacy service file
    echo "Removing legacy service file..."
    rm -f "${SYSTEMD_DIR}/${LEGACY_DAEMON_SERVICE_NAME}"
    cleanup_performed=true
  fi

  # Check for legacy installation directory (uppercase naming)
  if [ -d "${LEGACY_INSTALL_DIR}" ]; then
    echo -e "${BLUE}Found legacy installation directory: ${LEGACY_INSTALL_DIR}${NC}"
    echo "Removing legacy installation directory..."
    rm -rf "${LEGACY_INSTALL_DIR}"
    cleanup_performed=true
  fi

  # Check for other potential legacy artifacts
  local legacy_artifacts=(
    "/usr/local/bin/DAMX-Daemon"
    "/usr/share/applications/DAMX.desktop"
    "/usr/share/icons/hicolor/256x256/apps/DAMX.png"
  )

  for artifact in "${legacy_artifacts[@]}"; do
    if [ -f "$artifact" ] || [ -d "$artifact" ]; then
      echo "Removing legacy artifact: $artifact"
      rm -rf "$artifact"
      cleanup_performed=true
    fi
  done

  # Reload systemd daemon if any service changes were made
  if [ "$cleanup_performed" = true ]; then
    echo "Reloading systemd daemon configuration..."
    systemctl daemon-reload
    echo -e "${GREEN}Legacy installation cleanup completed.${NC}"
  else
    echo -e "${GREEN}No legacy installations found.${NC}"
  fi

  return 0
}

# Function to perform comprehensive cleanup for uninstall/reinstall
comprehensive_cleanup() {
  echo -e "${YELLOW}Performing comprehensive cleanup...${NC}"

  # Stop and disable current daemon service
  if systemctl is-active --quiet ${DAEMON_SERVICE_NAME} 2>/dev/null; then
    echo "Stopping current DAMX-Daemon service..."
    systemctl stop ${DAEMON_SERVICE_NAME}
  fi

  if systemctl is-enabled --quiet ${DAEMON_SERVICE_NAME} 2>/dev/null; then
    echo "Disabling current DAMX-Daemon service..."
    systemctl disable ${DAEMON_SERVICE_NAME}
  fi

  # Remove current service file
  if [ -f "${SYSTEMD_DIR}/${DAEMON_SERVICE_NAME}" ]; then
    echo "Removing current service file..."
    rm -f "${SYSTEMD_DIR}/${DAEMON_SERVICE_NAME}"
  fi

  # Clean up legacy installations
  cleanup_legacy_installation

  # Remove current installed files
  echo "Removing current installation files..."
  rm -rf ${INSTALL_DIR}
  rm -f ${BIN_DIR}/DAMX
  rm -f ${DESKTOP_FILE_DIR}/damx.desktop
  rm -f ${ICON_DIR}/damx.png

  # Final systemd daemon reload
  systemctl daemon-reload

  echo -e "${GREEN}Comprehensive cleanup completed.${NC}"
  return 0
}

# Detect the compiler used to build the running kernel.
is_llvm_kernel() {
  local kernel_release
  local kernel_build
  local config_file

  kernel_release=$(uname -r)
  kernel_build="/lib/modules/${kernel_release}/build"

  for config_file in "/boot/config-${kernel_release}" "${kernel_build}/.config"; do
    if [ -r "$config_file" ] && grep -q '^CONFIG_CC_IS_CLANG=y' "$config_file"; then
      return 0
    fi
  done

  if command -v zgrep &> /dev/null &&
     [ -r /proc/config.gz ] &&
     zgrep -q '^CONFIG_CC_IS_CLANG=y' /proc/config.gz; then
    return 0
  fi

  if grep -qsiE 'clang|llvm' /proc/version 2>/dev/null; then
    return 0
  fi

  if [ -r "${kernel_build}/include/generated/compile.h" ] &&
     grep -qsiE 'clang|llvm' "${kernel_build}/include/generated/compile.h"; then
    return 0
  fi

  # CachyOS kernels are LLVM-built by default. Keep this fallback for
  # installations where the running kernel does not expose its build config.
  if [ -r /etc/os-release ] && grep -qi 'cachyos' /etc/os-release; then
    return 0
  fi

  return 1
}

secure_boot_enabled() {
  local secure_boot_state
  local secure_boot_value
  local secure_boot_var

  if [ -r /sys/module/module/parameters/sig_enforce ] &&
     grep -qiE '^(1|y|yes)$' /sys/module/module/parameters/sig_enforce; then
    return 0
  fi

  if command -v mokutil &> /dev/null; then
    secure_boot_state=$(mokutil --sb-state 2>/dev/null || true)
    if echo "$secure_boot_state" | grep -qi 'SecureBoot enabled'; then
      return 0
    fi
    if echo "$secure_boot_state" | grep -qi 'SecureBoot disabled'; then
      return 1
    fi
  fi

  for secure_boot_var in /sys/firmware/efi/efivars/SecureBoot-*; do
    [ -r "$secure_boot_var" ] || continue
    secure_boot_value=$(od -An -j4 -N1 -t u1 "$secure_boot_var" 2>/dev/null | tr -d ' ')
    if [ "$secure_boot_value" = "1" ]; then
      return 0
    fi
  done

  return 1
}

install_secure_boot_deps() {
  if command -v pacman &> /dev/null; then
    pacman -S --needed --noconfirm mokutil openssl
  elif command -v apt-get &> /dev/null; then
    apt-get update && apt-get install -y mokutil openssl
  elif command -v dnf &> /dev/null; then
    dnf install -y mokutil openssl
  elif command -v yum &> /dev/null; then
    yum install -y mokutil openssl
  elif command -v zypper &> /dev/null; then
    zypper install -y mokutil openssl
  else
    echo -e "${RED}Error: Install mokutil and openssl before continuing.${NC}"
    return 1
  fi
}

prepare_secure_boot_signing() {
  local key_check_output

  MODULE_SIGNING_REQUIRED=false
  if ! secure_boot_enabled; then
    return 0
  fi

  MODULE_SIGNING_REQUIRED=true
  echo -e "${YELLOW}Secure Boot is enabled; the Linuwu-Sense module must be signed.${NC}"

  if ! command -v mokutil &> /dev/null || ! command -v openssl &> /dev/null; then
    echo -e "${YELLOW}Installing Secure Boot signing tools...${NC}"
    install_secure_boot_deps || return 1
  fi

  install -d -m 700 "$MODULE_SIGNING_DIR" || return 1

  if [ ! -s "$MODULE_SIGNING_KEY" ] || [ ! -s "$MODULE_SIGNING_CERT" ]; then
    echo -e "${YELLOW}Generating a DAMX Machine Owner Key...${NC}"
    (
      umask 077
      openssl req -new -x509 -newkey rsa:4096 \
        -keyout "${MODULE_SIGNING_KEY}.new" \
        -addext "extendedKeyUsage=codeSigning" \
        -outform DER \
        -out "${MODULE_SIGNING_CERT}.new" \
        -nodes \
        -days 36500 \
        -subj "/CN=DAMX Linuwu-Sense Module Signing/"
    ) || return 1
    mv -f "${MODULE_SIGNING_KEY}.new" "$MODULE_SIGNING_KEY"
    mv -f "${MODULE_SIGNING_CERT}.new" "$MODULE_SIGNING_CERT"
    chmod 600 "$MODULE_SIGNING_KEY"
    chmod 644 "$MODULE_SIGNING_CERT"
  fi

  key_check_output=$(LC_ALL=C mokutil --test-key "$MODULE_SIGNING_CERT" 2>&1 || true)
  case "$key_check_output" in
    *"is already enrolled"*|*"is already in db"*|*"built-in trusted keyring"*)
      echo -e "${GREEN}DAMX module-signing key is enrolled.${NC}"
      return 0
      ;;
    *"already in the enrollment request"*)
      echo -e "${YELLOW}DAMX MOK enrollment is already pending.${NC}"
      echo -e "${YELLOW}Reboot, complete enrollment in MOK Manager, then run this installer again.${NC}"
      return 2
      ;;
    *"blocked"*)
      echo -e "${RED}Error: The DAMX module-signing key is blocked by Secure Boot policy.${NC}"
      return 1
      ;;
    *"is not enrolled"*)
      ;;
    *)
      echo -e "${RED}Error: Could not determine whether the DAMX signing key is enrolled.${NC}"
      echo "$key_check_output"
      return 1
      ;;
  esac

  if [ ! -t 0 ]; then
    echo -e "${RED}Error: MOK enrollment requires an interactive terminal.${NC}"
    echo "Run this installer directly from a terminal, then reboot and enroll the DAMX key."
    return 1
  fi

  echo -e "${YELLOW}The DAMX signing key must be enrolled before the driver can load.${NC}"
  echo "You will be asked for a one-time password. After rebooting, choose:"
  echo "  Enroll MOK -> Continue -> Yes"
  echo "Then enter the same password and reboot once more."
  if ! mokutil --import "$MODULE_SIGNING_CERT"; then
    echo -e "${RED}Error: Failed to schedule DAMX MOK enrollment.${NC}"
    return 1
  fi

  echo -e "${YELLOW}MOK enrollment has been scheduled.${NC}"
  echo -e "${YELLOW}Reboot, complete enrollment, then run this installer again.${NC}"
  return 2
}

sign_driver_module() {
  local module_path=$1
  local sign_file
  local signer

  sign_file="/lib/modules/$(uname -r)/build/scripts/sign-file"

  if [ ! -x "$sign_file" ]; then
    echo -e "${RED}Error: Kernel signing tool not found at ${sign_file}.${NC}"
    return 1
  fi
  if [ ! -f "$module_path" ]; then
    echo -e "${RED}Error: Built module not found at ${module_path}.${NC}"
    return 1
  fi

  echo -e "${YELLOW}Signing Linuwu-Sense for Secure Boot...${NC}"
  "$sign_file" sha256 "$MODULE_SIGNING_KEY" "$MODULE_SIGNING_CERT" "$module_path" ||
    return 1

  if command -v modinfo &> /dev/null; then
    signer=$(modinfo -F signer "$module_path" 2>/dev/null || true)
    if [ -z "$signer" ]; then
      echo -e "${RED}Error: The built module does not contain a verifiable signature.${NC}"
      return 1
    fi
    echo -e "${GREEN}Module signed by: ${signer}${NC}"
  fi
}

# Install build dependencies based on distribution
install_build_deps() {
  if command -v apt-get &> /dev/null; then
    apt-get update && apt-get install -y build-essential linux-headers-$(uname -r)
  elif command -v yum &> /dev/null; then
    yum install -y gcc make kernel-devel
  elif command -v dnf &> /dev/null; then
    dnf install -y gcc make kernel-devel
  elif command -v pacman &> /dev/null; then
    pacman -S --needed --noconfirm base-devel linux-headers
    if is_llvm_kernel; then
      pacman -S --needed --noconfirm clang llvm lld
    fi
  elif command -v zypper &> /dev/null; then
    zypper install -y gcc make kernel-devel
  else
    echo -e "${RED}Error: Could not detect a supported package manager.${NC}"
    return 1
  fi
}

install_drivers() {
  local build_args=()
  local signing_status

  echo -e "${YELLOW}Installing Linuwu-Sense drivers...${NC}"

  if [ ! -d "$EXTRACTED_DIR/Linuwu-Sense" ]; then
    echo -e "${RED}Error: Linuwu-Sense directory not found in package!${NC}"
    return 1
  fi

  cd "$EXTRACTED_DIR/Linuwu-Sense" || return 1

  # Install build dependencies if needed
  if ! command -v make &> /dev/null; then
    echo -e "${YELLOW}Installing build tools...${NC}"
    install_build_deps || {
      cd "$TEMP_DIR" || return 1
      return 1
    }
  fi

  if [ "$MODULE_SIGNING_REQUIRED" != true ]; then
    prepare_secure_boot_signing
    signing_status=$?
    if [ $signing_status -ne 0 ]; then
      cd "$TEMP_DIR" || return 1
      return $signing_status
    fi
  fi

  # Build with appropriate compiler for the kernel
  if is_llvm_kernel; then
    echo -e "${YELLOW}Detected LLVM-compiled kernel, using Clang...${NC}"
    if ! command -v clang &> /dev/null; then
      install_build_deps || {
        cd "$TEMP_DIR" || return 1
        return 1
      }
    fi
    build_args=(LLVM=1 CC=clang)
  fi

  if ! make clean "${build_args[@]}" || ! make "${build_args[@]}"; then
    echo -e "${RED}Error: Failed to build Linuwu-Sense drivers${NC}"
    cd "$TEMP_DIR" || return 1
    return 1
  fi

  if [ "$MODULE_SIGNING_REQUIRED" = true ] &&
     ! sign_driver_module "src/linuwu_sense.ko"; then
    echo -e "${RED}Error: Failed to sign Linuwu-Sense for Secure Boot${NC}"
    cd "$TEMP_DIR" || return 1
    return 1
  fi

  if ! make install "${build_args[@]}"; then
    echo -e "${RED}Error: Failed to install Linuwu-Sense drivers${NC}"
    cd "$TEMP_DIR" || return 1
    return 1
  fi

  echo -e "${GREEN}Linuwu-Sense drivers installed successfully!${NC}"
  cd "$TEMP_DIR" || return 1
  return 0
}

install_daemon() {
  echo -e "${YELLOW}Installing DAMX-Daemon...${NC}"

  if [ ! -d "$EXTRACTED_DIR/DAMX-Daemon" ]; then
    echo -e "${RED}Error: DAMX-Daemon directory not found in package!${NC}"
    return 1
  fi

  # Create installation directory
  mkdir -p ${INSTALL_DIR}/daemon

  # Copy daemon binary
  cp -f "$EXTRACTED_DIR/DAMX-Daemon/DAMX-Daemon" ${INSTALL_DIR}/daemon/
  chmod +x ${INSTALL_DIR}/daemon/DAMX-Daemon

  # Create systemd service file with improved configuration
  cat > ${SYSTEMD_DIR}/${DAEMON_SERVICE_NAME} << EOL
[Unit]
Description=DAMX Daemon for Acer laptops
After=network.target

[Service]
Type=simple
ExecStart=${INSTALL_DIR}/daemon/DAMX-Daemon
Restart=on-failure
RestartSec=5
User=root
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOL

  # Enable and start the service
  systemctl daemon-reload
  systemctl enable ${DAEMON_SERVICE_NAME}
  systemctl start ${DAEMON_SERVICE_NAME}

  # Verify service is running
  if systemctl is-active --quiet ${DAEMON_SERVICE_NAME}; then
    echo -e "${GREEN}DAMX-Daemon installed and service started successfully!${NC}"
    return 0
  else
    echo -e "${RED}Warning: DAMX-Daemon service may not have started correctly. Check with 'systemctl status ${DAEMON_SERVICE_NAME}'${NC}"
    return 1
  fi
}

install_gui() {
  echo -e "${YELLOW}Installing DAMX-GUI...${NC}"

  if [ ! -d "$EXTRACTED_DIR/DAMX-GUI" ]; then
    echo -e "${RED}Error: DAMX-GUI directory not found in package!${NC}"
    return 1
  fi

  # Create installation directory
  mkdir -p ${INSTALL_DIR}/gui

  # Copy GUI files
  cp -rf "$EXTRACTED_DIR/DAMX-GUI"/* ${INSTALL_DIR}/gui/
  chmod +x ${INSTALL_DIR}/gui/DivAcerManagerMax

  # Create icon directory if it doesn't exist
  mkdir -p ${ICON_DIR}

  # Copy icon (try different possible icon names)
  if [ -f "$EXTRACTED_DIR/DAMX-GUI/icon.png" ]; then
    cp -f "$EXTRACTED_DIR/DAMX-GUI/icon.png" ${ICON_DIR}/damx.png
  elif [ -f "$EXTRACTED_DIR/DAMX-GUI/iconTransparent.png" ]; then
    cp -f "$EXTRACTED_DIR/DAMX-GUI/iconTransparent.png" ${ICON_DIR}/damx.png
  fi

  # Create desktop entry
  cat > ${DESKTOP_FILE_DIR}/damx.desktop << EOL
[Desktop Entry]
Name=DAMX
Comment=Div Acer Manager Max
Exec=${INSTALL_DIR}/gui/DivAcerManagerMax
Icon=damx
Terminal=false
Type=Application
Categories=Utility;System;
Keywords=acer;laptop;system;
EOL

  # Create command shortcut
  cat > ${BIN_DIR}/DAMX << EOL
#!/bin/bash
${INSTALL_DIR}/gui/DivAcerManagerMax "\$@"
EOL
  chmod +x ${BIN_DIR}/DAMX

  echo -e "${GREEN}DAMX-GUI installed successfully!${NC}"
  return 0
}

perform_install() {
  local signing_status

  # Do this before cleanup so a pending MOK enrollment never removes an
  # otherwise working installation.
  prepare_secure_boot_signing
  signing_status=$?
  if [ $signing_status -ne 0 ]; then
    if [ $signing_status -eq 2 ]; then
      pause
    fi
    return $signing_status
  fi

  echo -e "${BLUE}Performing cleanup before installation...${NC}"
  comprehensive_cleanup
  echo ""

  # Create main installation directory
  mkdir -p ${INSTALL_DIR}

  # Install components
  install_drivers
  DRIVER_RESULT=$?
  if [ $DRIVER_RESULT -ne 0 ]; then
    echo -e "${RED}Driver installation failed; daemon and GUI installation were not attempted.${NC}"
    return $DRIVER_RESULT
  fi

  install_daemon
  DAEMON_RESULT=$?

  install_gui
  GUI_RESULT=$?

  # Check if all installations were successful
  if [ $DRIVER_RESULT -eq 0 ] && [ $DAEMON_RESULT -eq 0 ] && [ $GUI_RESULT -eq 0 ]; then
    echo -e "${GREEN}DAMX Suite installation completed successfully!${NC}"
    echo -e "You can now run the GUI using the ${BLUE}DAMX${NC} command or from your application launcher."

    # Show service status
    echo ""
    echo -e "${BLUE}Service Status:${NC}"
    systemctl status ${DAEMON_SERVICE_NAME} --no-pager -l
    return 0
  else
    echo -e "${RED}Some components failed to install. Please check the errors above.${NC}"
    return 1
  fi
}

uninstall() {
  echo -e "${YELLOW}Uninstalling DAMX Suite...${NC}"
  comprehensive_cleanup
  echo -e "${GREEN}DAMX Suite uninstalled successfully!${NC}"
  return 0
}

# Function to check system compatibility
check_system() {
  echo -e "${BLUE}Checking system compatibility...${NC}"

  # Check if systemd is available (hard requirement)
  if ! command -v systemctl &> /dev/null; then
    echo -e "${RED}Error: systemd is required but not found on this system.${NC}"
    return 1
  fi
  echo -e "${GREEN}✓ systemd found${NC}"

  # Check kernel version (warning only)
  local kernel_version=$(uname -r | cut -d. -f1,2)
  local kernel_major=$(echo $kernel_version | cut -d. -f1)
  local kernel_minor=$(echo $kernel_version | cut -d. -f2)
  
  echo "Kernel version: $(uname -r)"
  
  # Check if kernel is less than 6.13
  if [ "$kernel_major" -lt 6 ] || ([ "$kernel_major" -eq 6 ] && [ "$kernel_minor" -lt 13 ]); then
    echo -e "${YELLOW}Warning: Kernel version $kernel_version is lower than 6.13. Installation may fail.${NC}"
    echo -e "${YELLOW}Recommended kernel version: 6.13 or higher${NC}"
  else
    echo -e "${GREEN}✓ Kernel version $kernel_version is supported${NC}"
  fi

  # Check distribution (informational only)
  if [ -f /etc/os-release ]; then
    . /etc/os-release
    echo "Detected OS: $PRETTY_NAME"
    
    # Check if it's Ubuntu (officially supported)
    if echo "$ID" | grep -q "ubuntu"; then
      echo -e "${GREEN}✓ Ubuntu detected (officially supported)${NC}"
    else
      echo -e "${YELLOW}Note: Only Ubuntu is officially supported. Other distributions may work but are not guaranteed.${NC}"
    fi
  else
    echo -e "${YELLOW}Note: Could not detect distribution. Only Ubuntu is officially supported.${NC}"
  fi

  echo -e "${GREEN}System compatibility check completed.${NC}"
  return 0
}

# Cleanup function to remove temporary files
cleanup_temp() {
  if [ -n "$TEMP_DIR" ] && [ -d "$TEMP_DIR" ]; then
    echo -e "${YELLOW}Cleaning up temporary files...${NC}"
    rm -rf "$TEMP_DIR"
  fi
}

# Main installation function
main() {
  # Set trap to cleanup on exit
  trap cleanup_temp EXIT

  print_banner

  # Check and elevate privileges if needed
  check_root "$@"

  # Perform initial system check
  if ! check_system; then
    echo -e "${RED}Critical system compatibility check failed. Exiting.${NC}"
    exit 1
  fi

  # Check dependencies
  check_dependencies

  # Get latest release information
  if ! get_latest_release; then
    echo -e "${RED}Failed to get release information. Exiting.${NC}"
    exit 1
  fi

  # Download package
  if ! download_package; then
    echo -e "${RED}Failed to download package. Exiting.${NC}"
    exit 1
  fi

  # Perform installation
  echo ""
  echo -e "${BLUE}Starting DAMX Suite installation...${NC}"
  perform_install
  install_result=$?
  if [ $install_result -eq 0 ]; then
    echo ""
    echo -e "${GREEN}🎉 DAMX Suite has been installed successfully!${NC}"
    echo -e "Release: ${RELEASE_NAME}"
    echo ""
    echo -e "${BLUE}Next steps:${NC}"
    echo -e "• Run ${GREEN}DAMX${NC} from the command line"
    echo -e "• Or find 'DAMX' in your application launcher"
    echo -e "• Check service status: ${GREEN}systemctl status ${DAEMON_SERVICE_NAME}${NC}"
    echo ""
  elif [ $install_result -eq 2 ]; then
    echo -e "${YELLOW}Installation is waiting for MOK enrollment and a reboot.${NC}"
    exit 2
  else
    echo -e "${RED}Installation failed. Please check the errors above.${NC}"
    exit 1
  fi
}

# Handle command line arguments
case "${1:-}" in
  --uninstall)
    check_root "$@"
    print_banner
    uninstall
    exit 0
    ;;
  --help|-h)
    echo "DAMX Remote Installer"
    echo ""
    echo "Usage:"
    echo "  curl -sSL https://raw.githubusercontent.com/PXDiv/Div-Acer-Manager-Max/main/remote-setup.sh | bash"
    echo "  curl -sSL https://raw.githubusercontent.com/PXDiv/Div-Acer-Manager-Max/main/remote-setup.sh | bash -s -- --uninstall"
    echo ""
    echo "Options:"
    echo "  --uninstall    Uninstall DAMX Suite"
    echo "  --help, -h     Show this help message"
    exit 0
    ;;
  *)
    main "$@"
    ;;
esac
