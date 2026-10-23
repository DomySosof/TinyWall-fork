<#
.SYNOPSIS
    Compila TinyWall en Windows y, opcionalmente, genera e instala el MSI.

.DESCRIPTION
    Script de compilacion para el proyecto "Firewall v1" (TinyWall).
    Busca MSBuild, restaura los paquetes NuGet y compila la aplicacion.
    Con -Msi genera tambien el instalador MSI x86 (requiere WiX Toolset v3).
    Con -Install instala el MSI generado (requiere permisos de administrador).

.PARAMETER Configuration
    Debug o Release. Por defecto Release.

.PARAMETER Msi
    Genera el instalador MSI en la carpeta dist.

.PARAMETER Install
    Instala el MSI generado. Implica -Msi.

.PARAMETER Clean
    Fuerza una recompilacion limpia (target Rebuild).

.PARAMETER NoRestore
    Omite la restauracion de paquetes NuGet.

.PARAMETER OutputPath
    Carpeta de salida de la compilacion.
    Por defecto: artifacts\<Configuration>.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Msi

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Msi -Install

.NOTES
    Requiere Visual Studio 2022 Build Tools (o Visual Studio 2022) con la
    carga de trabajo ".NET desktop development", el targeting pack de
    .NET Framework 4.8, el Windows SDK y el .NET SDK 8 o superior.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Msi,
    [switch]$Install,
    [switch]$Clean,
    [switch]$NoRestore,

    [string]$OutputPath
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'TinyWall\TinyWall.csproj'
$MsiProject = Join-Path $Root 'MsiSetup\MsiSetup.wixproj'

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Info {
    param([string]$Message)
    Write-Host "    $Message"
}

