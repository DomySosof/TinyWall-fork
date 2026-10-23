# Cómo subir esta carpeta a GitHub

Esta carpeta ya está preparada como repositorio listo para subir (**TinyWall-fork**).
No se ha instalado nada ni se ha creado ningún commit todavía.

---

## Paso 0 — Instalar Git (una sola vez)

Abre PowerShell y ejecuta:

```powershell
winget install --id Git.Git -e
```

O descárgalo de <https://git-scm.com/download/win>.
Después **cierra y vuelve a abrir la terminal**, y comprueba:

```powershell
git --version
```

---

## Opción 0 — GitHub Desktop (la más fácil, sin límite de archivos)

La subida por web **no sirve para este proyecto**: GitHub acepta como máximo
**100 archivos por arrastre**, y aquí `TinyWall\` solo ya tiene 241 archivos
(174 `.resx` de idiomas + 64 `.cs`). No se pueden repartir en lotes porque el
uploader web no deja escribir dentro de una subcarpeta existente.

**GitHub Desktop** es una app de escritorio que ya trae Git incluido (no hay que
instalar nada aparte) y no tiene límite de archivos:

1. Descarga e instala <https://desktop.github.com> (gratis, Windows).
2. Inicia sesión con tu cuenta de GitHub.
3. Menú **File → New repository…**
   - **Name:** `TinyWall-fork`
   - **Local path:** `C:\Users\gesti\Downloads\TinyWall-master\TinyWall-master` (la carpeta *padre*)
   - Deja marcado *Initialize this repository with a README* **desmarcado**.
   - Pulsa **Create repository**.
   - Mueve el contenido de `firewall v1` a la carpeta nueva si te lo pide.
4. Ve a la pestaña **Changes**: aparecerán los archivos (los de `bin`/`obj`
   quedan fuera por el `.gitignore`). Escribe un resumen, por ejemplo
   *"Import inicial del codigo fuente de TinyWall (fork)"* y pulsa
   **Commit to main**.
5. Arriba pulsa **Publish repository**, deja **Keep this code private**
   **desmarcado** (para que sea público) y confirma.

Listo: repositorio creado y subido, con la estructura de carpetas intacta.

> Nota: GitHub Desktop instala su propio Git. Si más adelante quieres usar la
> terminal, ejecuta `winget install --id Git.Git -e`.

---

## Paso 1 — Crear el repositorio vacío en GitHub

1. Entra en <https://github.com/new>
2. **Repository name:** `TinyWall-fork`
3. Visibilidad: **Public**
4. **No** marques "Add a README", "Add .gitignore" ni "Choose a license" (ya existen aquí y darían conflictos).
5. Pulsa **Create repository** y copia la URL, del estilo:
   `https://github.com/TU-USUARIO/TinyWall-fork.git`

---

## Paso 2 — Subir el código

### Opción A (automática, recomendada)

En esta carpeta, con el botón derecho → *Ejecutar con PowerShell*, o desde la terminal:

```powershell
powershell -ExecutionPolicy Bypass -File .\SUBIR-A-GITHUB.ps1 -RepoUrl https://github.com/TU-USUARIO/TinyWall-fork.git
```

El script comprueba Git, pide tu nombre/email si faltan, crea el repo local,
hace el primer commit y hace `push` a la rama `main`.

### Opción B (manual, 5 comandos)

```powershell
cd "C:\Users\gesti\Downloads\TinyWall-master\TinyWall-master\firewall v1"
git init -b main
git add -A
git commit -m "Import inicial del codigo fuente de TinyWall (fork)"
git remote add origin https://github.com/TU-USUARIO/TinyWall-fork.git
git push -u origin main
```

Si el `push` pide usuario y contraseña: la contraseña normal **no** sirve.
Usa un **Personal Access Token** (GitHub → Settings → Developer settings →
Personal access tokens → Tokens (classic) → scope `repo`), o instala GitHub CLI
y ejecuta `gh auth login`.

### Opción C (sin Git, por la web) — **no recomendada aquí**

GitHub permite arrastrar carpetas en <https://github.com/TU-USUARIO/TinyWall-fork/upload/main>,
pero con un tope de **100 archivos por arrastre**. Este proyecto tiene 509
archivos y `TinyWall\` solo ya son 241, así que verás el aviso
*"Yowza, that's a lot of files. Try uploading fewer than 100 at a time."*

No se puede dividir en lotes por carpetas, porque el uploader web no permite
escribir dentro de una subcarpeta ya existente en trozos. Por eso, usa
**GitHub Desktop** (Opción 0) o Git (Opción A/B).

---

## Qué NO se sube (ya está excluido en `.gitignore`)

- `MsiSetup\bin\` → contiene `TinyWall_x86.msi` (1.8 MB) y `.wixpdb`
- `MsiSetup\obj\` → objetos intermedios de compilación
- `*/bin/`, `*/obj/`, `.vs/`, `*.exe`, `*.dll`, `*.msi`, `*.suo`, `*.user`
- `build-config.ini`, `TinyWall\Database\profiles.json`

Es correcto: en GitHub va **solo el código fuente**, no los binarios generados.

---

## Aviso legal importante (licencia GPLv3)

TinyWall es un proyecto de **Károly Pados**, distribuido bajo **GNU GPL v3**
(ver `LICENSE.txt`). Al publicar un fork debes:

- Mantener el archivo `LICENSE.txt` sin cambios.
- Atribuir el autor original en el README o en la descripción del repo.
- Indicar que es un fork/modificación (por ejemplo, en la descripción del repo:
  *"Fork de TinyWall — firewall ligero para Windows"*).

Recomendación: añade al principio del `README.md` una línea del tipo:

```markdown
> **Fork** de [TinyWall](https://github.com/pylorak/TinyWall) (Károly Pados, GPLv3).
> Los cambios respecto al original están descritos en el historial de commits.
```

---

## Resumen de lo que se sube

| Elemento | Contenido |
|---|---|
| `TinyWall.sln` + proyectos | Código C# de la aplicación y el servicio |
| `pylorak.*` | Librerías auxiliares (WFP, servicios, utilidades) |
| `MsiSetup\` | Proyecto WiX del instalador (sin `bin`/`obj`) |
| `LocalizationPack\` | Recursos de traducción |
| `Docs\` | Documentación e imágenes |
| `.github\workflows\build.yml` | Compilación automática en GitHub Actions |
| `build.ps1`, `install.ps1` | Scripts de compilación e instalación |
| `LICENSE.txt`, `README.md`, `Changelog.txt` | Licencia y documentación |

Total aproximado: **~13 MB de fuente**, de los cuales `source.svg` (2.9 MB) es el
archivo gráfico más pesado.
