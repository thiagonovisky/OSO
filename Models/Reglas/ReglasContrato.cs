namespace OSO.Models;

public static class RegimenesLegales
{
    public const string Ley27551 = "LEY_27551";
    public const string Ley27737 = "LEY_27737";
    public const string Dnu70 = "DNU_70_2023";
}

// Qué se paga y quién lo paga según el contrato (una fila por tipo de gasto).
public class ReglaGasto
{
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public string Responsable { get; set; } = "Propietario"; // Inquilino | Propietario | Compartido
    public decimal PorcentajeInquilino { get; set; }
    public string Origen { get; set; } = "Default"; // IA | Manual | Default
    public string? TextoClausula { get; set; }
    public decimal? Confianza { get; set; }

    // Porcentaje que realmente paga el inquilino, según el responsable elegido.
    public decimal PorcentajeEfectivoInquilino => Responsable switch
    {
        "Inquilino" => 100m,
        "Propietario" => 0m,
        _ => PorcentajeInquilino
    };

    public bool LoPagaElInquilino => PorcentajeEfectivoInquilino > 0;

    public static readonly string[] OpcionesResponsable = { "Inquilino", "Propietario", "Compartido" };

    // Fila leída por la IA con poca seguridad: se marca para que el propietario la revise.
    public bool EsDudosa => Origen == "IA" && Confianza < ReglasContrato.UmbralBajaConfianza;

    // Quién paga, contado desde el lado del inquilino.
    public string QuienPagaParaInquilino => Responsable switch
    {
        "Compartido" => $"Compartido ({Formato.Numero(PorcentajeInquilino, "0.##")}% vos)",
        "Inquilino" => "Vos",
        _ => "Propietario"
    };
}

// Reglas legales de los contratos de alquiler (funciones puras, sin acceso a datos).
public static class ReglasContrato
{
    public const string TipoExtraordinarias = "Expensas extraordinarias";

    // Servicios que llegan todos los meses: con contrato, aparecen solos como pendientes de quien los paga.
    // El resto (extraordinarias, reparaciones, seguro, fondo de reserva, tipos propios) se carga cuando ocurre.
    public static readonly string[] TiposMensuales = { "Expensas", "Luz", "Gas", "Agua", "Internet", "ABL", "Impuesto inmobiliario" };
    public const decimal UmbralBajaConfianza = 0.7m;

    private static readonly DateTime FinLey27551 = new(2023, 10, 17);
    private static readonly DateTime FinLey27737 = new(2023, 12, 28);

    private static readonly List<string> Indices = new() { "IPC", "ICL", "CasaPropia", "Fijo", "Otro" };
    private static readonly List<string> Responsables = new() { "Inquilino", "Propietario", "Compartido" };

    public static string CalcularRegimen(DateTime fechaFirma)
    {
        DateTime dia = fechaFirma.Date;
        if (dia <= FinLey27551)
        {
            return RegimenesLegales.Ley27551;
        }

        return dia <= FinLey27737 ? RegimenesLegales.Ley27737 : RegimenesLegales.Dnu70;
    }

    // Índice y frecuencia que la ley impone (null si las partes pueden pactar libremente).
    public static (string Indice, int FrecuenciaMeses)? AjusteForzado(string? regimen) => regimen switch
    {
        RegimenesLegales.Ley27551 => ("ICL", 12),
        RegimenesLegales.Ley27737 => ("CasaPropia", 6),
        _ => null
    };

    public const string FinDePlazo = "FinDePlazo";
    public const int LargoMaximoDescripcionFin = 500;
    public const int LargoMaximoDisconformidad = 1000;

    public static readonly List<string> MotivosFin = new() { FinDePlazo, "RescisionInquilino", "Acuerdo", "Incumplimiento" };

    public static string NombreMotivoFin(string? motivo) => motivo switch
    {
        FinDePlazo => "Fin de plazo",
        "RescisionInquilino" => "Rescisión del inquilino",
        "Acuerdo" => "Acuerdo entre las partes",
        "Incumplimiento" => "Incumplimiento",
        _ => "Sin motivo"
    };

