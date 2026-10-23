# Baseera

Aplicación experimental para Windows: análisis local de archivos, traducción de
ventanas con Gemini, actualizaciones WinGet elegidas y conversión de imágenes/PDF.
Interfaz en árabe (RTL), inglés, francés y español.

Requisitos: Windows 10/11 x64. El runtime .NET está incluido. Para compilar con SDK 10:
`./build.ps1 -Test -Publish`. Abre `artifacts/Baseera-v0.4.10-Windows-x64/Baseera.exe`.
La extensión del navegador se descarga por separado: sigue `START-HERE.html` y carga la carpeta `extension` en Chrome/Edge. No abras sus archivos fuente con doble clic.

Activa solo las herramientas que necesitas. Elige entre 20 servicios directos, como Gemini, OpenAI (modelos ChatGPT), Claude y Grok. Introduce la clave del servicio y carga sus modelos con visión. Se eliminó OpenRouter y se corrigió el cierre al cambiar de servicio. El acceso y los precios dependen de la cuenta. [Servicios y pruebas](AI-SERVICES.md). Envía la captura al servicio seleccionado
cuando lo solicitas. Esc cierra la capa de traducción. La extensión en `extension/`
reemplaza el texto de la página y permite restaurar el original.

El asistente de errores aparece en «Tus herramientas» de la página principal y la barra lateral. Su página abre la ventana de análisis; si está activado, también se abre al iniciar Baseera normalmente. Puedes desactivarlo en Configuración.
Pega el mensaje o una imagen, copia un archivo de imagen, elige un archivo o captura una ventana abierta; la vista previa aparece enseguida. Solicita un análisis con la clave elegida. Puedes cerrar la ventana sin configurar
una clave y volver a abrirla con Ctrl+Shift+E desde Baseera. El texto y la imagen
elegidos se envían al servicio elegido solo al pulsar Analizar directamente. Revisa la captura antes de
enviarla: puede contener datos privados. Los pasos sugeridos no garantizan una
solución y no cambian automáticamente la configuración de Windows.

«Opiniones y comentarios», justo encima de Configuración, abre una incidencia de GitHub prellenada para que la revises y la envíes en el navegador. No se adjuntan imágenes ni claves API automáticamente.

El análisis es estático y limitado: no certifica que un archivo sea seguro o malicioso.
Las fuentes manuales admiten actualmente GitHub Releases para revisión; no ejecutan
sus instaladores automáticamente. Consulta [capacidades y límites](ROADMAP.md).

Desde 0.4.0, Baseera consulta su repositorio oficial al iniciar y cada dos horas. El diálogo permite descargar, verificar, reemplazar esta copia y reiniciar conservando la configuración. Guarda una copia para restaurar la versión anterior si falla. Búsqueda manual y opción automática en Configuración → Actualizaciones de Baseera. Usa una carpeta con permiso de escritura. Las versiones anteriores necesitan descargar 0.4.0 manualmente.

**Conexión sin clave:** elige OpenAI y « Continuar con ChatGPT ». Completa el acceso y la autorización en el navegador, carga los modelos de la cuenta y guarda. Se necesita un plan apto y permiso de uso. Los otros servicios siguen usando claves API. [Detalles y límites](CHATGPT-SIGN-IN.md).

**Presentación de respuestas:** logotipo del servicio y modelo utilizado sobre la respuesta, texto progresivo y botón « Mostrar todo ». Se respeta la reducción de movimiento de Windows. [Detalles](ANSWER-PRESENTATION.md).
