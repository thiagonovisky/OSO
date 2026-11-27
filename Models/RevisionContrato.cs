using System.Globalization;

namespace OSO.Models;

// Un campo del formulario de revisión del contrato.
public class CampoRevision
{
    public string Clave { get; set; } = string.Empty; // Nombre de la propiedad de Contratos (y del input)
    public string Etiqueta { get; set; } = string.Empty;
    public string Tipo { get; set; } = "text"; // text | number | date | select | textarea
    public string? Valor { get; set; }
    public string[] Opciones { get; set; } = Array.Empty<string>();
    public string? Paso { get; set; }
    public int? Maximo { get; set; }
    public int? Minimo { get; set; }
    public bool Bloqueado { get; set; }
    public bool Obligatorio { get; set; }
    public bool Dudoso { get; set; }
    public string? Ayuda { get; set; }
}

// Todo lo que necesitan las pantallas de revisión (propietario) y de lectura (inquilino).
public class RevisionContrato
{
    public Contratos Contrato { get; set; } = new();
    public Propiedades Propiedad { get; set; } = new();
    public string NombreInquilino { get; set; } = string.Empty;
    public List<ReglaGasto> Reglas { get; set; } = new();
    public List<string> Errores { get; set; } = new();
    public List<string> Advertencias { get; set; } = new();
    public List<ParteExtraida> Partes { get; set; } = new();
    public bool DesdeIa { get; set; }
    // Se subió un archivo para que lo lea la IA, pero no hay ninguna lectura exitosa: los datos están sin completar.
    public bool LecturaFallida { get; set; }
    // Por qué falló, si quedó registrado. Null si la lectura se cortó antes de terminar (se recargó la página, se reinició la app...).
    public string? MotivoLecturaFallida { get; set; }
    public List<CampoRevision> CamposDudosos { get; set; } = new();
    public List<CampoRevision> Campos { get; set; } = new();

    public static readonly string[] Monedas = { "ARS", "USD" };
    public static readonly string[] Destinos = { "Vivienda", "Comercial", "Temporario" };
    public static readonly string[] Indices = { "IPC", "ICL", "CasaPropia", "Fijo", "Otro" };
    public static readonly string[] Garantias = { "Propietaria", "Caucion", "ReciboSueldo", "Otra" };

    // Todo lo que necesita la pantalla de revisión del propietario: los datos, lo que leyó la IA y qué falta para confirmar.
    public static RevisionContrato ParaRevisar(Contratos contrato, Propiedades propiedad)
    {
        var bd = new BD();
        string? json = bd.ObtenerUltimaExtraccion(contrato.IdContrato);
        Usuario? inquilino = bd.ObtenerUsuarioPorIdInquilino(contrato.IdInquilino);

        var revision = new RevisionContrato
        {
            Contrato = contrato,
            Propiedad = propiedad,
            NombreInquilino = inquilino?.NombreCompleto ?? "Inquilino",
            Reglas = bd.ObtenerReglasGasto(contrato.IdContrato)
        };

        revision.Errores = ReglasContrato.Errores(contrato, revision.Reglas);
        revision.Advertencias = ReglasContrato.Advertencias(contrato, revision.Reglas);
        revision.ArmarCampos(json == null ? null : ResultadoExtraccion.Parsear(json));

        if (json == null && !string.IsNullOrEmpty(contrato.ArchivoContrato))
        {
            revision.LecturaFallida = true;
            revision.MotivoLecturaFallida = bd.ObtenerErrorUltimaExtraccion(contrato.IdContrato);
        }

        return revision;
    }

    // Lo que muestra la pantalla de solo lectura (contrato ya enviado o vigente): los datos y quién paga cada gasto.
    // No lleva errores ni advertencias: ya no se puede editar, no hay nada que corregir.
    public static RevisionContrato ParaLeer(Contratos contrato, Propiedades propiedad)
    {
        return new RevisionContrato
        {
            Contrato = contrato,
            Propiedad = propiedad,
            Reglas = new BD().ObtenerReglasGasto(contrato.IdContrato)
        };
    }

    public static string Fecha(DateTime? f) => f.HasValue && f.Value != default ? f.Value.ToString("yyyy-MM-dd") : string.Empty;
    private static string Num(decimal? n) => n.HasValue ? n.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;

