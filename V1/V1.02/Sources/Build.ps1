param([string]$Gradle = 'gradle', [string]$Sdk = $env:ANDROID_HOME)
$ErrorActionPreference = 'Stop'
$versionDir = Split-Path $PSScriptRoot -Parent
$logDir = Join-Path $versionDir 'Build/Logs'
New-Item -ItemType Directory -Force $logDir, "$versionDir/Distribution" | Out-Null
if ($Sdk) { $env:ANDROID_HOME = $Sdk }
& $Gradle -p "$PSScriptRoot/Android" assembleRelease --console plain *>> "$logDir/android-build.log"
if ($LASTEXITCODE -ne 0) { throw "Compilation Android échouée : $logDir/android-build.log" }
$apkName = if ($env:INTERNETUSB_KEYSTORE) { 'app-release.apk' } else { 'app-release-unsigned.apk' }
$outputName = if ($env:INTERNETUSB_KEYSTORE) { 'InternetUSB-1.02.apk' } else { 'InternetUSB-1.02-unsigned.apk' }
Copy-Item "$PSScriptRoot/Android/app/build/outputs/apk/release/$apkName" "$versionDir/Distribution/$outputName"
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/out:$versionDir\Distribution\InternetUSB.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\Windows\InternetUSB.cs" *>> "$logDir/windows-build.log"
if ($LASTEXITCODE -ne 0) { throw "Compilation Windows échouée : $logDir/windows-build.log" }
Write-Output 'Compilation V1.02 terminée. Une APK non signée doit être signée avant installation.'
