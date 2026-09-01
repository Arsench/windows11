# Avisos de terceros · Third-party notices

Zenith incorpora los componentes de código abierto que se listan aquí. Cada uno
conserva su propia licencia, que prevalece sobre la de Zenith en lo que a él
respecta.

Este archivo se copia junto al ejecutable y **debe distribuirse con la
aplicación**. Es accesible desde *Configuración → Licencia → Licencias de
terceros*.

---

## Resumen

| Componente | Versión | Licencia | Uso comercial |
|---|---|---|---|
| [WPF-UI](https://github.com/lepoco/wpfui) | 3.0.5 | MIT | Sí, conservando el aviso |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.3.2 | MIT | Sí, conservando el aviso |
| [Serilog](https://serilog.net) (+ sinks File y Debug) | 4.x / 6.0.0 / 3.0.0 | Apache-2.0 | Sí, conservando el aviso |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) | 8.0.x | MIT | Sí |
| [System.IO.Hashing](https://github.com/dotnet/runtime) | 8.0.0 | MIT | Sí |
| [System.Management](https://github.com/dotnet/runtime) | 8.0.0 | MIT | Sí |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | 0.9.4 | **MPL-2.0** | Sí, **con condiciones** — leer abajo |

---

## Atención antes de vender: LibreHardwareMonitorLib

Es el único componente con condiciones que hay que mirar con calma.

**1. La MPL-2.0 obliga a publicar el código de *ese* componente.** La MPL es de
copyleft por archivo, no viral: puedes distribuir Zenith como software
propietario, pero tienes que conservar los avisos y ofrecer el código fuente de
los archivos cubiertos por la MPL (y de cualquier modificación tuya sobre
ellos). Como Zenith usa la librería sin modificarla, basta con incluir este
aviso y el enlace al repositorio original.

**2. El controlador en modo kernel es el problema real.** La librería carga
WinRing0 para leer sensores. Para distribuir eso comercialmente hay tres cosas
que resolver:

- WinRing0 tiene sus **propias condiciones**, distintas de la MPL, y su
  redistribución comercial no es automática. Revísalas antes de cobrar por una
  versión que lo incluya.
- Los controladores en modo kernel deben ir **firmados por Microsoft** (proceso
  de atestación) para cargar en Windows 10/11 con arranque seguro.
- Varios antivirus y soluciones anti-trampas **bloquean o marcan** WinRing0.

**Mitigación que ya lleva Zenith:** los sensores están desactivados por defecto
y son una elección explícita del usuario. Si algún día vendes la aplicación,
la salida más limpia es distribuir las temperaturas como módulo opcional
separado, o sustituir la librería por un controlador propio firmado.

---

## Textos de licencia

### MIT (WPF-UI, CommunityToolkit.Mvvm, componentes de .NET)

```
Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

### Apache-2.0 (Serilog)

Texto completo: <https://www.apache.org/licenses/LICENSE-2.0>.
Licenciado bajo la Apache License, versión 2.0. Se distribuye "TAL CUAL", sin
garantías ni condiciones de ningún tipo.

### MPL-2.0 (LibreHardwareMonitorLib)

Texto completo: <https://mozilla.org/MPL/2.0/>.
Código fuente: <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>.
Este código fuente está sujeto a los términos de la Mozilla Public License 2.0.
Si no se distribuyó una copia de la MPL con este archivo, puedes obtener una en
la dirección anterior.

---

## Tipografías

Zenith usa **Segoe UI Variable** y **Segoe Fluent Icons**, que forman parte de
Windows. La aplicación las **referencia**, no las incluye ni las redistribuye.
Fuera de Windows recaen automáticamente en la tipografía del sistema. No
empaquetes estos archivos de fuente con la aplicación: su licencia no lo permite.