function Stop-WithError {
    param([string]$Message)
    Write-Host ''
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-MSBuild {
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $programFiles = [Environment]::GetEnvironmentVariable('ProgramFiles')

    if ($programFilesX86) {
        $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
            foreach ($line in @($found)) {
                $candidate = "$line".Trim()
                if ($candidate.Length -gt 0 -and (Test-Path -LiteralPath $candidate)) {
                    return $candidate
                }
            }
        }
    }

    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $candidates = @()
    $editions = @('Enterprise', 'Professional', 'Community', 'BuildTools')
    if ($programFiles) {
        foreach ($edition in $editions) {
            $candidates += Join-Path $programFiles "Microsoft Visual Studio\2022\$edition\MSBuild\Current\Bin\MSBuild.exe"
        }
    }
    if ($programFilesX86) {
        foreach ($edition in $editions) {
            $candidates += Join-Path $programFilesX86 "Microsoft Visual Studio\2022\$edition\MSBuild\Current\Bin\MSBuild.exe"
            $candidates += Join-Path $programFilesX86 "Microsoft Visual Studio\2019\$edition\MSBuild\Current\Bin\MSBuild.exe"
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Initialize-BuildEnvironment {
    # Some Build Tools installations cannot resolve Microsoft.NET.Sdk on their
    # own. Point MSBuild at the newest .NET SDK when that happens.
    if (-not $env:MSBuildSDKsPath) {
        $programFiles = [Environment]::GetEnvironmentVariable('ProgramFiles')
        if ($programFiles) {
            $sdkRoot = Join-Path $programFiles 'dotnet\sdk'
            if (Test-Path -LiteralPath $sdkRoot) {
                $sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory |
                    Sort-Object LastWriteTime -Descending |
                    Select-Object -First 1
                if ($null -ne $sdk) {
                    $sdkPath = Join-Path $sdk.FullName 'Sdks'
                    if (Test-Path -LiteralPath $sdkPath) {
                        $env:MSBuildSDKsPath = $sdkPath
                        Write-Info "MSBuildSDKsPath = $sdkPath"
                    }
                }
            }
        }
    }

    if (-not $env:MSBuildEnableWorkloadResolver) {
        $env:MSBuildEnableWorkloadResolver = 'false'
    }
}

function Find-Wix {
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $programFiles = [Environment]::GetEnvironmentVariable('ProgramFiles')

    $roots = @()
    if ($programFilesX86) { $roots += $programFilesX86 }
    if ($programFiles) { $roots += $programFiles }

    foreach ($root in $roots) {
        $targets = Join-Path $root 'MSBuild\Microsoft\WiX\v3.x\wix.targets'
        if (Test-Path -LiteralPath $targets) {
            return @{
                Targets = $targets
                InstallPath = (Split-Path -Parent $targets)
            }
        }
    }

    return $null
}

function New-StagingDirectory {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Build-MsiPackage {
    param(
        [string]$BuildOutput,
        [string]$Version
    )

    $wix = Find-Wix
    if ($null -eq $wix) {
        Stop-WithError ("No se encontro WiX Toolset v3. Instalalo con: " +
            "'choco install wixtoolset -y' o descargalo de " +
            "https://github.com/wixtoolset/wix3/releases")
    }

    Write-Step 'Preparando archivos para el MSI'

    $stageRoot = Join-Path $Root 'MsiSetup\Sources'
    $stageProgram = Join-Path $stageRoot 'ProgramFiles\TinyWall'
    $stageData = Join-Path $stageRoot 'CommonAppData\TinyWall'

    New-StagingDirectory -Path $stageProgram
    New-StagingDirectory -Path $stageData

    Copy-Item -LiteralPath (Join-Path $BuildOutput 'TinyWall.exe') -Destination $stageProgram -Force
    Copy-Item -LiteralPath (Join-Path $BuildOutput 'TinyWall.exe.config') -Destination $stageProgram -Force
    Get-ChildItem -LiteralPath $BuildOutput -Filter '*.dll' -File |
        Copy-Item -Destination $stageProgram -Force

    Get-ChildItem -LiteralPath $BuildOutput -Directory | ForEach-Object {
        $satellite = Join-Path $_.FullName 'TinyWall.resources.dll'
        if (Test-Path -LiteralPath $satellite) {
            $destination = Join-Path $stageProgram $_.Name
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Copy-Item -LiteralPath $satellite -Destination $destination -Force
        }
    }

    Copy-Item -LiteralPath (Join-Path $Root 'MsiSetup\License.rtf') -Destination $stageProgram -Force
    Copy-Item -LiteralPath (Join-Path $Root 'TinyWall\Resources\img\TinyWall.ico') -Destination $stageProgram -Force
    Copy-Item -LiteralPath (Join-Path $Root 'TinyWall\doc') -Destination (Join-Path $stageProgram 'doc') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $Root 'TinyWall\Resources\hosts') -Destination (Join-Path $stageData 'hosts.custom') -Force

    Write-Step 'Generando profiles.json'

    $databaseSource = Join-Path $Root 'TinyWall\Database'
    $creator = Start-Process -FilePath (Join-Path $BuildOutput 'TinyWall.exe') `
        -ArgumentList @(
            'database-creator',
            '/source-folder', ('"' + $databaseSource + '"'),
            '/output-folder', ('"' + $stageData + '"')
        ) -Wait -PassThru -NoNewWindow

    if ($creator.ExitCode -ne 0) {
        Stop-WithError "database-creator fallo con codigo $($creator.ExitCode)."
    }

    Write-Step 'Compilando el MSI'

    $msiArguments = @(
        $MsiProject,
        '/p:Configuration=Release',
        '/p:Platform=x86',
        "/p:WixTargetsPath=$($wix.Targets)",
        "/p:WixInstallPath=$($wix.InstallPath)",
        '/t:Rebuild',
        '/v:m',
        '/nologo'
    )
    & $MSBuild @msiArguments
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError 'La compilacion del MSI fallo.'
    }

    $builtMsi = Join-Path $Root 'MsiSetup\bin\Release\TinyWall_x86.msi'
    if (-not (Test-Path -LiteralPath $builtMsi)) {
        Stop-WithError "No se genero el MSI esperado: $builtMsi"
    }

    $dist = Join-Path $Root 'dist'
    New-Item -ItemType Directory -Path $dist -Force | Out-Null
    $msiDestination = Join-Path $dist "TinyWall-Setup-$Version-x86.msi"
    Copy-Item -LiteralPath $builtMsi -Destination $msiDestination -Force

    Write-Info "MSI: $msiDestination"
    Write-Info "SHA256: $((Get-FileHash -LiteralPath $msiDestination -Algorithm SHA256).Hash)"

    return $msiDestination
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if (-not (Test-Path -LiteralPath $Project)) {
    Stop-WithError "No se encontro el proyecto: $Project"
}

if ($Install) {
    if (-not (Test-IsAdministrator)) {
        Stop-WithError 'Para instalar hay que ejecutar PowerShell como administrador.'
    }
    $Msi = $true
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $Root "artifacts\$Configuration"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ($OutputPath.EndsWith('\')) {
    $OutputPath = $OutputPath.TrimEnd('\')
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

Write-Step "Buscando MSBuild"
$MSBuild = Find-MSBuild
if ($null -eq $MSBuild) {
    Stop-WithError ('No se encontro MSBuild. Instala Visual Studio 2022 Build Tools ' +
        'con la carga de trabajo ".NET desktop development".')
}
Write-Info "MSBuild: $MSBuild"

Initialize-BuildEnvironment

Write-Step "Restaurando paquetes ($Configuration)"
if (-not $NoRestore) {
    & $MSBuild $Project /t:Restore "/p:Configuration=$Configuration" /v:m /nologo
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError 'La restauracion de paquetes NuGet fallo.'
    }
}
else {
    Write-Info 'Restauracion omitida (-NoRestore).'
}

$target = if ($Clean) { 'Rebuild' } else { 'Build' }
Write-Step "Compilando TinyWall ($Configuration)"
& $MSBuild $Project "/t:$target" "/p:Configuration=$Configuration" '/p:Platform=AnyCPU' "/p:OutputPath=$OutputPath" /v:m /nologo
if ($LASTEXITCODE -ne 0) {
    Stop-WithError ('La compilacion fallo. Si el servicio TinyWall esta ejecutandose ' +
        'desde la carpeta de salida, detenlo o usa -OutputPath con otra carpeta.')
}

$exePath = Join-Path $OutputPath 'TinyWall.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    Stop-WithError "No se genero el ejecutable esperado: $exePath"
}

$version = (Get-Item -LiteralPath $exePath).VersionInfo.ProductVersion
Write-Step 'Compilacion completada'
Write-Info "Aplicacion: $exePath"
Write-Info "Version: $version"

$msiPath = $null
if ($Msi) {
    $msiPath = Build-MsiPackage -BuildOutput $OutputPath -Version $version
}

if ($Install) {
    if (-not (Test-IsAdministrator)) {
        Stop-WithError ('Para instalar hay que ejecutar PowerShell como administrador. ' +
            "MSI generado: $msiPath")
    }

    if (-not $msiPath -or -not (Test-Path -LiteralPath $msiPath)) {
        $candidate = Get-ChildItem -LiteralPath (Join-Path $Root 'dist') -Filter '*.msi' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
        if ($null -eq $candidate) {
            Stop-WithError 'No hay ningun MSI para instalar. Ejecuta el script con -Msi.'
        }
        $msiPath = $candidate.FullName
    }

    Write-Step "Instalando $msiPath"
    $installer = Start-Process -FilePath 'msiexec.exe' `
        -ArgumentList @('/i', ('"' + $msiPath + '"')) -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        Stop-WithError "msiexec devolvio el codigo $($installer.ExitCode)."
    }
    Write-Step 'Instalacion completada'
}

Write-Host ''
Write-Host 'Listo.' -ForegroundColor Green
