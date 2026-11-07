Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define PRODUCT_NAME "Controller Battery Native"
!define PRODUCT_DIR "$LOCALAPPDATA\Programs\ControllerBatteryNative"
!define PRODUCT_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ControllerBatteryNative"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define START_MENU_DIR "$SMPROGRAMS\Controller Battery Native"
!define MUI_ICON "${PROJECT_ROOT}\src\DualSenseBatteryTray.App\Assets\App\dualsense-disconnected.ico"
!define MUI_UNICON "${PROJECT_ROOT}\src\DualSenseBatteryTray.App\Assets\App\dualsense-disconnected.ico"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "${PRODUCT_DIR}"
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey "LegalCopyright" "Copyright 2026 AweStudioX"
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
    MessageBox MB_ICONSTOP "Installation is limited to ${PRODUCT_DIR}"
    Abort
  ${EndIf}
FunctionEnd

Function un.onInit
  SetShellVarContext current
  ${If} $INSTDIR != "${PRODUCT_DIR}"
    MessageBox MB_ICONSTOP "Unexpected uninstall location; no files were removed."
    Abort
  ${EndIf}
FunctionEnd

Section "Install"
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=stop-installed.ps1 "${PROJECT_ROOT}\rust\stop-installed.ps1"
  ExecWait '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "$PLUGINSDIR\stop-installed.ps1" -InstallDirectory "$INSTDIR"' $0
  ${If} $0 != 0
    SetErrorLevel 1
    Abort "Existing native process could not be stopped."
  ${EndIf}
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  File /oname=stop-installed.ps1 "${PROJECT_ROOT}\rust\stop-installed.ps1"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${RUN_KEY}" "ControllerBatteryNative" '"$INSTDIR\ControllerBatteryNative.exe" --watcher'
  WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${PRODUCT_KEY}" "Publisher" "AweStudioX"
  WriteRegStr HKCU "${PRODUCT_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_KEY}" "DisplayIcon" '"$INSTDIR\ControllerBatteryNative.exe",0'
  WriteRegStr HKCU "${PRODUCT_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${PRODUCT_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_KEY}" "NoRepair" 1
  CreateDirectory "${START_MENU_DIR}"
  CreateShortcut "${START_MENU_DIR}\Controller Battery Native.lnk" "$INSTDIR\ControllerBatteryNative.exe" "--show" "$INSTDIR\ControllerBatteryNative.exe" 0
  Exec '"$INSTDIR\ControllerBatteryNative.exe" --watcher'
SectionEnd

Section "Uninstall"
  ${If} $INSTDIR != "${PRODUCT_DIR}"
    SetErrorLevel 1
    Abort "Unexpected uninstall location; no files were removed."
  ${EndIf}
  ReadRegStr $0 HKCU "${RUN_KEY}" "ControllerBatteryNative"
  ${If} $0 == '"$INSTDIR\ControllerBatteryNative.exe" --watcher'
    DeleteRegValue HKCU "${RUN_KEY}" "ControllerBatteryNative"
  ${EndIf}
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  CopyFiles /SILENT "$INSTDIR\stop-installed.ps1" "$PLUGINSDIR\stop-installed.ps1"
  ExecWait '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "$PLUGINSDIR\stop-installed.ps1" -InstallDirectory "$INSTDIR"' $0
  ${If} $0 != 0
    SetErrorLevel 1
    Abort "Native process could not be stopped; files were kept."
  ${EndIf}
  Delete "${START_MENU_DIR}\Controller Battery Native.lnk"
  RMDir "${START_MENU_DIR}"
  DeleteRegKey HKCU "${PRODUCT_KEY}"
  RMDir /r "$INSTDIR"
SectionEnd
