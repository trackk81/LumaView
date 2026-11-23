# DollView 0.2.35

- FBX를 GLB로 변환할 때 외부 텍스처를 찾지 못해 변환이 중단되던 문제를 수정했습니다. 예를 들어 FBX에 `wing.tga`로 기록되어 있어도 패키지의 `textures` 폴더에 있는 `wing.tga.png` 또는 `wing.png`를 찾아 GLB에 포함합니다.
- 색상과 투명도에 같은 텍스처를 쓰는 FBX는 GLB와 glTF에서도 이미지의 알파 채널을 사용합니다. 불사조 모델로 깃털의 투명도 설정과 비행 애니메이션을 확인했습니다.

Windows x64용 설치 파일은 `DollView-Setup-0.2.35-x64.exe`, 휴대용은 `DollView-0.2.35-win-x64.zip`입니다. 기존 사용자는 DollView에서 **업데이트**를 누르면 됩니다.

개인과 기업 모두 업무용으로 무료로 사용할 수 있습니다. [무료 사용 라이선스](https://github.com/trackk81/LumaView/blob/v0.2.35/LICENSE.txt) · [동봉한 코덱의 소스 및 빌드 자료](https://github.com/trackk81/LumaView/releases/tag/v0.2.30)
