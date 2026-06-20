#!/usr/bin/env python3
"""
DAMX Build and Package Automation Script
Builds and packages the complete DAMX suite including daemon, GUI, and drivers.
to use it put this script outside the Div-Acer-Manager Folder and have Div-Linuwu-Sense next to it
used for internal local packaging
"""

import os
import sys
import subprocess
import shutil
import glob
import re
from pathlib import Path


class DAMXBuilder:
    def __init__(self):
        # Use the directory where the script is located
        script_path = Path(__file__).parent.absolute()
        self.base_dir = script_path
        self.daemon_dir = self.base_dir / "Div-Acer-Manager-Max" / "DAMM-Daemon"
        self.gui_dir = self.base_dir / "Div-Acer-Manager-Max" / "DivAcerManagerMax"
        self.drivers_dir = self.base_dir / "Div-Linuwu-Sense"
        self.publish_dir = self.base_dir / "Publish"
        self.setup_script = self.base_dir / "Div-Acer-Manager-Max/scripts/local-setup.sh"
        # self.setup_signed_script = self.base_dir / "Div-Acer-Manager-Max/Scripts/build_sign_install.sh"


        # Icon files to copy
        self.icon_files = [
            self.gui_dir / "icon.png",
            Path( self.base_dir  / "Div-Acer-Manager-Max/DivAcerManagerMax/iconTransparent.png")
        ]
        
        print(f"Script location: {script_path}")
        print(f"Working directory: {self.base_dir}")
        print(f"Looking for:")
        print(f"  - Daemon: {self.daemon_dir}")
        print(f"  - GUI: {self.gui_dir}")
        print(f"  - Drivers: {self.drivers_dir}")
        print(f"  - Setup script: {self.setup_script}")
        print(f"  - Icon files: {[str(f) for f in self.icon_files]}")
        print()
        
    def get_version_info(self):
        """Automatically detect version information from source files"""
        print("=== Detecting DAMX Versions ===")
        
        # Detect project version (GUI)
        project_version = self._detect_project_version()
        if not project_version:
            print("Error: Could not detect project version!")
            sys.exit(1)
            
        # Detect daemon version
        daemon_version = self._detect_daemon_version()
        if not daemon_version:
            print("Error: Could not detect daemon version!")
            sys.exit(1)
            
        # Detect drivers version
        drivers_version = self._detect_drivers_version()
        if not drivers_version:
            print("Error: Could not detect drivers version!")
            sys.exit(1)
            
        print(f"Detected versions:")
        print(f"  - Project: {project_version}")
        print(f"  - Daemon: {daemon_version}")
        print(f"  - Drivers: {drivers_version}")
        print()
            
        return {
            'project': project_version,
            'daemon': daemon_version,
            'drivers': drivers_version
        }
    
    def _detect_project_version(self):
        """Detect project version from GUI source file"""
        version_file = self.gui_dir / "MainWindow.axaml.cs"
        if not version_file.exists():
            print(f"Warning: Could not find version file at {version_file}")
            return None
            
        try:
            with open(version_file, 'r') as f:
                content = f.read()
                
            # Match: private readonly string ProjectVersion = "0.8.8";
            match = re.search(r'private const string ProjectVersion\s*=\s*"([\d.]+)"', content)
            if match:
                return match.group(1)
                
            print(f"Warning: Could not find ProjectVersion in {version_file}")
            return None
        except Exception as e:
            print(f"Error reading version file: {e}")
            return None
    
    def _detect_daemon_version(self):
        """Detect daemon version from Python source file"""
        version_file = self.daemon_dir / "DAMX-Daemon.py"
        if not version_file.exists():
            print(f"Warning: Could not find version file at {version_file}")
            return None
            
        try:
            with open(version_file, 'r') as f:
                content = f.read()
                
            # Match: VERSION = "0.4.2"
            match = re.search(r'VERSION\s*=\s*"([\d.]+)"', content)
            if match:
                return match.group(1)
                
            print(f"Warning: Could not find VERSION in {version_file}")
            return None
        except Exception as e:
            print(f"Error reading version file: {e}")
            return None
    
    def _detect_drivers_version(self):
        """Detect drivers version from C source file"""
        version_file = self.drivers_dir / "src" / "linuwu_sense.c"
        if not version_file.exists():
            print(f"Warning: Could not find version file at {version_file}")
            return None
            
        try:
            with open(version_file, 'r') as f:
                content = f.read()
                
            # Match: #define DRIVER_VERSION "25.625"
            match = re.search(r'#define\s+DRIVER_VERSION\s+"([\d.]+)"', content)
            if match:
                return match.group(1)
                
            print(f"Warning: Could not find DRIVER_VERSION in {version_file}")
            return None
        except Exception as e:
            print(f"Error reading version file: {e}")
            return None
    
    def check_dependencies(self):
        """Check if all required directories and files exist"""
        missing = []
        
        if not self.daemon_dir.exists():
            missing.append(f"Daemon directory: {self.daemon_dir}")
        if not self.gui_dir.exists():
            missing.append(f"GUI directory: {self.gui_dir}")
        if not self.drivers_dir.exists():
            missing.append(f"Drivers directory: {self.drivers_dir}")
        if not self.setup_script.exists():
            missing.append(f"Setup script: {self.setup_script}")
            
        # Check icon files
        for icon_file in self.icon_files:
            if not icon_file.exists():
                missing.append(f"Icon file: {icon_file}")
            
        if missing:
            print("Error: Missing required files/directories:")
            for item in missing:
                print(f"  - {item}")
            sys.exit(1)
            
        print("✓ All required directories and files found")
    
    def find_venv_python(self):
        """Find Python executable in venv or use current environment"""
        venv_paths = [
            self.daemon_dir / "venv" / "bin" / "python",
            self.daemon_dir / "venv" / "bin" / "python3",
            self.base_dir / "venv" / "bin" / "python",
            self.base_dir / "venv" / "bin" / "python3"
        ]
        
        for venv_python in venv_paths:
            if venv_python.exists():
                print(f"✓ Found venv Python: {venv_python}")
                return str(venv_python)
        
        print("⚠ No venv found, using system Python")
        return sys.executable
    
    def build_daemon(self):
        """Build the Python daemon using PyInstaller"""
        print("\n=== Building Daemon ===")
        
        if not (self.daemon_dir / "DAMX-Daemon.py").exists():
            print(f"Error: DAMX-Daemon.py not found in {self.daemon_dir}")
            sys.exit(1)
        
        python_exe = self.find_venv_python()
        
        # Change to daemon directory
        original_cwd = os.getcwd()
        os.chdir(self.daemon_dir)
        
        try:
            # Run PyInstaller
            cmd = [
                "pyinstaller",
                "--onefile",
                "--clean",
                "DAMX-Daemon.py"
            ]
            
            print(f"Running: {' '.join(cmd)}")
            result = subprocess.run(cmd, check=True, capture_output=True, text=True)
            print("✓ Daemon built successfully")
            
        except subprocess.CalledProcessError as e:
            print(f"Error building daemon: {e}")
            print(f"stdout: {e.stdout}")
            print(f"stderr: {e.stderr}")
            sys.exit(1)
        finally:
            os.chdir(original_cwd)
    
    def build_gui(self):
        """Build the .NET GUI application"""
        print("\n=== Building GUI ===")
        
        if not (self.gui_dir / "DivAcerManagerMax.csproj").exists() and not (self.gui_dir / "DivAcerManagerMax.sln").exists():
            # Try to find any .csproj file
            csproj_files = list(self.gui_dir.glob("*.csproj"))
            if not csproj_files:
                print(f"Error: No .csproj file found in {self.gui_dir}")
                sys.exit(1)
        
        # Change to GUI directory
        original_cwd = os.getcwd()
        os.chdir(self.gui_dir)
        
        try:
            cmd = [
                "dotnet", "publish",
                "-c", "Release",
                "-f", "net9.0",
                "-r", "linux-x64",
                "--self-contained", "true",
                "/p:PublishSingleFile=true",
                "/p:IncludeNativeLibrariesForSelfExtract=true",
                "/p:IncludeAllContentForSelfExtract=true"
            ]
            
            print(f"Running: {' '.join(cmd)}")
            result = subprocess.run(cmd, check=True, capture_output=True, text=True)
            print("✓ GUI built successfully")
            
        except subprocess.CalledProcessError as e:
            print(f"Error building GUI: {e}")
            print(f"stdout: {e.stdout}")
            print(f"stderr: {e.stderr}")
            sys.exit(1)
        finally:
            os.chdir(original_cwd)
    
    def create_package_structure(self, version):
        """Create the package directory structure"""
        print(f"\n=== Creating Package Structure ===")
        
        package_dir = self.publish_dir / f"DAMX-{version}"
        
        # Remove existing package directory if it exists
        if package_dir.exists():
            print(f"Removing existing package directory: {package_dir}")
            shutil.rmtree(package_dir)
        
        # Create package directory structure
        package_dir.mkdir(parents=True, exist_ok=True)
        daemon_target = package_dir / "DAMX-Daemon"
        gui_target = package_dir / "DAMX-GUI"
        drivers_target = package_dir / "Linuwu-Sense"
        
        daemon_target.mkdir(exist_ok=True)
        gui_target.mkdir(exist_ok=True)
        
        print(f"✓ Created package directory: {package_dir}")
        return package_dir, daemon_target, gui_target, drivers_target
    
    def copy_daemon_executable(self, daemon_target):
        """Copy the built daemon executable"""
        print("Copying daemon executable...")
        
        daemon_dist = self.daemon_dir / "dist"
        daemon_executable = daemon_dist / "DAMX-Daemon"
        
        if not daemon_executable.exists():
            print(f"Error: Daemon executable not found at {daemon_executable}")
            sys.exit(1)
        
        shutil.copy2(daemon_executable, daemon_target / "DAMX-Daemon")
        print("✓ Daemon executable copied")
    
    def copy_gui_executable(self, gui_target):
        """Copy the built GUI executable and icons"""
        print("Copying GUI executable and icons...")
        
        # Find the published GUI executable
        gui_publish_dir = self.gui_dir / "bin" / "Release" / "net9.0" / "linux-x64" / "publish"
        
        if not gui_publish_dir.exists():
            print(f"Error: GUI publish directory not found at {gui_publish_dir}")
            sys.exit(1)
        
        # Find the main executable (should be the .csproj name without extension)
        executables = [f for f in gui_publish_dir.iterdir() if f.is_file() and f.stat().st_mode & 0o111]
        
        if not executables:
            print(f"Error: No executable found in {gui_publish_dir}")
            sys.exit(1)
        
        # Use the first executable found (or look for specific name)
        gui_executable = executables[0]
        for exe in executables:
            if "DivAcerManagerMax" in exe.name:
                gui_executable = exe
                break
        
        shutil.copy2(gui_executable, gui_target / "DivAcerManagerMax")
        print(f"✓ GUI executable copied: {gui_executable.name}")
        
        # Copy icon files
        for icon_file in self.icon_files:
            if icon_file.exists():
                target_path = gui_target / icon_file.name
                shutil.copy2(icon_file, target_path)
                print(f"✓ Copied icon: {icon_file.name}")
            else:
                print(f"Warning: Icon file not found: {icon_file}")
    
    def copy_drivers(self, drivers_target):
        """Copy the drivers directory"""
        print("Copying drivers...")
        
        if not self.drivers_dir.exists():
            print(f"Error: Drivers directory not found at {self.drivers_dir}")
            sys.exit(1)
        
        shutil.copytree(self.drivers_dir, drivers_target)
        print("✓ Drivers copied and renamed to Linuwu-Sense")
    
    def update_setup_script(self, package_dir, versions):
        """Copy and update the setup script with version information"""
        print("Updating setup script...")
        
        setup_target = package_dir / "setup.sh"
        shutil.copy2(self.setup_script, setup_target)
        # setup_signed_target = package_dir / "build_sign_install.sh"
        # shutil.copy2(self.setup_signed_script, setup_signed_target)

        
        # Read the setup script content
        with open(setup_target, 'r') as f:
            content = f.read()
        
        # Update version information (basic replacement)
        # You may need to adjust these patterns based on your setup.sh structure
        content = content.replace("PROJECT_VERSION=", f"PROJECT_VERSION={versions['project']}")
        content = content.replace("DAEMON_VERSION=", f"DAEMON_VERSION={versions['daemon']}")
        content = content.replace("DRIVERS_VERSION=", f"DRIVERS_VERSION={versions['drivers']}")
        
        # Write the updated content
        with open(setup_target, 'w') as f:
            f.write(content)
        
        # Make setup script executable
        setup_target.chmod(0o755)
        print("✓ Setup script updated and made executable")
    
    def create_release_info(self, package_dir, versions):
        """Create release information file"""
        print("Creating release information...")
        
        release_file = package_dir / "release.txt"
        
        release_content = f"""DAMX Release Information
========================

Project Version: {versions['project']}
Daemon Version: {versions['daemon']}
Drivers Version: {versions['drivers']}

Build Date: {subprocess.check_output(['date'], text=True).strip()}
Built on: {subprocess.check_output(['uname', '-a'], text=True).strip()}

Components:
- DAMX-Daemon: Python daemon compiled with PyInstaller
- DAMX-GUI: .NET 9.0 GUI application (self-contained)
- Linuwu-Sense: Hardware drivers
- setup.sh: Installation script
"""
        
        with open(release_file, 'w') as f:
            f.write(release_content)
        
        print("✓ Release information created")
    
    def build_and_package(self):
        """Main build and package process"""
        print("DAMX Build and Package Script")
        print("=" * 50)
        
        # Get version information
        versions = self.get_version_info()
        
        # Check dependencies
        self.check_dependencies()
        
        # Build components
        self.build_daemon()
        self.build_gui()
        
        # Create package structure
        package_dir, daemon_target, gui_target, drivers_target = self.create_package_structure(versions['project'])
        
        # Copy all components
        self.copy_daemon_executable(daemon_target)
        self.copy_gui_executable(gui_target)
        self.copy_drivers(drivers_target)
        
        # Update setup script and create release info
        self.update_setup_script(package_dir, versions)
        self.create_release_info(package_dir, versions)
        
        print(f"\n🎉 Build and packaging completed successfully!")
        print(f"Package location: {package_dir}")
        print(f"Package contents:")
        for item in package_dir.iterdir():
            print(f"  - {item.name}")


def main():
    try:
        builder = DAMXBuilder()
        builder.build_and_package()
    except KeyboardInterrupt:
        print("\n⚠ Build cancelled by user")
        sys.exit(1)
    except Exception as e:
        print(f"\n❌ Build failed: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
