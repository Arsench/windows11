# Zenith — Análisis técnico y arquitectura

Documento de decisiones de la V1. Corto a propósito: recoge lo que hay que saber
para trabajar en el proyecto, no un tratado.

---

## 1. Decisión tecnológica

**WPF sobre .NET 8 (LTS) + WPF-UI para el theming Fluent, con un sistema de
diseño propio encima.**

| Opción | Por qué sí / por qué no |
|---|---|
| **WinUI 3 / Windows App SDK** | Es el stack "oficial moderno", pero su motor de binding es más pobre (`x:Bind` sin `MultiBinding` ni `RelativeSource` completos), el tooling sigue siendo frágil, arrastra la dependencia del runtime del Windows App SDK y el despliegue self-contained es pesado. La ganancia visual real frente a WPF con Fluent es pequeña. |
| **WPF .NET 8 + WPF-UI** ✅ | Mica, esquinas redondeadas, seguimiento del tema del sistema y controles Fluent; motor de binding maduro; virtualización sólida; `dotnet publish --self-contained` trivial; MSIX después sin tocar la arquitectura. El acceso al hardware es idéntico al de WinUI: mismas APIs de .NET, P/Invoke y WMI. |
| Avalonia | Multiplataforma que no se necesita; se nota menos nativo en Windows. |
| Electron / Tauri | Incompatible con el objetivo de consumo en reposo y con el acceso directo a sensores. |
| WinForms / MAUI | Calidad visual insuficiente / historia de escritorio floja. |

### Superficie de WPF-UI, acotada a propósito

Solo se usan cinco cosas: `ThemesDictionary`, `ControlsDictionary`,
`FluentWindow`, `ApplicationThemeManager` y el fondo Mica. Es API estable entre
la 3.x y la 4.x. **Todo lo demás es propio**: navegación, tarjetas, tipografía,
gráficos, diálogos y avisos. Así la aplicación no parece una demo de librería y
un cambio de versión no rompe la interfaz.

**Iconografía**: fuente **Segoe Fluent Icons**, que ya viene en Windows 11. Cero
dependencias de iconos. Los glifos de navegación están centralizados en
`ShellViewModel` para poder ajustar cualquiera en un solo sitio.

---

## 2. Arquitectura

```
┌──────────────────────────────────────────┐
│ Zenith.App (WPF)                         │  Vistas, ViewModels, controles,
│  Views · ViewModels · Controls · Services│  tema, diálogos
└───────────────┬──────────────────────────┘
                │  solo conoce interfaces
┌───────────────▼──────────────────────────┐
│ Zenith.Core  (net8.0, sin Windows)       │  Modelos, contratos, lógica de
│  Duplicates · Storage · Safety ·         │  duplicados, análisis de disco,
│  Monitoring · Settings · Primitives      │  seguridad de rutas. TESTEABLE.
└───────────────▲──────────────────────────┘
                │  implementa los contratos
┌───────────────┴──────────────────────────┐
│ Zenith.Platform.Windows                  │  WMI, P/Invoke, PDH, DXGI,
│  Cpu · Memory · Gpu · Thermal · Storage  │  papelera de reciclaje,
│  Processes · Files · Settings · Interop  │  LibreHardwareMonitor
└──────────────────────────────────────────┘
```

**Regla que sostiene todo:** `Zenith.Core` no referencia nada de Windows. La UI
habla con interfaces (`ICpuProvider`, `IStorageProvider`, `IFileSystemOperations`…).
Añadir un módulo nuevo es un servicio + un ViewModel + una página, sin tocar
nada existente.

### Piezas clave

- **`Metric<T>`** — un valor numérico que *puede no existir*, con el motivo
  (`NotSupported`, `RequiresElevation`, `Failed`, `Pending`) y un matiz opcional
  (`MetricDetail`). Sustituye a "devolver 0". Ninguna pantalla puede confundir un
  cero real con un dato que no tenemos, y el motivo se traduce como cualquier
  otro texto.
- **`MonitoringService`** — un único bucle de muestreo para toda la aplicación.
  Las páginas se suscriben mientras están visibles. Abrir cinco pantallas no
  multiplica por cinco el coste.
