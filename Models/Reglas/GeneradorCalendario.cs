namespace OSO.Models;

// Arma los eventos del calendario que salen de un contrato confirmado (función pura).
public static class GeneradorCalendario
{
    public const string VencimientoAlquiler = "VencimientoAlquiler";
    public const string Ajuste = "Ajuste";
    public const string AvisoFinContrato = "AvisoFinContrato";
    public const string FinContrato = "FinContrato";
    public const string DevolucionDeposito = "DevolucionDeposito";
    public const string VencimientoGasto = "VencimientoGasto";

    private static readonly TimeSpan HoraInicio = new(9, 0, 0);
    private static readonly TimeSpan HoraFin = new(10, 0, 0);

    public static List<Evento> EventosDelContrato(Contratos c, int idPropietario, string direccion)
    {
        var eventos = new List<Evento>();
        DateTime inicio = c.FechaInicio.Date;
        DateTime fin = c.FechaFin.Date;

        Evento Nuevo(string tipo, DateTime fecha, string titulo, string descripcion) => new()
        {
            IdPropietario = idPropietario,
            IdPropiedad = c.IdPropiedad,
            IdContrato = c.IdContrato,
            Tipo = tipo,
            Titulo = ArmarTitulo(titulo, direccion),
            Fecha = fecha.Date,
            HoraInicio = HoraInicio,
            HoraFin = HoraFin,
            Ubicacion = direccion.Length > 150 ? direccion[..150] : direccion,
            Invitado = "Inquilino",
            Descripcion = descripcion.Length > 300 ? descripcion[..300] : descripcion
        };

        // Un vencimiento por mes; si el mes no tiene ese día, cae el último día del mes.
        if (c.DiaVencimiento is >= 1 and <= 31)
        {
            for (DateTime mes = new(inicio.Year, inicio.Month, 1); mes <= fin; mes = mes.AddMonths(1))
            {
                int dia = Math.Min(c.DiaVencimiento.Value, DateTime.DaysInMonth(mes.Year, mes.Month));
                DateTime fecha = new(mes.Year, mes.Month, dia);
                if (fecha >= inicio && fecha <= fin)
                {
                    eventos.Add(Nuevo(VencimientoAlquiler, fecha, "Vence el alquiler", "Día de pago del alquiler según el contrato."));
                }
            }
        }

        // Fechas de ajuste: cada FrecuenciaAjusteMeses, hasta el fin del contrato.
        DateTime? ultimo = null;
        for (int i = 0; i < 600; i++)
        {
            DateTime? proxima = CalculadoraAjuste.ProximaFecha(c, ultimo);
            if (proxima == null || proxima.Value > fin)
            {
                break;
            }

            eventos.Add(Nuevo(Ajuste, proxima.Value, $"Ajuste de alquiler ({c.IndiceAjuste})",
                $"Corresponde ajustar el alquiler por {c.IndiceAjuste}. El propietario lo aplica desde la pantalla del contrato."));
            ultimo = proxima;
        }

        foreach (int dias in new[] { 90, 60 })
        {
            DateTime fecha = fin.AddDays(-dias);
            if (fecha >= inicio)
            {
                eventos.Add(Nuevo(AvisoFinContrato, fecha, $"Faltan {dias} días para el fin del contrato", "Es momento de hablar de la renovación o la devolución del inmueble."));
            }
        }

        eventos.Add(Nuevo(FinContrato, fin, "Fin del contrato", "Vence el contrato de alquiler."));

        if (c.MontoDeposito is > 0)
        {
            eventos.Add(Nuevo(DevolucionDeposito, fin, "Devolución del depósito", "Se devuelve el depósito en garantía al finalizar el contrato."));
        }

        return eventos;
    }

    // "{título} – {dirección}", sin pasar de 100 caracteres (el título del evento).
    public static string ArmarTitulo(string titulo, string direccion)
    {
        string completo = $"{titulo} – {direccion}";
        return completo.Length > 100 ? completo[..100] : completo;
    }
}
