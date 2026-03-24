# Spark Windows Installer Setup Guide

This guide explains how to build and distribute the Spark installer for Windows users.

## Overview

The Spark installer is built using **NSIS (Nullsoft Scriptable Install System)**, which creates a professional `.exe` installer that:

✅ Displays license agreement (EULA) during installation  
✅ Allows users to choose installation directory  
✅ Creates desktop and Start Menu shortcuts  
✅ Registers the app in Windows Control Panel (Add/Remove Programs)  
✅ Automatically launches Spark after installation  
✅ Supports clean uninstallation with a redirect to feedback URL  
✅ Works on Windows 10 and later (32-bit & 64-bit)  

## Prerequisites

Before building the installer, ensure you have:

### 1. NSIS Installed
- **Download**: Visit https://nsis.sourceforge.io/
- **Install**: Run the NSIS installer (default installation is fine)
- **Default Path**: `C:\Program Files (x86)\NSIS` or `C:\Program Files\NSIS`
- **Verify**: Open Command Prompt and run:
  ```cmd
  "C:\Program Files (x86)\NSIS\makensis.exe" /?
  ```
  Should display NSIS version information.

### 2. .NET 10 SDK
- **Download**: https://dotnet.microsoft.com/download
- **Install**: Run the installer and follow prompts
- **Verify**: Open Command Prompt and run:
  ```cmd
  dotnet --version
  ```
  Should display version 10.0.0 or later.

### 3. Project Files
Ensure these files exist in your project:
- `Spark.csproj` - Project file
- `LICENSE.txt` - License/EULA (included in repo)
- `appsettings.json` - App configuration
- `Assets/Branding/ico/App.ico` - Application icon
- `Installer/Spark.nsi` - NSIS script (included in repo)

## Building the Installer

### Option 1: Using PowerShell (Recommended)

1. Open **PowerShell** in the project root directory
2. Run the build script:
   ```powershell
   .\Build-Installer.ps1
   ```
   
   For Release build:
   ```powershell
   .\Build-Installer.ps1 -Release
   ```

### Option 2: Using Command Prompt (Windows Batch)

1. Open **Command Prompt** (cmd.exe) in the project root directory
2. Run the build script:
   ```cmd
   Build-Installer.bat
   ```
   
   For Release build:
   ```cmd
   Build-Installer.bat release
   ```

### Option 3: Manual Build (Advanced)

If you prefer manual control:

1. Build the application:
   ```cmd
   dotnet build -c Release
   ```

2. Compile the NSIS script:
   ```cmd
   "C:\Program Files (x86)\NSIS\makensis.exe" /V2 Installer\Spark.nsi
   ```

## Output

After successful build, you'll find:
- **SparkInstaller.exe** - The Windows installer executable (in project root)
- File size: ~20-50 MB (depending on .NET runtime size)

## Testing the Installer

Before distributing:

1. **Test Installation**:
   - Run `SparkInstaller.exe`
   - Accept the license agreement
   - Choose a test installation directory (e.g., `C:\Program Files\Spark-Test`)
   - Verify app launches automatically
   - Verify shortcuts appear on Desktop and Start Menu

