namespace OSO.Models;

public class AjusteContrato
{
    public int IdAjuste { get; set; }
    public int IdContrato { get; set; }
    public DateTime Fecha { get; set; }
    public decimal MontoAnterior { get; set; }
    public decimal MontoNuevo { get; set; }
    public decimal? MontoCalculado { get; set; }
    public string Indice { get; set; } = string.Empty;
    public decimal? ValorIndiceDesde { get; set; }
    public decimal? ValorIndiceHasta { get; set; }
    public string Origen { get; set; } = "Calculado"; // Calculado | Manual

    // Si el propietario puso otro monto, se muestra también el que daba el índice.
    public bool MuestraMontoCalculado => Origen == "Manual" && MontoCalculado.HasValue;
}

// El próximo ajuste de un contrato: su fecha y lo que da el índice (o qué falta para calcularlo).
public record ProximoAjuste(DateTime Fecha, ResultadoAjuste Calculo)
{
    public bool YaCorresponde => CalculadoraAjuste.YaCorresponde(Fecha);
}

public static class AjustesDeContrato
{
    // Null si el contrato no tiene más ajustes (no se ajusta o el próximo cae después del fin).
    public static ProximoAjuste? Proximo(Contratos contrato, List<AjusteContrato> ajustes)
    {
        DateTime? ultimo = ajustes.Count > 0 ? ajustes.Max(a => a.Fecha) : null;
        DateTime? proxima = CalculadoraAjuste.ProximaFecha(contrato, ultimo);
        if (proxima == null || proxima.Value > contrato.FechaFin)
        {
            return null;
        }

        ResultadoAjuste calculo = CalculadoraAjuste.Calcular(contrato.IndiceAjuste!, contrato.MontoAlquiler, ultimo ?? contrato.FechaInicio,
            proxima.Value, contrato.PorcentajeFijo, new BD().ObtenerValorIndice);
        return new ProximoAjuste(proxima.Value, calculo);
    }

    // El propietario aplica el ajuste cuando llega la fecha. Con la ley (ICL / Casa Propia) el monto es el calculado;
    // con el DNU puede escribir otro (se guarda como Manual, junto con el calculado).
    // Devuelve si se aplicó y el mensaje para mostrar.
    public static (bool Aplicado, string Mensaje) Aplicar(Contratos contrato, decimal? montoManual)
    {
        var bd = new BD();
        ProximoAjuste? proximo = Proximo(contrato, bd.ObtenerAjustes(contrato.IdContrato));
        if (proximo == null || !proximo.YaCorresponde)
        {
            return (false, "Todavía no corresponde aplicar un ajuste.");
        }

        ResultadoAjuste calculo = proximo.Calculo;
        bool puedeCambiarMonto = contrato.PermiteMontoManual;
        decimal? montoElegido = puedeCambiarMonto && montoManual.HasValue ? Math.Round(montoManual.Value, 2) : calculo.MontoNuevo;
        if (montoElegido == null)
        {
            return (false, (calculo.Faltante ?? "No se pudo calcular el ajuste") + (puedeCambiarMonto ? ", o ingresá el monto a mano." : "."));
        }

        if (montoElegido <= 0 || montoElegido > Contratos.MontoMaximo)
        {
            return (false, "El monto del alquiler no es válido.");
        }

        bool esManual = calculo.MontoNuevo != montoElegido;
        if (!bd.AplicarAjuste(contrato.IdContrato, proximo.Fecha, calculo, montoElegido.Value, esManual ? "Manual" : "Calculado"))
        {
            return (false, "Ese ajuste ya estaba aplicado.");
        }

        string aviso = $"Ajuste aplicado: el alquiler pasa a {Formato.Pesos(montoElegido)}.";
        if (esManual && calculo.MontoNuevo.HasValue && CalculadoraAjuste.SeAlejaDelCalculado(montoElegido.Value, calculo.MontoNuevo.Value))
        {
            aviso += " Ojo: el monto se aleja más de un 5% del calculado por el índice.";
        }

        return (true, aviso);
    }
}
