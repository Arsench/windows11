using Zenith.Core.Abstractions;
using Zenith.Core.Duplicates;
using Zenith.Core.Licensing;
using Zenith.Core.Models;
using Zenith.Core.Primitives;
using Zenith.Core.Safety;
using Zenith.Core.Storage;

namespace Zenith.App.Localization;

/// <summary>
/// Frontera entre el dominio y el idioma. Zenith.Core devuelve códigos; aquí —y
/// solo aquí— se convierten en frases. Añadir un idioma nuevo no toca ni una
/// línea de lógica.
/// </summary>
public static class Present
{
    private static Loc L => Loc.Instance;

    // ------------------------------------------------------------- métricas

    public static string MetricUnavailable(MetricStatus status, MetricDetail detail)
    {
        // El matiz concreto manda sobre el estado genérico cuando existe.
        var fromDetail = detail switch
        {
            MetricDetail.NoBaseClock => "MetricNoBaseClock",
            MetricDetail.IntegratedGpuNoDedicatedMemory => "MetricIntegratedGpu",
            MetricDetail.AdapterNotInstrumented => "MetricAdapterNotInstrumented",
            MetricDetail.AdapterMemoryUnknown => "MetricAdapterMemoryUnknown",
            MetricDetail.RequiresHardwareSensors => "MetricRequiresSensors",
            _ => null
        };

        if (fromDetail is not null) return L[fromDetail];

        return status switch
        {
            MetricStatus.Pending => L["CommonMeasuring"],
            MetricStatus.RequiresElevation => L["CommonRequiresAdministrator"],
            MetricStatus.Failed => L["CommonReadError"],
            _ => L["CommonNotAvailable"]
        };
    }

    // ------------------------------------------------------------- seguridad

    public static string Safety(SafetyVerdict verdict) => verdict.Reason switch
    {
        SafetyReason.None => string.Empty,
        SafetyReason.EmptyPath => L["SafetyEmptyPath"],
        SafetyReason.InvalidPath => L["SafetyInvalidPath"],
        SafetyReason.DriveRoot => L["SafetyDriveRoot"],
        SafetyReason.SystemFolder => L.Format("SafetySystemFolder", verdict.Detail),
        SafetyReason.UserExclusion => L.Format("SafetyUserExclusion", verdict.Detail),
        SafetyReason.ApplicationDataFolder => L.Format("SafetyApplicationDataFolder", verdict.Detail),
        SafetyReason.SuspiciousSegment => L.Format("SafetySuspiciousSegment", verdict.Detail),
        SafetyReason.SystemFile => L["SafetySystemFile"],
        SafetyReason.ReparsePoint => L["SafetyReparsePoint"],
        _ => L["SafetyAttributesUnreadable"]
    };

    public static string ScanError(ScanErrorKind kind) => kind switch
    {
        ScanErrorKind.AccessDenied => L["ScanErrorAccessDenied"],
        ScanErrorKind.DirectoryMissing => L["ScanErrorDirectoryMissing"],
        ScanErrorKind.FileMissing => L["ScanErrorFileMissing"],
        ScanErrorKind.PathTooLong => L["ScanErrorPathTooLong"],
        ScanErrorKind.InUse => L["ScanErrorInUse"],
        ScanErrorKind.InvalidPath => L["ScanErrorInvalidPath"],
        _ => L["ScanErrorUnknown"]
    };

    public static string FileActionFailure(FileActionFailure failure) => failure.Error switch
    {
        // Si el bloqueo viene del guardián, el motivo concreto es más útil.
        FileActionError.BlockedBySafety => failure.Verdict is { } v ? Safety(v) : L["FileErrorBlockedBySafety"],
        FileActionError.AccessDenied => L["FileErrorAccessDenied"],
        FileActionError.FileMissing => L["FileErrorFileMissing"],
        FileActionError.DestinationMissing => L["FileErrorDestinationMissing"],
        FileActionError.PathTooLong => L["FileErrorPathTooLong"],
        FileActionError.InUse => L["FileErrorInUse"],
        FileActionError.Cancelled => L["FileErrorCancelled"],
        FileActionError.CheckFailed => L["FileErrorCheckFailed"],
        FileActionError.NameCollision => L["FileErrorNameCollision"],
        _ => L["FileErrorUnknown"]
    };

