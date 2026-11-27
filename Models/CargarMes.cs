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

    // Solo para la pantalla: el número de la fila en el formulario, y si vuelve con error, lo que se había escrito y el error.
    public int Indice { get; set; }
    public FilaEnviada? Enviada { get; set; }
    public string? Error { get; set; }
}

// Las filas de un mismo mes (una tabla de la pantalla).
public class MesCargarMes
{
    public DateTime Periodo { get; set; }
    public List<FilaCargarMes> Filas { get; set; } = new();
}

// Las tablas de la pantalla: los últimos meses abiertos y los anteriores plegados (mismo criterio que "Mis gastos").
public class PantallaCargarMes
{
    public List<MesCargarMes> Recientes { get; set; } = new();
    public List<MesCargarMes> Anteriores { get; set; } = new();
    public int CantidadAnteriores { get; set; }
}

// Cómo terminó el guardado: el error de cada fila que no se guardó (por clave) y el resumen de lo que sí.
public class ResultadoCargaMes
{
    public Dictionary<string, string> Errores { get; set; } = new();
    public string Resumen { get; set; } = string.Empty;
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

    // La misma clave que FilaCargarMes.Clave.
    public string Clave => $"{IdPropiedad}-{IdGasto}-{Periodo:yyyyMM}";
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

    // Guarda las filas completas. Las vacías siguen pendientes; las que tienen error no se guardan;
    // las que la otra parte cargó mientras tanto no se pisan.
    public static ResultadoCargaMes Guardar(string lado, int idPersona, List<FilaEnviada> filas)
    {
        var bd = new BD();
        List<FilaCargarMes> pendientes = bd.ObtenerFilasCargarMes(lado, idPersona);
        var resultado = new ResultadoCargaMes();
        var avisos = new List<string>();
        int guardados = 0;

        foreach (FilaEnviada fila in filas)
        {
            if (!TieneDatos(fila))
            {
                continue;
            }

            string? error = Validar(fila);
            if (error != null)
            {
                resultado.Errores[fila.Clave] = error;
                continue;
            }

            // Un mes incluido en otra factura no lleva factura propia.
            string? factura = !fila.IncluidoEn.HasValue && GestorArchivos.TieneContenido(fila.Factura)
                ? ArchivosContrato.GuardarFactura(fila.IdPropiedad, fila.Factura!)
                : null;

            ResultadoFilaMes guardado = bd.GuardarFilaMes(lado, idPersona, fila, factura);
            if (guardado == ResultadoFilaMes.Guardado)
            {
                guardados++;
                continue;
            }

            ArchivosContrato.BorrarFactura(factura);
            if (guardado == ResultadoFilaMes.YaCargado)
            {
                avisos.Add($"{NombreDeFila(pendientes, fila)} ya estaba cargado.");
            }
            else
            {
                resultado.Errores[fila.Clave] = "Este servicio ya no está pendiente.";
            }
        }

        if (guardados > 0)
        {
            avisos.Insert(0, $"Se cargaron {guardados} servicio{(guardados == 1 ? "" : "s")}.");
        }

        resultado.Resumen = string.Join(" ", avisos);
        return resultado;
    }

    // "Luz de Av. Corrientes 1234 (noviembre 2026)", para avisar que una fila ya la había cargado la otra parte.
    private static string NombreDeFila(List<FilaCargarMes> pendientes, FilaEnviada fila)
    {
        FilaCargarMes? pendiente = pendientes.FirstOrDefault(p => p.Clave == fila.Clave);
        return pendiente != null
            ? $"{pendiente.TipoGasto} de {pendiente.NombrePropiedad} ({Formato.Mes(pendiente.Periodo)})"
            : $"El servicio de {Formato.Mes(fila.Periodo)}";
    }

