# Zenith

Centro de control y mantenimiento para Windows 11. Monitoriza el equipo en
tiempo real, analiza en qué se va el espacio en disco y encuentra archivos
duplicados de forma segura.

> Nombre e icono provisionales.

## Estado

**V1** con cuatro secciones, cada una terminada:

| Sección | Qué hace |
|---|---|
| **Panel** | CPU, memoria, gráfica, temperatura y ocupación de las unidades, en vivo. |
| **Sistema** | Detalle de procesador (con uso por núcleo), memoria y módulos, gráfica, sensores térmicos y procesos con más consumo. |
| **Almacenamiento** | Unidades con su tipo real (HDD / SSD / NVMe) y analizador de ocupación navegable, con progreso y cancelación. |
| **Duplicados** | Búsqueda por contenido en cascada, con verificación byte a byte y borrado protegido. |

No hay funciones a medias. Lo que aún no está, no aparece en la navegación.

## Requisitos

- Windows 10 20H1 (build 19041) o superior — pensado para Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar.

## Compilar y ejecutar

```powershell
dotnet restore
dotnet build -c Release
dotnet run --project src\Zenith.App
```

Pruebas de la lógica de dominio (duplicados, análisis de disco, seguridad):

```powershell
dotnet test
```

Publicar una carpeta autónoma, sin necesidad de instalar .NET:

```powershell
dotnet publish src\Zenith.App -c Release -r win-x64 --self-contained
```

## Estructura

```
src/
  Zenith.Core/              Lógica pura, sin dependencias de Windows. Es lo que se testea.
  Zenith.Platform.Windows/  WMI, P/Invoke, PDH, DXGI, papelera, sensores.
  Zenith.App/               Interfaz WPF: vistas, ViewModels, controles y tema.
tests/
  Zenith.Core.Tests/        Pruebas de duplicados, analizador, seguridad y primitivas.
tools/
  make_icon.py              Regenera el icono provisional (sin dependencias).
docs/
  ARQUITECTURA.md           Decisiones técnicas, riesgos y plan por fases.
```

## Tres cosas que conviene saber

**Las temperaturas requieren administrador.** Leer sensores reales obliga a
cargar un controlador en modo kernel. Por eso está desactivado por defecto y hay
que activarlo en *Sistema → Temperaturas*. Sin él verás *"Sensor no disponible"*
o, como mucho, la zona térmica ACPI **claramente etiquetada como tal**, porque no
es la temperatura del procesador. Zenith nunca muestra un valor inventado: si un
dato no existe, lo dice.

**Los duplicados se comparan por contenido, nunca por nombre.** Dos archivos que
se llaman igual pero tienen contenido distinto no se marcan. Y dos rutas unidas
por un vínculo duro tampoco: apuntan al mismo contenido físico, así que borrar
una no liberaría nada.

**Borrar es la operación más peligrosa de la aplicación**, y se trata como tal.
Por defecto todo va a la papelera de reciclaje; siempre se conserva al menos una
copia de cada grupo; las carpetas del sistema están bloqueadas de forma
incondicional; y antes de ejecutar nada se muestra la lista exacta de archivos
con su tamaño y su ruta.

## Configuración y registro

- Configuración: `%APPDATA%\Zenith\settings.json`
- Registro: `%APPDATA%\Zenith\logs\` (rotación diaria, 7 días)

Los mensajes de error que ve el usuario son frases en lenguaje llano; el detalle
técnico va al registro.

## Licencia

Proyecto personal. Sin licencia definida todavía.