2. **Test Uninstallation**:
   - Go to Control Panel → Programs → Programs and Features
   - Find "Spark - AI Text Transformation" in the list
   - Click "Uninstall"
   - Verify the feedback webpage opens (http://localhost:8080/uninstall)
   - Verify shortcuts and installation directory are removed

3. **Test on Clean Machine** (Optional):
   - Run in a Virtual Machine with fresh Windows 10/11
   - Verify installation works without admin user session

## Installation Behavior

### What Users See

**Step 1: Welcome**
- Splash screen with app logo
- "Welcome to Spark Setup Wizard"

**Step 2: License Agreement**
- Full text of LICENSE.txt
- User must accept to continue

**Step 3: Choose Installation Folder**
- Default: `C:\Program Files\Spark`
- User can browse and change location

**Step 4: Installation Progress**
- Progress bar showing file copy
- Auto-generated uninstaller

**Step 5: Finish**
- Option to launch Spark immediately
- Installer closes

### What Gets Installed

```
C:\Program Files\Spark\
├── Spark.exe                          (Main application)
├── Spark.dll                          (Application library)
├── Microsoft.Extensions.*.dll         (Dependencies)
├── Polly.dll                          (Retry library)
├── System.*.dll                       (Runtime libraries)
├── appsettings.json                   (Configuration)
├── LICENSE.txt                        (License copy)
├── README.md                          (Documentation)
├── Uninstall.exe                      (Uninstaller)
└── Assets/
    └── Branding/ico/
        └── App.ico                    (Icon files)
└── Locales/
    ├── en.json                        (English translations)
    └── es.json                        (Spanish translations)

%APPDATA%\Spark\                       (User data directory, auto-created)
C:\Users\[User]\Desktop\               (Spark.lnk shortcut)
C:\Users\[User]\Start Menu\Programs\Spark\   (Start Menu shortcuts)
```

### Registry Entries

The installer adds entries to Windows Registry for Control Panel integration:

```
HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark\

DisplayName:      "Spark - AI Text Transformation"
DisplayVersion:   "1.0.0"
InstallLocation:  "C:\Program Files\Spark"
UninstallString:  "C:\Program Files\Spark\Uninstall.exe"
DisplayIcon:      "C:\Program Files\Spark\Assets\Branding\ico\App.ico"
Publisher:        "Spark Development Team"
URLInfoAbout:     "https://github.com/your-repo/spark"
NoModify:         1
NoRepair:         1
EstimatedSize:    50000 (KB)
```

## Customization

### Changing Installation Directory

Edit `Installer/Spark.nsi`:
```nsi
InstallDir "$PROGRAMFILES\Spark"  ; Change "Spark" to your desired folder name
```

### Changing Uninstall Webpage

Edit `Installer/Spark.nsi` in the `[Uninstall]` section:
```nsi
ExecShell "open" "https://your-feedback-url.com"
```

### Adding More Files

Edit `Installer/Spark.nsi` in the `[Install]` section:
```nsi
File "path/to/additional/file.txt"
```

### Changing Publisher Information

Edit `Installer/Spark.nsi`:
```nsi
WriteRegStr HKLM "..." "Publisher" "Your Company Name"
WriteRegStr HKLM "..." "URLInfoAbout" "https://your-website.com"
```

### Windows Defender SmartScreen Warning (Optional)

For enterprise/signed installers, consider:
1. Acquiring an Extended Validation (EV) code signing certificate
2. Signing the installer with `signtool.exe`
3. Submitting to Microsoft SmartScreen reputation service

Without signing, Windows may show a warning. This is normal for new applications.

## Troubleshooting

### "NSIS not found" Error

**Solution**: 
1. Download NSIS from https://nsis.sourceforge.io/
2. Install to default location: `C:\Program Files (x86)\NSIS`
3. Run build script again

### "Build artifacts not found" Error

**Solution**:
1. Ensure `.NET 10 SDK` is installed: `dotnet --version`
2. Manually build first: `dotnet build -c Release`
3. Verify files exist in: `bin\Release\net10.0-windows\`

### Installer exe is too large (>100 MB)

**Solution**:
- This is normal if .NET runtime is bundled
- To reduce size, NSIS compresses files automatically
- For distribution, can further optimize by:
  1. Excluding unnecessary DLLs
  2. Using app trimming: Add `<PublishTrimmed>true</PublishTrimmed>` to Spark.csproj
  3. Using self-contained deployment: `dotnet publish -c Release -r win-x64 -f net10.0-windows --self-contained`

### App doesn't launch after installation

**Solution**:
1. Verify `SparkInstaller.exe` has correct path to `Spark.exe`
2. Check `Installer/Spark.nsi` has correct paths in `[Install]` section
3. Manually run: `C:\Program Files\Spark\Spark.exe`
4. Check event logs for runtime errors

### Uninstall doesn't open webpage

**Solution**:
1. Verify URL is accessible: Test in browser manually
2. Check `Installer/Spark.nsi` `[Uninstall]` section has correct URL
3. Ensure default browser is set (Windows → Settings → Default Apps)

## Distribution

### Recommended Steps

1. **Sign the installer** (Optional but recommended):
   ```cmd
   signtool sign /f cert.pfx /p password /t http://timestamp.server SparkInstaller.exe
   ```

2. **Create checksum for integrity**:
   ```powershell
   Get-FileHash SparkInstaller.exe -Algorithm SHA256
   ```

3. **Upload to distribution platform**:
   - GitHub Releases
   - Company website
   - Windows Package Manager (`winget install spark`)

4. **Create release notes**:
   - Installation instructions
   - Requirements (Windows 10+, .NET 10)
   - Known issues
   - Changelog

### Hosting Example

```markdown
## Download Spark

**Latest Version**: 1.0.0

| Platform | Download | Checksum (SHA256) |
|----------|----------|-------------------|
| Windows 64-bit | [SparkInstaller.exe](link) | abc123... |

**System Requirements**:
- Windows 10 or later
- 100 MB free disk space
- .NET 10 Runtime (included in installer)

**Installation**:
1. Download SparkInstaller.exe
2. Double-click to run
3. Accept license agreement
4. Choose installation folder
5. Click Install

To uninstall: Settings → Apps → Apps & Files → Search "Spark" → Uninstall
```

## Next Steps

1. Install NSIS if you haven't already
2. Run `Build-Installer.ps1` or `Build-Installer.bat`
3. Test `SparkInstaller.exe` on a test machine
4. Distribute to users

## Support

For NSIS documentation: https://nsis.sourceforge.io/Docs/  
For .NET deployment: https://docs.microsoft.com/dotnet/core/distribution/  

---

**Last Updated**: March 2026  
**NSIS Version**: 3.x  
**.NET Version**: 10.0  
