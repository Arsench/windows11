// Espacios de nombres globales, declarados a mano.
//
// Por qué no se usan los "implicit usings" del SDK aquí: para compilar el XAML,
// WPF genera un proyecto temporal (Zenith.App_xxxx_wpftmp.csproj) que no arrastra
// de forma fiable el archivo GlobalUsings.g.cs que genera el SDK. El resultado son
// errores CS0103 que solo aparecen en la compilación de marcado y no en el resto
// de proyectos. Este archivo sí es un elemento Compile normal, así que el proyecto
// temporal lo incluye siempre.
//
// La lista es exactamente la que activa <ImplicitUsings>enable</ImplicitUsings>.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Threading;
global using System.Threading.Tasks;
