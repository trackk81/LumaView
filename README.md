# LumaView

Windows용 이미지·시퀀스·영상·3D 모델 뷰어. 한국어/영어, 어두운/밝은/사용자 색상 테마를 지원합니다.

## 설치

Windows 10/11 64비트, .NET Framework 4.8 이상이 필요합니다.

1. Releases에서 `LumaView-Setup-0.1.0-x64.exe`를 실행합니다.
2. 설치 위치와 연결할 확장자를 선택합니다. 기본 위치는 `%LOCALAPPDATA%\Programs\LumaView`입니다.
3. 완료 화면의 **선택한 확장자의 기본 앱 설정 열기**를 누르고 Windows에서 LumaView를 선택합니다.

설치 시 지원 형식을 ‘연결 프로그램’에 자동 등록합니다. Windows 10/11의 기본 앱 변경은 사용자 확인이 필요하며, 기존 UserChoice를 강제로 변경하지 않습니다. 최신 Windows 11에서는 선택한 형식의 설정 화면으로 바로 이동합니다. 구형 Windows에서는 기본 앱 화면의 LumaView 항목에서 선택하세요.

3D 보기에 필요한 Microsoft Edge WebView2 Runtime이 없으면 설치 프로그램이 Microsoft의 공식 설치 도구로 설치합니다. 이 경우 인터넷 연결이 필요합니다. .NET Framework 4.8이 없는 PC에는 설치 안내가 표시됩니다.

설정과 캐시는 사용자 프로필에 보관합니다. 제거 시 개인 설정은 유지하며 이 설치본의 파일 형식 등록만 정리합니다. WebView2는 다른 프로그램과 공유하므로 제거하지 않습니다.

## 업데이트

일반 탐색 화면에서 **설정 → 오른쪽의 업데이트** 버튼을 누르세요. `trackk81/LumaView`의 최신 공개 정식 릴리스를 확인합니다. 새 버전은 사용자 확인 후 내려받고 SHA-256 검사값을 검증한 뒤 설치합니다. 초안과 사전 릴리스는 자동 업데이트 대상에서 제외합니다.

아직 공개 릴리스가 없으면 ‘아직 공개된 업데이트가 없습니다’로 표시됩니다. 현재 배포 후보는 서명되지 않았으므로 Windows가 게시자 확인 안내를 표시할 수 있습니다.

## 주요 기능

- 왼쪽 폴더 탐색기와 현재 폴더의 이미지/영상 미리보기. 더블클릭하면 전체화면.
- 이미지 확대·회전·반전, 투명 배경 색/체크 크기, 상세 정보, 슬라이드쇼.
- 시퀀스를 SQ 항목으로 묶기, 재생 FPS 설정, 실제 프레임 번호 표시, 시간 기준 재생.
- 영상 재생과 한 프레임 이동. 영상 코덱은 Windows에 설치된 미디어 기능에 따라 달라집니다.
- OBJ/MTL, GLB/glTF, STL, FBX의 정적 3D 보기. 플랫 컬러, SSAO, 스카이돔, 포인트 라이트 색/강도/이동.
- 전체화면 메뉴 자동 숨김, 핀 고정, 오른쪽 위 종료 버튼.
- 복사/붙여넣기/이름 변경, 파일 연결 설정.

이미지: JPG, JPEG, PNG, BMP, GIF, TIFF, ICO, WebP, EXR, PSD/PSB, TGA, HDR, DPX, DDS. GIF/TIFF는 첫 프레임, PSD/PSB는 저장된 합성 이미지입니다. EXR/HDR은 화면용 SDR 미리보기이며 AOV·OCIO·HDR 출력은 지원하지 않습니다. FBX 애니메이션 및 제작 프로그램 전용 셰이더는 지원하지 않습니다.

## 단축키

| 기능 | 키 |
| --- | --- |
| 목록으로 / 전체화면 해제 | Esc |
| 현재 폴더 이미지 선택 | Enter |
| 복사 / 붙여넣기 / 이름 변경 | Ctrl+C / Ctrl+V / F2 |
| 영상·시퀀스 이전 / 다음 프레임 | D / F |
| 영상·시퀀스 재생·정지 | Space |
| 3D 화면 맞춤 / 라이트 추가 / 상세 정보 | F / L / I |

## 빌드와 배포

Windows PowerShell과 Windows의 .NET Framework C# 컴파일러를 사용합니다. 런타임 의존 파일은 저장소에 함께 포함됩니다.

```powershell
.\build.ps1 -OutputDirectory .\dist\app
.\scripts\test-release.ps1
.\scripts\package.ps1 -InnoCompiler 'C:\Program Files\Inno Setup 7\ISCC.exe'
```

`VERSION`은 `major.minor.patch` 형식입니다. Inno Setup 6.5 이상이 필요합니다. `dist`에 설치 EXE, 휴대용 ZIP, `SHA256SUMS.txt`가 생성됩니다. `.github/workflows/release.yml`은 `v0.1.0` 형태의 태그를 푸시하면 검사와 빌드를 수행하고 GitHub 릴리스 **초안**을 만듭니다. 검사 후 게시하면 앱의 업데이트 버튼에서 확인할 수 있습니다.

기존 전체화면 이미지/영상/시퀀스/3D 표시는 직접 실행되는 로컬 프로그램입니다. 자동 시작 업데이트 검사와 사용 기록 전송은 없으며, 업데이트 버튼을 누를 때 GitHub에 접속합니다.

## 라이선스

LumaView 자체 소스의 공개 라이선스는 아직 지정하지 않았습니다. 공개 라이선스를 부여하려면 배포 전에 소유자가 결정해야 합니다. 포함된 제3자 구성 요소에는 각자의 라이선스가 적용됩니다. `THIRD-PARTY-NOTICES.txt`와 `licenses/`를 확인하세요.
