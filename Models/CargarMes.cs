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