- **`PathSafetyGuard`** — última línea de defensa antes de mover o borrar. Se
  consulta siempre, incluso si el usuario eligió la ruta a mano.
- **`DuplicateActionPlanner`** — convierte "lo que el usuario ha marcado" en "lo
  que la aplicación está dispuesta a hacer".

---

## 3. Cómo se obtienen los datos (y por qué así)

| Dato | Fuente | Motivo |
|---|---|---|
| Uso de CPU (total y por núcleo) | `NtQuerySystemInformation` | **Los contadores de rendimiento están traducidos**: en un Windows en español `"Processor Information"` no existe. Los tiempos del kernel son independientes del idioma y prácticamente gratis. |
| Frecuencia actual de CPU | PDH `% Processor Performance` × frecuencia base, vía `PdhAddEnglishCounterW`; alternativa `CallNtPowerInformation` | `Win32_Processor.CurrentClockSpeed` suele devolver la nominal, no la real. |
| Memoria | `GlobalMemoryStatusEx` + `GetPerformanceInfo` | Directo y sin coste. |
| Módulos de RAM | WMI `Win32_PhysicalMemory` | Los nombres de clase WMI no están traducidos. |
| Uso de GPU | Contadores WDDM `GPU Engine` / `GPU Adapter Memory` por PDH inglés | Único camino **independiente del fabricante**: vale para NVIDIA, AMD, Intel e integradas sin NVAPI/ADL. |
| Identidad y VRAM de la GPU | **DXGI** (`IDXGIFactory1::EnumAdapters1`) | Da el LUID, que es lo que aparece en el nombre de las instancias de los contadores. Además `Win32_VideoController.AdapterRAM` es un `uint32` y **miente por encima de 4 GB**. |
| Tipo de unidad (HDD/SSD/NVMe) | WMI `MSFT_PhysicalDisk` + `MSFT_Partition` | Si el espacio de nombres Storage no es accesible, se muestra "Tipo no disponible" en lugar de adivinar. |
| Temperaturas | LibreHardwareMonitor (opt-in) → alternativa zona térmica ACPI | Ver más abajo. |
| Procesos | `System.Diagnostics.Process` con delta de tiempo de CPU | Suficiente para un top-N cada 3 s. |

### Temperaturas: la limitación que no se oculta

Leer temperaturas reales de CPU, GPU o NVMe **exige un controlador en modo
kernel**. LibreHardwareMonitor carga WinRing0, lo que implica:

1. Permisos de **administrador**.
2. Algunos antivirus lo señalan como actividad inusual.

Por eso está **desactivado por defecto** y hay que activarlo explícitamente, con
un diálogo que explica exactamente esto. Sin él:

- Se intenta `MSAcpi_ThermalZoneTemperature`, y se etiqueta como **"Zona térmica
  ACPI"**, porque *no es* la temperatura del die de la CPU.
- Si tampoco hay nada: **"Sensor no disponible"**. Nunca un número inventado.

---

## 4. Detección de duplicados

Cascada de cinco fases. Cada una recibe solo lo que sobrevivió a la anterior, de
modo que el trabajo caro (leer contenido) se hace sobre un conjunto mínimo.
**El nombre del archivo no interviene nunca en la decisión.**

1. **Enumerar** — se saltan enlaces simbólicos y puntos de análisis (evitan
   ciclos y falsos duplicados), ocultos y de sistema, y las rutas excluidas.
2. **Agrupar por tamaño** — descarta la inmensa mayoría en una pasada sin E/S.
3. **Colapsar vínculos duros** — dos rutas con hardlink apuntan al mismo
   contenido físico: borrar una **no libera nada**, así que no son duplicados.
   Se detecta con `GetFileInformationByHandle`.
4. **Huella parcial** — XxHash128 de los primeros y últimos 64 KB, con el tamaño
   como sal.
