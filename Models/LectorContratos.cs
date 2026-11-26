using System.Collections.Concurrent;

namespace OSO.Models;

// Lee los contratos subidos con la IA en segundo plano, para que la subida no quede esperando a Gemini
// (con los reintentos y los modelos de respaldo puede tardar varios minutos).
// Mientras se lee, el borrador está "en lectura": la pantalla de revisión muestra un aviso y se recarga sola,
// en vez de mostrar el borrador vacío. Si la aplicación se reinicia a mitad de una lectura, el borrador queda
// vacío para completar a mano (igual que cuando la IA falla).
public class LectorContratos(IServiceScopeFactory scopeFactory, ILogger<LectorContratos> logger)
{
    private readonly ConcurrentDictionary<int, byte> _enLectura = new();

    public bool EstaLeyendo(int idContrato) => _enLectura.ContainsKey(idContrato);

    public void Empezar(int idContrato, byte[] archivo, string mimeType)
    {
        if (!_enLectura.TryAdd(idContrato, 0))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await LeerAsync(idContrato, archivo, mimeType);
            }
            catch (Exception ex)
            {
                // No se loguea el contenido: solo el tipo de error.
                logger.LogError("Falló la lectura del contrato {IdContrato}: {Tipo}", idContrato, ex.GetType().Name);
                try
                {
                    new BD().GuardarExtraccion(idContrato, "-", null, "Error", "Ocurrió un error inesperado al leer el contrato.");
                }
                catch (Exception)
                {
                    // Si tampoco se puede guardar el error, el borrador queda vacío para completar a mano.
                }
            }
            finally
            {
                _enLectura.TryRemove(idContrato, out _);
            }
        });
    }

    private async Task LeerAsync(int idContrato, byte[] archivo, string mimeType)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        var extractor = scope.ServiceProvider.GetRequiredService<ExtractorContratoGemini>();
        var bd = new BD();

        List<Gastos> catalogo = bd.ObtenerCatalogoGastos();
        var (json, errorIa) = await extractor.ExtraerAsync(archivo, mimeType, catalogo.Select(g => g.TipoGasto));
        ResultadoExtraccion resultado = ResultadoExtraccion.Parsear(json);
        bool leyoAlgo = json != null && (resultado.Gastos.Count > 0 || resultado.ConfianzaPorCampo().Values.Any(c => c > 0));

        // Si mientras tanto el borrador se descartó, no hay nada que completar.
        Contratos? contrato = bd.ObtenerContratoPorId(idContrato);
        if (contrato == null || contrato.Estado != "Borrador")
        {
            return;
        }

        if (!leyoAlgo)
        {
            // El aviso lo muestra la pantalla de revisión cada vez que se abre.
            bd.GuardarExtraccion(idContrato, extractor.Modelo, null, "Error", errorIa ?? "La respuesta de la IA no tenía datos del contrato.");
            return;
        }

        bd.GuardarExtraccion(idContrato, extractor.Modelo, json, "Ok", null);
        ResultadoExtraccion.AplicarA(resultado, contrato);
        bd.GuardarDatosContrato(contrato);
        bd.GuardarReglasGasto(idContrato, ResultadoExtraccion.ReglasDesde(resultado, catalogo));
    }
}
