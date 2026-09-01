using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;

namespace Zenith.Platform.Windows.Thermal;

/// <summary>
/// Temperaturas del equipo.
///
/// Advertencia deliberada: leer sensores reales (SuperIO, MSR de la CPU, SMART
/// del SSD) obliga a cargar un controlador en modo kernel. Por eso está
/// desactivado por defecto, hace falta ejecutar como administrador y algunos
/// antivirus lo señalan. Si no se puede, esta clase NO inventa un valor: informa
/// del motivo y la interfaz muestra "Sensor no disponible".
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareThermalProvider(ILogger<HardwareThermalProvider> logger) : IThermalProvider
{
    private static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Computer? _computer;
    private ThermalSnapshot _cached = ThermalSnapshot.Unavailable("Sensores de hardware desactivados");
    private DateTimeOffset _lastSample = DateTimeOffset.MinValue;
    private bool _acpiFallback;

    public bool IsEnabled { get; private set; }

    public async Task<string?> TryEnableAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsEnabled) return null;

            if (!IsRunningAsAdministrator())
            {
                // Sin privilegios el driver no carga; probamos la vía ACPI, que es
                // limitada pero no requiere nada especial.
                if (TryEnableAcpiFallback())
                {
                    IsEnabled = true;
                    _acpiFallback = true;
                    return null;
                }

                return "Para leer los sensores hay que ejecutar Zenith como administrador.";
            }

            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true
            };

            await Task.Run(computer.Open, ct).ConfigureAwait(false);

            _computer = computer;
            IsEnabled = true;
            _lastSample = DateTimeOffset.MinValue;
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se han podido inicializar los sensores de hardware");
            _computer = null;

            if (TryEnableAcpiFallback())
            {
                IsEnabled = true;
                _acpiFallback = true;
                return null;
            }

            return "Este equipo no expone sensores de temperatura compatibles.";
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Disable()
    {
        _gate.Wait();
        try
        {
            CloseComputer();
            IsEnabled = false;
            _acpiFallback = false;
            _cached = ThermalSnapshot.Unavailable("Sensores de hardware desactivados");
        }
        finally
        {
            _gate.Release();
        }
    }

    public ThermalSnapshot Sample()
    {
        if (!IsEnabled) return ThermalSnapshot.Unavailable("Sensores de hardware desactivados");

        // Muestrear sensores es caro comparado con leer contadores: se cachea.
        if (DateTimeOffset.UtcNow - _lastSample < MinimumSampleInterval) return _cached;

        if (!_gate.Wait(0)) return _cached;
        try
        {
            _lastSample = DateTimeOffset.UtcNow;
            _cached = _acpiFallback ? ReadAcpi() : ReadHardware();
            return _cached;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Fallo al leer sensores");
            _cached = ThermalSnapshot.Unavailable("No se han podido leer los sensores.");
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ThermalSnapshot ReadHardware()
    {
        if (_computer is null) return ThermalSnapshot.Unavailable("Sensores no inicializados");

        var readings = new List<ThermalReading>();

        foreach (var hardware in _computer.Hardware)
        {
            Collect(hardware, readings);
            foreach (var sub in hardware.SubHardware) Collect(sub, readings);
        }

        return readings.Count > 0
            ? new ThermalSnapshot(readings, null)
            : ThermalSnapshot.Unavailable("El hardware de este equipo no expone sensores de temperatura.");
    }

    private static void Collect(IHardware hardware, List<ThermalReading> readings)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception)
        {
            return; // Un sensor caído no debe tumbar la lectura completa.
        }

        var component = hardware.HardwareType switch
        {
            HardwareType.Cpu => ThermalComponent.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => ThermalComponent.Gpu,
            HardwareType.Storage => ThermalComponent.Storage,
            HardwareType.Motherboard or HardwareType.SuperIO => ThermalComponent.Motherboard,
            _ => ThermalComponent.Other
        };

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Temperature) continue;
            if (sensor.Value is not { } value || !float.IsFinite(value)) continue;
            // Lecturas absurdas: sensor no conectado.
            if (value <= 0 || value > 150) continue;

            readings.Add(new ThermalReading(
                component,
                $"{hardware.Name} · {sensor.Name}",
                Math.Round(value, 1),
                ThermalSource.Hardware));
        }
    }

    private bool TryEnableAcpiFallback()
    {
        try
        {
            return ReadAcpi().Readings.Count > 0;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "La zona térmica ACPI no está disponible");
            return false;
        }
    }

    /// <summary>
    /// Zona térmica ACPI. Importante: NO es la temperatura del die de la CPU y
    /// se etiqueta como tal para no engañar al usuario.
    /// </summary>
    private static ThermalSnapshot ReadAcpi()
    {
        var readings = new List<ThermalReading>();

        using var searcher = new ManagementObjectSearcher(
            @"root\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

        var index = 0;
        foreach (var item in searcher.Get().Cast<ManagementObject>())
        {
            using (item)
            {
                if (item["CurrentTemperature"] is not uint raw) continue;

                // El valor viene en décimas de kelvin.
                var celsius = raw / 10.0 - 273.15;
                if (celsius is <= 0 or > 150) continue;

                readings.Add(new ThermalReading(
                    ThermalComponent.Other,
                    $"Zona térmica {++index}",
                    Math.Round(celsius, 1),
                    ThermalSource.AcpiThermalZone));
            }
        }

        return readings.Count > 0
            ? new ThermalSnapshot(readings, null)
            : ThermalSnapshot.Unavailable("Este equipo no publica zonas térmicas ACPI.");
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void CloseComputer()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error al cerrar la capa de sensores");
        }
        finally
        {
            _computer = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        Disable();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
