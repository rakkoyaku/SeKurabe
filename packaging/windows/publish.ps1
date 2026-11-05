# 使い方: pwsh packaging/windows/publish.ps1 [-Version 0.1.0] [-SingleFile]
#   既定      : Velopack でパッケージを作る（dist/Releases に Setup.exe・Portable.zip・更新用 nupkg）。vpk が必要:
#               dotnet tool install -g vpk --version 1.2.161
#   -SingleFile: 自動更新なしの 1 ファイル exe（dist/SeKurabe-<ver>-win-x64.exe）を作る
# 公開用のリリースは GitHub Actions（タグの push）で作るので、これは手元での確認用。
param([string]$Version = "0.1.0", [switch]$SingleFile)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/../.."
$out = Join-Path $root "dist/win-x64"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }

if ($SingleFile) {
    dotnet publish "$root/src/SeKurabe" -c Release -r win-x64 -p:Version=$Version -p:SingleFile=true -o $out
    Copy-Item (Join-Path $out "SeKurabe.exe") (Join-Path $root "dist/SeKurabe-$Version-win-x64.exe") -Force
    Write-Host "created dist/SeKurabe-$Version-win-x64.exe"
    exit
}

dotnet publish "$root/src/SeKurabe" -c Release -r win-x64 -p:Version=$Version -o $out
vpk pack -u SeKurabe -v $Version -p $out -o (Join-Path $root "dist/Releases") `
    --channel win -r win-x64 -e SeKurabe.exe -i "$root/src/SeKurabe/Assets/SeKurabe.ico" --packTitle SeKurabe
Write-Host "created dist/Releases"
