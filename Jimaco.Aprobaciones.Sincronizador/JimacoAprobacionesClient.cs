using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Cliente HTTP contra la API pública de Jimaco Aprobaciones — el mismo <c>POST /api/documentos</c>
/// que usa el formulario manual del frontend. El Sincronizador no tiene ningún atajo especial ni
/// endpoint propio: entra por la puerta de siempre, autenticado como un usuario de servicio normal
/// (ver Usuarios en el admin), para no duplicar reglas de negocio del lado de la API.
/// </summary>
public class JimacoAprobacionesClient(HttpClient http, string email, string password)
{
    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private string? _token;
    private DateTimeOffset _tokenExpira = DateTimeOffset.MinValue;

    public async Task<InstanciaDocumentoDetalleDto> CrearDocumentoAsync(CrearInstanciaDocumentoDto dto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/documentos")
        {
            Content = JsonContent.Create(dto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<InstanciaDocumentoDetalleDto>(JsonOpciones, ct))!;
    }

    public async Task<IReadOnlyList<TipoDocumentoDto>> ListarTiposDocumentoAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/tiposdocumento");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<TipoDocumentoDto>>(JsonOpciones, ct))!;
    }

    public async Task<IReadOnlyList<PendienteEscrituraWODto>> ListarPendientesEscrituraWOAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sincronizacion/pendientes-wo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PendienteEscrituraWODto>>(JsonOpciones, ct))!;
    }

    public async Task ConfirmarEscrituraWOAsync(int instanciaDocumentoId, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sincronizacion/pendientes-wo/{instanciaDocumentoId}/confirmar");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task ReportarConflictoWOAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sincronizacion/pendientes-wo/{instanciaDocumentoId}/conflicto")
        {
            Content = JsonContent.Create(new ReportarConflictoWODto(mensaje), options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task<IReadOnlyList<PendienteComprobanteEgresoDto>> ListarPendientesComprobanteEgresoAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sincronizacion/pendientes-comprobante-egreso");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PendienteComprobanteEgresoDto>>(JsonOpciones, ct))!;
    }

    public async Task AdjuntarComprobanteEgresoAsync(int instanciaDocumentoId, ComprobanteEgresoEncontradoDto dto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sincronizacion/pendientes-comprobante-egreso/{instanciaDocumentoId}/encontrado")
        {
            Content = JsonContent.Create(dto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task ReportarConflictoComprobanteEgresoAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sincronizacion/pendientes-comprobante-egreso/{instanciaDocumentoId}/conflicto")
        {
            Content = JsonContent.Create(new ReportarConflictoWODto(mensaje), options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task EnviarCuentasPorPagarAsync(FotoCuentasPorPagarDto foto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sincronizacion/cuentas-por-pagar")
        {
            Content = JsonContent.Create(foto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    /// <summary>Cuándo se mandó por última vez el catálogo de terceros (null = nunca).</summary>
    public async Task<DateTime?> ObtenerFechaCorteTercerosAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sincronizacion/terceros/estado");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        var texto = (await response.Content.ReadAsStringAsync(ct)).Trim();
        return string.IsNullOrEmpty(texto) || texto == "null" ? null : System.Text.Json.JsonSerializer.Deserialize<DateTime?>(texto, JsonOpciones);
    }

    public async Task EnviarTercerosAsync(IReadOnlyList<TerceroWOInputDto> terceros, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sincronizacion/terceros")
        {
            Content = JsonContent.Create(terceros, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task<IReadOnlyList<PendienteCreacionCEDto>> ListarPendientesCreacionCEAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sincronizacion/pagos-proveedor/pendientes-ce");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PendienteCreacionCEDto>>(JsonOpciones, ct))!;
    }

    public async Task ConfirmarCECreadoAsync(int instanciaDocumentoId, CECreadoDto dto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sincronizacion/pagos-proveedor/{instanciaDocumentoId}/ce-creado")
        {
            Content = JsonContent.Create(dto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task ReportarConflictoCreacionCEAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/sincronizacion/pagos-proveedor/{instanciaDocumentoId}/conflicto")
        {
            Content = JsonContent.Create(new ReportarConflictoWODto(mensaje), options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task<IReadOnlyList<PendienteRemisionDto>> ListarPendientesRemisionAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sincronizacion/pendientes-remision");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PendienteRemisionDto>>(JsonOpciones, ct))!;
    }

    public async Task MarcarRemisionadaAsync(MarcarRemisionadaDto dto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sincronizacion/pendientes-remision/marcar")
        {
            Content = JsonContent.Create(dto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task<IReadOnlyList<PendienteRefrescoDto>> ListarPendientesRefrescoAsync(CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/sincronizacion/hojas-alistamiento/pendientes-refresco");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);

        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PendienteRefrescoDto>>(JsonOpciones, ct))!;
    }

    public async Task RefrescarHojaAlistamientoAsync(DocumentoVinculadoInputDto dto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sincronizacion/hojas-alistamiento/refrescar")
        {
            Content = JsonContent.Create(dto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    public async Task CrearHojaAlistamientoSinOcAsync(DocumentoVinculadoInputDto dto, CancellationToken ct = default)
    {
        await AsegurarTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sincronizacion/hojas-alistamiento/crear-sin-oc")
        {
            Content = JsonContent.Create(dto, options: JsonOpciones)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);

        using var response = await http.SendAsync(request, ct);
        await LanzarSiErrorAsync(response, ct);
    }

    private async Task AsegurarTokenAsync(CancellationToken ct)
    {
        // Un margen de 1 minuto antes de que expire, para no arrancar una petición con un token
        // que vence a mitad de camino.
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpira.AddMinutes(-1))
            return;

        using var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequestDto(email, password), JsonOpciones, ct);
        await LanzarSiErrorAsync(response, ct);

        var login = (await response.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOpciones, ct))!;
        _token = login.Token;
        _tokenExpira = ObtenerExpiracion(login.Token);
    }

    // El token ya trae su propia fecha de expiración (claim "exp") — la leemos de ahí en vez de
    // hardcodear la duración configurada del lado de la Api, así nunca se desincroniza.
    private static DateTimeOffset ObtenerExpiracion(string token)
    {
        try
        {
            var payload = token.Split('.')[1];
            var base64 = payload.Replace('-', '+').Replace('_', '/').PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            using var doc = JsonDocument.Parse(json);
            var exp = doc.RootElement.GetProperty("exp").GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(exp);
        }
        catch
        {
            // Si algo raro pasa parseando el JWT, forzamos a re-loguear en la próxima llamada
            // en vez de asumir una expiración larga.
            return DateTimeOffset.UtcNow;
        }
    }

    private static async Task LanzarSiErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var cuerpo = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase} — {cuerpo}");
    }
}
