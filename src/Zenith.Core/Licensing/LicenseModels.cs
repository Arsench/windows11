namespace Zenith.Core.Licensing;

/// <summary>
/// Estado de licencia de esta copia.
///
/// Advertencia deliberada: aquí NO hay validación real. Comprobar una licencia
/// de verdad exige un servidor que firme y verifique la clave; cualquier
/// comprobación que ocurra solo en el equipo del usuario se salta en cinco
/// minutos con un depurador. Preferimos un estado honesto —"pendiente de
/// verificación"— antes que un candado de mentira que dé falsa seguridad.
/// </summary>
public enum LicenseState
{
    /// <summary>Sin clave: uso personal. Es el estado normal hoy.</summary>
    Personal,

    /// <summary>Hay una clave con formato correcto, pero nadie la ha verificado todavía.</summary>
    PendingVerification,

    /// <summary>La clave no tiene un formato válido.</summary>
    Malformed
}

public sealed record LicenseStatus(LicenseState State, string? Key)
{
    public static LicenseStatus Personal { get; } = new(LicenseState.Personal, null);
}

public enum LicenseKeyValidation
{
    Ok,
    Empty,
    BadFormat,
    BadChecksum
}
