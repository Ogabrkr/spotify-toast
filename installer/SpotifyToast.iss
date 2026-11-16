; Instalador do Spotify Toast (Inno Setup 6).
; Gere com:  powershell -ExecutionPolicy Bypass -File build-installer.ps1

#define AppName "Spotify Toast"
#define AppExe "SpotifyToast.exe"
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif

[Setup]
AppId={{6E7B3F2A-8C41-4D9B-A0E5-2F1C9D7B5A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Gabriel Kono
AppPublisherURL=https://github.com/Ogabrkr/spotify-toast
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Instalador do {#AppName}
VersionInfoProductName={#AppName}
; instala só para o usuário atual: não pede administrador
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=yes
WizardStyle=modern
WizardSizePercent=100
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=SpotifyToast-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=force
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
ptbr.StartWithWindows=Iniciar com o Windows
ptbr.Options=Opções:
ptbr.LaunchNow=Abrir o {#AppName} agora

[Tasks]
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Options}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:Options}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; mesmo valor que o menu do tray liga/desliga
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SpotifyToast"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--demo"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\SpotifyToast"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  UninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6E7B3F2A-8C41-4D9B-A0E5-2F1C9D7B5A31}_is1';

var
  IsUpgrade, HadStartup: Boolean;

function InitializeSetup(): Boolean;
begin
  IsUpgrade := RegKeyExists(HKEY_CURRENT_USER, UninstKey);
  HadStartup := RegValueExists(HKEY_CURRENT_USER, RunKey, 'SpotifyToast');
  Result := True;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { numa atualização, a caixa mostra o que o usuário escolheu no menu do app }
  if (CurPageID = wpSelectTasks) and IsUpgrade then
    if HadStartup then WizardSelectTasks('startup') else WizardSelectTasks('!startup');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardSilent and IsUpgrade and (ExpandConstant('{param:TASKS|*}') = '*') then
    begin
      { atualização silenciosa sem /TASKS: mantém exatamente o que o usuário tinha
        (as tarefas lembradas da 1ª instalação podem estar desatualizadas) }
      if HadStartup then
        RegWriteStringValue(HKEY_CURRENT_USER, RunKey, 'SpotifyToast', '"' + ExpandConstant('{app}\{#AppExe}') + '"')
      else
        RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'SpotifyToast');
    end
    else if not WizardIsTaskSelected('startup') then
      RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'SpotifyToast');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { garante que a entrada de inicialização some mesmo se foi ligada pelo menu do app }
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'SpotifyToast');
end;

