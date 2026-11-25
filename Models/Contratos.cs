namespace OSO.Models;

public class Contratos
{
    public int IdContrato { get; set; }
    public int IdPropiedad { get; set; }
    public int IdInquilino { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public decimal MontoAlquiler { get; set; }

    public DateTime? FechaFirma { get; set; }
    public string? RegimenLegal { get; set; }
    public string? Destino { get; set; }
    public string Moneda { get; set; } = "ARS";
    public string? IndiceAjuste { get; set; }
    public int? FrecuenciaAjusteMeses { get; set; }
    public DateTime? FechaPrimerAjuste { get; set; }
    public decimal? PorcentajeFijo { get; set; }
    public byte? DiaVencimiento { get; set; }
    public string? MedioPago { get; set; }
    public decimal? PunitorioDiario { get; set; }
    public decimal? MontoDeposito { get; set; }
    public string? MonedaDeposito { get; set; }
    public string? TipoGarantia { get; set; }
    public int? PreavisoRescisionDias { get; set; }
    public string? CondicionesRescision { get; set; }
    public string? ArchivoContrato { get; set; }
    public string Estado { get; set; } = "Confirmado";
    public bool ConfirmadoPorPropietario { get; set; }
    public bool ConfirmadoPorInquilino { get; set; }

    // Terminación del contrato (Estado = Finalizado)
    public DateTime? FechaFinReal { get; set; }
    public string? MotivoFin { get; set; }
    public string? DescripcionFin { get; set; }
    public string? DisconformidadInquilino { get; set; }
    public DateTime? FechaDisconformidad { get; set; }

    // Hasta cuándo rige de verdad: la fecha real de fin si el contrato se terminó antes (o después) de lo previsto.
    public DateTime FechaFinEfectiva => FechaFinReal ?? FechaFin;

    // Se carga en algunas consultas (no es una columna de Contratos)
    public string? Direccion { get; set; }

    // Devuelve el primer mensaje de error, o null si el contrato es válido.
    public string? Validar(IEnumerable<ReglaGasto>? reglas = null)
    {
        if (IdInquilino <= 0)
        {
            return "Debe seleccionar un inquilino para crear el contrato.";
        }

        return ReglasContrato.Errores(this, reglas ?? Enumerable.Empty<ReglaGasto>()).FirstOrDefault();
    }
}
