; Spark - NSIS Installer Script
; This script creates a professional Windows installer for Spark application
; Output: SparkInstaller.exe

;-------- NSIS Configuration --------
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

; General Settings
Name "Spark"
InstallDir "$PROGRAMFILES64\Spark"
InstallDirRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "InstallLocation"

; Request application privileges for Windows Vista/7+
RequestExecutionLevel admin

; Version & Appearance
!define VERSION "1.0.0"
!define MUI_ABORTWARNING
!define MUI_ICON "..\Assets\Branding\ico\App.ico"
!define MUI_UNICON "..\Assets\Branding\ico\App.ico"

!ifndef BUILD_CONFIG
  !define BUILD_CONFIG "Debug"
!endif

!ifndef RUNTIME_ID
  !define RUNTIME_ID "win-x64"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\\bin\\${BUILD_CONFIG}\\net10.0-windows\\${RUNTIME_ID}\\publish"
!endif

!ifndef INSTALLER_NAME
  !define INSTALLER_NAME "SparkInstaller-${RUNTIME_ID}.exe"
!endif

OutFile "..\${INSTALLER_NAME}"

;-------- MUI Settings --------
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_LANGUAGE "English"

Function .onInit
  ${If} ${RunningX64}
    SetRegView 64
    StrCpy $INSTDIR "$PROGRAMFILES64\Spark"
  ${Else}
    StrCpy $INSTDIR "$PROGRAMFILES\Spark"
  ${EndIf}
FunctionEnd

;-------- Installer Sections --------

Section "Install"
  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}

  SetOutPath "$INSTDIR"
  
  ; Copy published self-contained app files (includes .NET runtime)
  File /r "${PUBLISH_DIR}\*.*"
  
  ; Copy configuration files
  SetOutPath "$INSTDIR"
  File "..\appsettings.json"
  File "..\LICENSE.txt"
  File "..\README.md"
  
  ; Copy localization files
  SetOutPath "$INSTDIR\Locales"
  File "..\Locales\en.json"
  File "..\Locales\es.json"
  
  ; Copy branding/icons
  SetOutPath "$INSTDIR\Assets\Branding\ico"
  File "..\Assets\Branding\ico\App.ico"
  
  ; Create application data directory
  CreateDirectory "$APPDATA\Spark"
  
  ; Registry entries for Control Panel (Add/Remove Programs)
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "DisplayName" "Spark - AI Text Transformation"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "DisplayIcon" "$INSTDIR\Assets\Branding\ico\App.ico"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "Publisher" "Spark Development Team"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "URLInfoAbout" "https://www.spark-app.in/"
  WriteRegDWord HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "NoModify" 1
  WriteRegDWord HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "NoRepair" 1
  WriteRegDWord HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark" "EstimatedSize" 50000
  
  ; Create uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  
SectionEnd

;-------- Desktop & Start Menu Shortcuts --------

Section "Create Shortcuts"
  
  ; Desktop Shortcut
  CreateShortcut "$DESKTOP\Spark.lnk" "$INSTDIR\Spark.exe" "" "$INSTDIR\Assets\Branding\ico\App.ico" 0
  
  ; Start Menu Shortcuts
  CreateDirectory "$SMPROGRAMS\Spark"
  CreateShortcut "$SMPROGRAMS\Spark\Spark.lnk" "$INSTDIR\Spark.exe" "" "$INSTDIR\Assets\Branding\ico\App.ico" 0
  CreateShortcut "$SMPROGRAMS\Spark\Uninstall.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Assets\Branding\ico\App.ico" 0
  
SectionEnd

;-------- Run Application After Installation --------

Section "Launch App"
  
  ; Auto-start Spark after installation
  ExecShell "open" "$INSTDIR\Spark.exe"
  
SectionEnd

;-------- Uninstaller --------

Section "Uninstall"
  ${If} ${RunningX64}
    SetRegView 64
  ${EndIf}

  
  ; Open uninstall webpage
  ExecShell "open" "https://www.spark-app.in/uninstall"
  
  ; Remove application files
  Delete "$INSTDIR\Spark.exe"
  Delete "$INSTDIR\Spark.dll"
  Delete "$INSTDIR\appsettings.json"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\Assets\Branding\ico\App.ico"
  Delete "$INSTDIR\Locales\en.json"
  Delete "$INSTDIR\Locales\es.json"
  Delete "$INSTDIR\Uninstall.exe"
  
  ; Remove all DLL files
  RMDir /r "$INSTDIR\Locales"
  RMDir /r "$INSTDIR\Assets"
  RMDir /r "$INSTDIR"
  
  ; Remove shortcuts
  Delete "$DESKTOP\Spark.lnk"
  RMDir /r "$SMPROGRAMS\Spark"
  
  ; Remove registry entries
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\Spark"
  
SectionEnd

;-------- Language Support --------

!insertmacro MUI_LANGUAGE "Spanish"
LangString ^UninstallCaption ${LANG_SPANISH} "Desinstalar Spark"
LangString ^UninstallSubCaption ${LANG_SPANISH} "Eliminar Spark de su computadora"
