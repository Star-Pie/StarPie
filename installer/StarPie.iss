; =====================================================================
; StarPie (原 WinPieGestures) - Inno Setup 自动化安装与打包配置脚本
; 支持 Windows 11 Modern Dynamic 视觉风格、多语言 (简体中文/英文/日文)、
; 极速 LZMA2 固实压缩、单例互斥锁检测与自包含 .NET 8 桌面运行时一键安装。
; =====================================================================

#define MyAppName "StarPie"
#define MyAppPublisher "SoftBlack42"
#define MyAppURL "https://github.com/SoftBlack42/StarPie"
#define MyAppExeName "StarPie.exe"

; 兜底版本号：正常由 build-installer.ps1 以 /DMyAppVersion=... 覆盖（脚本从 csproj 读 <Version>）。
; 直接编译本文件（或 ISCC 未传参）时会用到下面这两个值，所以每次发版也要一起改。
#ifndef MyAppVersion
  #define MyAppVersion "1.8.0-beta.3"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\Standalone"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "StarPie-" + MyAppVersion + "-Setup-win-x64"
#endif

[Setup]
; 沿用项目继承 GUID，保证覆盖升级能正确定位并无缝替换旧版本
AppId={{09668CE4-8740-4636-9EA7-73F672BDE235}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

; 版本元数据信息 (显示于 Windows 资源管理器文件属性详细信息中)
; 同样是兜底值，与上面的 MyAppVersion 一起改：只改上面不改这里，
; 会出现「文件名是 beta.1、属性页里却是旧版号」的不一致。
#ifndef MyAppNumericVersion
  #define MyAppNumericVersion "1.8.0.0"
#endif
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=StarPie Setup
VersionInfoTextVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (c) 2026 SoftBlack42
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppNumericVersion}
VersionInfoProductTextVersion={#MyAppVersion}

; 硬件架构限制：仅支持 64 位 Windows (x64 及 Windows 11 on Arm 仿真)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

; 许可协议与安装权限
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; 输出文件设定
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\WinPieGestures\app_icon.ico

; 压缩算法：采用 LZMA2 极限固实压缩，自包含 Standalone 包压缩后仅 ~30MB 出头
SolidCompression=yes
Compression=lzma2/ultra64
WizardStyle=modern dynamic windows11

; 进程互斥锁：与 App.xaml.cs 进程级 MutexName 一致，运行中安装或升级自动检测并提示退出
AppMutex=Global\StarPie_SingleInstance_Mutex_9B8A7C
CloseApplications=force
CloseApplicationsFilter=*.exe

[Languages]
; 简体中文为默认首选语言，内置于项目仓储 Languages 目录以避免 CI 依赖缺失
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
chinesesimplified.CreateDesktopIcon=创建桌面快捷方式(&D)
chinesesimplified.CreateQuickLaunchIcon=创建快速启动快捷方式(&Q)
chinesesimplified.AdditionalIcons=附加图标：
chinesesimplified.LaunchProgram=运行 %1(&L)
chinesesimplified.UninstallProgram=卸载 %1
chinesesimplified.NameAndVersion=%1 版本 %2
chinesesimplified.AutoStartProgramGroupDescription=启动：
chinesesimplified.AutoStartProgram=自动启动 %1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 将已发布的完整 Standalone（自带 .NET 8 独立运行时）解压缩至安装目录，开箱即用零环境依赖
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安装完成后提供直接启动 StarPie 的复选框（默认勾选）
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// 检查目标系统是否已安装 .NET 8 Desktop Runtime（仅作为特殊轻量模式下的辅助诊断）
function IsNet8DesktopRuntimeInstalled(): Boolean;
var
  Installed: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost\fx\Microsoft.WindowsDesktop.App\8.0', 'Version', Installed) then
    Result := True
  else if RegKeyExists(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost\fx\Microsoft.WindowsDesktop.App') then
    Result := True;
end;