    // Controla si se puede terminar el contrato. Devuelve el mensaje de error, o null si se puede.
    // Fin de plazo: solo cuando ya llegó la fecha de fin (la fecha real es la de fin). Cualquier otro motivo: lleva
    // descripción y la fecha real de fin, que no puede ser futura ni estar fuera del plazo del contrato.
    public static string? ValidarTerminacion(Contratos c, string? motivo, DateTime? fechaFinReal, string? descripcion, DateTime hoy)
    {
        if (c.Estado != "Confirmado")
        {
            return "Solo se puede terminar un contrato vigente (confirmado).";
        }

        if (motivo == null || !MotivosFin.Contains(motivo))
        {
            return "Elegí el motivo de la terminación.";
        }

        if (motivo == FinDePlazo)
        {
            return hoy.Date >= c.FechaFin.Date
                ? null
                : $"El contrato vence el {c.FechaFin:dd/MM/yyyy}: todavía no se puede terminar por fin de plazo. Si lo querés terminar antes, elegí otro motivo y explicalo.";
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            return "Escribí qué pasó: la descripción es obligatoria para este motivo.";
        }

        if (descripcion.Trim().Length > LargoMaximoDescripcionFin)
        {
            return $"La descripción no puede superar los {LargoMaximoDescripcionFin} caracteres.";
        }

        // Si todavía no empezó, es una cancelación: la fecha real la pone el sistema (ver FechaFinCancelacion).
        if (c.FechaInicio.Date > hoy.Date)
        {
            return null;
        }

        if (fechaFinReal == null || fechaFinReal.Value == default)
        {
            return "Indicá la fecha real de fin del contrato.";
        }

        if (fechaFinReal.Value.Date > hoy.Date)
        {
            return "La fecha real de fin no puede ser futura.";
        }

        if (fechaFinReal.Value.Date < c.FechaInicio.Date)
        {
            return "La fecha real de fin no puede ser anterior al inicio del contrato.";
        }

        if (fechaFinReal.Value.Date > c.FechaFin.Date)
        {
            return "La fecha real de fin no puede ser posterior al fin previsto del contrato.";
        }

        return null;
    }

    // Fecha real de fin al terminar el contrato (ya validado con ValidarTerminacion). En fin de plazo es la de fin del
    // contrato; si todavía no empezó, es una cancelación y la fecha la pone el sistema; si no, la que indicó el propietario.
    public static DateTime FechaFinReal(Contratos c, string motivo, DateTime? fechaIndicada, DateTime hoy)
    {
        if (motivo == FinDePlazo)
        {
            return c.FechaFin;
        }

        return c.FechaInicio.Date > hoy.Date ? FechaFinCancelacion(c) : fechaIndicada!.Value;
    }

    // Fecha real de fin de un contrato cancelado antes de empezar: el último día del mes anterior al inicio,
    // así no queda ningún mes (ni gasto ni vencimiento) dentro del contrato, aunque empiece a mitad de mes.
    public static DateTime FechaFinCancelacion(Contratos c) =>
        new DateTime(c.FechaInicio.Year, c.FechaInicio.Month, 1).AddDays(-1);

    // Calcula el régimen desde la fecha de firma y, si la ley lo impone, fuerza el índice y la frecuencia.
    public static void AplicarRegimen(Contratos c)
    {
        c.RegimenLegal = c.FechaFirma.HasValue ? CalcularRegimen(c.FechaFirma.Value) : null;

        var forzado = AjusteForzado(c.RegimenLegal);
        if (forzado.HasValue)
        {
            c.IndiceAjuste = forzado.Value.Indice;
            c.FrecuenciaAjusteMeses = forzado.Value.FrecuenciaMeses;
        }
    }

    public static string NombreRegimen(string? regimen) => regimen switch
    {
        RegimenesLegales.Ley27551 => "Ley 27.551 (firmado hasta el 17/10/2023)",
        RegimenesLegales.Ley27737 => "Ley 27.737 (firmado entre el 18/10/2023 y el 28/12/2023)",
        RegimenesLegales.Dnu70 => "DNU 70/2023 (firmado desde el 29/12/2023)",
        _ => "Sin determinar"
    };

    private static bool ExtraordinariasAlInquilino(List<ReglaGasto> reglas) =>
        reglas.FirstOrDefault(r => r.TipoGasto == TipoExtraordinarias && r.LoPagaElInquilino) != null;

