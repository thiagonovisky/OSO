namespace OSO.Models;

// Una fila de la pantalla "Cargar el mes": un servicio de una propiedad en un mes que todavía no tiene gasto cargado.
public class FilaCargarMes
{
    public int IdPropiedad { get; set; }
    public string NombrePropiedad { get; set; } = string.Empty;
    public int IdGasto { get; set; }
    public string TipoGasto { get; set; } = string.Empty;
    public DateTime Periodo { get; set; }

    // Quién paga, contado para quien mira la pantalla ("Lo pagás vos", "Lo paga el inquilino", "Compartido: 50% vos").
    public string QuienPaga { get; set; } = string.Empty;

    // La paga toda la otra parte: cargarla no genera nada para pagar a quien mira, y la otra parte va a querer ver la factura.
    public bool LaPagaElOtro { get; set; }

    // Le toca pagar algo a la otra parte (toda o compartida): conviene adjuntar la factura.
    public bool ElOtroPagaAlgo { get; set; }

    // "el inquilino" | "el propietario", según quién mira.
    public string ElOtro { get; set; } = string.Empty;

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

    // La factura de la fila (opcional). Se guarda con el gasto para que la otra parte pueda verla.
    public IFormFile? Factura { get; set; }
}

public enum ResultadoFilaMes { Guardado, YaCargado, NoCorresponde }

public static class FilasCargarMes
{
    private const decimal MontoMaximo = 9_999_999_999m;

    // Una fila vacía (sin monto y sin marcar como incluida) no se guarda: sigue pendiente.
    public static bool TieneDatos(FilaEnviada fila) => fila.Monto.HasValue || fila.IncluidoEn.HasValue;

    // Devuelve el error de la fila, o null si se puede guardar.
    public static string? Validar(FilaEnviada fila)
    {
        if (fila.IncluidoEn.HasValue)
        {
            return fila.IncluidoEn.Value.Date > fila.Periodo.Date ? null : "La factura que lo incluye tiene que ser de un mes posterior.";
        }

        if (fila.Monto is <= 0)
        {
            return "El monto tiene que ser mayor a cero.";
        }

        if (fila.Monto > MontoMaximo)
        {
            return "El monto no es válido.";
        }

        return fila.Factura != null && fila.Factura.Length > 0 ? ArchivosContrato.ValidarFactura(fila.Factura) : null;
    }

    // Arma las filas de la pantalla a partir de los pendientes de los dos lados (un mes compartido figura una sola vez).
    // lado: quién mira (BD.LadoPropietario | BD.LadoInquilino), para contar quién paga desde su punto de vista.
    // montos: monto de cada gasto ya cargado por (IdPropiedad, IdGasto, mes), para sugerir el del mes anterior.
    public static List<FilaCargarMes> Armar(string lado, IEnumerable<PendienteContrato> pendientes, IReadOnlyDictionary<(int, int, DateTime), decimal> montos) =>
        pendientes
            .GroupBy(p => (p.IdPropiedad, p.IdGasto, p.Periodo))
            .Select(g => g.First())
            .Select(p =>
            {
                bool miraElPropietario = lado == BD.LadoPropietario;
                decimal mio = miraElPropietario ? 100m - p.PorcentajeInquilino : p.PorcentajeInquilino;
                string elOtro = miraElPropietario ? "el inquilino" : "el propietario";
                return new FilaCargarMes
                {
                    IdPropiedad = p.IdPropiedad,
                    NombrePropiedad = p.NombrePropiedad,
                    IdGasto = p.IdGasto,
                    TipoGasto = p.TipoGasto,
                    Periodo = p.Periodo,
                    QuienPaga = mio >= 100m ? "Lo pagás vos"
                        : mio <= 0m ? $"Lo paga {elOtro}"
                        : $"Compartido: {Formato.Numero(mio, "0.##")}% vos",
                    LaPagaElOtro = mio <= 0m,
                    ElOtroPagaAlgo = mio < 100m,
                    ElOtro = elOtro,
                    MontoSugerido = montos.TryGetValue((p.IdPropiedad, p.IdGasto, p.Periodo.AddMonths(-1)), out decimal anterior) ? anterior : null
                };
            })
            .OrderByDescending(f => f.Periodo)
            .ThenBy(f => f.NombrePropiedad)
            .ThenBy(f => f.TipoGasto)
            .ToList();
}
