Unicode true
RequestExecutionLevel user
SetCompressor zlib

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define PRODUCT_NAME "DualSense Battery Tray"
!define PRODUCT_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DualSenseBatteryTray"
!define PRODUCT_DIR "$LOCALAPPDATA\Programs\DualSenseBatteryTray"
!define START_MENU_DIR "$SMPROGRAMS\DualSense Battery Tray"
!define MUI_ICON "${PROJECT_ROOT}\src\DualSenseBatteryTray.App\Assets\App\dualsense-disconnected.ico"
!define MUI_UNICON "${PROJECT_ROOT}\src\DualSenseBatteryTray.App\Assets\App\dualsense-disconnected.ico"
!define MUI_ABORTWARNING

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "${PRODUCT_DIR}"
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey "CompanyName" "AweStudioX"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Function .onInit
  SetShellVarContext current
  ${If} $INSTDIR != "${PRODUCT_DIR}"
    MessageBox MB_ICONSTOP "Installation is limited to the current-user location: ${PRODUCT_DIR}"
    Abort
  ${EndIf}
FunctionEnd

Function un.onInit
  SetShellVarContext current
  ${If} $INSTDIR != "${PRODUCT_DIR}"
    MessageBox MB_ICONSTOP "Uninstaller is outside its expected current-user location. No files were removed."
    Abort
  ${EndIf}
FunctionEnd

Section "Install"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=install.ps1 "${PROJECT_ROOT}\scripts\install.ps1"
  File /oname=device-watcher-task.xml "${PROJECT_ROOT}\scripts\device-watcher-task.xml"

  SetOutPath "$PLUGINSDIR\payload"
  File "${PUBLISH_DIR}\DualSenseBatteryTray.App.exe"
  File "${PUBLISH_DIR}\DualSenseBatteryTray.Watcher.exe"
  File "${PROJECT_ROOT}\scripts\uninstall.ps1"
  File /r "${PROJECT_ROOT}\LICENSES"
  WriteUninstaller "$PLUGINSDIR\payload\Uninstall.exe"
  IfErrors install_failed

  ClearErrors
  ExecWait '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\install.ps1" -PublishDirectory "$PLUGINSDIR\payload"' $0
  IfErrors install_failed
  ${If} $0 != 0
    Goto install_failed
  ${EndIf}

  ClearErrors
  WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayName" "${PRODUCT_NAME}"
  IfErrors registration_failed
  WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  IfErrors registration_failed
  WriteRegStr HKCU "${PRODUCT_KEY}" "Publisher" "AweStudioX"
  IfErrors registration_failed
  WriteRegStr HKCU "${PRODUCT_KEY}" "InstallLocation" "$INSTDIR"
  IfErrors registration_failed
  WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayIcon" '"$INSTDIR\DualSenseBatteryTray.App.exe",0'
  IfErrors registration_failed
  WriteRegStr HKCU "${PRODUCT_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  IfErrors registration_failed
  WriteRegStr HKCU "${PRODUCT_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  IfErrors registration_failed
  WriteRegDWORD HKCU "${PRODUCT_KEY}" "NoModify" 1
  IfErrors registration_failed
  WriteRegDWORD HKCU "${PRODUCT_KEY}" "NoRepair" 1
  IfErrors registration_failed

  ClearErrors
  CreateDirectory "${START_MENU_DIR}"
  IfErrors registration_failed
  CreateShortcut "${START_MENU_DIR}\DualSense Battery Tray.lnk" "$INSTDIR\DualSenseBatteryTray.App.exe" "" "$INSTDIR\DualSenseBatteryTray.App.exe" 0
  IfErrors registration_failed
  CreateShortcut "${START_MENU_DIR}\Uninstall DualSense Battery Tray.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Uninstall.exe" 0
  IfErrors registration_failed
  Goto install_done

install_failed:
  SetErrorLevel 1
  Abort "Setup failed. Existing installation, if any, was restored by the installer script."

registration_failed:
  SetErrorLevel 1
  Abort "Setup installed the files but could not register Windows shortcuts or Installed apps. Run $INSTDIR\Uninstall.exe to remove it."

install_done:
SectionEnd

Section "Uninstall"
  ${If} $INSTDIR != "${PRODUCT_DIR}"
    SetErrorLevel 1
    Abort "Unexpected installation location; no files were removed."
  ${EndIf}

  InitPluginsDir
  ClearErrors
  CopyFiles /SILENT "$INSTDIR\uninstall.ps1" "$PLUGINSDIR\uninstall.ps1"
  IfErrors uninstall_failed
  ClearErrors
  ExecWait '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\uninstall.ps1"' $0
  IfErrors uninstall_failed
  ${If} $0 != 0
    Goto uninstall_failed
  ${EndIf}

  Delete "${START_MENU_DIR}\DualSense Battery Tray.lnk"
  Delete "${START_MENU_DIR}\Uninstall DualSense Battery Tray.lnk"
  RMDir "${START_MENU_DIR}"
  DeleteRegKey HKCU "${PRODUCT_KEY}"
  Goto uninstall_done

uninstall_failed:
  SetErrorLevel 1
  Abort "Uninstallation failed. Installed apps entry was kept so you can retry."

uninstall_done:
SectionEnd