    // Problemas que impiden confirmar el contrato.
    public static List<string> Errores(Contratos c, List<ReglaGasto> reglas)
    {
        var errores = new List<string>();

        if (c.FechaFirma == null)
        {
            errores.Add("Completá la fecha de firma del contrato: sin ella no se puede saber qué régimen legal le corresponde.");
        }
        else if (c.FechaFin != default && c.FechaFirma.Value.Date > c.FechaFin.Date)
        {
            errores.Add("La fecha de firma no puede ser posterior al fin del contrato.");
        }

        if (c.FechaInicio == default || c.FechaFin == default)
        {
            errores.Add("Completá las fechas de inicio y fin del contrato.");
        }
        else if (c.FechaFin <= c.FechaInicio)
        {
            errores.Add("La fecha de fin tiene que ser posterior a la de inicio.");
        }

        if (c.MontoAlquiler <= 0)
        {
            errores.Add("El monto del alquiler tiene que ser mayor a cero.");
        }

        if (c.DiaVencimiento is < 1 or > 31)
        {
            errores.Add("El día de vencimiento tiene que estar entre 1 y 31.");
        }

        if (c.IndiceAjuste == "Fijo" && (c.PorcentajeFijo == null || c.PorcentajeFijo < 0 || c.PorcentajeFijo > 1000))
        {
            errores.Add("Indicá el porcentaje fijo de ajuste.");
        }

        if (!string.IsNullOrEmpty(c.IndiceAjuste) && !Indices.Contains(c.IndiceAjuste))
        {
            errores.Add("El índice de ajuste elegido no es válido.");
        }

        if (!string.IsNullOrEmpty(c.IndiceAjuste) && c.IndiceAjuste != "Otro" && (c.FrecuenciaAjusteMeses ?? 0) <= 0)
        {
            errores.Add("Indicá cada cuántos meses se ajusta el alquiler.");
        }

        foreach (ReglaGasto regla in reglas)
        {
            if (!Responsables.Contains(regla.Responsable) || regla.PorcentajeInquilino < 0 || regla.PorcentajeInquilino > 100)
            {
                errores.Add($"El reparto de {regla.TipoGasto} no es válido: el porcentaje del inquilino va de 0 a 100.");
            }
        }

        if (c.FechaFirma != null && CalcularRegimen(c.FechaFirma.Value) != RegimenesLegales.Dnu70 && ExtraordinariasAlInquilino(reglas))
        {
            errores.Add("Las expensas extraordinarias no pueden ir a cargo del inquilino en contratos firmados antes del DNU 70/2023: las paga el propietario.");
        }

        return errores;
    }

    // Toma de lo que mandó el formulario solo el responsable y el porcentaje de los tipos que ya tiene el contrato.
    // Una regla que cambia pasa a ser "Manual".
    public static void CopiarCambiosDeReparto(List<ReglaGasto> guardadas, List<ReglaGasto>? recibidas)
    {
        foreach (ReglaGasto actual in guardadas)
        {
            ReglaGasto? recibida = recibidas?.FirstOrDefault(r => r.IdGasto == actual.IdGasto);
            if (recibida == null || !Responsables.Contains(recibida.Responsable))
            {
                continue;
            }

            decimal porcentaje = Math.Round(Math.Clamp(recibida.PorcentajeInquilino, 0m, 100m), 2);
            bool cambio = recibida.Responsable != actual.Responsable
                          || (recibida.Responsable == "Compartido" && porcentaje != actual.PorcentajeInquilino);
            if (cambio)
            {
                actual.Responsable = recibida.Responsable;
                actual.PorcentajeInquilino = porcentaje;
                actual.Origen = "Manual";
            }
        }
    }

    // El inquilino con el que se puede hacer el contrato: si la propiedad ya tiene uno, siempre ése.
    // Null si no está entre los posibles.
    public static int? ElegirInquilino(Propiedades propiedad, int idElegido, List<Usuario> posibles)
    {
        int idInquilino = propiedad.IdInquilino ?? idElegido;
        return posibles.FirstOrDefault(u => u.IdInquilino == idInquilino) != null ? idInquilino : null;
    }

    // Avisos que no impiden confirmar.
    public static List<string> Advertencias(Contratos c, List<ReglaGasto> reglas)
    {
        var advertencias = new List<string>();

        if (c.FechaFirma != null)
        {
            string regimen = CalcularRegimen(c.FechaFirma.Value);

            if (regimen == RegimenesLegales.Dnu70 && ExtraordinariasAlInquilino(reglas))
            {
                advertencias.Add("Las expensas extraordinarias quedaron a cargo del inquilino. Está permitido por el DNU 70/2023, pero conviene que ambos lo tengan claro.");
            }

            if (c.FechaInicio != default && CalcularRegimen(c.FechaInicio) != regimen)
            {
                advertencias.Add("El contrato empieza bajo un régimen distinto al de su fecha de firma. Se aplica el de la fecha de firma.");
            }
        }

        foreach (ReglaGasto regla in reglas)
        {
            if (regla.Confianza is < UmbralBajaConfianza)
            {
                advertencias.Add($"Revisá el reparto de {regla.TipoGasto}: la lectura del contrato no fue segura.");
            }
        }

        return advertencias;
    }
}
