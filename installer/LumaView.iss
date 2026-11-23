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
  #define InstallName "DollView"
#endif

[Setup]
AppId={#SetupAppId}
AppName=DollView
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
UninstallDisplayIcon={app}\DollView.exe
SetupIconFile=..\assets\LumaView.ico
OutputDir={#OutputPath}
OutputBaseFilename=DollView-Setup-{#AppVersion}-x64
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
korean.Thumbnails=Windows 탐색기에 EXR, PSD, HDR, TGA 썸네일 표시
english.Thumbnails=Show EXR, PSD, HDR and TGA thumbnails in Windows Explorer
korean.PdfThumbnails=Windows 탐색기에 PDF 첫 페이지 썸네일 표시
english.PdfThumbnails=Show PDF first-page thumbnails in Windows Explorer
korean.ModelThumbnails=Windows 탐색기에 3D 모델 썸네일 표시
english.ModelThumbnails=Show 3D model thumbnails in Windows Explorer
korean.ThumbnailError=Windows 탐색기 썸네일 설정에 실패했습니다. DollView 설정의 파일 연결에서 다시 시도해 주세요.
english.ThumbnailError=Could not configure Explorer thumbnails. Try again in DollView file association settings.
korean.OpenDefaults=선택한 확장자의 기본 앱 설정 열기
english.OpenDefaults=Open default app settings for the selected extensions
korean.Launch=지금 DollView 실행
english.Launch=Launch DollView now
korean.TasksInfo=묶음을 선택하거나 확장자를 직접 체크하세요. 설치를 마치면 Windows 설정에서 기본 앱을 확인할 수 있습니다.
english.TasksInfo=Choose a group or check individual extensions. After setup, confirm your default apps in Windows Settings.
korean.GroupPrompt=형식 묶음 선택…
english.GroupPrompt=Select a group of file types…
korean.Images=이미지와 PDF
english.Images=Images and PDFs
korean.Videos=영상만
english.Videos=Videos only
korean.ImagesVideos=이미지, PDF와 영상
english.ImagesVideos=Images, PDFs and videos
korean.Models=3D 모델만
english.Models=3D models only
korean.AllTypes=모든 형식
english.AllTypes=All formats
korean.NoTypes=모두 해제
english.NoTypes=Clear all formats
korean.NeedNet=Microsoft .NET Framework 4.8 이상이 필요합니다. Windows Update 또는 https://dotnet.microsoft.com/download/dotnet-framework/net48 에서 설치한 뒤 다시 실행해 주세요.
english.NeedNet=Microsoft .NET Framework 4.8 or later is required. Install it via Windows Update or https://dotnet.microsoft.com/download/dotnet-framework/net48 and run setup again.
korean.NeedWebView=3D 보기를 위한 Microsoft Edge WebView2 설치에 실패했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.
english.NeedWebView=Could not install Microsoft Edge WebView2 for 3D viewing. Check your internet connection and try again.
korean.RegisterError=파일 형식 등록에 실패했습니다. DollView 설정의 파일 연결에서 다시 시도해 주세요.
english.RegisterError=Could not register file types. Try again from file associations in DollView settings.

[Tasks]
Name: "explorerthumbnails"; Description: "{cm:Thumbnails}"
Name: "pdfthumbnails"; Description: "{cm:PdfThumbnails}"
Name: "modelthumbnails"; Description: "{cm:ModelThumbnails}"
#include "association-tasks.iss"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{group}\DollView"; Filename: "{app}\DollView.exe"
Name: "{autodesktop}\DollView"; Filename: "{app}\DollView.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DollView.exe"; Parameters: "{code:DefaultsParameters}"; Description: "{cm:OpenDefaults}"; Flags: postinstall skipifsilent; Check: HasSelectedTypes
Filename: "{app}\DollView.exe"; Description: "{cm:Launch}"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
Filename: "{app}\DollView.exe"; Parameters: "--unregister-pdf-thumbnails"; RunOnceId: "UnregisterDollViewPdfThumbnails"; Flags: runhidden waituntilterminated
Filename: "{app}\DollView.exe"; Parameters: "--disable-model-thumbnails"; RunOnceId: "UnregisterDollViewModelThumbnails"; Flags: runhidden waituntilterminated
Filename: "{app}\DollView.exe"; Parameters: "--disable-explorer-thumbnails"; RunOnceId: "UnregisterDollViewThumbnails"; Flags: runhidden waituntilterminated
Filename: "{app}\DollView.exe"; Parameters: "--unregister-associations"; RunOnceId: "UnregisterLumaView"; Flags: runhidden waituntilterminated

[Code]
var
  Groups: array[0..2] of TArrayOfString;
  Preset: TNewComboBox;

function SelectedExtensions: String;
var G, I: Integer;
begin
  Result := '';
  for G := 0 to 2 do
    for I := 0 to GetArrayLength(Groups[G])-1 do
      if WizardIsTaskSelected('assoc_' + Groups[G][I]) then begin
        if Result <> '' then Result := Result + ',';
        Result := Result + '.' + Groups[G][I];
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

procedure ChoosePreset(Sender: TObject);
var G, I, Choice: Integer; Tasks, Prefix: String; IncludeGroup: Boolean;
begin
  Choice := Preset.ItemIndex;
  if Choice <= 0 then Exit;
  Tasks := '';
  for G := 0 to 2 do begin
    IncludeGroup := (Choice = 5) or ((Choice = 1) and (G = 0)) or
      ((Choice = 2) and (G = 1)) or ((Choice = 3) and (G < 2)) or
      ((Choice = 4) and (G = 2));
    if IncludeGroup then Prefix := 'assoc_' else Prefix := '!assoc_';
    for I := 0 to GetArrayLength(Groups[G])-1 do begin
      if Tasks <> '' then Tasks := Tasks + ',';
      Tasks := Tasks + Prefix + Groups[G][I];
    end;
  end;
  WizardSelectTasks(Tasks);
  Preset.ItemIndex := 0;
end;

procedure InitializeWizard;
begin
  Groups[0] := ['jpg','jpeg','png','bmp','gif','tif','tiff','ico','webp','exr','psd','psb','tga','hdr','dpx','dds','pdf'];
  Groups[1] := ['mp4','m4v','mov','wmv','avi','mpg','mpeg','mkv','webm'];
  Groups[2] := ['obj','glb','gltf','stl','fbx'];
  WizardForm.SelectTasksLabel.Caption := CustomMessage('TasksInfo');
end;

procedure CurPageChanged(CurPageID: Integer);
var Offset: Integer;
begin
  if (CurPageID <> wpSelectTasks) or Assigned(Preset) then Exit;
  Preset := TNewComboBox.Create(WizardForm);
  Preset.Parent := WizardForm.SelectTasksPage;
  Preset.Style := csDropDownList;
  Preset.Items.Add(CustomMessage('GroupPrompt'));
  Preset.Items.Add(CustomMessage('Images'));
  Preset.Items.Add(CustomMessage('Videos'));
  Preset.Items.Add(CustomMessage('ImagesVideos'));
  Preset.Items.Add(CustomMessage('Models'));
  Preset.Items.Add(CustomMessage('AllTypes'));
  Preset.Items.Add(CustomMessage('NoTypes'));
  Preset.ItemIndex := 0;
  Preset.OnChange := @ChoosePreset;
  Preset.Left := WizardForm.TasksList.Left;
  Preset.Top := WizardForm.TasksList.Top;
  Preset.Width := WizardForm.TasksList.Width;
  Preset.Anchors := [akLeft, akTop, akRight];
  Preset.TabOrder := WizardForm.TasksList.TabOrder;
  Offset := Preset.Height + ScaleY(6);
  WizardForm.TasksList.Height := WizardForm.TasksList.Height - Offset;
  WizardForm.TasksList.Top := WizardForm.TasksList.Top + Offset;
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
var ExitCode: Integer; Selected, ThumbnailCommand: String;
begin
  if CurStep = ssPostInstall then begin
    if WizardIsTaskSelected('explorerthumbnails') then ThumbnailCommand := '--enable-explorer-thumbnails'
    else ThumbnailCommand := '--disable-explorer-thumbnails';
    if not Exec(ExpandConstant('{app}\DollView.exe'), ThumbnailCommand, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      RaiseException(CustomMessage('ThumbnailError'));
    Selected := SelectedExtensions;
    if WizardIsTaskSelected('pdfthumbnails') then ThumbnailCommand := '--enable-pdf-thumbnails'
    else ThumbnailCommand := '--disable-pdf-thumbnails';
    if not Exec(ExpandConstant('{app}\DollView.exe'), ThumbnailCommand, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      RaiseException(CustomMessage('ThumbnailError'));
    if WizardIsTaskSelected('modelthumbnails') then ThumbnailCommand := '--enable-model-thumbnails'
    else ThumbnailCommand := '--disable-model-thumbnails';
    if not Exec(ExpandConstant('{app}\DollView.exe'), ThumbnailCommand, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      RaiseException(CustomMessage('ThumbnailError'));
    if Selected <> '' then
      if not Exec(ExpandConstant('{app}\DollView.exe'), '--register-associations "' + Selected + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
        RaiseException(CustomMessage('RegisterError'));
  end;
end;
