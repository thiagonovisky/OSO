namespace OSO.Models;

// Mantiene al día la tabla IndicesAjuste: al arrancar la app y cada 24 horas trae los IPC (INDEC) y los ICL diarios (BCRA)
// que faltan. Nunca borra ni pisa lo que ya está cargado. Casa Propia no tiene API: se carga a mano con el script.
public class ActualizadorIndices(IHttpClientFactory httpClientFactory, ILogger<ActualizadorIndices> logger) : BackgroundService
{
    private const string FuenteIpc = "INDEC - IPC Nacional base dic-2016";
    private const string FuenteIcl = "BCRA - ICL (variable 40)";
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);
    private static readonly DateTime InicioIpc = new(2020, 1, 1);
    private static readonly DateTime InicioIcl = new(2020, 7, 1);

    private readonly BD bd = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ActualizarTodoAsync(stoppingToken);

            try
            {
                await Task.Delay(Intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Cada índice por separado: si uno falla (sin internet, API caída, tabla inexistente) no afecta al otro ni a la app.
    public async Task ActualizarTodoAsync(CancellationToken cancelacion)
    {
        await Intentar("IPC", () => ActualizarIpcAsync(cancelacion));
        await Intentar("ICL", () => ActualizarIclAsync(cancelacion));
    }

    private async Task Intentar(string indice, Func<Task> actualizar)
    {
        try
        {
            await actualizar();
        }
        catch (Exception ex)
        {
            logger.LogWarning("No se pudo actualizar el índice {Indice}: {Tipo} {Mensaje}", indice, ex.GetType().Name, ex.Message);
        }
    }

    private async Task ActualizarIpcAsync(CancellationToken cancelacion)
    {
        DateTime? ultimo = bd.UltimoPeriodoIndice("IPC");
        DateTime desde = ultimo?.AddMonths(1) ?? InicioIpc;
        if (desde > DateTime.Today)
        {
            return;
        }

        string url = $"https://apis.datos.gob.ar/series/api/series/?ids=148.3_INIVELNAL_DICI_M_26&format=json&start_date={desde:yyyy-MM-dd}&limit=1000";
        string json = await DescargarAsync(url, cancelacion);
        int nuevos = bd.InsertarIndicesFaltantes("IPC", LectorIndices.LeerIpc(json), FuenteIpc);
        logger.LogInformation("IPC: {Nuevos} valores nuevos", nuevos);
    }

    private async Task ActualizarIclAsync(CancellationToken cancelacion)
    {
        // Mientras solo haya valores del día 1 de cada mes (los del script), se pide todo el historial diario.
        DateTime? ultimo = bd.UltimoPeriodoIndice("ICL");
        DateTime desde = ultimo.HasValue && bd.TieneIclDiario() ? ultimo.Value.AddDays(1) : InicioIcl;
        DateTime hasta = DateTime.Today;
        if (desde > hasta)
        {
            return;
        }

        string url = $"https://api.bcra.gob.ar/estadisticas/v4.0/monetarias/40?desde={desde:yyyy-MM-dd}&hasta={hasta:yyyy-MM-dd}&limit=3000";
        string json = await DescargarAsync(url, cancelacion);
        int nuevos = bd.InsertarIndicesFaltantes("ICL", LectorIndices.LeerIcl(json), FuenteIcl);
        logger.LogInformation("ICL: {Nuevos} valores nuevos", nuevos);
    }

    private async Task<string> DescargarAsync(string url, CancellationToken cancelacion)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelacion);
        limite.CancelAfter(TimeSpan.FromSeconds(60));

        using HttpClient cliente = httpClientFactory.CreateClient();
        using HttpResponseMessage respuesta = await cliente.GetAsync(url, limite.Token);
        respuesta.EnsureSuccessStatusCode();
        return await respuesta.Content.ReadAsStringAsync(limite.Token);
    }
}
