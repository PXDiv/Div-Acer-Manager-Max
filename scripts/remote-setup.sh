#!/bin/bash

# Constants
SCRIPT_VERSION="0.7.10-1"
INSTALL_DIR="/opt/damx"
BIN_DIR="/usr/local/bin"
SYSTEMD_DIR="/etc/systemd/system"
DAEMON_SERVICE_NAME="damx-daemon.service"
DESKTOP_FILE_DIR="/usr/share/applications"
ICON_DIR="/usr/share/icons/hicolor/256x256/apps"
LINUWU_SENSE_REPO="0x7375646F/Linuwu-Sense"
DAMX_REPO="PXDiv/Div-Acer-Manager-Max"
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

pause() {
  echo -e "${BLUE}Press any key to continue...${NC}"
  read -n 1 -s -r
}

check_root() {
  if [ "$EUID" -ne 0 ]; then
    echo -e "${YELLOW}This script requires root privileges.${NC}"
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
  echo -e "${BLUE}      DAMX Suite Installer v${SCRIPT_VERSION}        ${NC}"
  echo -e "${BLUE}   Acer Laptop WMI Controls for Linux     ${NC}"
  echo -e "${BLUE}==========================================${NC}"
  echo ""
}

cleanup_legacy_installation() {
  echo -e "${YELLOW}Checking for legacy installations...${NC}"
  local cleanup_performed=false

  if [ -f "${SYSTEMD_DIR}/${LEGACY_DAEMON_SERVICE_NAME}" ]; then
    echo -e "${BLUE}Found legacy service file: ${LEGACY_DAEMON_SERVICE_NAME}${NC}"
    if systemctl is-active --quiet ${LEGACY_DAEMON_SERVICE_NAME} 2>/dev/null; then
      echo "Stopping legacy service..."
      systemctl stop ${LEGACY_DAEMON_SERVICE_NAME}
    fi
    if systemctl is-enabled --quiet ${LEGACY_DAEMON_SERVICE_NAME} 2>/dev/null; then
      echo "Disabling legacy service..."
      systemctl disable ${LEGACY_DAEMON_SERVICE_NAME}
    fi
    echo "Removing legacy service file..."
    rm -f "${SYSTEMD_DIR}/${LEGACY_DAEMON_SERVICE_NAME}"
    cleanup_performed=true
  fi

  if [ -d "${LEGACY_INSTALL_DIR}" ]; then
    echo -e "${BLUE}Found legacy installation directory: ${LEGACY_INSTALL_DIR}${NC}"
    echo "Removing legacy installation directory..."
    rm -rf "${LEGACY_INSTALL_DIR}"
    cleanup_performed=true
  fi

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

  if [ "$cleanup_performed" = true ]; then
    echo "Reloading systemd daemon configuration..."
    systemctl daemon-reload
    echo -e "${GREEN}Legacy installation cleanup completed.${NC}"
  else
    echo -e "${GREEN}No legacy installations found.${NC}"
  fi

  return 0
}

comprehensive_cleanup() {
  echo -e "${YELLOW}Performing comprehensive cleanup...${NC}"

  if systemctl is-active --quiet ${DAEMON_SERVICE_NAME} 2>/dev/null; then
    echo "Stopping current DAMX-Daemon service..."
    systemctl stop ${DAEMON_SERVICE_NAME}
  fi

  if systemctl is-enabled --quiet ${DAEMON_SERVICE_NAME} 2>/dev/null; then
    echo "Disabling current DAMX-Daemon service..."
    systemctl disable ${DAEMON_SERVICE_NAME}
  fi

  if [ -f "${SYSTEMD_DIR}/${DAEMON_SERVICE_NAME}" ]; then
    echo "Removing current service file..."
    rm -f "${SYSTEMD_DIR}/${DAEMON_SERVICE_NAME}"
  fi

  cleanup_legacy_installation

  echo "Removing current installation files..."
  rm -rf ${INSTALL_DIR}
  rm -f ${BIN_DIR}/DAMX
  rm -f ${DESKTOP_FILE_DIR}/damx.desktop
  rm -f ${ICON_DIR}/damx.png

  # Uninstall drivers if Linuwu-Sense folder exists
  if [ -d "Linuwu-Sense" ]; then
    echo "Uninstalling drivers..."
    cd Linuwu-Sense
    if [ -f "Makefile" ]; then
      make uninstall 2>/dev/null || true
    fi
    cd ..
  fi

  systemctl daemon-reload

  echo -e "${GREEN}Comprehensive cleanup completed.${NC}"
  return 0
}