5. **Huella completa** — XxHash128 en streaming, solo para lo que sigue en pie.
6. **Verificación byte a byte** — activada por defecto. XxHash128 no es
   criptográfico; la probabilidad de colisión a 128 bits es despreciable, pero
   como la prioridad declarada es la **precisión**, la comparación real es la
   que decide. El hash solo sirve para estrechar candidatos.

Los archivos ilegibles (permisos, en uso) **se descartan**, nunca se agrupan a
ciegas, y se cuentan aparte para informar al usuario.

---

## 5. Seguridad al borrar

Reglas que la aplicación no salta bajo ninguna circunstancia:

1. **Nunca** se toca `Windows`, `System32`, `Program Files`, `ProgramData`,
   `WindowsApps`, `$Recycle.Bin`, `System Volume Information`, `Recovery`,
   `Boot`, `PerfLogs` ni la raíz de una unidad.
2. **Nunca** se eliminan todas las copias de un grupo: siempre queda al menos una.
3. **Nunca** se toca un archivo con el atributo *sistema* ni un punto de análisis.
4. La ruta se **revalida justo antes de actuar**, no solo al planificar.
5. Por defecto todo va a la **papelera de reciclaje** (`SHFileOperation` con
   `FOF_ALLOWUNDO`, en un hilo STA, que es lo que espera el shell).
6. El diálogo de confirmación muestra **la lista exacta** de archivos, su tamaño
   y su ruta antes de ejecutar nada.
7. Al mover, **nunca se sobrescribe**: si el nombre existe se genera `nombre (2).ext`.
8. Si algo falla, se informa **archivo por archivo** de qué pasó y por qué. No se
   deja una operación a medias en silencio.

---

## 5 bis. Idiomas

**Español e inglés, con cambio en caliente.** La decisión que sostiene todo esto
es de arquitectura, no de traducción:

> **`Zenith.Core` no contiene ni una cadena visible.** Devuelve *códigos*:
> `ScanErrorKind.AccessDenied`, `SafetyReason.SystemFolder`,
> `MetricDetail.IntegratedGpuNoDedicatedMemory`, `ThermalUnavailableReason.RequiresElevation`…
> La frontera con el idioma es un único archivo, `Zenith.App/Localization/Present.cs`.

Sin esa separación, un usuario en inglés vería media interfaz en español, porque
los mensajes de error de análisis y los motivos de seguridad nacían en el núcleo.

**Cómo funciona el cambio sin reiniciar.** `Loc` expone un indexador y notifica
`Binding.IndexerName` al cambiar de idioma; WPF reevalúa entonces todos los
enlaces. En XAML se usa así:

```xml
<TextBlock Text="{loc:T DuplicatesScan}" />
```

Los textos que compone un ViewModel (frases con números dentro) se rehacen
suscribiéndose a `Loc.LanguageChanged`.

**Reglas al añadir texto**

1. Nada de frases partidas en varios `<Run>`: el orden de las palabras cambia
   según el idioma. Se compone la frase entera con `Loc.Format`.
2. Singular y plural con **claves distintas** (`CountFileOne` / `CountFileMany`).
3. Las claves no llevan puntos: el analizador de rutas de enlace de WPF los
   interpretaría como navegación de propiedades.
4. Los números y las fechas usan `Loc.Culture`, que cambia con el idioma.

**Añadir un idioma** = copiar `Strings.resx` a `Strings.<código>.resx`, traducir
los valores y añadir el código en `Loc.IsSupported` y en `AppLanguage`. No se
toca ni una línea de lógica.

---

## 5 ter. Licencia

Zenith tiene un **sitio** para la licencia, no un sistema de licencias. La
diferencia importa:

`LicenseService` guarda la clave y comprueba su **forma** (4 grupos de 5
caracteres de un alfabeto sin `I`, `O`, `0` ni `1`, con carácter de control para
detectar erratas). El estado máximo al que llega una clave correcta es
**`PendingVerification`**, nunca "activada".

**Por qué no hay validación real.** Cualquier comprobación que ocurra solo en el
equipo del usuario se salta con un depurador en cinco minutos. Un candado local
no protege nada y sí da una falsa sensación de seguridad, así que la pantalla
dice exactamente lo que hace: guarda la clave hasta que exista un servidor que
pueda verificarla. Cuando ese servidor exista, lo único que cambia es
`LicenseService.ActivateAsync`.

