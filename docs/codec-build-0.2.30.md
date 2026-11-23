# DollView v0.2.30 코덱 소스와 빌드 안내

설치판과 휴대용 ZIP에 들어 있는 Windows x64 코덱을 기준으로 정리했습니다. 아래 자료는 [v0.2.30 배포 페이지](https://github.com/trackk81/LumaView/releases/tag/v0.2.30)에서 무료로 받을 수 있습니다. DollView 본체의 무료 사용 조건은 별도 첨부한 `DollView-Freeware-License.txt`에 있습니다. 코덱과 라이브러리에는 각 소스에 포함된 라이선스가 적용됩니다.

| 다운로드 | 내용 |
| --- | --- |
| `ImageMagick-7.1.2-35-Windows.7z` | 제작자가 공개한 Windows 소스 원본. ImageMagick, Configure 및 이미지 라이브러리의 소스가 들어 있습니다. |
| `Codec-Sources-0.2.30.zip` | mpv, FFmpeg와 의존 소스, Rust 크레이트, 서브모듈, 패치, 빌드 레시피, ImageMagick의 Windows 프로젝트와 SBOM(사용 구성 요소 명세) |
| `Codec-SHA256SUMS-0.2.30.txt` | 이 릴리스에 추가한 자료의 SHA-256. Windows에서는 `Get-FileHash 파일명 -Algorithm SHA256`으로 확인할 수 있습니다. |

## 이미지 코덱을 다시 빌드하려면

배포한 `codecs/magick.exe`는 **ImageMagick 7.1.2-35 portable Q16-HDRI x64**입니다. 공식 배포본에서 추출한 실행 파일과 SHA-256이 일치했습니다.

1. Visual Studio 2026의 C++ 데스크톱 개발 워크로드와 x64용 ATL·MFC를 설치합니다. `Codec-Sources-0.2.30.zip`의 `image/ImageMagick-Windows-project.tar.gz`를 풀고, 그 안의 프로젝트 루트에 `ImageMagick-7.1.2-35-Windows.7z`를 풉니다. 루트의 `Directory.Build.props`와 같은 위치에 `Configure`, `Dependencies`, `ImageMagick` 폴더가 있어야 합니다.
2. 이미지 라이브러리를 수정하려면 `Dependencies/Dependencies` 안의 해당 소스를 편집합니다. `Configure/Configure.sln`을 Release·x64로 빌드합니다. 구성 도구 실행 파일은 `Configure/Artifacts`에 생성됩니다. 프로젝트 루트의 `Readme.md`에 도구 사용법이 있습니다.
3. 구성 도구에서 **Visual Studio 2026, x64, Q16, HDRI, static, 정적 C 런타임**을 선택합니다. 포터블 공식 빌드의 구성 인자는 `/noWizard /VS2026 /hdri /Q16 /x64 /static /linkRuntime`입니다. 생성된 솔루션을 Release·x64로 다시 빌드하면 새 라이브러리와 실행 파일이 `Artifacts`에 생성됩니다. 의존 라이브러리를 별도로 빌드할 때는 `Dependencies/.github/workflows/main.yml`의 static·OpenMP·linked-runtime 작업을 따릅니다. 원본의 작업 순서와 MSBuild 명령은 `image/ImageMagick-release.yml`에도 있습니다.
4. DollView 휴대용 ZIP을 새 폴더에 풀어 테스트용 사본을 만들고, 그 안의 `codecs/magick.exe`를 새로 만든 파일로 교체합니다. `codecs/magick.exe -version`으로 Q16-HDRI와 필요한 포맷이 표시되는지 확인한 뒤 이미지를 엽니다.

배포 실행 파일의 SHA-256은 `7521696162629ee27e6e4c68ca3884a5cbd7d17d760a2a0efc9a001470e5b822`입니다. [ImageMagick 공식 릴리스](https://github.com/ImageMagick/ImageMagick/releases/tag/7.1.2-35) · [사용한 Windows 프로젝트](https://github.com/ImageMagick/Windows/tree/00ddd4dfc905f970f1ab658d9a86afce9ea9d387)

## 영상 코덱을 다시 빌드하려면

DollView는 `video-runtime/mpv.exe`를 독립 프로세스로 실행합니다. 이 파일에는 FFmpeg가 포함되어 있습니다. ZIP의 `source-manifest.json`에서 패키지별 저장소, 버전 식별 근거, 소스 파일명과 해시를 확인할 수 있습니다. `packages/`에는 소스 아카이브가, `additional/`에는 Cargo.lock에 기록된 크레이트와 서브모듈 소스가 있습니다. `mpv-winbuild-cmake.tar.gz` 안의 `packages/` 디렉터리에는 빌드 설정과 적용할 패치가 들어 있습니다.

| 항목 | 배포본에 대응하는 버전 |
| --- | --- |
| mpv | `bcde6b93cca4d128d84e1a60d1a6db2cd81c889e` |
| FFmpeg | `cd4e96eb314630788b7ff2f9820c395788c023e2`, GPL 및 version3 옵션 사용 |
| Windows 빌드 레시피 | `bda10a2816ce9908d419b3f4b564227330457e76`, shinchiro의 2026-11-11 Clang x86_64 작업 |

Linux x86_64에서 ZIP을 풀고 해당 폴더에서 다음 명령을 실행합니다. Python 3.12 이상과 원 제작자가 지정한 CMake·Ninja·Clang용 빌드 환경을 사용합니다. 인자와 호스트 도구 설치법은 생성된 `recipe/README.md` 및 `recipe/.github/workflows/mpv_clang.yml`에 있습니다. 원본 빌드 컨테이너는 `ghcr.io/shinchiro/archlinux@sha256:f531a32ce7b58411385c29ca307b0366b919effb5e1a5fe12028c2cdf09ab2be`입니다.

```sh
python3 pin-recipe.py recipe
cmake -G Ninja -B build -S recipe \
  -DTARGET_ARCH=x86_64-w64-mingw32 -DCOMPILER_TOOLCHAIN=clang \
  -DCMAKE_INSTALL_PREFIX="$PWD/clang_root" \
  -DMINGW_INSTALL_PREFIX="$PWD/build/x86_64-w64-mingw32" \
  -DSINGLE_SOURCE_LOCATION="$PWD/src_packages" \
  -DRUSTUP_LOCATION="$PWD/clang_root/install_rustup" \
  -DENABLE_CCACHE=ON -DCLANG_PACKAGES_LTO=ON
ninja -C build llvm
ninja -C build rustup
ninja -C build llvm-clang
ninja -C build mpv
```

`pin-recipe.py`는 동봉된 소스 아카이브의 해시를 검사하고 새 폴더에 버전을 고정한 레시피를 만듭니다. Git 의존성은 해당 커밋에서 받아오므로 최초 빌드에는 인터넷 연결이 필요합니다. 결과물은 `build/packages/mpv-package/mpv.exe`에 있습니다. 새 실행 파일을 DollView 테스트용 사본의 `video-runtime/mpv.exe`에 넣고 `--version`과 영상 재생을 확인합니다.

배포 실행 파일의 SHA-256은 `3ea89fe7a4284b25dff3c72e1bde83e27324c90cfc4a074fe679293d19ee7c85`입니다. mpv와 FFmpeg 버전은 배포 바이너리에서 확인했습니다. 일부 간접 의존성은 원 제작자가 체크아웃 기록을 남기지 않아 당시 브랜치 이력으로 복원했습니다. libcurl 8.22.0-DEV 소스는 배포 실행 파일에 대응하는 디버그 심벌과 대조했습니다. 빌드에 쓰이는 코드가 같고 CI 라벨 설정만 다른 두 후보의 차이도 `additional/curl-alternate-ci-only.patch`에 포함했습니다. 원 제작자의 컴파일러 캐시를 사용한 전체 재빌드와 바이트 단위 일치는 검증 범위에 포함되지 않습니다. [원본 x64 빌드 기록](https://github.com/shinchiro/mpv-winbuild-cmake/actions/runs/42927840447/job/130218793202) · [원본 영상 코덱 배포](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261111)

영상 폴더의 `d3dcompiler_43.dll`은 원본 mpv 패키지에 포함된 Microsoft 서명 바이너리입니다. 코덱을 교체할 때는 테스트용 DollView 사본에 포함된 이 파일을 그대로 사용할 수 있습니다. [Microsoft Direct3D 컴파일러 배포 안내](https://learn.microsoft.com/en-us/windows/win32/directx-sdk--august-2009-)