    public static string Blocker(PlanBlocker blocker) => blocker.Kind switch
    {
        PlanBlockerKind.WholeGroupSelected =>
            L.Format("BlockerWholeGroupSelected", (blocker.GroupIndex ?? 0).ToString("00", L.Culture)),
        PlanBlockerKind.MissingDestination => L["BlockerMissingDestination"],
        PlanBlockerKind.UnsafeDestination =>
            L.Format("BlockerUnsafeDestination", blocker.Verdict is { } v ? Safety(v) : string.Empty),
        _ => L["BlockerNothingSafeToDo"]
    };

    // ------------------------------------------------------------- duplicados

    public static string Phase(DuplicatePhase phase) => phase switch
    {
        DuplicatePhase.Enumerating => L["PhaseEnumerating"],
        DuplicatePhase.GroupingBySize => L["PhaseGrouping"],
        DuplicatePhase.PartialHashing => L["PhasePartialHashing"],
        DuplicatePhase.FullHashing => L["PhaseFullHashing"],
        DuplicatePhase.Verifying => L["PhaseVerifying"],
        DuplicatePhase.Completed => L["PhaseCompleted"],
        DuplicatePhase.Cancelled => L["PhaseCancelled"],
        _ => L["PhaseIdle"]
    };

    // ------------------------------------------------------------- almacenamiento

    public static string Category(FileCategory category) => category switch
    {
        FileCategory.Video => L["CategoryVideo"],
        FileCategory.Images => L["CategoryImages"],
        FileCategory.Audio => L["CategoryAudio"],
        FileCategory.Documents => L["CategoryDocuments"],
        FileCategory.Archives => L["CategoryArchives"],
        FileCategory.Applications => L["CategoryApplications"],
        FileCategory.DiskImages => L["CategoryDiskImages"],
        FileCategory.Code => L["CategoryCode"],
        _ => L["CategoryOther"]
    };

    public static string Media(DriveMedia media) => media switch
    {
        DriveMedia.Nvme => L["MediaNvme"],
        DriveMedia.SolidState => L["MediaSsd"],
        DriveMedia.HardDisk => L["MediaHardDisk"],
        DriveMedia.Removable => L["MediaRemovable"],
        DriveMedia.Network => L["MediaNetwork"],
        DriveMedia.Optical => L["MediaOptical"],
        _ => L["MediaUnknown"]
    };

    // ------------------------------------------------------------- sensores

    public static string Thermal(ThermalUnavailableReason reason) => reason switch
    {
        ThermalUnavailableReason.SensorsDisabled => L["ThermalSensorsDisabled"],
        ThermalUnavailableReason.Measuring => L["ThermalMeasuring"],
        ThermalUnavailableReason.RequiresElevation => L["ThermalRequiresElevation"],
        ThermalUnavailableReason.NoCompatibleSensors => L["ThermalNoCompatibleSensors"],
        ThermalUnavailableReason.NoAcpiZones => L["ThermalNoAcpiZones"],
        ThermalUnavailableReason.ReadFailed => L["ThermalReadFailed"],
        ThermalUnavailableReason.NotInitialised => L["ThermalNotInitialised"],
        _ => L["CommonSensorUnavailable"]
    };

    /// <summary>El nombre lo da el hardware; las zonas ACPI se numeran y sí se traducen.</summary>
    public static string SensorName(ThermalReading reading) =>
        reading.SensorName ?? L.Format("ThermalZoneName", reading.Index);

    public static string ThermalSourceLabel(ThermalSource source) =>
        source == ThermalSource.AcpiThermalZone ? L["SourceAcpiZone"] : L["SourceHardwareSensor"];

    // ------------------------------------------------------------- licencia

    public static string LicenseStatus(LicenseState state) => state switch
    {
        LicenseState.PendingVerification => L["LicensePending"],
        LicenseState.Malformed => L["LicenseMalformed"],
        _ => L["LicensePersonal"]
    };

    public static string LicenseStatusHint(LicenseState state) => state switch
    {
        LicenseState.PendingVerification => L["LicensePendingHint"],
        LicenseState.Malformed => L["LicenseMalformedHint"],
        _ => L["LicensePersonalHint"]
    };

    public static string LicenseValidation(LicenseKeyValidation validation) => validation switch
    {
        LicenseKeyValidation.Empty => L["LicenseErrorEmpty"],
        LicenseKeyValidation.BadFormat => L["LicenseErrorFormat"],
        LicenseKeyValidation.BadChecksum => L["LicenseErrorChecksum"],
        _ => L["LicenseSaved"]
    };
}
