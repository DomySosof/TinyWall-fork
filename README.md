# Firewall v1 (TinyWall)

Compilación personalizada de [TinyWall](https://tinywall.pados.hu), un firewall
gratuito, ligero y no intrusivo para Windows. TinyWall no instala drivers de
kernel y bloquea por defecto todo el tráfico que no hayas autorizado.

Esta versión añade:

- **Tema claro u oscuro** en el panel principal (Dashboard) y en el widget
  flotante de tráfico.
- **Colores personalizables** para la serie de descarga, la serie de subida y
  el color de acento de la interfaz.
- **Transparencia del fondo** del panel principal, ajustable del 30% al 100%.
- Aplicación **en vivo** de los cambios de tema, color y transparencia al
  guardar los ajustes.
- Interfaz disponible en español, inglés y el resto de idiomas de TinyWall.

## Requisitos

### Para usar la aplicación

- Windows 10 21H2 (build 19044) o superior, o Windows 11.
- .NET Framework 4.8.
- Permisos de administrador para instalar el servicio.

### Para compilar

- Windows 10/11.
- Visual Studio 2022 Build Tools o Visual Studio 2022 con:
  - Carga de trabajo **.NET desktop development**.
  - **.NET Framework 4.8 targeting pack**.
  - **Windows 10/11 SDK** (necesario para las referencias COM
    `NetFwTypeLib` y `TaskScheduler`).
- **.NET SDK 8 o superior**.
- **WiX Toolset v3** (solo si quieres generar el instalador MSI).
- PowerShell 5.1 o superior.

## Compilar

Desde la raíz del proyecto:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

El resultado queda en:

```
artifacts\Release\TinyWall.exe
```

### Generar el instalador MSI

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Msi
```

El MSI se genera en:

```
dist\TinyWall-Setup-<version>-x86.msi
```

### Generar e instalar

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Msi -Install
```

`-Install` requiere ejecutar PowerShell **como administrador**.

### Opciones de install.ps1

| Opción | Descripción |
| --- | --- |
| `-Configuration Debug\|Release` | Configuración de compilación. Por defecto `Release`. |
| `-Msi` | Genera el instalador MSI x86. Requiere WiX Toolset v3. |
| `-Install` | Instala el MSI generado. Implica `-Msi`. |
| `-Clean` | Recompila desde cero (target `Rebuild`). |
| `-NoRestore` | Omite la restauración de paquetes NuGet. |
| `-OutputPath <ruta>` | Carpeta de salida de la compilación. |

Ejemplos:

```powershell
# Compilación Debug en una carpeta concreta
.\install.ps1 -Configuration Debug -OutputPath C:\build\tinywall

# Recompilar limpio y generar MSI
.\install.ps1 -Clean -Msi
```

### Compilación manual

Si prefieres usar MSBuild directamente:

```powershell
# Ajusta la ruta a la version de .NET SDK instalada en tu equipo
$sdk = Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory |
    Sort-Object Name -Descending | Select-Object -First 1
$env:MSBuildSDKsPath = Join-Path $sdk.FullName "Sdks"
$env:MSBuildEnableWorkloadResolver = "false"

msbuild TinyWall\TinyWall.csproj `
  /p:Configuration=Release `
  /p:Platform=AnyCPU `
  /t:Rebuild
```

> **Nota:** no uses `dotnet build` para este proyecto. Las referencias COM
> (`NetFwTypeLib`, `TaskScheduler`) provocan el error `MSB4803`; hay que usar
> MSBuild de Visual Studio.

## Uso de las novedades

1. Abre **Ajustes** desde el icono de TinyWall en la bandeja del sistema.
2. En la pestaña **General**:
   - **Tema de interfaz:** `Claro` u `Oscuro`.
   - **Colores del gráfico:** botones **Descarga**, **Subida** y **Acento**.
     Cada uno abre el selector de color de Windows.
   - **Restablecer** devuelve los tres colores a los valores del tema actual.
   - **Transparencia del fondo:** deslizador del 30% al 100%. Al bajarlo, el
     panel se vuelve translúcido y el gráfico conserva sus colores. Para ver
     solo el gráfico, usa el widget flotante y su menú de transparencia (clic
     derecho sobre el widget).
3. Pulsa **Aceptar**. El panel y el widget ya abiertos se actualizan al
   instante.

Los colores se guardan como valores hexadecimales (`#RRGGBB`). Si se dejan
vacíos se usan los colores predeterminados del tema claro u oscuro.

## Estructura del proyecto

```
firewall v1/
├── .github/workflows/        CI de GitHub Actions (compilación y MSI)
├── Docs/                     Documentación
├── LocalizationPack/         Recursos para traducir TinyWall
├── Microsoft.Samples/        Código de ejemplo de Microsoft (TaskDialog)
├── MsiSetup/                 Proyecto WiX del instalador
├── pylorak.Utilities/        Utilidades comunes
├── pylorak.Windows/          Utilidades de Windows
├── pylorak.Windows.Services/ Utilidades de servicios
├── pylorak.Windows.WFP/      Utilidades del filtro WFP
├── TinyWall/                 Aplicación principal
│   ├── Database/             Base de datos de perfiles y excepciones
│   ├── Resources/            Iconos, textos y recursos localizados
│   └── ...
├── TinyWall.sln              Solución de Visual Studio
├── install.ps1               Compila y genera/instala el MSI
├── build.ps1                 Script oficial de publicación
├── LICENSE.txt               Licencia GPLv3
└── README.md
```

## Instalador

El MSI es de arquitectura **x86**, pero funciona también en Windows de 64 bits
(la aplicación es AnyCPU). No está firmado digitalmente, por lo que SmartScreen
puede mostrar un aviso al ejecutarlo; es normal en compilaciones locales.

Si tienes una instancia de desarrollo de TinyWall ejecutándose desde otra
carpeta, desinstala antes su servicio para que el instalador registre el de
`Program Files`:

```powershell
& "C:\ruta\a\TinyWall.exe" /uninstall
```

## Subir a GitHub

1. Crea un repositorio vacío en GitHub (sin README ni licencia).
2. Desde esta carpeta:

```powershell
git init
git add .
git commit -m "Firewall v1: tema claro/oscuro y colores personalizables"
git branch -M main
git remote add origin https://github.com/TU_USUARIO/firewall-v1.git
git push -u origin main
```

El archivo `.gitignore` ya excluye `bin/`, `obj/`, `artifacts/`, `dist/` y
`MsiSetup/Sources/`, por lo que no se subirán binarios ni archivos temporales.

## Licencia y créditos

Este proyecto se basa en **TinyWall**, creado por Károly Pados
(<https://tinywall.pados.hu>), y se distribuye bajo la **GNU General Public
License v3**. Consulta [LICENSE.txt](LICENSE.txt).

Los cambios de esta versión (tema claro/oscuro, colores personalizables y
transparencia del fondo) se añadieron sobre el código original de TinyWall
3.6.1.
