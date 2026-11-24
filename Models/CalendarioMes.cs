using System.Globalization;

namespace OSO.Models;

// Un día de la grilla del calendario, con sus eventos.
public class DiaCalendario
{
    public DateTime Fecha { get; set; }
    public bool EsHoy { get; set; }
    public List<Evento> Eventos { get; set; } = new();
}

// Todo lo que la vista del calendario necesita para dibujar un mes.
public class CalendarioMes
{
    private static readonly CultureInfo Cultura = new("es-AR");

    public static readonly string[] NombresDias = { "Lun", "Mar", "Mier", "Jue", "Vie", "Sab", "Dom" };

    public DateTime Mes { get; set; }
    public DateTime MesAnterior => Mes.AddMonths(-1);
    public DateTime MesSiguiente => Mes.AddMonths(1);
    public bool SoloLectura { get; set; }

    // Celdas vacías antes del día 1 (la semana empieza el lunes).
    public int CeldasVacias { get; set; }
    public List<DiaCalendario> Dias { get; set; } = new();
    public List<Evento> Proximos { get; set; } = new();

    public CalendarioMes(DateTime mes, List<Evento> eventosDelMes, List<Evento> proximos, bool soloLectura)
    {
        Mes = new DateTime(mes.Year, mes.Month, 1);
        Proximos = proximos;
        SoloLectura = soloLectura;
        CeldasVacias = ((int)Mes.DayOfWeek + 6) % 7;

        int cantidadDias = DateTime.DaysInMonth(Mes.Year, Mes.Month);
        for (int numeroDia = 1; numeroDia <= cantidadDias; numeroDia++)
        {
            DateTime fecha = new(Mes.Year, Mes.Month, numeroDia);
            Dias.Add(new DiaCalendario { Fecha = fecha, EsHoy = fecha == DateTime.Today });
        }

        foreach (Evento evento in eventosDelMes)
        {
            Dias[evento.Fecha.Day - 1].Eventos.Add(evento);
        }
    }

    // Arma el primer día del mes pedido; si el año o el mes no son válidos, usa el mes actual.
    public static DateTime ObtenerPrimerDia(int? anio, int? mes)
    {
        DateTime hoy = DateTime.Today;
        bool fechaValida = anio is >= 2000 and <= 2100 && mes is >= 1 and <= 12;
        return fechaValida ? new DateTime(anio!.Value, mes!.Value, 1) : new DateTime(hoy.Year, hoy.Month, 1);
    }

    public static string NombreDelMes(DateTime fecha)
    {
        return Cultura.TextInfo.ToTitleCase(fecha.ToString("MMMM", Cultura));
    }
}
