namespace OSO.Models;

public class AjusteContrato
{
    public int IdAjuste { get; set; }
    public int IdContrato { get; set; }
    public DateTime Fecha { get; set; }
    public decimal MontoAnterior { get; set; }
    public decimal MontoNuevo { get; set; }
    public decimal? MontoCalculado { get; set; }
    public string Indice { get; set; } = string.Empty;
    public decimal? ValorIndiceDesde { get; set; }
    public decimal? ValorIndiceHasta { get; set; }
    public string Origen { get; set; } = "Calculado"; // Calculado | Manual
}
