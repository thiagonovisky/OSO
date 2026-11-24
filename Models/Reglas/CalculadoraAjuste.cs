namespace OSO.Models;

// Resultado de calcular un ajuste. Si falta algún dato, MontoNuevo es null y Faltante dice qué.
public record ResultadoAjuste(decimal? MontoNuevo, decimal? ValorDesde, decimal? ValorHasta, string? Faltante);

// Cálculo del ajuste del alquiler (funciones puras: los valores de los índices llegan por parámetro).
public static class CalculadoraAjuste
{
    public const decimal UmbralAlejamiento = 0.05m;

    private static DateTime PrimerDia(DateTime f) => new(f.Year, f.Month, 1);

    private static string Faltante(string indice, DateTime fecha) =>
        indice == "ICL"
            ? $"Falta cargar el índice {indice} de {fecha:dd/MM/yyyy}"
            : $"Falta cargar el índice {indice} de {fecha:MM/yyyy}";

    // montoAnterior: lo que se paga hoy. desde: inicio del contrato o fecha del último ajuste.
    // valor(indice, fecha): valor publicado del índice (null si todavía no está cargado).
    public static ResultadoAjuste Calcular(string indice, decimal montoAnterior, DateTime desde, DateTime fechaAjuste,
                                           decimal? porcentajeFijo, Func<string, DateTime, decimal?> valor)
    {
        switch (indice)
        {
            case "IPC":
            {
                // Valor del mes anterior al ajuste sobre el del mes anterior al inicio (o al último ajuste).
                DateTime mesHasta = PrimerDia(fechaAjuste).AddMonths(-1);
                DateTime mesDesde = PrimerDia(desde).AddMonths(-1);
                return PorCociente(indice, montoAnterior, mesDesde, mesHasta, valor);
            }

            case "ICL":
                // Días exactos: el del ajuste sobre el del inicio (o último ajuste).
                return PorCociente(indice, montoAnterior, desde.Date, fechaAjuste.Date, valor);

            case "CasaPropia":
            {
                // Producto de los 6 coeficientes mensuales de los meses que terminan en el anterior al ajuste.
                decimal factor = 1m;
                for (int i = 6; i >= 1; i--)
                {
                    DateTime mes = PrimerDia(fechaAjuste).AddMonths(-i);
                    decimal? coeficiente = valor(indice, mes);
                    if (coeficiente is null or <= 0)
                    {
                        return new ResultadoAjuste(null, null, null, Faltante(indice, mes));
                    }

                    factor *= coeficiente.Value;
                }

                return new ResultadoAjuste(Redondear(montoAnterior * factor), 1m, factor, null);
            }

            case "Fijo":
                if (porcentajeFijo is null or < 0)
                {
                    return new ResultadoAjuste(null, null, null, "Falta indicar el porcentaje fijo de ajuste");
                }

                decimal multiplicador = 1m + porcentajeFijo.Value / 100m;
                return new ResultadoAjuste(Redondear(montoAnterior * multiplicador), 1m, multiplicador, null);

            default:
                return new ResultadoAjuste(null, null, null, "Este contrato se ajusta por un índice que la app no calcula: ingresá el monto a mano");
        }
    }

    private static ResultadoAjuste PorCociente(string indice, decimal montoAnterior, DateTime desde, DateTime hasta, Func<string, DateTime, decimal?> valor)
    {
        decimal? valorDesde = valor(indice, desde);
        if (valorDesde is null or <= 0)
        {
            return new ResultadoAjuste(null, null, null, Faltante(indice, desde));
        }

        decimal? valorHasta = valor(indice, hasta);
        if (valorHasta is null or <= 0)
        {
            return new ResultadoAjuste(null, valorDesde, null, Faltante(indice, hasta));
        }

        return new ResultadoAjuste(Redondear(montoAnterior * valorHasta.Value / valorDesde.Value), valorDesde, valorHasta, null);
    }

    private static decimal Redondear(decimal monto) => Math.Round(monto, 2, MidpointRounding.AwayFromZero);

    // Fecha del próximo ajuste después de ultimoAjuste (o la primera, si todavía no hubo ninguno).
    // Null si el contrato no tiene ajustes. Se cuenta siempre desde la fecha base para que un día 31 no se corra de a poco.
    public static DateTime? ProximaFecha(Contratos c, DateTime? ultimoAjuste)
    {
        int frecuencia = c.FrecuenciaAjusteMeses ?? 0;
        if (string.IsNullOrEmpty(c.IndiceAjuste) || frecuencia <= 0)
        {
            return null;
        }

        DateTime baseAjuste = (c.FechaPrimerAjuste ?? c.FechaInicio.AddMonths(frecuencia)).Date;
        if (ultimoAjuste == null)
        {
            return baseAjuste;
        }

        DateTime fecha = baseAjuste;
        for (int k = 1; fecha <= ultimoAjuste.Value.Date && k < 1200; k++)
        {
            fecha = baseAjuste.AddMonths(k * frecuencia);
        }

        return fecha;
    }

    // true si el monto propuesto se aleja más de un 5% del calculado.
    public static bool SeAlejaDelCalculado(decimal propuesto, decimal calculado)
    {
        if (calculado == 0)
        {
            return propuesto != 0;
        }

        return Math.Abs(propuesto - calculado) / calculado > UmbralAlejamiento;
    }
}