    // Separa las filas en tablas por mes. Si la pantalla vuelve con errores, cada fila recupera lo que se había escrito.
    public static PantallaCargarMes ArmarPantalla(List<FilaCargarMes> filas, List<FilaEnviada>? enviadas, Dictionary<string, string> errores)
    {
        var enviadasPorClave = new Dictionary<string, FilaEnviada>();
        foreach (FilaEnviada enviada in enviadas ?? new List<FilaEnviada>())
        {
            enviadasPorClave.TryAdd(enviada.Clave, enviada);
        }

        var pantalla = new PantallaCargarMes();
        var mesesPorPeriodo = new Dictionary<DateTime, MesCargarMes>();
        foreach (FilaCargarMes fila in filas)
        {
            fila.Enviada = enviadasPorClave.GetValueOrDefault(fila.Clave);
            fila.Error = errores.GetValueOrDefault(fila.Clave);
            bool esReciente = fila.Periodo >= PendienteContrato.DesdeReciente;

            if (!mesesPorPeriodo.TryGetValue(fila.Periodo, out MesCargarMes? mes))
            {
                mes = new MesCargarMes { Periodo = fila.Periodo };
                mesesPorPeriodo[fila.Periodo] = mes;
                (esReciente ? pantalla.Recientes : pantalla.Anteriores).Add(mes);
            }

            mes.Filas.Add(fila);
        }

        pantalla.CantidadAnteriores = filas.Count(f => f.Periodo < PendienteContrato.DesdeReciente);

        // Cada fila lleva su número en el formulario, en el orden en que se muestra.
        int indice = 0;
        var meses = new List<MesCargarMes>(pantalla.Recientes);
        meses.AddRange(pantalla.Anteriores);
        foreach (MesCargarMes mes in meses)
        {
            foreach (FilaCargarMes fila in mes.Filas)
            {
                fila.Indice = indice++;
            }
        }

        return pantalla;
    }

    // Los servicios distintos que aparecen en las filas (sin repetir, sin importar mayúsculas).
    public static List<string> Servicios(List<FilaCargarMes> filas)
    {
        var servicios = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FilaCargarMes fila in filas)
        {
            if (vistos.Add(fila.TipoGasto))
            {
                servicios.Add(fila.TipoGasto);
            }
        }

        return servicios;
    }

    // Arma las filas de la pantalla a partir de los pendientes de los dos lados (un mes compartido figura una sola vez).
    // lado: quién mira (BD.LadoPropietario | BD.LadoInquilino), para contar quién paga desde su punto de vista.
    // montos: monto de cada gasto ya cargado por (IdPropiedad, IdGasto, mes), para sugerir el del mes anterior.
    public static List<FilaCargarMes> Armar(string lado, List<PendienteContrato> pendientes, Dictionary<(int, int, DateTime), decimal> montos)
    {
        bool miraElPropietario = lado == BD.LadoPropietario;
        string elOtro = miraElPropietario ? "el inquilino" : "el propietario";
        var filas = new List<FilaCargarMes>();
        var agregados = new HashSet<(int, int, DateTime)>();

        foreach (PendienteContrato p in pendientes)
        {
            if (!agregados.Add((p.IdPropiedad, p.IdGasto, p.Periodo)))
            {
                continue;
            }

            decimal mio = miraElPropietario ? 100m - p.PorcentajeInquilino : p.PorcentajeInquilino;
            filas.Add(new FilaCargarMes
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
            });
        }

        // Del mes más nuevo al más viejo; dentro de cada mes, por propiedad y servicio.
        filas.Sort((a, b) =>
        {
            int orden = b.Periodo.CompareTo(a.Periodo);
            if (orden == 0)
            {
                orden = string.Compare(a.NombrePropiedad, b.NombrePropiedad);
            }

            if (orden == 0)
            {
                orden = string.Compare(a.TipoGasto, b.TipoGasto);
            }

            return orden != 0 ? orden : a.IdPropiedad.CompareTo(b.IdPropiedad);
        });
        return filas;
    }
}
