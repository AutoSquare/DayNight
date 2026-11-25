; 晨昏线 DayNight 安装包脚本（Inno Setup 6.4+）
; 编译前先用 FolderProfile 发布：dotnet publish .\DayNight\DayNight.csproj -c Release -p:PublishProfile=FolderProfile
; 版本、公司与版权信息从已发布的 DayNight.exe 读取，不在此手工维护；发布产物早于 DayNight.csproj 时中止编译。

#define MyAppName "晨昏线"
#define MyAppExeName "DayNight.exe"
#define MyAppURL "https://www.autosquare.org.cn/"
#define PublishDir AddBackslash(SourcePath) + "..\DayNight\bin\Publish"
#define AppExe AddBackslash(PublishDir) + MyAppExeName
#if !FileExists(AppExe)
  #error 请先使用 FolderProfile 发布 DayNight，再编译安装包。
#endif
#define MyAppVersion GetFileProductVersion(AppExe)

; 发布目录中的程序按修改时间原地覆盖。它早于 DayNight.csproj，说明项目文件（含版本号）在上次发布之后改动过，
; 此时读到的版本号和打进去的程序都是上一版的，因此中止编译。
#define ProjectFile AddBackslash(SourcePath) + "..\DayNight\DayNight.csproj"
#define StampFormat "yyyy/mm/dd hh:nn:ss"
#if !FileExists(ProjectFile)
  #error 找不到 DayNight.csproj，无法确认发布产物是否为最新，已中止编译。
#endif
#if GetFileDateTimeString(AppExe, StampFormat, "-", ":") < GetFileDateTimeString(ProjectFile, StampFormat, "-", ":")
  #error 发布目录中的 DayNight.exe 早于 DayNight.csproj，版本号和内容是上一版的。请先用 FolderProfile 重新发布，再编译安装包。
#endif

[Setup]
AppId={{5CEE1F89-F37F-4CAF-B128-EF048EF0B9D7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#GetFileCompany(AppExe)}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppCopyright={#GetFileCopyright(AppExe)}
VersionInfoVersion={#GetVersionNumbersString(AppExe)}
VersionInfoCompany={#GetFileCompany(AppExe)}
VersionInfoCopyright={#GetFileCopyright(AppExe)}
VersionInfoDescription={#MyAppName}安装程序
DefaultDirName={localappdata}\Programs\DayNight
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=setup.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Local\DayNight.SingleInstance
OutputDir=Output
OutputBaseFilename=DayNight-Setup-{#MyAppVersion}-win-x64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure OfferConfigRemoval;
var
  DataDir: String;
  Cleaned: Boolean;
begin
  DataDir := ExpandConstant('{userappdata}\DayNight');
  if UninstallSilent or not DirExists(DataDir) then Exit;
  if SuppressibleMsgBox(
    '是否同时删除当前 Windows 用户的计划和设置？' + #13#10#13#10 +
    '选择“是”将删除 config.json 及其配置备份和临时文件；选择“否”可在重装后继续使用原计划。运行日志会保留。',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then Exit;

  Cleaned := DelTree(DataDir + '\config.json', False, True, False);
  if not DelTree(DataDir + '\config.json.before-schedule-repair-*.bak', False, True, False) then Cleaned := False;
  if not DelTree(DataDir + '\config.json.*.tmp', False, True, False) then Cleaned := False;
  if not Cleaned then
    MsgBox('部分配置文件未能删除，请检查目录：' + DataDir, mbError, MB_OK);
  RemoveDir(DataDir);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunKey, Command: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
    if RegQueryStringValue(HKCU, RunKey, 'DayNight', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" --silent') = 0 then
        RegDeleteValue(HKCU, RunKey, 'DayNight');
    OfferConfigRemoval;
  end;
end;