download_latest_release() {
  echo -e "${YELLOW}Fetching latest DAMX release info from GitHub...${NC}" >&2
  local api_url="https://api.github.com/repos/${DAMX_REPO}/releases/latest"
  local release_json
  if command -v curl &> /dev/null; then
    release_json=$(curl -sSL "${api_url}")
  elif command -v wget &> /dev/null; then
    release_json=$(wget -qO- "${api_url}")
  else
    echo -e "${RED}curl or wget required to fetch release info.${NC}" >&2
    return 1
  fi

  local tar_url
  tar_url=$(echo "$release_json" | grep 'browser_download_url' | grep 'DAMX-.*\.tar\.xz' | head -n1 | cut -d '"' -f 4)
  if [ -z "$tar_url" ]; then
    echo -e "${RED}No DAMX-<tag>.tar.xz asset found in latest release!${NC}" >&2
    return 1
  fi

  local file_name
  file_name=$(basename "$tar_url")
  if [ -f "$file_name" ]; then
    echo "$file_name"
    return 0
  else
    if command -v curl &> /dev/null; then
      curl -Lf --retry 3 -o "$file_name" "$tar_url"
    else
      wget -qO "$file_name" "$tar_url"
    fi
    if [ $? -ne 0 ]; then
      echo -e "${RED}Failed to download $file_name${NC}" >&2
      return 1
    fi
    echo "$file_name"
    return 0
  fi
}

extract_release() {
  local tarball="$1"
  local target_dir="$2"
  echo -e "${YELLOW}Extracting $tarball...${NC}"
  tar -xJf "$tarball" -C "$target_dir"
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
  if command -v pacman &> /dev/null; then
    pacman -S --needed --noconfirm base-devel
    if is_llvm_kernel; then
      pacman -S --needed --noconfirm clang llvm lld
    fi
  elif command -v apt-get &> /dev/null; then
    apt-get update && apt-get install -y build-essential
  elif command -v dnf &> /dev/null; then
    dnf install -y gcc make kernel-devel
  elif command -v zypper &> /dev/null; then
    zypper install -y gcc make kernel-devel
  else
    echo -e "${RED}Error: Could not detect a supported package manager.${NC}"
    return 1
  fi
}

clone_and_install_linuwu_sense() {
  local build_args=()
  local signing_status

  echo -e "${YELLOW}Cloning and installing Linuwu-Sense drivers...${NC}"
  rm -rf Linuwu-Sense
  if ! git clone --depth=1 "https://github.com/${LINUWU_SENSE_REPO}.git"; then
    echo -e "${RED}Failed to clone Linuwu-Sense repo!${NC}"
    pause
    return 1
  fi

  cd Linuwu-Sense || return 1

  # Install build dependencies if needed
  if ! command -v make &> /dev/null; then
    echo -e "${YELLOW}Installing build tools...${NC}"
    install_build_deps || {
      cd .. || return 1
      return 1
    }
  fi

  if [ "$MODULE_SIGNING_REQUIRED" != true ]; then
    prepare_secure_boot_signing
    signing_status=$?
    if [ $signing_status -ne 0 ]; then
      cd .. || return 1
      return $signing_status
    fi
  fi

  # Build with appropriate compiler for the kernel
  if is_llvm_kernel; then
    echo -e "${YELLOW}Detected LLVM-compiled kernel, using Clang...${NC}"
    if ! command -v clang &> /dev/null; then
      install_build_deps || {
        cd .. || return 1
        return 1
      }
    fi
    build_args=(LLVM=1 CC=clang)
  fi

  if ! make clean "${build_args[@]}" || ! make "${build_args[@]}"; then
    echo -e "${RED}Error: Failed to build Linuwu-Sense drivers${NC}"
    cd .. || return 1
    pause
    return 1
  fi

  if [ "$MODULE_SIGNING_REQUIRED" = true ] &&
     ! sign_driver_module "src/linuwu_sense.ko"; then
    echo -e "${RED}Error: Failed to sign Linuwu-Sense for Secure Boot${NC}"
    cd .. || return 1
    pause
    return 1
  fi

  if ! make install "${build_args[@]}"; then
    echo -e "${RED}Error: Failed to install Linuwu-Sense drivers${NC}"
    cd .. || return 1
    pause
    return 1
  fi

  cd .. || return 1
  echo -e "${GREEN}Linuwu-Sense drivers installed successfully!${NC}"
  return 0
}

