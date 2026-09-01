using Zenith.Core.Licensing;

namespace Zenith.Core.Tests;

public sealed class LicenseServiceTests
{
    /// <summary>Genera una clave con el dígito de control correcto, como haría el emisor.</summary>
    private static string BuildValidKey(string body19)
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

        var sum = 0;
        for (var i = 0; i < body19.Length; i++)
        {
            sum += alphabet.IndexOf(body19[i]) * (i % 2 == 0 ? 3 : 1);
        }

        return LicenseService.Normalize(body19 + alphabet[sum % alphabet.Length]);
    }

    [Fact]
    public void Una_clave_vacia_se_rechaza()
    {
        Assert.Equal(LicenseKeyValidation.Empty, LicenseService.Validate(null));
        Assert.Equal(LicenseKeyValidation.Empty, LicenseService.Validate("   "));
    }

    [Fact]
    public void Una_clave_con_longitud_incorrecta_se_rechaza()
    {
        Assert.Equal(LicenseKeyValidation.BadFormat, LicenseService.Validate("ABCDE-ABCDE"));
    }

    [Fact]
    public void Una_clave_con_caracteres_ambiguos_se_rechaza()
    {
        // La I, la O, el 0 y el 1 quedan fuera del alfabeto a propósito.
        Assert.Equal(LicenseKeyValidation.BadFormat, LicenseService.Validate("ABCDE-ABCDE-ABCDE-ABCI0"));
    }

    [Fact]
    public void Una_errata_la_detecta_el_digito_de_control()
    {
        var key = BuildValidKey("ABCDEFGHJKMNPQRSTVW");
        var last = key[^1];
        var wrong = key[..^1] + (last == 'A' ? 'B' : 'A');

        Assert.Equal(LicenseKeyValidation.BadChecksum, LicenseService.Validate(wrong));
    }

    [Fact]
    public void Una_clave_bien_formada_se_acepta()
    {
        Assert.Equal(LicenseKeyValidation.Ok, LicenseService.Validate(BuildValidKey("ABCDEFGHJKMNPQRSTVW")));
    }

    [Fact]
    public void Se_normalizan_espacios_y_minusculas()
    {
        Assert.Equal("ABCDE-FGHJK-MNPQR-STVWX", LicenseService.Normalize("abcde fghjk mnpqr stvwx"));
        Assert.Equal("ABCDE-FGHJK-MNPQR-STVWX", LicenseService.Normalize("ABCDE-FGHJK-MNPQR-STVWX"));
    }

    [Fact]
    public async Task Sin_clave_el_estado_es_uso_personal()
    {
        var store = new InMemorySettingsStore();
        var service = new LicenseService(store);

        service.Refresh();

        Assert.Equal(LicenseState.Personal, service.Current.State);
        Assert.Null(service.Current.Key);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Una_clave_valida_queda_pendiente_de_verificacion_nunca_activada()
    {
        var store = new InMemorySettingsStore();
        var service = new LicenseService(store);

        var result = await service.ActivateAsync(BuildValidKey("ABCDEFGHJKMNPQRSTVW"));

        Assert.Equal(LicenseKeyValidation.Ok, result);

        // Deliberado: sin servidor que la verifique, jamás pasa a "activada".
        Assert.Equal(LicenseState.PendingVerification, service.Current.State);
    }

    [Fact]
    public async Task Quitar_la_clave_vuelve_a_uso_personal()
    {
        var store = new InMemorySettingsStore();
        var service = new LicenseService(store);

        await service.ActivateAsync(BuildValidKey("ABCDEFGHJKMNPQRSTVW"));
        await service.ClearAsync();

        Assert.Equal(LicenseState.Personal, service.Current.State);
        Assert.Null(store.Current.LicenseKey);
    }
}