**Lo que sí hay que mirar antes de vender** está en `THIRD-PARTY-NOTICES.md`. El
resumen: casi todo es MIT o Apache-2.0 y no da problemas, pero
**LibreHardwareMonitorLib es MPL-2.0** y, sobre todo, carga **WinRing0**, un
controlador en modo kernel con sus propias condiciones, que necesita firma
atestada de Microsoft y que varios antivirus marcan. Si algún día se cobra por
la aplicación, la salida limpia es sacar las temperaturas a un módulo opcional
aparte o firmar un controlador propio.

---

## 6. Rendimiento

- Un único bucle de muestreo; las páginas se suscriben solo mientras están visibles.
- Cadencia de 1 s en primer plano, **4 s cuando la ventana pierde el foco o se
  minimiza**.
- Series de gráficos en buffer circular de tamaño fijo: sin asignaciones por muestra.
- Gráficos dibujados a mano (`OnRender`): una geometría por fotograma, sin
  librería de charting.
- El sensor térmico se cachea con un mínimo de 2 s: es la lectura más cara.
- El analizador de disco guarda **agregados por carpeta**, no un objeto por
  archivo: analizar 1 TB no puede costar gigabytes de RAM.
- Escaneos y hashing siempre en segundo plano, con `IProgress<T>` y
  `CancellationToken` de principio a fin.

---

## 7. Riesgos técnicos conocidos

| Riesgo | Mitigación |
|---|---|
| WinRing0 (sensores) señalado por antivirus | Opt-in explícito, desactivado por defecto, con explicación previa. |
| DXGI no disponible (sesión sin escritorio) | Camino alternativo por WMI; la utilización se marca como no disponible en lugar de inventarse. |
| Contadores GPU ausentes en drivers antiguos | `Metric.NotSupported` con motivo visible. |
| Equipos con más de 64 procesadores lógicos | `NtQuerySystemInformation` devuelve solo el grupo actual. Documentado; afecta a estaciones de trabajo grandes. |
| WMI degradado o sin permisos | Todas las consultas van en `try/catch`; la UI muestra "No disponible". |
| Rutas de más de 260 caracteres | `longPathAware` en el manifiesto; los fallos se registran por archivo. |
| Volumen de resultados de duplicados | Grupos ordenados por espacio recuperable; carpetas del árbol limitadas a 200 hijos por nivel en la vista. |

---

## 8. Plan por fases

- **Fase 1 — Base** ✅ Solución, capas, DI, logging, tema claro/oscuro, ventana,
  navegación, diálogos y avisos.
- **Fase 2 — Monitorización** ✅ CPU, memoria, GPU, sensores, procesos, panel y
  página de sistema.
- **Fase 3 — Almacenamiento** ✅ Unidades, tipo físico, analizador con progreso y
  cancelación, categorías y archivos grandes.
- **Fase 4 — Duplicados** ✅ Escáner en cascada, planificador, guardián de
  seguridad, papelera, mover, informes de error.
- **Fase 5 — Idioma y licencia** ✅ Español e inglés con cambio en caliente,
  núcleo libre de cadenas, sección de licencia y avisos de terceros.
- **Fase 6 — Pulido** ⏳ Pruebas en hardware real, ajuste fino de animaciones y
  escalado, empaquetado.

### Después de la V1 (no antes)

Archivos grandes como sección propia, limpieza de temporales, papelera, cachés,
programas de inicio, servicios, red, SMART, informes e histórico. La arquitectura
ya los admite: son servicios nuevos en `Core` + su implementación en `Platform` +
una página. **Ninguno se ha implementado a medias.**

---

## 9. Distribución

- **Ahora**: `dotnet publish -c Release -r win-x64 --self-contained` produce una
  carpeta que funciona sin instalar .NET.
- **Después**: MSIX (el manifiesto ya declara `asInvoker`, DPI per-monitor v2 y
  rutas largas) o un instalador clásico. Nada en la arquitectura lo impide.
