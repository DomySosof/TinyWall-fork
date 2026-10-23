<#
    SUBIR-A-GITHUB.ps1  -  Prepara y sube esta carpeta a GitHub.

    Uso rapido (con el repo ya creado en github.com):
        .\SUBIR-A-GITHUB.ps1 -RepoUrl https://github.com/TU-USUARIO/TinyWall-fork.git

    Uso interactivo (te pregunta la URL):
        .\SUBIR-A-GITHUB.ps1

    Si PowerShell bloquea la ejecucion del script, usa:
        powershell -ExecutionPolicy Bypass -File .\SUBIR-A-GITHUB.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoUrl = "",
    [string]$Branch = "main",
    [string]$CommitMessage = "Import inicial del codigo fuente de TinyWall (fork)"
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

function Stop-WithError([string]$msg) {
    Write-Host ""
    Write-Host "ERROR: $msg" -ForegroundColor Red
    Write-Host ""
    exit 1
}

# ---------------------------------------------------------------- 1) Git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "Git no esta instalado (o no esta en el PATH)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Instalalo con:"
    Write-Host "    winget install --id Git.Git -e"
    Write-Host ""
    Write-Host "o descargalo de: https://git-scm.com/download/win"
    Write-Host ""
    Write-Host "Despues CIERRA esta terminal, abre una nueva y vuelve a ejecutar este script."
    exit 1
}

# ---------------------------------------------------------------- 2) Autor
$authorName  = (git config --global user.name)  2>$null
$authorEmail = (git config --global user.email) 2>$null

if ([string]::IsNullOrWhiteSpace($authorName)) {
    $authorName = Read-Host "Tu nombre para los commits (ej: Juan Perez)"
    if ([string]::IsNullOrWhiteSpace($authorName)) { Stop-WithError "El nombre no puede estar vacio." }
    git config --global user.name $authorName
}
if ([string]::IsNullOrWhiteSpace($authorEmail)) {
    $authorEmail = Read-Host "Tu email de GitHub"
    if ([string]::IsNullOrWhiteSpace($authorEmail)) { Stop-WithError "El email no puede estar vacio." }
    git config --global user.email $authorEmail
}

# ---------------------------------------------------------------- 3) Repo local
if (-not (Test-Path (Join-Path $PSScriptRoot ".git"))) {
    git init -b $Branch 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        git init | Out-Null
        git checkout -b $Branch 2>$null | Out-Null
    }
    Write-Host "Repositorio local creado (rama $Branch)." -ForegroundColor Green
} else {
    Write-Host "Ya existe un repositorio local (.git), se reutiliza." -ForegroundColor Green
}

# ---------------------------------------------------------------- 4) Commit
git add -A
if ([string]::IsNullOrWhiteSpace((git status --porcelain) -join "")) {
    Write-Host "No hay cambios nuevos que guardar." -ForegroundColor Yellow
} else {
    git commit -m $CommitMessage | Out-Null
    Write-Host "Commit inicial creado." -ForegroundColor Green
}

# ---------------------------------------------------------------- 5) Remoto + push
if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    Write-Host ""
    Write-Host "Pega la URL del repositorio (Enter para omitir y subirlo tu mas tarde):"
    $RepoUrl = Read-Host "URL"
}

if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    Write-Host ""
    Write-Host "Repo local listo, sin remoto configurado. Cuando quieras:" -ForegroundColor Yellow
    Write-Host "    git remote add origin https://github.com/TU-USUARIO/TinyWall-fork.git"
    Write-Host "    git push -u origin $Branch"
} else {
    $RepoUrl = $RepoUrl.Trim()
    if (-not $RepoUrl.EndsWith(".git")) { $RepoUrl = $RepoUrl.TrimEnd("/") + ".git" }

    $remotes = @(git remote)
    if ($remotes -contains "origin") {
        git remote set-url origin $RepoUrl
    } else {
        git remote add origin $RepoUrl
    }

    Write-Host ""
    Write-Host "Subiendo a $RepoUrl ..." -ForegroundColor Cyan
    git push -u origin $Branch
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError "El push fallo. Revisa tus credenciales (o ejecuta 'gh auth login' si tienes GitHub CLI)."
    }
    Write-Host ""
    Write-Host "Listo: codigo subido a GitHub." -ForegroundColor Green
}

# ---------------------------------------------------------------- 6) Resumen
Write-Host ""
Write-Host "----- Resumen -----" -ForegroundColor Cyan
Write-Host ("Rama      : " + (git rev-parse --abbrev-ref HEAD))
Write-Host ("Commits   : " + (git rev-list --count HEAD))
Write-Host ("Remoto    : " + (@(git remote -v) -join " "))