    // Campos editables de la revisión. Con extracción de la IA, los de baja confianza van aparte y primero.
    public void ArmarCampos(ResultadoExtraccion? extraccion)
    {
        Contratos c = Contrato;
        var forzado = ReglasContrato.AjusteForzado(c.RegimenLegal);
        string explicacion = forzado.HasValue
            ? $"Lo fija la ley ({ReglasContrato.NombreRegimen(c.RegimenLegal)}); no se puede cambiar."
            : string.Empty;

        var campos = new List<CampoRevision>
        {
            new() { Clave = "FechaFirma", Etiqueta = "Fecha de firma", Tipo = "date", Valor = Fecha(c.FechaFirma), Obligatorio = true, Ayuda = "De acá sale el régimen legal." },
            new() { Clave = "FechaInicio", Etiqueta = "Inicio del contrato", Tipo = "date", Valor = Fecha(c.FechaInicio), Obligatorio = true },
            new() { Clave = "FechaFin", Etiqueta = "Fin del contrato", Tipo = "date", Valor = Fecha(c.FechaFin), Obligatorio = true },
            new() { Clave = "MontoAlquiler", Etiqueta = "Alquiler mensual inicial", Tipo = "number", Valor = Num(c.MontoAlquiler), Paso = "0.01", Minimo = 0, Obligatorio = true },
            new() { Clave = "Moneda", Etiqueta = "Moneda", Tipo = "select", Valor = c.Moneda, Opciones = Monedas },
            new() { Clave = "Destino", Etiqueta = "Destino", Tipo = "select", Valor = c.Destino, Opciones = Destinos },
            new() { Clave = "IndiceAjuste", Etiqueta = "Índice de ajuste", Tipo = "select", Valor = c.IndiceAjuste, Opciones = Indices, Bloqueado = forzado.HasValue, Ayuda = explicacion },
            new() { Clave = "FrecuenciaAjusteMeses", Etiqueta = "Ajuste cada (meses)", Tipo = "number", Valor = c.FrecuenciaAjusteMeses?.ToString(), Paso = "1", Minimo = 1, Maximo = 60, Bloqueado = forzado.HasValue, Ayuda = explicacion },
            new() { Clave = "FechaPrimerAjuste", Etiqueta = "Fecha del primer ajuste", Tipo = "date", Valor = Fecha(c.FechaPrimerAjuste), Ayuda = "Si la dejás vacía, se cuenta desde el inicio." },
            new() { Clave = "PorcentajeFijo", Etiqueta = "Porcentaje fijo de ajuste (%)", Tipo = "number", Valor = Num(c.PorcentajeFijo), Paso = "0.01", Minimo = 0, Maximo = 100, Ayuda = "Solo si el índice es Fijo." },
            new() { Clave = "DiaVencimiento", Etiqueta = "Día de vencimiento del pago", Tipo = "number", Valor = c.DiaVencimiento?.ToString(), Paso = "1", Minimo = 1, Maximo = 31 },
            new() { Clave = "MedioPago", Etiqueta = "Medio de pago (CBU, alias o domicilio)", Tipo = "text", Valor = c.MedioPago },
            new() { Clave = "PunitorioDiario", Etiqueta = "Punitorio por día de mora (%)", Tipo = "number", Valor = Num(c.PunitorioDiario), Paso = "0.001", Minimo = 0, Maximo = 100 },
            new() { Clave = "MontoDeposito", Etiqueta = "Depósito en garantía", Tipo = "number", Valor = Num(c.MontoDeposito), Paso = "0.01", Minimo = 0 },
            new() { Clave = "MonedaDeposito", Etiqueta = "Moneda del depósito", Tipo = "select", Valor = c.MonedaDeposito, Opciones = Monedas },
            new() { Clave = "TipoGarantia", Etiqueta = "Tipo de garantía", Tipo = "select", Valor = c.TipoGarantia, Opciones = Garantias },
            new() { Clave = "PreavisoRescisionDias", Etiqueta = "Preaviso para rescindir (días)", Tipo = "number", Valor = c.PreavisoRescisionDias?.ToString(), Paso = "1", Minimo = 0, Maximo = 365 },
            new() { Clave = "CondicionesRescision", Etiqueta = "Condiciones de rescisión", Tipo = "textarea", Valor = c.CondicionesRescision }
        };

        DesdeIa = extraccion != null;
        if (extraccion != null)
        {
            Dictionary<string, decimal> confianzas = extraccion.ConfianzaPorCampo();
            foreach (CampoRevision campo in campos)
            {
                campo.Dudoso = confianzas.TryGetValue(campo.Clave, out decimal confianza) && confianza < ReglasContrato.UmbralBajaConfianza;
            }

            Partes = extraccion.Partes;
        }

        CamposDudosos = new List<CampoRevision>();
        Campos = new List<CampoRevision>();
        foreach (CampoRevision campo in campos)
        {
            if (campo.Dudoso)
            {
                CamposDudosos.Add(campo);
            }
            else
            {
                Campos.Add(campo);
            }
        }
    }
}
