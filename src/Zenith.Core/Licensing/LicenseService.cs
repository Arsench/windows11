using Zenith.Core.Abstractions;

namespace Zenith.Core.Licensing;

/// <summary>
/// Punto único donde vivirá la licencia cuando exista. Hoy solo guarda la clave
/// y comprueba su <b>forma</b>, nunca su validez: eso requiere un servidor.
///
/// Cuando llegue ese momento, lo único que cambia es
/// <see cref="ActivateAsync"/>: pasará a llamar al servicio de activación y a
/// devolver <see cref="LicenseState"/> reales. Nada del resto de la aplicación
/// tiene que enterarse.
/// </summary>
public sealed class LicenseService(ISettingsStore settings)
{
    private const int GroupLength = 5;
    private const int GroupCount = 4;

    // Sin I, L, O, U ni 0/1: evita que el usuario confunda caracteres al teclear.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public LicenseStatus Current { get; private set; } = LicenseStatus.Personal;

    public event EventHandler<LicenseStatus>? Changed;

    public void Refresh()
    {
        var key = settings.Current.LicenseKey;
        Current = string.IsNullOrWhiteSpace(key)
            ? LicenseStatus.Personal
            : new LicenseStatus(
                Validate(key) == LicenseKeyValidation.Ok ? LicenseState.PendingVerification : LicenseState.Malformed,
                Normalize(key));

        Changed?.Invoke(this, Current);
    }

    /// <summary>
    /// Guarda la clave si tiene forma válida. NO activa nada: devuelve
    /// <see cref="LicenseState.PendingVerification"/> hasta que exista un
    /// servidor que la verifique de verdad.
    /// </summary>
    public async Task<LicenseKeyValidation> ActivateAsync(string? key, CancellationToken ct = default)
    {
        var validation = Validate(key);
        if (validation != LicenseKeyValidation.Ok) return validation;

        var normalized = Normalize(key!);
        await settings.UpdateAsync(s => s.LicenseKey = normalized, ct).ConfigureAwait(false);
        Refresh();
        return LicenseKeyValidation.Ok;
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await settings.UpdateAsync(s => s.LicenseKey = null, ct).ConfigureAwait(false);
        Refresh();
    }

    /// <summary>
    /// Comprobación de <b>formato</b>: 4 grupos de 5 caracteres del alfabeto,
    /// con dígito de control. Sirve para detectar erratas al teclear, nada más.
    /// </summary>
    public static LicenseKeyValidation Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return LicenseKeyValidation.Empty;

        var normalized = Normalize(key);
        var groups = normalized.Split('-');

        if (groups.Length != GroupCount) return LicenseKeyValidation.BadFormat;
        if (groups.Any(g => g.Length != GroupLength)) return LicenseKeyValidation.BadFormat;

        var body = string.Concat(groups);
        if (body.Any(c => !Alphabet.Contains(c, StringComparison.Ordinal))) return LicenseKeyValidation.BadFormat;

        return Checksum(body.AsSpan(0, body.Length - 1)) == body[^1]
            ? LicenseKeyValidation.Ok
            : LicenseKeyValidation.BadChecksum;
    }

    /// <summary>Mayúsculas, sin espacios y con guiones cada cinco caracteres.</summary>
    public static string Normalize(string key)
    {
        var body = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        var groups = new List<string>();
        for (var i = 0; i < body.Length; i += GroupLength)
        {
            groups.Add(body.Substring(i, Math.Min(GroupLength, body.Length - i)));
        }

        return string.Join('-', groups);
    }

    private static char Checksum(ReadOnlySpan<char> body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var index = Alphabet.IndexOf(body[i]);
            if (index < 0) return '\0';
            sum += index * (i % 2 == 0 ? 3 : 1);
        }

        return Alphabet[sum % Alphabet.Length];
    }
}
