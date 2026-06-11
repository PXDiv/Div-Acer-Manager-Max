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

# Detect if kernel was compiled with LLVM/Clang (e.g., CachyOS, some Arch variants)
is_llvm_kernel() {
  if grep -qi "clang\|llvm" /proc/version 2>/dev/null; then
    return 0
  fi
  if [ -f /etc/os-release ] && grep -qi "cachyos" /etc/os-release; then
    return 0
  fi
  return 1
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
  fi
}

install_drivers() {
  echo -e "${YELLOW}Installing Linuwu-Sense drivers...${NC}"

  if [ ! -d "$EXTRACTED_DIR/Linuwu-Sense" ]; then
    echo -e "${RED}Error: Linuwu-Sense directory not found in package!${NC}"
    return 1
  fi

  cd "$EXTRACTED_DIR/Linuwu-Sense"

  # Install build dependencies if needed
  if ! command -v make &> /dev/null; then
    echo -e "${YELLOW}Installing build tools...${NC}"
    install_build_deps
  fi

  # Build with appropriate compiler for the kernel
  if is_llvm_kernel; then
    echo -e "${YELLOW}Detected LLVM-compiled kernel, using Clang...${NC}"
    install_build_deps  # Ensure clang is installed
    make clean LLVM=1 CC=clang
    make LLVM=1 CC=clang
    make install LLVM=1 CC=clang
  else
    make clean
    make
    make install
  fi

  if [ $? -eq 0 ]; then
    echo -e "${GREEN}Linuwu-Sense drivers installed successfully!${NC}"
    cd "$TEMP_DIR"
    return 0
  else
    echo -e "${RED}Error: Failed to install Linuwu-Sense drivers${NC}"
    cd "$TEMP_DIR"
    return 1
  fi
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
  echo -e "${BLUE}Performing cleanup before installation...${NC}"
  comprehensive_cleanup
  echo ""

  # Create main installation directory
  mkdir -p ${INSTALL_DIR}

  # Install components
  install_drivers
  DRIVER_RESULT=$?

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
  if perform_install; then
    echo ""
    echo -e "${GREEN}🎉 DAMX Suite has been installed successfully!${NC}"
    echo -e "Release: ${RELEASE_NAME}"
    echo ""
    echo -e "${BLUE}Next steps:${NC}"
    echo -e "• Run ${GREEN}DAMX${NC} from the command line"
    echo -e "• Or find 'DAMX' in your application launcher"
    echo -e "• Check service status: ${GREEN}systemctl status ${DAEMON_SERVICE_NAME}${NC}"
    echo ""
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