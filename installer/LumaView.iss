#ifndef AppVersion
  #error AppVersion must be supplied by package.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by package.ps1
#endif
#ifndef OutputPath
  #error OutputPath must be supplied by package.ps1
#endif
#ifndef SetupAppId
  #define SetupAppId "LumaView.Desktop"
#endif
#ifndef InstallName
  #define InstallName "LumaView"
#endif

[Setup]
AppId={#SetupAppId}
AppName=LumaView
AppVersion={#AppVersion}
AppPublisher=trackk81
AppPublisherURL=https://github.com/trackk81/LumaView
AppSupportURL=https://github.com/trackk81/LumaView/issues
AppUpdatesURL=https://github.com/trackk81/LumaView/releases
DefaultDirName={localappdata}\Programs\{#InstallName}
DefaultGroupName={#InstallName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
UninstallDisplayIcon={app}\LumaView.exe
SetupIconFile=..\assets\LumaView.ico
OutputDir={#OutputPath}
OutputBaseFilename=LumaView-Setup-{#AppVersion}-x64
VersionInfoVersion={#AppVersion}.0
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes
SetupLogging=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.Associations=기본 앱으로 연결할 확장자 (설치 후 Windows에서 최종 확인)
english.Associations=Extensions to set as defaults (confirm in Windows after setup)
korean.DesktopIcon=바탕 화면에 바로가기 만들기
english.DesktopIcon=Create a desktop shortcut
korean.OpenDefaults=선택한 확장자의 기본 앱 설정 열기
english.OpenDefaults=Open default app settings for the selected extensions
korean.Launch=지금 LumaView 실행
english.Launch=Launch LumaView now
korean.TasksInfo=지원 형식은 '연결 프로그램'에 자동 등록됩니다. 아래에서 선택한 형식은 설치 후 기본 앱 설정에서 바로 확인할 수 있습니다. Windows에서 LumaView를 최종 선택해 주세요.
english.TasksInfo=Supported formats are registered in Open with. Selected types are shown directly in default app settings after setup. Confirm LumaView in Windows to finish.
korean.NeedNet=Microsoft .NET Framework 4.8 이상이 필요합니다. Windows Update 또는 https://dotnet.microsoft.com/download/dotnet-framework/net48 에서 설치한 뒤 다시 실행해 주세요.
english.NeedNet=Microsoft .NET Framework 4.8 or later is required. Install it via Windows Update or https://dotnet.microsoft.com/download/dotnet-framework/net48 and run setup again.
korean.NeedWebView=3D 보기를 위한 Microsoft Edge WebView2 설치에 실패했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.
english.NeedWebView=Could not install Microsoft Edge WebView2 for 3D viewing. Check your internet connection and try again.
korean.RegisterError=파일 형식 등록에 실패했습니다. LumaView 설정의 파일 연결에서 다시 시도해 주세요.
english.RegisterError=Could not register file types. Try again from file associations in LumaView settings.

[Tasks]
#include "association-tasks.iss"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\LumaView"; Filename: "{app}\LumaView.exe"
Name: "{autodesktop}\LumaView"; Filename: "{app}\LumaView.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LumaView.exe"; Parameters: "{code:DefaultsParameters}"; Description: "{cm:OpenDefaults}"; Flags: postinstall skipifsilent; Check: HasSelectedTypes
Filename: "{app}\LumaView.exe"; Description: "{cm:Launch}"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
Filename: "{app}\LumaView.exe"; Parameters: "--unregister-associations"; RunOnceId: "UnregisterLumaView"; Flags: runhidden waituntilterminated

[Code]
var Extensions: TArrayOfString;

function SelectedExtensions: String;
var I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(Extensions)-1 do
    if WizardIsTaskSelected('assoc_' + Extensions[I]) then begin
      if Result <> '' then Result := Result + ',';
      Result := Result + '.' + Extensions[I];
    end;
end;

function HasSelectedTypes: Boolean;
begin
  Result := SelectedExtensions <> '';
end;

function DefaultsParameters(Param: String): String;
begin
  Result := '--open-selected-defaults "' + SelectedExtensions + '"';
end;

function HasWebView: Boolean;
var V: String;
begin
  Result := (RegQueryStringValue(HKLM32, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) and (V <> '') and (V <> '0.0.0.0'));
  if not Result then
    Result := (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', V) and (V <> '') and (V <> '0.0.0.0'));
end;

procedure InitializeWizard;
begin
  Extensions := ['jpg','jpeg','png','bmp','gif','tif','tiff','ico','webp','exr','psd','psb','tga','hdr','dpx','dds','mp4','m4v','mov','wmv','avi','mpg','mpeg','mkv','webm','obj','glb','gltf','stl','fbx'];
  WizardForm.SelectTasksLabel.Caption := CustomMessage('TasksInfo');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var NetRelease: Cardinal; ExitCode: Integer;
begin
  Result := '';
  if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', NetRelease) or (NetRelease < 528040) then begin
    Result := CustomMessage('NeedNet'); Exit;
  end;
  if not HasWebView then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
      Result := CustomMessage('NeedWebView'); Exit;
    end;
    if not HasWebView then Result := CustomMessage('NeedWebView');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ExitCode: Integer; Selected: String;
begin
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{app}\LumaView.exe'), '--register-associations', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      RaiseException(CustomMessage('RegisterError'));
    Selected := SelectedExtensions;
    if Selected <> '' then
      if not Exec(ExpandConstant('{app}\LumaView.exe'), '--prepare-default-associations "' + Selected + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
        RaiseException(CustomMessage('RegisterError'));
  end;
end;
