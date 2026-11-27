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

    // Se terminó antes de empezar: la fecha real de fin quedó antes del inicio (ver ReglasContrato.FechaFinCancelacion).
    public bool CanceladoAntesDeEmpezar => FechaFinReal.HasValue && FechaFinReal.Value.Date < FechaInicio.Date;

    // Se carga en algunas consultas (no es una columna de Contratos)
    public string? Direccion { get; set; }

    public bool EsBorrador => Estado == "Borrador";
    public bool EmpiezaEnElFuturo => FechaInicio.Date > DateTime.Today;
    public bool YaVencio => DateTime.Today >= FechaFin.Date;

    // La fecha real de fin no puede ser futura ni posterior al fin del contrato.
    public DateTime FechaMaximaFinReal => YaVencio ? FechaFin.Date : DateTime.Today;

    // Con el DNU 70/2023 el propietario puede aplicar un monto distinto al calculado.
    public bool PermiteMontoManual => RegimenLegal == RegimenesLegales.Dnu70;

    public string EstadoParaInquilino => Estado switch
    {
        "PendienteInquilino" => "Para aceptar",
        "Finalizado" => "Terminado",
        _ => "Vigente"
    };

    public string IndiceConPorcentaje => IndiceAjuste == "Fijo" ? $"{IndiceAjuste} {PorcentajeFijo}%" : IndiceAjuste ?? string.Empty;

    public string DescripcionAjuste
    {
        get
        {
            if (string.IsNullOrEmpty(IndiceAjuste))
            {
                return "-";
            }

            string porcentaje = IndiceAjuste == "Fijo" ? $" ({PorcentajeFijo}%)" : string.Empty;
            string frecuencia = FrecuenciaAjusteMeses.HasValue ? $" cada {FrecuenciaAjusteMeses} meses" : string.Empty;
            return IndiceAjuste + porcentaje + frecuencia;
        }
    }

    public string DepositoTexto => MontoDeposito.HasValue ? $"{Formato.Numero(MontoDeposito)} {MonedaDeposito}" : "-";

    public const decimal MontoMaximo = 9_999_999_999m;

    public bool EsperaAlInquilino => Estado == "PendienteInquilino";
    public bool EstaTerminado => Estado == "Finalizado";

    // Color de la etiqueta del estado (estado-pill--...).
    public string EstadoCss => EsperaAlInquilino ? "pagar" : EstaTerminado ? "neutro" : "pagado";

    // Cómo se nombra el estado según quién lo mira.
    public string EstadoPara(bool comoPropietario) => comoPropietario && EsperaAlInquilino ? "Esperando al inquilino" : EstadoParaInquilino;

    // Valida lo que llegó del formulario de revisión y copia a este contrato solo los datos editables.
    // Devuelve el error si no se puede guardar.
    public string? CopiarDatosRevision(Contratos recibido)
    {
        if (recibido.FechaInicio == default || recibido.FechaFin == default)
        {
            return "Completá las fechas de inicio y fin del contrato.";
        }

        if (recibido.FechaFin <= recibido.FechaInicio)
        {
            return "La fecha de fin tiene que ser posterior a la de inicio.";
        }

        if (recibido.MontoAlquiler < 0 || recibido.MontoAlquiler > MontoMaximo)
        {
            return "El monto del alquiler no es válido.";
        }

        if ((recibido.PorcentajeFijo ?? 0) is < 0 or > 100 || (recibido.PunitorioDiario ?? 0) is < 0 or > 100)
        {
            return "Los porcentajes tienen que estar entre 0 y 100.";
        }

        if ((recibido.MontoDeposito ?? 0) < 0 || (recibido.MontoDeposito ?? 0) > MontoMaximo)
        {
            return "El monto del depósito no es válido.";
        }

        FechaInicio = recibido.FechaInicio.Date;
        FechaFin = recibido.FechaFin.Date;
        MontoAlquiler = recibido.MontoAlquiler;
        FechaFirma = recibido.FechaFirma?.Date;
        FechaPrimerAjuste = recibido.FechaPrimerAjuste?.Date;
        Moneda = Opciones.Buscar(RevisionContrato.Monedas, recibido.Moneda) ?? "ARS";
        Destino = Opciones.Buscar(RevisionContrato.Destinos, recibido.Destino);
        IndiceAjuste = Opciones.Buscar(RevisionContrato.Indices, recibido.IndiceAjuste);
        FrecuenciaAjusteMeses = recibido.FrecuenciaAjusteMeses is >= 1 and <= 60 ? recibido.FrecuenciaAjusteMeses : null;
        PorcentajeFijo = recibido.PorcentajeFijo;
        DiaVencimiento = recibido.DiaVencimiento is >= 1 and <= 31 ? recibido.DiaVencimiento : null;
        MedioPago = Recortar(recibido.MedioPago, 200);
        PunitorioDiario = recibido.PunitorioDiario;
        MontoDeposito = recibido.MontoDeposito;
        MonedaDeposito = Opciones.Buscar(RevisionContrato.Monedas, recibido.MonedaDeposito);
        TipoGarantia = Opciones.Buscar(RevisionContrato.Garantias, recibido.TipoGarantia);
        PreavisoRescisionDias = recibido.PreavisoRescisionDias is >= 0 and <= 365 ? recibido.PreavisoRescisionDias : null;
        CondicionesRescision = Recortar(recibido.CondicionesRescision, 1000);

        // El régimen sale de la fecha de firma; si la ley impone índice y frecuencia, se pisan.
        ReglasContrato.AplicarRegimen(this);
        return null;
    }

    // Texto sin espacios de más y con un largo máximo; null si quedó vacío.
    private static string? Recortar(string? valor, int largo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        string texto = valor.Trim();
        return texto.Length > largo ? texto[..largo] : texto;
    }
}