install_daemon() {
  echo -e "${YELLOW}Installing DAMX-Daemon...${NC}"

  if [ ! -d "DAMX-Daemon" ]; then
    echo -e "${RED}Error: DAMX-Daemon directory not found!${NC}"
    pause
    return 1
  fi

  mkdir -p ${INSTALL_DIR}/daemon
  cp -f DAMX-Daemon/DAMX-Daemon ${INSTALL_DIR}/daemon/
  chmod +x ${INSTALL_DIR}/daemon/DAMX-Daemon

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

  systemctl daemon-reload
  systemctl enable ${DAEMON_SERVICE_NAME}
  systemctl start ${DAEMON_SERVICE_NAME}

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

  if [ ! -d "DAMX-GUI" ]; then
    echo -e "${RED}Error: DAMX-GUI directory not found!${NC}"
    pause
    return 1
  fi

  mkdir -p ${INSTALL_DIR}/gui
  cp -rf DAMX-GUI/* ${INSTALL_DIR}/gui/
  chmod +x ${INSTALL_DIR}/gui/DivAcerManagerMax

  mkdir -p ${ICON_DIR}
  cp -f DAMX-GUI/icon.png ${ICON_DIR}/damx.png

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

  cat > ${BIN_DIR}/DAMX << EOL
#!/bin/bash
${INSTALL_DIR}/gui/DivAcerManagerMax "\$@"
EOL
  chmod +x ${BIN_DIR}/DAMX

  echo -e "${GREEN}DAMX-GUI installed successfully!${NC}"
  return 0
}

prepare_and_extract_release() {
  # Download latest release if not present
  local tarball=""
  for file in DAMX-*.tar.xz; do
    if [ -f "$file" ]; then
      tarball="$file"
      break
    fi
  done
  if [ -z "$tarball" ]; then
    tarball=$(download_latest_release 2>/dev/null)
    if [ -z "$tarball" ] || [ ! -f "$tarball" ]; then
      echo -e "${RED}Could not obtain DAMX release archive.${NC}"
      pause
      return 1
    fi
  fi

  # Extract to temp dir
  local temp_dir="damx_installer_temp"
  rm -rf "$temp_dir"
  mkdir -p "$temp_dir"
  extract_release "$tarball" "$temp_dir"
  rm -rf DAMX-GUI DAMX-Daemon
  mv "$temp_dir/DAMX-GUI" .
  mv "$temp_dir/DAMX-Daemon" .
  rm -rf "$temp_dir"
  echo -e "${GREEN}Release extracted and prepared.${NC}"
}

perform_install() {
  local skip_drivers=$1
  local is_update=$2
  local signing_status

  # Do this before cleanup so a pending MOK enrollment never removes an
  # otherwise working installation.
  if [ "$skip_drivers" = false ]; then
    prepare_secure_boot_signing
    signing_status=$?
    if [ $signing_status -ne 0 ]; then
      if [ $signing_status -eq 2 ]; then
        pause
      fi
      return $signing_status
    fi
  fi

  if [ "$is_update" = true ]; then
    echo -e "${BLUE}Performing cleanup before installation...${NC}"
    comprehensive_cleanup
    echo ""
  else
    cleanup_legacy_installation
    echo ""
  fi

  mkdir -p ${INSTALL_DIR}

  prepare_and_extract_release || return 1

  # Install components
  if [ "$skip_drivers" = false ]; then
    clone_and_install_linuwu_sense
    DRIVER_RESULT=$?
    if [ $DRIVER_RESULT -ne 0 ]; then
      echo -e "${RED}Driver installation failed; daemon and GUI installation were not attempted.${NC}"
      return $DRIVER_RESULT
    fi
  else
    echo -e "${YELLOW}Skipping driver installation as requested.${NC}"
    DRIVER_RESULT=0
  fi

  install_daemon
  DAEMON_RESULT=$?

  install_gui
  GUI_RESULT=$?

  if [ $DRIVER_RESULT -eq 0 ] && [ $DAEMON_RESULT -eq 0 ] && [ $GUI_RESULT -eq 0 ]; then
    echo -e "${GREEN}DAMX Suite installation completed successfully!${NC}"
    echo -e "You can now run the GUI using the ${BLUE}DAMX${NC} command or from your application launcher."
    echo ""
    echo -e "${BLUE}Service Status:${NC}"
    systemctl status ${DAEMON_SERVICE_NAME} --no-pager -l
    pause
    return 0
  else
    echo -e "${RED}Some components failed to install. Please check the errors above.${NC}"
    pause
    return 1
  fi
}

uninstall() {
  echo -e "${YELLOW}Uninstalling DAMX Suite...${NC}"
  comprehensive_cleanup
  echo -e "${GREEN}DAMX Suite uninstalled successfully!${NC}"
  pause
  return 0
}

check_system() {
  echo -e "${BLUE}Checking system compatibility...${NC}"

  if ! command -v systemctl &> /dev/null; then
    echo -e "${RED}Error: systemd is required but not found on this system.${NC}"
    return 1
  fi

  if [ -f /etc/os-release ]; then
    . /etc/os-release
    echo "Detected OS: $PRETTY_NAME"
  fi

  echo -e "${GREEN}System compatibility check passed.${NC}"
  return 0
}

main_menu() {
  if ! check_system; then
    echo -e "${RED}System compatibility check failed. Exiting.${NC}"
    pause
    exit 1
  fi

  while true; do
    print_banner

    echo -e "Please select an option:"
    echo -e "  ${GREEN}1${NC}) Install DAMX Suite (complete)"
    echo -e "  ${GREEN}2${NC}) Install DAMX Suite (without drivers)"
    echo -e "  ${GREEN}3${NC}) Uninstall DAMX Suite"
    echo -e "  ${GREEN}4${NC}) Reinstall/Update DAMX Suite (recommended for upgrades)"
    echo -e "  ${GREEN}5${NC}) Check service status"
    echo -e "  ${GREEN}q${NC}) Quit"
    echo ""

    read -p "Enter your choice [1-5 or q]: " choice

    case $choice in
      1)
        print_banner
        echo -e "${BLUE}Starting complete installation...${NC}"
        perform_install false false
        if [ $? -eq 2 ]; then
          exit 2
        fi
        ;;
      2)
        print_banner
        echo -e "${BLUE}Starting installation without drivers...${NC}"
        perform_install true false
        ;;
      3)
        print_banner
        echo -e "${BLUE}Starting uninstallation...${NC}"
        uninstall
        ;;
      4)
        print_banner
        echo -e "${BLUE}Starting reinstallation/update...${NC}"
        echo -e "${YELLOW}This will completely remove the existing installation before installing the new version.${NC}"
        perform_install false true
        if [ $? -eq 2 ]; then
          exit 2
        fi
        ;;
      5)
        print_banner
        echo -e "${BLUE}Checking DAMX service status...${NC}"
        echo ""
        if systemctl list-unit-files | grep -q ${DAEMON_SERVICE_NAME}; then
          systemctl status ${DAEMON_SERVICE_NAME} --no-pager -l
        else
          echo -e "${YELLOW}DAMX service not found. The suite may not be installed.${NC}"
        fi
        echo ""
        pause
        ;;
      q|Q)
        echo -e "${BLUE}Exiting installer. Goodbye!${NC}"
        exit 0
        ;;
      *)
        echo -e "${RED}Invalid option. Please try again.${NC}"
        sleep 2
        ;;
    esac
  done
}

check_root "$@"
main_menu
exit 0
