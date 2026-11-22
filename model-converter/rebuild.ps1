param([string]$CMake='cmake')
$ErrorActionPreference='Stop'
$work=Join-Path (Split-Path $PSScriptRoot -Parent) ('dist\converter-build-'+[guid]::NewGuid().ToString('N'))
$source=Join-Path $work 'assimp'
$build=Join-Path $work 'build'
New-Item -ItemType Directory -Force -Path $work | Out-Null
& git clone --quiet --depth 1 --branch v6.0.6 --filter=blob:none --sparse https://github.com/assimp/assimp.git $source
if ($LASTEXITCODE -or (git -C $source rev-parse HEAD) -ne 'a79ff6bbce54d3e01186a918dc6374ed55317426') { throw 'Assimp source mismatch' }
& git -C $source sparse-checkout set code include contrib cmake-modules packaging
if ($LASTEXITCODE) { throw 'Assimp checkout failed' }
& git -C $source apply (Join-Path $PSScriptRoot 'fbx-scaling.patch')
if ($LASTEXITCODE) { throw 'FBX scaling animation patch failed' }
& $CMake -S $PSScriptRoot -B $build -G 'Visual Studio 17 2022' -A x64 "-DASSIMP_SOURCE_DIR=$source"
if ($LASTEXITCODE) { throw 'Configure failed' }
& $CMake --build $build --config Release --target DollView.Convert --parallel 6
if ($LASTEXITCODE) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $build 'Release\DollView.Convert.exe') -Destination $PSScriptRoot
