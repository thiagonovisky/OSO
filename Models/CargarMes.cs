namespace OSO.Models;

// Una fila de la pantalla "Cargar el mes": un servicio de una propiedad en un mes que todavía no tiene gasto cargado.
public class FilaCargarMes
{
    public int IdPropiedad { get; set; }
    public string NombrePropiedad { get; set; } = string.Empty;
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public DateTime Periodo { get; set; }
    public string QuienPaga { get; set; } = string.Empty;

    // Monto del mismo servicio el mes anterior: solo se muestra como sugerencia, nunca se guarda solo.
    public decimal? MontoSugerido { get; set; }

    // Identifica la fila en la pantalla (y en lo que devuelve la lectura de facturas).
    public string Clave => $"{IdPropiedad}-{IdGasto}-{Periodo:yyyyMM}";
}

// Lo que llega del formulario por cada fila. IncluidoEn: mes de la factura que cubre este mes (null si no está incluido).
public class FilaEnviada
{
    public int IdPropiedad { get; set; }
    public int IdGasto { get; set; }
    public DateTime Periodo { get; set; }
    public decimal? Monto { get; set; }
    public DateTime? Vencimiento { get; set; }
    public DateTime? IncluidoEn { get; set; }
}

public enum ResultadoFilaMes { Guardado, YaCargado, NoCorresponde }

public static class FilasCargarMes
{
    // Arma las filas de la pantalla a partir de los pendientes de los dos lados (un mes compartido figura una sola vez).
    // montos: monto de cada gasto ya cargado por (IdPropiedad, IdGasto, mes), para sugerir el del mes anterior.
    public static List<FilaCargarMes> Armar(IEnumerable<PendienteContrato> pendientes, IReadOnlyDictionary<(int, int, DateTime), decimal> montos) =>
        pendientes
            .GroupBy(p => (p.IdPropiedad, p.IdGasto, p.Periodo))
            .Select(g => g.First())
            .Select(p => new FilaCargarMes
            {
                IdPropiedad = p.IdPropiedad,
                NombrePropiedad = p.NombrePropiedad,
                IdGasto = p.IdGasto,
                TipoGasto = p.TipoGasto,
                Periodo = p.Periodo,
                QuienPaga = p.QuienPaga,
                MontoSugerido = montos.TryGetValue((p.IdPropiedad, p.IdGasto, p.Periodo.AddMonths(-1)), out decimal anterior) ? anterior : null
            })
            .OrderByDescending(f => f.Periodo)
            .ThenBy(f => f.NombrePropiedad)
            .ThenBy(f => f.TipoGasto)
            .ToList();
}
