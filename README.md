# LumaView

Windows용 이미지·시퀀스·영상·3D 모델 뷰어. 한국어/영어, 어두운/밝은/사용자 색상 테마를 지원합니다.

## ZIP으로 실행

Windows 10/11 64비트, .NET Framework 4.8 이상이 필요합니다.

1. [최신 배포](https://github.com/trackk81/LumaView/releases/latest)에서 `LumaView-0.1.1-win-x64.zip`을 받습니다.
2. ZIP 파일의 **압축을 모두 풀고** 원하는 폴더에 둡니다.
3. 폴더 안의 `LumaView.exe`를 실행합니다. `codecs`, `model-viewer`, DLL 파일도 함께 유지하세요.

JPG/PNG 등을 더블클릭해서 열고 싶다면 **설정 → 파일 연결**에서 확장자를 선택하고 연결을 누르세요. Windows에서 LumaView를 최종 선택해야 하며 기존 UserChoice를 강제로 변경하지 않습니다. 연결한 뒤 프로그램 폴더를 옮겼다면 새 위치에서 다시 연결하세요.

3D 보기를 사용하려면 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)이 필요합니다. 대부분의 Windows 10/11에는 이미 설치되어 있습니다. .NET Framework 4.8이 없는 PC에서는 먼저 해당 구성 요소를 설치하세요.

프로그램 설치 과정은 없습니다. 설정과 캐시는 사용자 프로필에 보관하므로 다른 버전 폴더에서도 기존 설정을 사용합니다.

## 업데이트

일반 탐색 화면에서 **설정 오른쪽의 업데이트** 버튼을 누르세요. `trackk81/LumaView`의 최신 공개 정식 릴리스를 확인합니다. 새 버전의 ZIP을 내려받고 SHA-256 검사값을 검증한 뒤 파일이 있는 폴더를 엽니다. 압축을 모두 풀고 새 폴더의 `LumaView.exe`를 실행하세요. 실행 중인 파일을 자동으로 덮어쓰지 않습니다. 초안과 사전 릴리스는 업데이트 대상에서 제외합니다.

공개 릴리스가 없으면 ‘아직 공개된 업데이트가 없습니다’로 표시됩니다. 프로그램은 아직 코드 서명되지 않았습니다. 0.1.0 개발 버전은 설치 파일을 확인하는 방식이므로 처음 한 번 0.1.1 ZIP을 직접 받아 실행하세요.

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
.\scripts\package.ps1
```

`VERSION`은 `major.minor.patch` 형식입니다. `dist`에 휴대용 ZIP과 `SHA256SUMS.txt`가 생성됩니다. `.github/workflows/release.yml`은 `v0.1.1` 형태의 태그를 푸시하면 검사와 빌드를 수행하고 GitHub 릴리스 **초안**을 만듭니다. 검사 후 게시하면 앱의 업데이트 버튼에서 확인할 수 있습니다. 설치 도구는 ZIP 배포에 필요하지 않습니다.

기존 전체화면 이미지/영상/시퀀스/3D 표시는 직접 실행되는 로컬 프로그램입니다. 자동 시작 업데이트 검사와 사용 기록 전송은 없으며, 업데이트 버튼을 누를 때 GitHub에 접속합니다.

## 라이선스

LumaView 자체 소스의 공개 라이선스는 아직 지정하지 않았습니다. 공개 라이선스를 부여하려면 배포 전에 소유자가 결정해야 합니다. 포함된 제3자 구성 요소에는 각자의 라이선스가 적용됩니다. `THIRD-PARTY-NOTICES.txt`와 `licenses/`를 확인하세요.
