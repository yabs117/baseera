# Baseera

Aplicación experimental para Windows: análisis local de archivos, traducción de
ventanas con Gemini, actualizaciones WinGet elegidas y conversión de imágenes/PDF.
Interfaz en árabe (RTL), inglés, francés y español.

Requisitos: Windows 10/11 x64. El runtime .NET está incluido. Para compilar con SDK 10:
`./build.ps1 -Test -Publish`. Abre `artifacts/Baseera-v0.4.5-Windows-x64/Baseera.exe`.
La extensión del navegador se descarga por separado: sigue `START-HERE.html` y carga la carpeta `extension` en Chrome/Edge. No abras sus archivos fuente con doble clic.

Activa solo las herramientas que necesitas. La traducción requiere tu clave Gemini
y un modelo compatible con imágenes. Envía la captura de la ventana elegida a Google
cuando lo solicitas. Esc cierra la capa de traducción. La extensión en `extension/`
reemplaza el texto de la página y permite restaurar el original.

Una ventana independiente de ayuda con errores se abre al iniciar Baseera normalmente.
Pega el mensaje o una imagen, elige un archivo de imagen o captura una ventana visible
y solicita un análisis con tu clave Gemini. Puedes cerrar la ventana sin configurar
una clave y volver a abrirla con Ctrl+Shift+E desde Baseera. El texto y la imagen
elegidos se envían a Google solo al pulsar Analizar. Revisa la captura antes de
enviarla: puede contener datos privados. Los pasos sugeridos no garantizan una
solución y no cambian automáticamente la configuración de Windows.

El análisis es estático y limitado: no certifica que un archivo sea seguro o malicioso.
Las fuentes manuales admiten actualmente GitHub Releases para revisión; no ejecutan
sus instaladores automáticamente. Consulta [capacidades y límites](ROADMAP.md).

Desde 0.4.0, Baseera consulta su repositorio oficial al iniciar y cada dos horas. El diálogo permite descargar, verificar, reemplazar esta copia y reiniciar conservando la configuración. Guarda una copia para restaurar la versión anterior si falla. Búsqueda manual y opción automática en Configuración → Actualizaciones de Baseera. Usa una carpeta con permiso de escritura. Las versiones anteriores necesitan descargar 0.4.0 manualmente.
