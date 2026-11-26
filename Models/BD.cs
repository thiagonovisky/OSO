namespace OSO.Models;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public class BD
{
    private readonly string connectionString = "Server=localhost;Database=OSO;Integrated Security=true;TrustServerCertificate=True;";

    public void RegistrarUsuario(Usuario usuario, bool esPropietario)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            "sp_RegistrarUsuario",
            new
            {
                Nombre = usuario.Nombre,
                Apellido = usuario.Apellido,
                Correo = usuario.Correo,
                Contrasena = usuario.Contrasena,
                Telefono = usuario.Telefono,
                FechaAlta = usuario.FechaAlta == default ? DateTime.Now : usuario.FechaAlta,
                FotoPerfil = usuario.FotoPerfil,
                esPropietario = esPropietario
            },
            commandType: CommandType.StoredProcedure);
    }

    public Usuario? ObtenerUsuarioPorCorreo(string correo)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE Correo = @pCorreo";
        return connection.QueryFirstOrDefault<Usuario>(query, new { pCorreo = correo });
    }

    public List<Propiedades> ObtenerPropiedadesPorIdPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT p.*,
                                      (SELECT TOP 1 c.MontoAlquiler FROM Contratos c
                                       WHERE c.IdPropiedad = p.IdPropiedad AND c.Estado = 'Confirmado'
                                         AND c.FechaFin >= CAST(GETDATE() AS date)
                                       ORDER BY CASE WHEN c.FechaInicio <= CAST(GETDATE() AS date) THEN 0 ELSE 1 END, c.FechaInicio DESC) AS AlquilerActual
                               FROM Propiedades p
                               WHERE p.IdPropietario = @pIdPropietario";
        return connection.Query<Propiedades>(query, new { pIdPropietario = idPropietario }).AsList();
    }

    public List<Propiedades> ObtenerPropiedadesPorIdInquilino(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT p.*,
                                      contrato.MontoAlquiler AS AlquilerActual,
                                      contrato.FechaFin AS FinContrato,
                                      LTRIM(RTRIM(CONCAT(u.Nombre, ' ', u.Apellido))) AS NombrePropietario,
                                      u.Correo AS CorreoPropietario,
                                      u.Telefono AS TelefonoPropietario
                               FROM Propiedades p
                               LEFT JOIN Usuarios u ON u.IdPropietario = p.IdPropietario
                               OUTER APPLY (SELECT TOP 1 c.MontoAlquiler, c.FechaFin FROM Contratos c
                                            WHERE c.IdPropiedad = p.IdPropiedad AND c.Estado = 'Confirmado'
                                              AND c.FechaFin >= CAST(GETDATE() AS date)
                                            ORDER BY CASE WHEN c.FechaInicio <= CAST(GETDATE() AS date) THEN 0 ELSE 1 END, c.FechaInicio DESC) contrato
                               WHERE p.IdInquilino = @IdInquilino";
        return connection.Query<Propiedades>(query, new { IdInquilino = idInquilino }).AsList();
    }

    public int AgregarPropiedad(Propiedades propiedad)
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<int>(
            @"INSERT INTO Propiedades (Direccion, Superficie, Ambientes, Foto, Estado, IdPropietario, FechaDeAlta)
              VALUES (@Direccion, @Superficie, @Ambientes, @Foto, @Estado, @IdPropietario, @FechaDeAlta);
              SELECT CAST(SCOPE_IDENTITY() AS int);",
            propiedad);
    }

    public Propiedades? ObtenerPropiedadPorId(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT p.*,
                                      (SELECT TOP 1 c.MontoAlquiler FROM Contratos c
                                       WHERE c.IdPropiedad = p.IdPropiedad AND c.Estado = 'Confirmado'
                                         AND c.FechaFin >= CAST(GETDATE() AS date)
                                       ORDER BY CASE WHEN c.FechaInicio <= CAST(GETDATE() AS date) THEN 0 ELSE 1 END, c.FechaInicio DESC) AS AlquilerActual
                               FROM Propiedades p
                               WHERE p.IdPropiedad = @pIdPropiedad";
        return connection.QueryFirstOrDefault<Propiedades>(query, new { pIdPropiedad = idPropiedad });
    }

    // Devuelve la propiedad sólo si pertenece a ese propietario.
    public Propiedades? ObtenerPropiedadDePropietario(int idPropiedad, int idPropietario)
    {
        Propiedades? propiedad = ObtenerPropiedadPorId(idPropiedad);
        return propiedad != null && propiedad.IdPropietario == idPropietario ? propiedad : null;
    }

    public Contratos? ObtenerContratoActivoPorPropiedad(int idPropiedad, DateTime? fechaReferencia = null)
    {
        using SqlConnection connection = new(connectionString);
        var fecha = fechaReferencia ?? DateTime.Today;

        // Igual que la tarjeta de la propiedad: el vigente, o si todavía no empezó, el próximo confirmado.
        const string query = @"SELECT TOP 1 *
                              FROM Contratos
                              WHERE IdPropiedad = @IdPropiedad
                                AND Estado = 'Confirmado'
                                AND FechaFin >= @FechaReferencia
                              ORDER BY CASE WHEN FechaInicio <= @FechaReferencia THEN 0 ELSE 1 END, FechaInicio DESC";

        return connection.QueryFirstOrDefault<Contratos>(query, new
        {
            IdPropiedad = idPropiedad,
            FechaReferencia = fecha.Date
        });
    }

    // ---------------------------------------------------------------------
    // Contratos: Borrador -> PendienteInquilino -> Confirmado
    // ---------------------------------------------------------------------

    // Crea el contrato en Borrador con datos provisorios; se completan en la revisión.
    public int CrearContratoBorrador(int idPropiedad, int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int contratosNoVencidos = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Contratos WHERE IdPropiedad = @IdPropiedad AND FechaFin >= @Hoy AND Estado <> 'Finalizado'",
            new { IdPropiedad = idPropiedad, Hoy = DateTime.Today },
            transaction);

        if (contratosNoVencidos > 0)
        {
            throw new InvalidOperationException("Esta propiedad ya tiene un contrato vigente o en curso.");
        }

        int idContrato = connection.ExecuteScalar<int>(
            @"INSERT INTO Contratos (IdPropiedad, IdInquilino, FechaInicio, FechaFin, MontoAlquiler, Estado)
              VALUES (@IdPropiedad, @IdInquilino, @FechaInicio, @FechaFin, 1, 'Borrador');
              SELECT CAST(SCOPE_IDENTITY() AS int);",
            new
            {
                IdPropiedad = idPropiedad,
                IdInquilino = idInquilino,
                FechaInicio = DateTime.Today,
                FechaFin = DateTime.Today.AddMonths(24)
            },
            transaction);

        transaction.Commit();
        return idContrato;
    }

    public Contratos? ObtenerContratoPorId(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<Contratos>("SELECT * FROM Contratos WHERE IdContrato = @IdContrato", new { IdContrato = idContrato });
    }

    // El contrato Borrador o PendienteInquilino de la propiedad, si hay uno.
    public Contratos? ObtenerContratoEnCursoPorPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<Contratos>(
            @"SELECT TOP 1 * FROM Contratos
              WHERE IdPropiedad = @IdPropiedad AND Estado IN ('Borrador', 'PendienteInquilino')
              ORDER BY IdContrato DESC",
            new { IdPropiedad = idPropiedad });
    }

    // Guarda los datos que se completan en la revisión. Solo mientras el contrato es Borrador.
    public bool GuardarDatosContrato(Contratos c)
    {
        using SqlConnection connection = new(connectionString);
        int filas = connection.Execute(
            @"UPDATE Contratos
              SET FechaInicio = @FechaInicio, FechaFin = @FechaFin, MontoAlquiler = @MontoAlquiler,
                  FechaFirma = @FechaFirma, RegimenLegal = @RegimenLegal, Destino = @Destino, Moneda = @Moneda,
                  IndiceAjuste = @IndiceAjuste, FrecuenciaAjusteMeses = @FrecuenciaAjusteMeses,
                  FechaPrimerAjuste = @FechaPrimerAjuste, PorcentajeFijo = @PorcentajeFijo,
                  DiaVencimiento = @DiaVencimiento, MedioPago = @MedioPago, PunitorioDiario = @PunitorioDiario,
                  MontoDeposito = @MontoDeposito, MonedaDeposito = @MonedaDeposito, TipoGarantia = @TipoGarantia,
                  PreavisoRescisionDias = @PreavisoRescisionDias, CondicionesRescision = @CondicionesRescision
              WHERE IdContrato = @IdContrato AND Estado = 'Borrador'",
            new
            {
                c.IdContrato,
                FechaInicio = c.FechaInicio.Date,
                FechaFin = c.FechaFin.Date,
                c.MontoAlquiler,
                FechaFirma = c.FechaFirma?.Date,
                c.RegimenLegal,
                c.Destino,
                c.Moneda,
                c.IndiceAjuste,
                c.FrecuenciaAjusteMeses,
                FechaPrimerAjuste = c.FechaPrimerAjuste?.Date,
                c.PorcentajeFijo,
                c.DiaVencimiento,
                c.MedioPago,
                c.PunitorioDiario,
                c.MontoDeposito,
                c.MonedaDeposito,
                c.TipoGarantia,
                c.PreavisoRescisionDias,
                c.CondicionesRescision
            });

        return filas > 0;
    }

    // ---- Ajustes del alquiler ----

    // Valor publicado del índice en ese período (IPC y Casa Propia: primer día del mes; ICL: el día exacto).
    public decimal? ObtenerValorIndice(string indice, DateTime periodo)
    {
        using SqlConnection connection = new(connectionString);
        try
        {
            return connection.ExecuteScalar<decimal?>(
                "SELECT Valor FROM IndicesAjuste WHERE Indice = @Indice AND Periodo = @Periodo",
                new { Indice = indice, Periodo = periodo.Date });
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            // La tabla de índices todavía no existe en esta base: se trata como índice sin cargar.
            return null;
        }
    }

    public DateTime? UltimoPeriodoIndice(string indice)
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<DateTime?>("SELECT MAX(Periodo) FROM IndicesAjuste WHERE Indice = @Indice", new { Indice = indice });
    }

    // true si ya hay valores diarios del ICL (no solo los del día 1 de cada mes que carga el script).
    public bool TieneIclDiario()
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM IndicesAjuste WHERE Indice = 'ICL' AND DAY(Periodo) <> 1") > 0;
    }

    // Inserta solo los períodos que todavía no están; nunca pisa un valor cargado. Devuelve cuántos agregó.
    public int InsertarIndicesFaltantes(string indice, IEnumerable<(DateTime Periodo, decimal Valor)> valores, string fuente)
    {
        var filas = valores.Where(v => v.Valor > 0).GroupBy(v => v.Periodo.Date).Select(g => new { p = g.Key.ToString("yyyy-MM-dd"), v = g.First().Valor }).ToList();
        if (filas.Count == 0)
        {
            return 0;
        }

        using SqlConnection connection = new(connectionString);
        return connection.Execute(
            @"INSERT INTO IndicesAjuste (Indice, Periodo, Valor, Fuente)
              SELECT @Indice, j.Periodo, j.Valor, @Fuente
              FROM OPENJSON(@Json) WITH (Periodo date '$.p', Valor decimal(18, 6) '$.v') j
              WHERE NOT EXISTS (SELECT 1 FROM IndicesAjuste i WHERE i.Indice = @Indice AND i.Periodo = j.Periodo)",
            new { Indice = indice, Fuente = fuente, Json = System.Text.Json.JsonSerializer.Serialize(filas) });
    }

    public List<AjusteContrato> ObtenerAjustes(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<AjusteContrato>(
            "SELECT * FROM AjustesContrato WHERE IdContrato = @IdContrato ORDER BY Fecha DESC, IdAjuste DESC",
            new { IdContrato = idContrato }).AsList();
    }

    // Registra el ajuste de la fecha indicada, actualiza el alquiler y avisa al inquilino.
    // Devuelve false si ese ajuste ya estaba aplicado o el contrato no está vigente.
    public bool AplicarAjuste(int idContrato, DateTime fechaAjuste, ResultadoAjuste calculo, decimal montoNuevo, string origen)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        Contratos? contrato = connection.QueryFirstOrDefault<Contratos>(
            "SELECT * FROM Contratos WITH (UPDLOCK) WHERE IdContrato = @IdContrato AND Estado = 'Confirmado'",
            new { IdContrato = idContrato },
            transaction);

        bool yaAplicado = contrato == null || connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM AjustesContrato WHERE IdContrato = @IdContrato AND Fecha = @Fecha",
            new { IdContrato = idContrato, Fecha = fechaAjuste.Date },
            transaction) > 0;

        if (yaAplicado)
        {
            transaction.Rollback();
            return false;
        }

        connection.Execute(
            @"INSERT INTO AjustesContrato (IdContrato, Fecha, MontoAnterior, MontoNuevo, MontoCalculado, Indice, ValorIndiceDesde, ValorIndiceHasta, Origen)
              VALUES (@IdContrato, @Fecha, @MontoAnterior, @MontoNuevo, @MontoCalculado, @Indice, @ValorDesde, @ValorHasta, @Origen)",
            new
            {
                IdContrato = idContrato,
                Fecha = fechaAjuste.Date,
                MontoAnterior = contrato!.MontoAlquiler,
                MontoNuevo = montoNuevo,
                MontoCalculado = calculo.MontoNuevo,
                Indice = contrato.IndiceAjuste ?? "Otro",
                ValorDesde = calculo.ValorDesde,
                ValorHasta = calculo.ValorHasta,
                Origen = origen
            },
            transaction);

        connection.Execute("UPDATE Contratos SET MontoAlquiler = @Monto WHERE IdContrato = @IdContrato",
            new { Monto = montoNuevo, IdContrato = idContrato }, transaction);

        string detalle = calculo.ValorDesde.HasValue && calculo.ValorHasta.HasValue && calculo.ValorDesde.Value != 1m
            ? $" Cálculo: {contrato.IndiceAjuste} {calculo.ValorHasta.Value:0.####} / {calculo.ValorDesde.Value:0.####}."
            : string.Empty;

        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, Texto)
              SELECT u.IdUsuario,
                     LEFT(CONCAT('El alquiler de ', p.Direccion, ' se ajustó el ', CONVERT(varchar(10), @Fecha, 103), ': de $', CONVERT(varchar(20), @Anterior),
                                 ' a $', CONVERT(varchar(20), @Nuevo), '.', @Detalle), 300)
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              INNER JOIN Usuarios u ON u.IdInquilino = c.IdInquilino
              WHERE c.IdContrato = @IdContrato",
            new { IdContrato = idContrato, Fecha = fechaAjuste.Date, Anterior = contrato.MontoAlquiler, Nuevo = montoNuevo, Detalle = detalle },
            transaction);

        transaction.Commit();
        return true;
    }

    // Empieza el aviso que recibe el inquilino cuando el propietario confirma el contrato; la pantalla de avisos lo reconoce
    // para mostrar el botón que lleva directo a ese contrato (no hay otra forma de saber a qué contrato se refiere el aviso).
    public const string TextoContratoCargado = "Tu propietario cargó el contrato de ";

    // Contratos que el propietario ya confirmó y el inquilino todavía tiene que aceptar.
    public List<Contratos> ObtenerContratosPendientesDeInquilino(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<Contratos>(
            @"SELECT c.*, p.Direccion
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              WHERE c.IdInquilino = @IdInquilino AND c.Estado = 'PendienteInquilino'
              ORDER BY c.IdContrato DESC",
            new { IdInquilino = idInquilino }).AsList();
    }

    // Guarda lo que devolvió la IA (o el error) para poder auditar y reprocesar.
    public void GuardarExtraccion(int idContrato, string modelo, string? json, string estado, string? error)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            @"INSERT INTO ExtraccionesContrato (IdContrato, Modelo, ResultadoJson, Estado, Error)
              VALUES (@IdContrato, @Modelo, @Json, @Estado, @Error)",
            new
            {
                IdContrato = idContrato,
                Modelo = modelo.Length > 50 ? modelo[..50] : modelo,
                Json = json,
                Estado = estado,
                Error = error != null && error.Length > 500 ? error[..500] : error
            });
    }

    // JSON de la última extracción exitosa, o null si no hubo.
    public string? ObtenerUltimaExtraccion(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<string?>(
            "SELECT TOP 1 ResultadoJson FROM ExtraccionesContrato WHERE IdContrato = @IdContrato AND Estado = 'Ok' ORDER BY IdExtraccion DESC",
            new { IdContrato = idContrato });
    }

    // Motivo guardado de la última lectura fallida, o null si no hay ninguna registrada.
    public string? ObtenerErrorUltimaExtraccion(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<string?>(
            "SELECT TOP 1 Error FROM ExtraccionesContrato WHERE IdContrato = @IdContrato AND Estado = 'Error' ORDER BY IdExtraccion DESC",
            new { IdContrato = idContrato });
    }

    // Tipos del catálogo (no los que crearon otros propietarios) que la propiedad todavía no tiene: se sugieren al agregar uno.
    public List<string> ObtenerTiposSugeridosParaPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<string>(
            @"SELECT g.TipoGasto FROM Gastos g
              WHERE ISNULL(g.Descripcion, '') <> @DescripcionTipoPropio
                AND NOT EXISTS (SELECT 1 FROM PropiedadTipoGasto pt WHERE pt.IdPropiedad = @IdPropiedad AND pt.IdGasto = g.IdGasto)
              ORDER BY g.TipoGasto",
            new { IdPropiedad = idPropiedad, DescripcionTipoPropio = Gastos.DescripcionTipoPropio }).AsList();
    }

    public List<Gastos> ObtenerCatalogoGastos()
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<Gastos>("SELECT * FROM Gastos ORDER BY IdGasto").AsList();
    }

    // Descarta un contrato que todavía es Borrador, con sus reglas y lecturas de la IA. Devuelve el nombre del archivo
    // subido (para borrarlo del disco) o null si no se descartó nada. Un contrato ya confirmado no se toca.
    public (bool Descartado, string? Archivo) DescartarBorrador(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        string? archivo = connection.QueryFirstOrDefault<string?>(
            "SELECT ISNULL(ArchivoContrato, '') FROM Contratos WITH (UPDLOCK) WHERE IdContrato = @IdContrato AND Estado = 'Borrador'",
            new { IdContrato = idContrato },
            transaction);

        if (archivo == null)
        {
            transaction.Rollback();
            return (false, null);
        }

        var parametros = new { IdContrato = idContrato };
        connection.Execute("DELETE FROM ContratoResponsabilidadGasto WHERE IdContrato = @IdContrato", parametros, transaction);
        connection.Execute("DELETE FROM ExtraccionesContrato WHERE IdContrato = @IdContrato", parametros, transaction);
        connection.Execute("DELETE FROM Contratos WHERE IdContrato = @IdContrato AND Estado = 'Borrador'", parametros, transaction);

        transaction.Commit();
        return (true, archivo.Length == 0 ? null : archivo);
    }

    public void GuardarNombreArchivoContrato(int idContrato, string nombreArchivo)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute("UPDATE Contratos SET ArchivoContrato = @Archivo WHERE IdContrato = @IdContrato",
            new { Archivo = nombreArchivo, IdContrato = idContrato });
    }

    // Todos los tipos de gasto del catálogo; los que el contrato no define salen con el responsable por defecto.
    public List<ReglaGasto> ObtenerReglasGasto(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<ReglaGasto>(
            @"SELECT g.IdGasto, g.TipoGasto,
                     ISNULL(r.Responsable, g.ResponsableDefault) AS Responsable,
                     ISNULL(r.PorcentajeInquilino, CASE g.ResponsableDefault WHEN 'Inquilino' THEN 100 ELSE 0 END) AS PorcentajeInquilino,
                     ISNULL(r.Origen, 'Default') AS Origen,
                     r.TextoClausula, r.Confianza
              FROM Gastos g
              LEFT JOIN ContratoResponsabilidadGasto r ON r.IdGasto = g.IdGasto AND r.IdContrato = @IdContrato
              ORDER BY g.IdGasto",
            new { IdContrato = idContrato }).AsList();
    }

    public void GuardarReglasGasto(int idContrato, IEnumerable<ReglaGasto> reglas)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (ReglaGasto regla in reglas)
        {
            string texto = regla.TextoClausula ?? string.Empty;
            connection.Execute(
                @"MERGE ContratoResponsabilidadGasto AS destino
                  USING (SELECT @IdContrato AS IdContrato, @IdGasto AS IdGasto) AS origen
                     ON destino.IdContrato = origen.IdContrato AND destino.IdGasto = origen.IdGasto
                  WHEN MATCHED THEN UPDATE SET Responsable = @Responsable, PorcentajeInquilino = @Porcentaje,
                       Origen = @Origen, TextoClausula = @TextoClausula, Confianza = @Confianza
                  WHEN NOT MATCHED THEN INSERT (IdContrato, IdGasto, Responsable, PorcentajeInquilino, Origen, TextoClausula, Confianza)
                       VALUES (@IdContrato, @IdGasto, @Responsable, @Porcentaje, @Origen, @TextoClausula, @Confianza);",
                new
                {
                    IdContrato = idContrato,
                    regla.IdGasto,
                    regla.Responsable,
                    Porcentaje = regla.PorcentajeEfectivoInquilino,
                    regla.Origen,
                    TextoClausula = texto.Length == 0 ? null : (texto.Length > 1000 ? texto[..1000] : texto),
                    Confianza = regla.Confianza.HasValue ? Math.Round(Math.Clamp(regla.Confianza.Value, 0m, 1m), 2) : (decimal?)null
                },
                transaction);
        }

        transaction.Commit();
    }

    // El propietario confirma: pasa a PendienteInquilino y se le avisa al inquilino.
    public bool ConfirmarPorPropietario(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int filas = connection.Execute(
            "UPDATE Contratos SET Estado = 'PendienteInquilino', ConfirmadoPorPropietario = 1 WHERE IdContrato = @IdContrato AND Estado = 'Borrador'",
            new { IdContrato = idContrato },
            transaction);

        if (filas > 0)
        {
            connection.Execute(
                @"INSERT INTO Notificaciones (IdUsuario, Texto)
                  SELECT u.IdUsuario,
                         LEFT(CONCAT(@Prefijo, p.Direccion, '. Revisalo y aceptalo para dejarlo vigente.'), 300)
                  FROM Contratos c
                  INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
                  INNER JOIN Usuarios u ON u.IdInquilino = c.IdInquilino
                  WHERE c.IdContrato = @IdContrato",
                new { IdContrato = idContrato, Prefijo = TextoContratoCargado },
                transaction);
        }

        transaction.Commit();
        return filas > 0;
    }

    // El inquilino acepta: el contrato queda Confirmado y se vincula a la propiedad.
    public bool AceptarPorInquilino(int idContrato, int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int filas = connection.Execute(
            @"UPDATE Contratos SET Estado = 'Confirmado', ConfirmadoPorInquilino = 1
              WHERE IdContrato = @IdContrato AND IdInquilino = @IdInquilino AND Estado = 'PendienteInquilino'",
            new { IdContrato = idContrato, IdInquilino = idInquilino },
            transaction);

        if (filas == 0)
        {
            transaction.Rollback();
            return false;
        }

        // Si la propiedad ya la tomó otro inquilino, el contrato no se puede aceptar.
        int vinculadas = connection.Execute(
            @"UPDATE p SET p.IdInquilino = @IdInquilino, p.Estado = 'Ocupada'
              FROM Propiedades p
              INNER JOIN Contratos c ON c.IdPropiedad = p.IdPropiedad
              WHERE c.IdContrato = @IdContrato AND (p.IdInquilino IS NULL OR p.IdInquilino = @IdInquilino)",
            new { IdContrato = idContrato, IdInquilino = idInquilino },
            transaction);

        if (vinculadas == 0)
        {
            transaction.Rollback();
            return false;
        }

        // Con el contrato confirmado se arma el calendario de todo su plazo, en la misma transacción.
        GenerarEventosContrato(connection, transaction, idContrato);

        // Los tipos de gasto con regla en el contrato quedan como servicios de la propiedad (sin tocar los que ya hay).
        connection.Execute(
            @"INSERT INTO PropiedadTipoGasto (IdPropiedad, IdGasto)
              SELECT DISTINCT c.IdPropiedad, r.IdGasto
              FROM Contratos c
              INNER JOIN ContratoResponsabilidadGasto r ON r.IdContrato = c.IdContrato
              WHERE c.IdContrato = @IdContrato
                AND NOT EXISTS (SELECT 1 FROM PropiedadTipoGasto pt WHERE pt.IdPropiedad = c.IdPropiedad AND pt.IdGasto = r.IdGasto)",
            new { IdContrato = idContrato },
            transaction);

        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, Texto)
              SELECT u.IdUsuario, LEFT(CONCAT('El inquilino aceptó el contrato de ', p.Direccion, '. Ya está vigente.'), 300)
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              INNER JOIN Usuarios u ON u.IdPropietario = p.IdPropietario
              WHERE c.IdContrato = @IdContrato",
            new { IdContrato = idContrato },
            transaction);

        transaction.Commit();
        return true;
    }

    // El propietario ve el contrato en cualquier estado; el inquilino, recién cuando se le pide aceptarlo.
    public bool EsParteDelContrato(int idContrato, int? idPropietario, int? idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<int>(
            @"SELECT COUNT(*)
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              WHERE c.IdContrato = @IdContrato
                AND (p.IdPropietario = @IdPropietario
                     OR (c.IdInquilino = @IdInquilino AND c.Estado IN ('PendienteInquilino', 'Confirmado', 'Finalizado')))",
            new { IdContrato = idContrato, IdPropietario = idPropietario, IdInquilino = idInquilino }) > 0;
    }


    public List<Usuario> ObtenerInquilinosDisponibles()
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE IdInquilino IS NOT NULL ORDER BY Apellido, Nombre";
        return connection.Query<Usuario>(query).AsList();
    }

    public void EditarPropiedad(Propiedades propiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"UPDATE Propiedades
                               SET Direccion = @Direccion,
                                   Superficie = @Superficie,
                                   Ambientes = @Ambientes,
                                   Foto = @Foto
                               WHERE IdPropiedad = @IdPropiedad AND IdPropietario = @IdPropietario";

        connection.Execute(query, propiedad);
    }

    // ---------------------------------------------------------------------
    // Gastos y expensas
    // Un gasto (GastosPropiedad) cuelga directo de la propiedad. Quien paga se
    // define en GastoResponsables (por ahora solo el propietario) y los pagos
    // viven en Pagos. El estado se deduce de los pagos, no se guarda.
    // ---------------------------------------------------------------------

    private const string TipoExpensas = "Expensas";

    // Un gasto con la parte de un responsable (Propietario | Inquilino) y lo que ya pagó.
    // Siempre trae también cuánto le toca pagar al inquilino y cuánto pagó, para deducir el estado.
    private static string SelectGastoDe(string responsable) => $@"
        SELECT gp.*, g.TipoGasto, p.Direccion AS NombrePropiedad,
               gr.IdGastoResponsable, gr.Monto AS MontoResponsable,
               ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0) AS MontoPagado,
               (SELECT MAX(pg.Fecha) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable) AS FechaPago,
               (SELECT TOP 1 pg.Comprobante FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable ORDER BY pg.Fecha DESC, pg.IdPago DESC) AS Comprobante,
               ISNULL(gi.Monto, 0) AS MontoInquilino,
               ISNULL(gpr.Monto, 0) AS MontoPropietario,
               ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gi.IdGastoResponsable), 0) AS PagadoInquilino
        FROM GastosPropiedad gp
        INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
        INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
        INNER JOIN GastoResponsables gr ON gr.IdGastoPropiedad = gp.IdGastoPropiedad AND gr.Responsable = '{responsable}'
        LEFT JOIN GastoResponsables gi ON gi.IdGastoPropiedad = gp.IdGastoPropiedad AND gi.Responsable = 'Inquilino'
        LEFT JOIN GastoResponsables gpr ON gpr.IdGastoPropiedad = gp.IdGastoPropiedad AND gpr.Responsable = 'Propietario'";

    // Un gasto es del inquilino solo si su período cae dentro de un contrato Confirmado suyo en esa propiedad
    // (un inquilino nuevo no ve los gastos del anterior). Usa los alias gp y el parámetro @IdInquilino.
    private const string ContratoConfirmadoDelInquilino = @"EXISTS (SELECT 1 FROM Contratos ci
                  WHERE ci.IdPropiedad = gp.IdPropiedad AND ci.IdInquilino = @IdInquilino AND ci.Estado IN ('Confirmado', 'Finalizado')
                    AND ci.FechaInicio <= EOMONTH(gp.Periodo) AND ISNULL(ci.FechaFinReal, ci.FechaFin) >= gp.Periodo)";

    private static readonly string SelectGastoPropietario = SelectGastoDe("Propietario");
    private static readonly string SelectGastoInquilino = SelectGastoDe("Inquilino");

    // Días de anticipación con los que se avisa al inquilino que un gasto está por vencer.
    private const int DiasAvisoVencimiento = 3;

    private static DateTime PrimerDiaDelMes(DateTime fecha) => new(fecha.Year, fecha.Month, 1);

    // Regla vigente para un tipo de gasto en un período: la del contrato Confirmado que cubre ese mes.
    // Sin contrato devuelve null (el gasto va todo al propietario). Con contrato pero sin regla propia, se usa el responsable por defecto del catálogo.
    private static ReglaGasto? ObtenerReglaVigente(SqlConnection connection, SqlTransaction? transaction, int idPropiedad, int idGasto, DateTime periodo)
    {
        DateTime primerDia = PrimerDiaDelMes(periodo);
        return connection.QueryFirstOrDefault<ReglaGasto>(
            @"SELECT TOP 1 g.IdGasto, g.TipoGasto,
                     ISNULL(r.Responsable, g.ResponsableDefault) AS Responsable,
                     ISNULL(r.PorcentajeInquilino, CASE g.ResponsableDefault WHEN 'Inquilino' THEN 100 ELSE 0 END) AS PorcentajeInquilino,
                     ISNULL(r.Origen, 'Default') AS Origen,
                     r.TextoClausula, r.Confianza
              FROM Contratos c
              INNER JOIN Gastos g ON g.IdGasto = @IdGasto
              LEFT JOIN ContratoResponsabilidadGasto r ON r.IdContrato = c.IdContrato AND r.IdGasto = g.IdGasto
              WHERE c.IdPropiedad = @IdPropiedad AND c.Estado IN ('Confirmado', 'Finalizado')
                AND c.FechaInicio <= EOMONTH(@Periodo) AND ISNULL(c.FechaFinReal, c.FechaFin) >= @Periodo
              ORDER BY c.FechaInicio DESC",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto, Periodo = primerDia },
            transaction);
    }

    public ReglaGasto? ObtenerReglaVigente(int idPropiedad, int idGasto, DateTime periodo)
    {
        using SqlConnection connection = new(connectionString);
        return ObtenerReglaVigente(connection, null, idPropiedad, idGasto, periodo);
    }

    // Crea o actualiza el gasto del período y lo reparte. Si se indica un responsable (Propietario | Inquilino) el total
    // es todo de él (Origen 'Manual'); si no, se reparte según el contrato vigente (Origen 'Regla') o va todo al propietario.
    private static int GuardarGastoPropiedad(SqlConnection connection, SqlTransaction transaction, int idPropiedad, int idGasto, GastoPropiedad gasto, string? responsableManual = null)
    {
        DateTime periodo = PrimerDiaDelMes(gasto.Periodo);

        connection.Execute(
            "IF NOT EXISTS (SELECT 1 FROM PropiedadTipoGasto WHERE IdPropiedad = @IdPropiedad AND IdGasto = @IdGasto) INSERT INTO PropiedadTipoGasto (IdPropiedad, IdGasto) VALUES (@IdPropiedad, @IdGasto)",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto },
            transaction);

        int? existente = connection.QuerySingleOrDefault<int?>(
            "SELECT IdGastoPropiedad FROM GastosPropiedad WHERE IdPropiedad = @IdPropiedad AND IdGasto = @IdGasto AND Periodo = @Periodo",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto, Periodo = periodo },
            transaction);

        int idGastoPropiedad;
        if (existente.HasValue)
        {
            idGastoPropiedad = existente.Value;
            connection.Execute(
                @"UPDATE GastosPropiedad
                  SET Monto = @Monto, FechaVencimiento = @FechaVencimiento, FechaAlta = @FechaAlta, Aclaracion = @Aclaracion
                  WHERE IdGastoPropiedad = @IdGastoPropiedad",
                new { gasto.Monto, gasto.FechaVencimiento, gasto.FechaAlta, gasto.Aclaracion, IdGastoPropiedad = idGastoPropiedad },
                transaction);
        }
        else
        {
            idGastoPropiedad = connection.ExecuteScalar<int>(
                @"INSERT INTO GastosPropiedad (IdPropiedad, IdGasto, Periodo, Monto, FechaVencimiento, FechaAlta, Aclaracion)
                  VALUES (@IdPropiedad, @IdGasto, @Periodo, @Monto, @FechaVencimiento, @FechaAlta, @Aclaracion);
                  SELECT CAST(SCOPE_IDENTITY() AS int);",
                new { IdPropiedad = idPropiedad, IdGasto = idGasto, Periodo = periodo, gasto.Monto, gasto.FechaVencimiento, gasto.FechaAlta, gasto.Aclaracion },
                transaction);
        }

        // Quién paga y cuánto: elección manual, regla del contrato o todo el propietario.
        decimal porcentajeInquilino = 0m;
        string origen = "Manual";
        string? justificacion = null;
        if (responsableManual is "Inquilino" or "Propietario")
        {
            porcentajeInquilino = responsableManual == "Inquilino" ? 100m : 0m;
        }
        else
        {
            ReglaGasto? regla = ObtenerReglaVigente(connection, transaction, idPropiedad, idGasto, periodo);
            if (regla != null)
            {
                porcentajeInquilino = regla.PorcentajeEfectivoInquilino;
                origen = "Regla";
                justificacion = string.IsNullOrWhiteSpace(regla.TextoClausula) ? "Según el responsable habitual de este tipo de gasto." : regla.TextoClausula;
                justificacion = justificacion.Length > 500 ? justificacion[..500] : justificacion;
            }
        }

        List<ParteGasto> partes = RepartoGasto.Repartir(gasto.Monto, porcentajeInquilino);
        ParteGasto parteInquilino = partes.First(p => p.Responsable == "Inquilino");
        ParteGasto partePropietario = partes.First(p => p.Responsable == "Propietario");

        connection.Execute(
            @"MERGE GastoResponsables AS destino
              USING (SELECT @IdGastoPropiedad AS IdGastoPropiedad) AS origen
                 ON destino.IdGastoPropiedad = origen.IdGastoPropiedad AND destino.Responsable = 'Propietario'
              WHEN MATCHED THEN UPDATE SET Monto = @Monto, Porcentaje = @Porcentaje, Origen = @Origen, Justificacion = @Justificacion
              WHEN NOT MATCHED THEN INSERT (IdGastoPropiedad, Responsable, Monto, Porcentaje, Origen, Justificacion)
                   VALUES (@IdGastoPropiedad, 'Propietario', @Monto, @Porcentaje, @Origen, @Justificacion);",
            new { IdGastoPropiedad = idGastoPropiedad, partePropietario.Monto, Porcentaje = partePropietario.Porcentaje, Origen = origen, Justificacion = justificacion },
            transaction);

        if (parteInquilino.Monto > 0)
        {
            connection.Execute(
                @"MERGE GastoResponsables AS destino
                  USING (SELECT @IdGastoPropiedad AS IdGastoPropiedad) AS origen
                     ON destino.IdGastoPropiedad = origen.IdGastoPropiedad AND destino.Responsable = 'Inquilino'
                  WHEN MATCHED THEN UPDATE SET Monto = @Monto, Porcentaje = @Porcentaje, Origen = @Origen, Justificacion = @Justificacion
                  WHEN NOT MATCHED THEN INSERT (IdGastoPropiedad, Responsable, Monto, Porcentaje, Origen, Justificacion)
                       VALUES (@IdGastoPropiedad, 'Inquilino', @Monto, @Porcentaje, @Origen, @Justificacion);",
                new { IdGastoPropiedad = idGastoPropiedad, parteInquilino.Monto, Porcentaje = parteInquilino.Porcentaje, Origen = origen, Justificacion = justificacion },
                transaction);
        }
        else
        {
            // Todo es del propietario: se descarta la parte del inquilino salvo que ya haya pagado algo.
            connection.Execute(
                @"DELETE n FROM Notificaciones n
                  INNER JOIN GastoResponsables gr ON gr.IdGastoResponsable = n.IdGastoResponsable
                  WHERE gr.IdGastoPropiedad = @IdGastoPropiedad AND gr.Responsable = 'Inquilino'
                    AND NOT EXISTS (SELECT 1 FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable);
                  DELETE gr FROM GastoResponsables gr
                  WHERE gr.IdGastoPropiedad = @IdGastoPropiedad AND gr.Responsable = 'Inquilino'
                    AND NOT EXISTS (SELECT 1 FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable);",
                new { IdGastoPropiedad = idGastoPropiedad },
                transaction);
        }

        GuardarEventoVencimientoGasto(connection, transaction, idGastoPropiedad, gasto, parteInquilino.Monto > 0);
        return idGastoPropiedad;
    }

    // Registra un pago por lo que falta de la parte de un responsable. Devuelve false si no corresponde.
    private static bool PagarParte(SqlConnection connection, SqlTransaction transaction, int idGastoResponsable, string responsable, string? comprobante)
    {
        int filas = connection.Execute(
            @"INSERT INTO Pagos (IdGastoResponsable, Fecha, Monto, Concepto, Comprobante)
              SELECT gr.IdGastoResponsable, @Fecha,
                     gr.Monto - ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0),
                     LEFT(g.TipoGasto, 50), @Comprobante
              FROM GastoResponsables gr
              INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
              INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
              WHERE gr.IdGastoResponsable = @IdGastoResponsable
                AND gr.Responsable = @Responsable
                AND gr.Monto > ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0)",
            new { IdGastoResponsable = idGastoResponsable, Responsable = responsable, Fecha = DateTime.Today, Comprobante = comprobante },
            transaction);

        return filas > 0;
    }

    // Una expensa trae dos importes: ordinarias y extraordinarias. Cada una es un gasto con su propio reparto.
    public void AgregarExpensa(GastoPropiedad expensa, int idPropiedad, decimal montoOrdinarias, decimal montoExtraordinarias)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        expensa.Periodo = PrimerDiaDelMes(expensa.FechaVencimiento);
        var importes = new (string Tipo, string Descripcion, decimal Monto)[]
        {
            (TipoExpensas, "Expensas ordinarias", montoOrdinarias),
            (ReglasContrato.TipoExtraordinarias, "Expensas extraordinarias del edificio", montoExtraordinarias)
        };

        foreach (var (tipo, descripcion, monto) in importes)
        {
            if (monto <= 0)
            {
                continue;
            }

            int idGasto = ObtenerOCrearTipoGasto(connection, transaction, tipo, descripcion);
            var gasto = new GastoPropiedad
            {
                Periodo = expensa.Periodo,
                Monto = monto,
                FechaVencimiento = expensa.FechaVencimiento,
                FechaAlta = expensa.FechaAlta,
                Aclaracion = expensa.Aclaracion
            };

            int idGastoPropiedad = GuardarGastoPropiedad(connection, transaction, idPropiedad, idGasto, gasto);
            AvisarGastoNuevoAlInquilino(connection, transaction, idGastoPropiedad);
        }

        transaction.Commit();
    }

    public List<GastoPropiedad> ObtenerExpensasPorIdPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoPropietario + @"
            WHERE p.IdPropietario = @IdPropietario AND g.TipoGasto = @TipoExtraordinarias
            ORDER BY gp.FechaAlta DESC, gp.IdGastoPropiedad DESC";

        return connection.Query<GastoPropiedad>(query, new { IdPropietario = idPropietario, TipoGasto = TipoExpensas, TipoExtraordinarias = ReglasContrato.TipoExtraordinarias }).AsList();
    }

    // Paga la parte del propietario de un gasto (expensa), validando que la propiedad sea suya.
    // El propietario paga su parte de un gasto de una de sus propiedades (con comprobante).
    public bool PagarParteDelPropietario(int idGastoResponsable, int idPropietario, string comprobante)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        bool esDelPropietario = connection.ExecuteScalar<int>(
            @"SELECT COUNT(*)
              FROM GastoResponsables gr
              INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              WHERE gr.IdGastoResponsable = @IdGastoResponsable AND p.IdPropietario = @IdPropietario",
            new { IdGastoResponsable = idGastoResponsable, IdPropietario = idPropietario },
            transaction) > 0;

        bool pagado = esDelPropietario && PagarParte(connection, transaction, idGastoResponsable, "Propietario", comprobante);
        transaction.Commit();
        return pagado;
    }

    // ---- Gastos del inquilino y notificaciones ----

    public List<GastoPropiedad> ObtenerGastosPorIdInquilino(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoInquilino + @"
            WHERE " + ContratoConfirmadoDelInquilino + @"
            ORDER BY CASE WHEN gr.Monto > ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0) THEN 0 ELSE 1 END,
                     gp.FechaVencimiento DESC, gp.IdGastoPropiedad DESC";

        return connection.Query<GastoPropiedad>(query, new { IdInquilino = idInquilino }).AsList();
    }

    public bool InquilinoTieneContratoConfirmado(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Contratos WHERE IdInquilino = @IdInquilino AND Estado IN ('Confirmado', 'Finalizado')", new { IdInquilino = idInquilino }) > 0;
    }

    // El inquilino paga lo que le toca de un gasto de su propiedad subiendo el comprobante.
    public bool PagarGastoInquilino(int idGastoResponsable, int idInquilino, string comprobante)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        bool esDelInquilino = connection.ExecuteScalar<int>(
            @"SELECT COUNT(*)
              FROM GastoResponsables gr
              INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              WHERE gr.IdGastoResponsable = @IdGastoResponsable AND gr.Responsable = 'Inquilino' AND " + ContratoConfirmadoDelInquilino,
            new { IdGastoResponsable = idGastoResponsable, IdInquilino = idInquilino },
            transaction) > 0;

        bool pagado = esDelInquilino && PagarParte(connection, transaction, idGastoResponsable, "Inquilino", comprobante);

        if (pagado)
        {
            AvisarPagoAlPropietario(connection, transaction, idGastoResponsable);
        }

        transaction.Commit();
        return pagado;
    }

    // ---- Pendientes automáticos del contrato ----
    // Cada servicio mensual (ReglasContrato.TiposMensuales) que el contrato pone a cargo de alguien figura como pendiente
    // de esa persona en cada mes del contrato (hasta el actual) mientras no haya un gasto cargado para ese mes.
    // Lado "Inquilino": lo que paga todo o en parte el inquilino. Lado "Propietario": lo que paga todo o en parte el propietario.
    // Quien paga primero carga el monto de la factura; si es compartido, al otro le queda su parte como gasto a pagar.
    // Los gastos que no son mensuales (extraordinarias, reparaciones, seguro...) los carga el propietario cuando ocurren.

    public const string LadoInquilino = "Inquilino";
    public const string LadoPropietario = "Propietario";
    private const int DiasParaPagarLaOtraParte = 10;

    private class ReglaPendienteContrato
    {
        public int IdPropiedad { get; set; }
        public string Direccion { get; set; } = string.Empty;
        public int IdGasto { get; set; }
        public string TipoGasto { get; set; } = string.Empty;
        public string Responsable { get; set; } = "Inquilino";
        public decimal PorcentajeInquilino { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
    }

    // filtro: condición extra sobre el contrato (alias c) o la propiedad (alias p), con sus parámetros.
    private static List<PendienteContrato> CalcularPendientesContrato(SqlConnection connection, SqlTransaction? transaction, string lado, string filtro, object parametros)
    {
        // Ojo: en las reglas "Inquilino" el porcentaje guardado puede ser 0 (solo importa si es Compartido).
        string reglaDelLado = lado == LadoPropietario
            ? "(r.Responsable = 'Propietario' OR (r.Responsable = 'Compartido' AND r.PorcentajeInquilino < 100))"
            : "(r.Responsable = 'Inquilino' OR (r.Responsable = 'Compartido' AND r.PorcentajeInquilino > 0))";

        var parametrosReglas = new DynamicParameters(parametros);
        parametrosReglas.Add("TiposMensuales", ReglasContrato.TiposMensuales);

        List<ReglaPendienteContrato> reglas = connection.Query<ReglaPendienteContrato>(
            @"SELECT c.IdPropiedad, p.Direccion, r.IdGasto, g.TipoGasto, r.Responsable, r.PorcentajeInquilino,
                     c.FechaInicio, ISNULL(c.FechaFinReal, c.FechaFin) AS FechaFin
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              INNER JOIN ContratoResponsabilidadGasto r ON r.IdContrato = c.IdContrato
              INNER JOIN Gastos g ON g.IdGasto = r.IdGasto
              WHERE c.Estado = 'Confirmado' AND g.TipoGasto IN @TiposMensuales AND " + reglaDelLado + " AND " + filtro,
            parametrosReglas,
            transaction).AsList();

        var pendientes = new List<PendienteContrato>();
        if (reglas.Count == 0)
        {
            return pendientes;
        }

        // Meses que ya tienen un gasto cargado (por cualquiera de los dos): ahí cada uno ve su parte como gasto a pagar.
        var cargados = connection.Query<(int IdPropiedad, int IdGasto, DateTime Periodo)>(
            "SELECT IdPropiedad, IdGasto, Periodo FROM GastosPropiedad WHERE IdPropiedad IN @Ids",
            new { Ids = reglas.Select(r => r.IdPropiedad).Distinct().ToList() },
            transaction).Select(x => (x.IdPropiedad, x.IdGasto, PrimerDiaDelMes(x.Periodo))).ToHashSet();

        DateTime mesActual = PrimerDiaDelMes(DateTime.Today);
        foreach (ReglaPendienteContrato regla in reglas)
        {
            DateTime ultimo = PrimerDiaDelMes(regla.FechaFin) < mesActual ? PrimerDiaDelMes(regla.FechaFin) : mesActual;
            for (DateTime mes = PrimerDiaDelMes(regla.FechaInicio); mes <= ultimo; mes = mes.AddMonths(1))
            {
                if (!cargados.Contains((regla.IdPropiedad, regla.IdGasto, mes)))
                {
                    pendientes.Add(new PendienteContrato
                    {
                        IdPropiedad = regla.IdPropiedad,
                        NombrePropiedad = regla.Direccion,
                        IdGasto = regla.IdGasto,
                        TipoGasto = regla.TipoGasto,
                        Responsable = regla.Responsable,
                        PorcentajeInquilino = regla.Responsable switch { "Inquilino" => 100m, "Propietario" => 0m, _ => regla.PorcentajeInquilino },
                        Lado = lado,
                        Periodo = mes
                    });
                }
            }
        }

        return pendientes
            .OrderByDescending(x => x.Periodo)
            .ThenBy(x => x.NombrePropiedad)
            .ThenBy(x => x.TipoGasto)
            .ToList();
    }

    public List<PendienteContrato> ObtenerPendientesContratoInquilino(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        return CalcularPendientesContrato(connection, null, LadoInquilino, "c.IdInquilino = @IdInquilino", new { IdInquilino = idInquilino });
    }

    public List<PendienteContrato> ObtenerPendientesContratoPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        return CalcularPendientesContrato(connection, null, LadoPropietario, "p.IdPropietario = @IdPropietario", new { IdPropietario = idPropietario });
    }

    // Los meses sin cargar de una propiedad, de los dos lados (un mes compartido figura una sola vez).
    public List<PendienteContrato> ObtenerPendientesContratoPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        return CalcularPendientesDePropiedad(connection, idPropiedad);
    }

    // Filas de la pantalla "Cargar el mes": lo que falta cargar en todas las propiedades del propietario, de los dos lados,
    // con el monto del mes anterior como sugerencia (los meses "incluidos", de monto 0, no sirven de sugerencia).
    // lado: quién carga (LadoPropietario: sus propiedades; LadoInquilino: las de sus contratos confirmados).
    public List<FilaCargarMes> ObtenerFilasCargarMes(string lado, int idPersona)
    {
        using SqlConnection connection = new(connectionString);
        bool esInquilino = lado == LadoInquilino;
        var parametros = new { IdPersona = idPersona };
        string filtro = esInquilino ? "c.IdInquilino = @IdPersona" : "p.IdPropietario = @IdPersona";
        List<PendienteContrato> pendientes = CalcularPendientesContrato(connection, null, LadoInquilino, filtro, parametros)
            .Concat(CalcularPendientesContrato(connection, null, LadoPropietario, filtro, parametros))
            .ToList();

        // Montos del mes anterior para sugerir. Al inquilino solo los de meses de su contrato (no los del inquilino anterior).
        string query = esInquilino
            ? @"SELECT gp.IdPropiedad, gp.IdGasto, gp.Periodo, gp.Monto
                FROM GastosPropiedad gp
                WHERE gp.Monto > 0 AND " + ContratoConfirmadoDelInquilino.Replace("@IdInquilino", "@IdPersona")
            : @"SELECT gp.IdPropiedad, gp.IdGasto, gp.Periodo, gp.Monto
                FROM GastosPropiedad gp
                INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
                WHERE p.IdPropietario = @IdPersona AND gp.Monto > 0";
        Dictionary<(int, int, DateTime), decimal> montos = connection.Query<(int IdPropiedad, int IdGasto, DateTime Periodo, decimal Monto)>(query, parametros)
            .GroupBy(x => (x.IdPropiedad, x.IdGasto, PrimerDiaDelMes(x.Periodo)))
            .ToDictionary(g => g.Key, g => g.First().Monto);

        return FilasCargarMes.Armar(lado, pendientes, montos);
    }

    // Guarda una fila de "Cargar el mes" (lado: quién carga). Nunca pisa un mes que ya tiene gasto (por ejemplo, si la otra
    // parte lo cargó mientras tanto): el chequeo y el alta van en la misma transacción, con el mes bloqueado hasta terminar.
    // factura: nombre del archivo ya guardado, o null.
    public ResultadoFilaMes GuardarFilaMes(string lado, int idPersona, FilaEnviada fila, string? factura)
    {
        if (lado != LadoInquilino && lado != LadoPropietario)
        {
            return ResultadoFilaMes.NoCorresponde;
        }

        bool esInquilino = lado == LadoInquilino;
        DateTime mes = PrimerDiaDelMes(fila.Periodo);

        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        // Solo se carga un mes pendiente de un contrato confirmado de quien carga (su propiedad, o su alquiler).
        var parametros = new { IdPersona = idPersona, fila.IdPropiedad };
        string filtro = (esInquilino ? "c.IdInquilino = @IdPersona" : "p.IdPropietario = @IdPersona") + " AND c.IdPropiedad = @IdPropiedad";

        bool esParte = connection.ExecuteScalar<int>(
            @"SELECT COUNT(1) FROM Contratos c INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              WHERE c.Estado = 'Confirmado' AND " + filtro,
            parametros, transaction) > 0;
        if (!esParte)
        {
            return ResultadoFilaMes.NoCorresponde;
        }

        bool yaCargado = connection.ExecuteScalar<int>(
            "SELECT COUNT(1) FROM GastosPropiedad WITH (UPDLOCK, HOLDLOCK) WHERE IdPropiedad = @IdPropiedad AND IdGasto = @IdGasto AND Periodo = @Periodo",
            new { fila.IdPropiedad, fila.IdGasto, Periodo = mes }, transaction) > 0;
        if (yaCargado)
        {
            return ResultadoFilaMes.YaCargado;
        }

        bool esPendiente = CalcularPendientesContrato(connection, transaction, LadoInquilino, filtro, parametros)
            .Concat(CalcularPendientesContrato(connection, transaction, LadoPropietario, filtro, parametros))
            .Any(p => p.IdGasto == fila.IdGasto && p.Periodo == mes);
        if (!esPendiente)
        {
            return ResultadoFilaMes.NoCorresponde;
        }

        var gasto = fila.IncluidoEn.HasValue
            ? new GastoPropiedad
            {
                Periodo = mes,
                Monto = 0,
                FechaVencimiento = mes,
                FechaAlta = DateTime.Today,
                Aclaracion = GastoPropiedad.AclaracionIncluido(fila.IncluidoEn.Value)
            }
            : new GastoPropiedad
            {
                Periodo = mes,
                Monto = fila.Monto ?? 0,
                FechaVencimiento = fila.Vencimiento ?? DateTime.Today.AddDays(GastoPropiedad.DiasVencimientoPropuesto),
                FechaAlta = DateTime.Today,
                // Lo que carga el inquilino queda marcado para que el propietario lo revise (y para mostrar quién lo cargó).
                Aclaracion = esInquilino ? GastoPropiedad.AclaracionCargaInquilino : null
            };

        int idGastoPropiedad = GuardarGastoPropiedad(connection, transaction, fila.IdPropiedad, fila.IdGasto, gasto);
        GuardarFacturaDeGasto(connection, transaction, idGastoPropiedad, factura);

        if (esInquilino)
        {
            AvisarGastoCargadoAlPropietario(connection, transaction, idGastoPropiedad);
        }
        else
        {
            AvisarGastoNuevoAlInquilino(connection, transaction, idGastoPropiedad);
        }

        transaction.Commit();
        return ResultadoFilaMes.Guardado;
    }

    private static void GuardarFacturaDeGasto(SqlConnection connection, SqlTransaction transaction, int idGastoPropiedad, string? factura)
    {
        if (factura == null)
        {
            return;
        }

        connection.Execute(
            "UPDATE GastosPropiedad SET Factura = @Factura WHERE IdGastoPropiedad = @IdGastoPropiedad",
            new { Factura = factura, IdGastoPropiedad = idGastoPropiedad },
            transaction);
    }

    // La factura de un gasto, solo para el propietario de la propiedad o el inquilino del contrato que cubre ese mes.
    public string? ObtenerFacturaDeGasto(int idGastoPropiedad, int? idPropietario, int? idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<string?>(
            @"SELECT gp.Factura
              FROM GastosPropiedad gp
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              WHERE gp.IdGastoPropiedad = @IdGastoPropiedad
                AND ((@IdPropietario IS NOT NULL AND p.IdPropietario = @IdPropietario)
                     OR (@IdInquilino IS NOT NULL AND " + ContratoConfirmadoDelInquilino + "))",
            new { IdGastoPropiedad = idGastoPropiedad, IdPropietario = idPropietario, IdInquilino = idInquilino });
    }

    // Avisa al propietario que el inquilino cargó la factura de un servicio, para que revise el monto.
    private static void AvisarGastoCargadoAlPropietario(SqlConnection connection, SqlTransaction transaction, int idGastoPropiedad)
    {
        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, IdGastoResponsable, Texto)
              SELECT u.IdUsuario, gr.IdGastoResponsable,
                     LEFT(CONCAT('El inquilino cargó la factura de ', g.TipoGasto, ' de ', p.Direccion, ' por $', CONVERT(varchar(20), gp.Monto),
                                 CASE WHEN gr.Monto > 0 THEN CONCAT('. Tu parte es $', CONVERT(varchar(20), gr.Monto), ', vence el ', CONVERT(varchar(10), gp.FechaVencimiento, 103)) ELSE '' END,
                                 '. Podés revisar el monto en los servicios de la propiedad.'), 300)
              FROM GastoResponsables gr
              INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
              INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              INNER JOIN Usuarios u ON u.IdPropietario = p.IdPropietario
              WHERE gp.IdGastoPropiedad = @IdGastoPropiedad AND gr.Responsable = 'Propietario' AND gp.Monto > 0
                AND NOT EXISTS (SELECT 1 FROM Notificaciones n WHERE n.IdUsuario = u.IdUsuario AND n.IdGastoResponsable = gr.IdGastoResponsable)",
            new { IdGastoPropiedad = idGastoPropiedad },
            transaction);
    }

    private static List<PendienteContrato> CalcularPendientesDePropiedad(SqlConnection connection, int idPropiedad)
    {
        var parametros = new { IdPropiedad = idPropiedad };
        return CalcularPendientesContrato(connection, null, LadoInquilino, "c.IdPropiedad = @IdPropiedad", parametros)
            .Concat(CalcularPendientesContrato(connection, null, LadoPropietario, "c.IdPropiedad = @IdPropiedad", parametros))
            .GroupBy(x => (x.IdGasto, x.Periodo))
            .Select(g => g.First())
            .OrderByDescending(x => x.Periodo)
            .ThenBy(x => x.TipoGasto)
            .ToList();
    }

    // El gasto ya cargado de un tipo en un mes (para corregirlo), con la parte del propietario; null si no existe.
    public GastoPropiedad? ObtenerGastoDePropiedadPorPeriodo(int idPropiedad, int idGasto, DateTime periodo)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<GastoPropiedad>(
            SelectGastoPropietario + " WHERE gp.IdPropiedad = @IdPropiedad AND gp.IdGasto = @IdGasto AND gp.Periodo = @Periodo",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto, Periodo = PrimerDiaDelMes(periodo) });
    }

    // Alguien (inquilino o propietario, según lado) paga un pendiente del contrato: carga el monto de la factura, el comprobante
    // y, si la adjuntó, la factura (nombre ya guardado).
    // Se crea el gasto del mes, se reparte según el contrato y se registra el pago de la parte de quien paga.
    // Si es compartido, al otro le queda su parte como gasto a pagar (y se le avisa).
    public bool PagarPendienteContrato(string lado, int idPersona, int idPropiedad, int idGasto, DateTime periodo, decimal monto, string comprobante, string? factura = null)
    {
        if (monto <= 0 || (lado != LadoInquilino && lado != LadoPropietario))
        {
            return false;
        }

        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        bool esInquilino = lado == LadoInquilino;
        DateTime mes = PrimerDiaDelMes(periodo);
        string filtro = (esInquilino ? "c.IdInquilino = @IdPersona" : "p.IdPropietario = @IdPersona") + " AND c.IdPropiedad = @IdPropiedad";
        bool corresponde = CalcularPendientesContrato(connection, transaction, lado, filtro, new { IdPersona = idPersona, IdPropiedad = idPropiedad })
            .Any(x => x.IdGasto == idGasto && x.Periodo == mes);

        if (!corresponde)
        {
            return false;
        }

        var gasto = new GastoPropiedad
        {
            Periodo = mes,
            Monto = monto,
            // Si es compartido, el otro tiene este plazo para pagar su parte (para quien carga, ya queda pagado).
            FechaVencimiento = DateTime.Today.AddDays(DiasParaPagarLaOtraParte),
            FechaAlta = DateTime.Today,
            // Lo que declara el inquilino queda marcado para que el propietario lo revise.
            Aclaracion = esInquilino ? GastoPropiedad.AclaracionCargaInquilino : null
        };
        int idGastoPropiedad = GuardarGastoPropiedad(connection, transaction, idPropiedad, idGasto, gasto);
        GuardarFacturaDeGasto(connection, transaction, idGastoPropiedad, factura);

        int? idGastoResponsable = connection.QuerySingleOrDefault<int?>(
            "SELECT IdGastoResponsable FROM GastoResponsables WHERE IdGastoPropiedad = @IdGastoPropiedad AND Responsable = @Responsable",
            new { IdGastoPropiedad = idGastoPropiedad, Responsable = lado },
            transaction);

        if (!idGastoResponsable.HasValue || !PagarParte(connection, transaction, idGastoResponsable.Value, lado, comprobante))
        {
            return false;
        }

        if (esInquilino)
        {
            AvisarPagoAlPropietario(connection, transaction, idGastoResponsable.Value);
        }
        else
        {
            AvisarGastoNuevoAlInquilino(connection, transaction, idGastoPropiedad);
        }

        transaction.Commit();
        return true;
    }

    // Recordatorio de una vez por mes: qué servicios del mes tiene que cargar y pagar la persona (según el contrato).
    public const string TextoRecordatorioFacturas = "Recordatorio del mes: ";

    public void GenerarRecordatorioFacturas(int idUsuario, int? idInquilino, int? idPropietario)
    {
        if (!idInquilino.HasValue && !idPropietario.HasValue)
        {
            return;
        }

        using SqlConnection connection = new(connectionString);
        DateTime mes = PrimerDiaDelMes(DateTime.Today);

        bool yaAvisado = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM Notificaciones WHERE IdUsuario = @IdUsuario AND Texto LIKE @Prefijo AND FechaCreacion >= @Mes",
            new { IdUsuario = idUsuario, Prefijo = TextoRecordatorioFacturas + "%", Mes = mes }) > 0;
        if (yaAvisado)
        {
            return;
        }

        List<PendienteContrato> delMes = (idInquilino.HasValue
                ? CalcularPendientesContrato(connection, null, LadoInquilino, "c.IdInquilino = @Id", new { Id = idInquilino.Value })
                : CalcularPendientesContrato(connection, null, LadoPropietario, "p.IdPropietario = @Id", new { Id = idPropietario!.Value }))
            .Where(x => x.Periodo == mes)
            .ToList();
        if (delMes.Count == 0)
        {
            return;
        }

        string detalle = string.Join("; ", delMes
            .GroupBy(x => x.NombrePropiedad)
            .Select(g => $"{string.Join(", ", g.Select(x => x.TipoGasto).Distinct())} de {g.Key}"));
        string texto = $"{TextoRecordatorioFacturas}este mes tenés para cargar y pagar {detalle}. Subí la factura en Mis gastos.";

        connection.Execute(
            "INSERT INTO Notificaciones (IdUsuario, Texto) VALUES (@IdUsuario, @Texto)",
            new { IdUsuario = idUsuario, Texto = texto.Length > 300 ? texto[..297] + "..." : texto });
    }

    // Los gastos con parte del propietario (de todas sus propiedades): lo que tiene que pagar y lo que ya pagó.
    public List<GastoPropiedad> ObtenerGastosPorIdPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoPropietario + @"
            WHERE p.IdPropietario = @IdPropietario AND gr.Monto > 0
            ORDER BY CASE WHEN gr.Monto > ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0) THEN 0 ELSE 1 END,
                     gp.FechaVencimiento DESC, gp.IdGastoPropiedad DESC";

        return connection.Query<GastoPropiedad>(query, new { IdPropietario = idPropietario }).AsList();
    }

    // Avisa al propietario que el inquilino pagó su parte de un gasto y subió el comprobante (una sola vez por gasto).
    private static void AvisarPagoAlPropietario(SqlConnection connection, SqlTransaction transaction, int idGastoResponsable)
    {
        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, IdGastoResponsable, Texto)
              SELECT u.IdUsuario, gr.IdGastoResponsable,
                     LEFT(CONCAT('El inquilino pagó ', g.TipoGasto, ' de ', p.Direccion, ' ($', CONVERT(varchar(20), gr.Monto),
                                 ') y subió el comprobante.'), 300)
              FROM GastoResponsables gr
              INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
              INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              INNER JOIN Usuarios u ON u.IdPropietario = p.IdPropietario
              WHERE gr.IdGastoResponsable = @IdGastoResponsable
                AND NOT EXISTS (SELECT 1 FROM Notificaciones n WHERE n.IdUsuario = u.IdUsuario AND n.IdGastoResponsable = gr.IdGastoResponsable)",
            new { IdGastoResponsable = idGastoResponsable },
            transaction);
    }

    // Avisa al inquilino de los gastos suyos sin pagar que vencen en los próximos días (o ya vencieron).
    // Se avisa una sola vez por gasto.
    public void GenerarNotificacionesVencimiento(int idUsuario, int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"
            INSERT INTO Notificaciones (IdUsuario, IdGastoResponsable, Texto)
            SELECT @IdUsuario, gr.IdGastoResponsable,
                   LEFT(CONCAT(g.TipoGasto, ' de ', p.Direccion, CASE WHEN gp.FechaVencimiento < @Hoy THEN ' venció el ' ELSE ' vence el ' END,
                               CONVERT(varchar(10), gp.FechaVencimiento, 103), ' ($', CONVERT(varchar(20), gr.Monto - pag.Pagado),
                               '). Subí el comprobante para marcarlo como pagado.'), 300)
            FROM GastoResponsables gr
            INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
            INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
            INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
            CROSS APPLY (SELECT ISNULL(SUM(pg.Monto), 0) AS Pagado FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable) pag
            WHERE gr.Responsable = 'Inquilino' AND " + ContratoConfirmadoDelInquilino + @"
              AND gr.Monto > pag.Pagado
              AND gp.FechaVencimiento <= DATEADD(day, @Dias, @Hoy)
              AND NOT EXISTS (SELECT 1 FROM Notificaciones n WHERE n.IdUsuario = @IdUsuario AND n.IdGastoResponsable = gr.IdGastoResponsable)";

        try
        {
            connection.Execute(query, new { IdUsuario = idUsuario, IdInquilino = idInquilino, Hoy = DateTime.Today, Dias = DiasAvisoVencimiento });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Otra solicitud simultánea ya creó el aviso.
        }
    }

    // Avisa de los eventos que arma el contrato (vencimiento del alquiler 3 días antes, ajuste el día que corresponde,
    // avisos y fin de contrato en su fecha). Sirve para propietario e inquilino y no duplica: lo evita UQ_Notificaciones_Evento.
    public void GenerarNotificacionesEventos(int idUsuario, int? idPropietario, int? idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        DateTime hoy = DateTime.Today;

        var eventos = connection.Query<Evento>(
            @"SELECT e.IdEvento, e.Tipo, e.Fecha, e.Titulo, e.IdContrato, e.IdPropiedad
              FROM Eventos e
              INNER JOIN Propiedades p ON p.IdPropiedad = e.IdPropiedad
              WHERE e.Tipo IN ('VencimientoAlquiler', 'Ajuste', 'AvisoFinContrato', 'FinContrato', 'DevolucionDeposito')
                AND ((@IdPropietario IS NOT NULL AND e.IdPropietario = @IdPropietario)
                     OR (@IdInquilino IS NOT NULL AND p.IdInquilino = @IdInquilino AND e.Invitado = 'Inquilino'))
                AND ((e.Tipo = 'VencimientoAlquiler' AND e.Fecha BETWEEN @Hoy AND DATEADD(day, @Dias, @Hoy))
                  OR (e.Tipo = 'Ajuste' AND e.Fecha BETWEEN DATEADD(day, -7, @Hoy) AND @Hoy)
                  OR (e.Tipo IN ('AvisoFinContrato', 'FinContrato', 'DevolucionDeposito') AND e.Fecha BETWEEN DATEADD(day, -3, @Hoy) AND @Hoy))
                AND NOT EXISTS (SELECT 1 FROM Notificaciones n WHERE n.IdUsuario = @IdUsuario AND n.IdEvento = e.IdEvento)",
            new { IdUsuario = idUsuario, IdPropietario = idPropietario, IdInquilino = idInquilino, Hoy = hoy, Dias = DiasAvisoVencimiento }).AsList();

        foreach (Evento evento in eventos)
        {
            string texto = $"{evento.Titulo}: {evento.Fecha:dd/MM/yyyy}.";

            if (evento.Tipo == GeneradorCalendario.Ajuste)
            {
                string? detalle = DetalleAvisoAjuste(evento);
                if (detalle == null)
                {
                    continue; // ya se aplicó o el contrato no está vigente
                }

                texto = $"{evento.Titulo}: {evento.Fecha:dd/MM/yyyy}. {detalle}";
            }

            try
            {
                connection.Execute(
                    "INSERT INTO Notificaciones (IdUsuario, IdEvento, Texto) VALUES (@IdUsuario, @IdEvento, @Texto)",
                    new { IdUsuario = idUsuario, evento.IdEvento, Texto = texto.Length > 300 ? texto[..300] : texto });
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // Otra solicitud simultánea ya creó el aviso.
            }
        }
    }

    // Monto calculado del ajuste (o qué falta) para el aviso; null si ese ajuste ya se aplicó.
    private string? DetalleAvisoAjuste(Evento evento)
    {
        Contratos? contrato = evento.IdContrato.HasValue ? ObtenerContratoPorId(evento.IdContrato.Value) : null;
        if (contrato == null || contrato.Estado != "Confirmado" || string.IsNullOrEmpty(contrato.IndiceAjuste))
        {
            return null;
        }

        List<AjusteContrato> ajustes = ObtenerAjustes(contrato.IdContrato);
        if (ajustes.Any(a => a.Fecha.Date == evento.Fecha.Date))
        {
            return null;
        }

        DateTime desde = ajustes.Count > 0 ? ajustes.Max(a => a.Fecha) : contrato.FechaInicio;
        ResultadoAjuste calculo = CalculadoraAjuste.Calcular(contrato.IndiceAjuste, contrato.MontoAlquiler, desde, evento.Fecha,
            contrato.PorcentajeFijo, ObtenerValorIndice);

        return calculo.MontoNuevo.HasValue
            ? $"Monto calculado: ${calculo.MontoNuevo.Value:0.00}."
            : $"{calculo.Faltante}.";
    }

    // Reemplaza los eventos que arma el contrato (los manuales no se tocan). Es idempotente.
    private static void GenerarEventosContrato(SqlConnection connection, SqlTransaction transaction, int idContrato)
    {
        Contratos? contrato = connection.QueryFirstOrDefault<Contratos>("SELECT * FROM Contratos WHERE IdContrato = @IdContrato", new { IdContrato = idContrato }, transaction);
        Propiedades? propiedad = contrato == null ? null : connection.QueryFirstOrDefault<Propiedades>("SELECT * FROM Propiedades WHERE IdPropiedad = @IdPropiedad", new { contrato.IdPropiedad }, transaction);
        if (contrato == null || propiedad == null || propiedad.IdPropietario <= 0)
        {
            return;
        }

        connection.Execute(
            @"DELETE n FROM Notificaciones n INNER JOIN Eventos e ON e.IdEvento = n.IdEvento WHERE e.IdContrato = @IdContrato AND e.Tipo <> 'Manual';
              DELETE FROM Eventos WHERE IdContrato = @IdContrato AND Tipo <> 'Manual';",
            new { IdContrato = idContrato },
            transaction);

        connection.Execute(
            @"INSERT INTO Eventos (IdPropietario, IdPropiedad, Titulo, Fecha, HoraInicio, HoraFin, Ubicacion, Invitado, Descripcion, IdContrato, Tipo)
              VALUES (@IdPropietario, @IdPropiedad, @Titulo, @Fecha, @HoraInicio, @HoraFin, @Ubicacion, @Invitado, @Descripcion, @IdContrato, @Tipo)",
            GeneradorCalendario.EventosDelContrato(contrato, propiedad.IdPropietario, propiedad.Direccion),
            transaction);
    }

    // Un evento de vencimiento por gasto: se crea la primera vez y después se actualiza.
    private static void GuardarEventoVencimientoGasto(SqlConnection connection, SqlTransaction transaction, int idGastoPropiedad, GastoPropiedad gasto, bool loPagaElInquilino)
    {
        var datos = connection.QueryFirstOrDefault<(int? IdPropietario, int IdPropiedad, string Direccion, string TipoGasto)>(
            @"SELECT p.IdPropietario, p.IdPropiedad, p.Direccion, g.TipoGasto
              FROM GastosPropiedad gp
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
              WHERE gp.IdGastoPropiedad = @IdGastoPropiedad",
            new { IdGastoPropiedad = idGastoPropiedad },
            transaction);

        if (datos.IdPropietario == null)
        {
            return;
        }

        if (!gasto.TieneVencimiento)
        {
            connection.Execute("DELETE FROM Eventos WHERE IdGastoPropiedad = @IdGastoPropiedad AND Tipo = 'VencimientoGasto'",
                new { IdGastoPropiedad = idGastoPropiedad }, transaction);
            return;
        }

        string titulo = GeneradorCalendario.ArmarTitulo($"Vence {datos.TipoGasto}", datos.Direccion);
        string descripcion = $"Gasto de {gasto.Periodo:MM/yyyy}: ${gasto.Monto:0.00}.";

        connection.Execute(
            @"UPDATE Eventos SET Titulo = @Titulo, Fecha = @Fecha, Invitado = @Invitado, Descripcion = @Descripcion
              WHERE IdGastoPropiedad = @IdGastoPropiedad AND Tipo = 'VencimientoGasto';
              IF @@ROWCOUNT = 0
                  INSERT INTO Eventos (IdPropietario, IdPropiedad, Titulo, Fecha, HoraInicio, HoraFin, Ubicacion, Invitado, Descripcion, Tipo, IdGastoPropiedad)
                  VALUES (@IdPropietario, @IdPropiedad, @Titulo, @Fecha, '09:00', '10:00', @Direccion, @Invitado, @Descripcion, 'VencimientoGasto', @IdGastoPropiedad);",
            new
            {
                IdGastoPropiedad = idGastoPropiedad,
                datos.IdPropietario,
                datos.IdPropiedad,
                Direccion = datos.Direccion.Length > 150 ? datos.Direccion[..150] : datos.Direccion,
                Titulo = titulo,
                Fecha = gasto.FechaVencimiento.Date,
                Invitado = loPagaElInquilino ? "Inquilino" : "No",
                Descripcion = descripcion
            },
            transaction);
    }

    public int ContarNotificacionesNoLeidas(int idUsuario)
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Notificaciones WHERE IdUsuario = @IdUsuario AND Leida = 0", new { IdUsuario = idUsuario });
    }

    public List<Notificacion> ObtenerNotificaciones(int idUsuario)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<Notificacion>("SELECT * FROM Notificaciones WHERE IdUsuario = @IdUsuario ORDER BY FechaCreacion DESC, IdNotificacion DESC", new { IdUsuario = idUsuario }).AsList();
    }

    public void MarcarNotificacionesComoLeidas(int idUsuario)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute("UPDATE Notificaciones SET Leida = 1 WHERE IdUsuario = @IdUsuario AND Leida = 0", new { IdUsuario = idUsuario });
    }

    private static int ObtenerOCrearTipoGasto(SqlConnection connection, SqlTransaction? transaction, string nombreTipo, string descripcion)
    {
        string nombre = nombreTipo.Trim();
        int? existente = connection.QueryFirstOrDefault<int?>(
            "SELECT TOP 1 IdGasto FROM Gastos WHERE TipoGasto = @TipoGasto",
            new { TipoGasto = nombre },
            transaction);

        if (existente.HasValue)
        {
            return existente.Value;
        }

        return connection.ExecuteScalar<int>(
            @"INSERT INTO Gastos (TipoGasto, Descripcion)
              VALUES (@TipoGasto, @Descripcion);
              SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { TipoGasto = nombre, Descripcion = descripcion },
            transaction);
    }

    public int CrearTipoGasto(string nombreTipo)
    {
        using SqlConnection connection = new(connectionString);
        return ObtenerOCrearTipoGasto(connection, null, nombreTipo, Gastos.DescripcionTipoPropio);
    }

    public Gastos? ObtenerTipoGastoPorId(int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Gastos WHERE IdGasto = @IdGasto";
        return connection.QueryFirstOrDefault<Gastos>(query, new { IdGasto = idGasto });
    }

    // Crea (o actualiza) el gasto del mes. Si se indica que está pagado, registra el pago.
    // responsableManual: "Propietario" o "Inquilino" para que todo sea de uno; null para repartir según el contrato.
    public void AgregarGasto(int idPropiedad, int idGasto, GastoPropiedad gasto, bool pagado, string? comprobante, string? responsableManual = null)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int idGastoPropiedad = GuardarGastoPropiedad(connection, transaction, idPropiedad, idGasto, gasto, responsableManual);
        FijarEstadoPagado(connection, transaction, idGastoPropiedad, pagado, comprobante);

        // El aviso depende de la parte del inquilino (solo si le queda algo sin pagar), no de que el propietario haya pagado la suya:
        // así también se entera si el propietario corrige hacia arriba el monto que él había declarado.
        AvisarGastoNuevoAlInquilino(connection, transaction, idGastoPropiedad);

        transaction.Commit();
    }

    // Avisa al inquilino apenas se le asigna un gasto, salvo que ya tenga un aviso de ese gasto.
    private static void AvisarGastoNuevoAlInquilino(SqlConnection connection, SqlTransaction transaction, int idGastoPropiedad)
    {
        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, IdGastoResponsable, Texto)
              SELECT u.IdUsuario, gr.IdGastoResponsable,
                     LEFT(CONCAT('Nuevo gasto: ', g.TipoGasto, ' de ', p.Direccion, ' por $', CONVERT(varchar(20), gr.Monto - ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0)),
                                 ', vence el ', CONVERT(varchar(10), gp.FechaVencimiento, 103),
                                 '. Subí el comprobante para marcarlo como pagado.'), 300)
              FROM GastoResponsables gr
              INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
              INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
              INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
              INNER JOIN Contratos c ON c.IdPropiedad = gp.IdPropiedad AND c.Estado IN ('Confirmado', 'Finalizado')
                    AND c.FechaInicio <= EOMONTH(gp.Periodo) AND ISNULL(c.FechaFinReal, c.FechaFin) >= gp.Periodo
              INNER JOIN Usuarios u ON u.IdInquilino = c.IdInquilino
              WHERE gp.IdGastoPropiedad = @IdGastoPropiedad AND gr.Responsable = 'Inquilino'
                AND gr.Monto > ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0)
                AND NOT EXISTS (SELECT 1 FROM Notificaciones n WHERE n.IdUsuario = u.IdUsuario AND n.IdGastoResponsable = gr.IdGastoResponsable)",
            new { IdGastoPropiedad = idGastoPropiedad },
            transaction);
    }

    public List<GastoPropiedad> ObtenerGastosPorServicioYTipo(int idPropiedad, string tipoGasto)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoPropietario + @"
            WHERE gp.IdPropiedad = @IdPropiedad AND g.TipoGasto = @TipoGasto
            ORDER BY gp.Periodo DESC";

        return connection.Query<GastoPropiedad>(query, new { IdPropiedad = idPropiedad, TipoGasto = tipoGasto }).AsList();
    }

    // Tipos de gasto de la propiedad, cada uno con cuántos gastos tiene y cuántos faltan pagar.
    public List<ServicioPropiedad> ObtenerServiciosDePropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        string queryGastos = SelectGastoPropietario + " WHERE gp.IdPropiedad = @IdPropiedad";
        List<GastoPropiedad> gastos = connection.Query<GastoPropiedad>(queryGastos, new { IdPropiedad = idPropiedad }).AsList();

        // Quién paga cada tipo según el contrato Confirmado; sin contrato o sin regla propia no se muestra nada.
        var reglas = new Dictionary<int, ReglaGasto>();
        Contratos? contrato = ObtenerContratoConfirmadoDePropiedad(idPropiedad);
        if (contrato != null)
        {
            var conRegla = connection.Query<int>(
                "SELECT IdGasto FROM ContratoResponsabilidadGasto WHERE IdContrato = @IdContrato",
                new { contrato.IdContrato }).ToHashSet();
            foreach (ReglaGasto regla in ObtenerReglasGasto(contrato.IdContrato))
            {
                if (conRegla.Contains(regla.IdGasto))
                {
                    reglas[regla.IdGasto] = regla;
                }
            }
        }

        // Meses del contrato que todavía nadie cargó (de los servicios mensuales, de cualquiera de los dos lados).
        List<PendienteContrato> pendientesContrato = CalcularPendientesDePropiedad(connection, idPropiedad);

        var servicios = new List<ServicioPropiedad>();
        foreach (Gastos tipo in ObtenerTiposGastoPorPropiedad(idPropiedad))
        {
            int sinCargar = pendientesContrato.Count(x => x.IdGasto == tipo.IdGasto);
            var servicio = new ServicioPropiedad
            {
                IdGasto = tipo.IdGasto,
                Nombre = tipo.TipoGasto,
                CantidadGastos = sinCargar,
                GastosPendientes = sinCargar
            };
            if (reglas.TryGetValue(tipo.IdGasto, out ReglaGasto? reglaTipo))
            {
                servicio.QuienPaga = reglaTipo.Responsable switch
                {
                    "Inquilino" => "Lo paga el inquilino",
                    "Propietario" => "Lo paga el propietario",
                    _ => $"Compartido ({reglaTipo.PorcentajeInquilino:0.##}% inquilino)"
                };
            }

            foreach (GastoPropiedad gasto in gastos)
            {
                if (gasto.IdGasto == tipo.IdGasto)
                {
                    servicio.CantidadGastos++;
                    if (gasto.Estado != "Pagado")
                    {
                        servicio.GastosPendientes++;
                    }
                }
            }

            servicios.Add(servicio);
        }

        return servicios;
    }

    // Pagado: registra el pago de lo que falta. A pagar: anula los pagos de la parte del propietario.
    public void ActualizarEstadoGasto(int idPropiedad, int idGastoPropiedad, bool pagado)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        bool perteneceAPropiedad = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM GastosPropiedad WHERE IdGastoPropiedad = @IdGastoPropiedad AND IdPropiedad = @IdPropiedad",
            new { IdGastoPropiedad = idGastoPropiedad, IdPropiedad = idPropiedad },
            transaction) > 0;

        if (perteneceAPropiedad)
        {
            FijarEstadoPagado(connection, transaction, idGastoPropiedad, pagado, null);
        }

        transaction.Commit();
    }

    private static void FijarEstadoPagado(SqlConnection connection, SqlTransaction transaction, int idGastoPropiedad, bool pagado, string? comprobante)
    {
        int idGastoResponsable = connection.ExecuteScalar<int>(
            "SELECT IdGastoResponsable FROM GastoResponsables WHERE IdGastoPropiedad = @IdGastoPropiedad AND Responsable = 'Propietario'",
            new { IdGastoPropiedad = idGastoPropiedad },
            transaction);

        if (pagado)
        {
            PagarParte(connection, transaction, idGastoResponsable, "Propietario", string.IsNullOrWhiteSpace(comprobante) ? null : comprobante);
        }
        else
        {
            connection.Execute("DELETE FROM Pagos WHERE IdGastoResponsable = @IdGastoResponsable", new { IdGastoResponsable = idGastoResponsable }, transaction);
        }
    }

    public void EliminarPropiedad(int idPropiedad, int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var propiedad = connection.QueryFirstOrDefault<Propiedades>(
            "SELECT * FROM Propiedades WHERE IdPropiedad = @IdPropiedad AND IdPropietario = @IdPropietario",
            new { IdPropiedad = idPropiedad, IdPropietario = idPropietario },
            transaction: transaction);

        if (propiedad == null)
        {
            transaction.Rollback();
            return;
        }

        BorrarGastosDePropiedad(connection, transaction, idPropiedad, null);

        connection.Execute(
            "DELETE FROM PropiedadTipoGasto WHERE IdPropiedad = @IdPropiedad",
            new { IdPropiedad = idPropiedad },
            transaction: transaction);

        connection.Execute(
            "DELETE FROM Propiedades WHERE IdPropiedad = @IdPropiedad AND IdPropietario = @IdPropietario",
            new { IdPropiedad = idPropiedad, IdPropietario = idPropietario },
            transaction: transaction);

        transaction.Commit();
    }

    // Borra pagos, responsables y gastos de la propiedad (de un tipo, o de todos si idGasto es null).
    private static void BorrarGastosDePropiedad(SqlConnection connection, SqlTransaction transaction, int idPropiedad, int? idGasto)
    {
        var parametros = new { IdPropiedad = idPropiedad, IdGasto = idGasto };
        const string filtro = "gp.IdPropiedad = @IdPropiedad AND (@IdGasto IS NULL OR gp.IdGasto = @IdGasto)";

        // Los vencimientos de esos gastos en el calendario (y sus avisos) se van con ellos.
        connection.Execute(
            $@"DELETE n FROM Notificaciones n
               INNER JOIN Eventos e ON e.IdEvento = n.IdEvento
               INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = e.IdGastoPropiedad
               WHERE {filtro}", parametros, transaction);

        connection.Execute(
            $@"DELETE e FROM Eventos e
               INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = e.IdGastoPropiedad
               WHERE {filtro}", parametros, transaction);

        connection.Execute(
            $@"DELETE pg FROM Pagos pg
               INNER JOIN GastoResponsables gr ON gr.IdGastoResponsable = pg.IdGastoResponsable
               INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
               WHERE {filtro}", parametros, transaction);

        connection.Execute(
            $@"DELETE n FROM Notificaciones n
               INNER JOIN GastoResponsables gr ON gr.IdGastoResponsable = n.IdGastoResponsable
               INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
               WHERE {filtro}", parametros, transaction);

        connection.Execute(
            $@"DELETE gr FROM GastoResponsables gr
               INNER JOIN GastosPropiedad gp ON gp.IdGastoPropiedad = gr.IdGastoPropiedad
               WHERE {filtro}", parametros, transaction);

        connection.Execute($"DELETE gp FROM GastosPropiedad gp WHERE {filtro}", parametros, transaction);
    }

    public List<Gastos> ObtenerTiposGastoPorPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT g.*
                              FROM Gastos g
                              INNER JOIN PropiedadTipoGasto ptg ON ptg.IdGasto = g.IdGasto
                              WHERE ptg.IdPropiedad = @IdPropiedad
                              ORDER BY g.TipoGasto";

        return connection.Query<Gastos>(query, new { IdPropiedad = idPropiedad }).AsList();
    }

    public Gastos? ObtenerTipoGastoDePropiedad(int idPropiedad, string nombreTipo)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT g.*
                              FROM Gastos g
                              INNER JOIN PropiedadTipoGasto ptg ON ptg.IdGasto = g.IdGasto
                              WHERE ptg.IdPropiedad = @IdPropiedad AND g.TipoGasto = @TipoGasto";

        return connection.QueryFirstOrDefault<Gastos>(query, new { IdPropiedad = idPropiedad, TipoGasto = nombreTipo.Trim() });
    }

    public bool PropiedadTieneTipoGasto(int idPropiedad, int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        return connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM PropiedadTipoGasto WHERE IdPropiedad = @IdPropiedad AND IdGasto = @IdGasto",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto }) > 0;
    }

    public void RegistrarTipoGastoEnPropiedad(int idPropiedad, int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            @"IF NOT EXISTS (SELECT 1 FROM PropiedadTipoGasto WHERE IdPropiedad = @IdPropiedad AND IdGasto = @IdGasto)
                  INSERT INTO PropiedadTipoGasto (IdPropiedad, IdGasto) VALUES (@IdPropiedad, @IdGasto)",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto });
    }

    public void EliminarTipoGastoDePropiedad(int idPropiedad, int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        BorrarGastosDePropiedad(connection, transaction, idPropiedad, idGasto);

        connection.Execute(
            "DELETE FROM PropiedadTipoGasto WHERE IdPropiedad = @IdPropiedad AND IdGasto = @IdGasto",
            new { IdPropiedad = idPropiedad, IdGasto = idGasto },
            transaction: transaction);

        transaction.Commit();
    }

    public Usuario? ObtenerUsuarioPorId(int idUsuario)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE IdUsuario = @pIdUsuario";
        return connection.QueryFirstOrDefault<Usuario>(query, new { pIdUsuario = idUsuario });
    }

    // ---- Edición del perfil ----

    public void ActualizarNombre(int idUsuario, string nombre, string apellido)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute("UPDATE Usuarios SET Nombre = @Nombre, Apellido = @Apellido WHERE IdUsuario = @IdUsuario",
            new { IdUsuario = idUsuario, Nombre = nombre, Apellido = apellido });
    }

    public void ActualizarFotoPerfil(int idUsuario, string fotoPerfil)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute("UPDATE Usuarios SET FotoPerfil = @FotoPerfil WHERE IdUsuario = @IdUsuario",
            new { IdUsuario = idUsuario, FotoPerfil = fotoPerfil });
    }

    // El correo y el teléfono son los datos de contacto: al cambiarlos se le avisa a la otra parte de sus alquileres.
    public void ActualizarCorreo(int idUsuario, string correo)
    {
        ActualizarDatoDeContacto(idUsuario, "Correo", correo, $"su mail de contacto: ahora es {correo}");
    }

    public void ActualizarTelefono(int idUsuario, string telefono)
    {
        ActualizarDatoDeContacto(idUsuario, "Telefono", telefono, $"su teléfono de contacto: ahora es {TelefonoFormato.Formatear(telefono)}");
    }

    // columna: "Correo" o "Telefono" (fija, nunca viene del usuario).
    private void ActualizarDatoDeContacto(int idUsuario, string columna, string valor, string detalleDelAviso)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute($"UPDATE Usuarios SET {columna} = @Valor WHERE IdUsuario = @IdUsuario",
            new { IdUsuario = idUsuario, Valor = valor }, transaction);
        AvisarCambioDeContacto(connection, transaction, idUsuario, detalleDelAviso);

        transaction.Commit();
    }

    // Avisa a la otra parte de cada alquiler del usuario (vinculado a la propiedad o con un contrato pendiente o vigente):
    // si es propietario, a sus inquilinos; si es inquilino, a sus propietarios. Una sola notificación por persona.
    private static void AvisarCambioDeContacto(SqlConnection connection, SqlTransaction transaction, int idUsuario, string detalle)
    {
        connection.Execute(
            @"WITH Yo AS (
                  SELECT IdUsuario, Nombre, Apellido, NULLIF(IdPropietario, 0) AS IdPropietario, NULLIF(IdInquilino, 0) AS IdInquilino
                  FROM Usuarios WHERE IdUsuario = @IdUsuario
              ),
              Alquileres AS (
                  SELECT p.IdPropietario, p.IdInquilino FROM Propiedades p WHERE p.IdInquilino IS NOT NULL
                  UNION
                  SELECT p.IdPropietario, c.IdInquilino
                  FROM Contratos c INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
                  WHERE c.Estado IN ('PendienteInquilino', 'Confirmado')
              ),
              OtraParte AS (
                  SELECT DISTINCT u.IdUsuario
                  FROM Yo
                  INNER JOIN Alquileres a ON a.IdPropietario = Yo.IdPropietario OR a.IdInquilino = Yo.IdInquilino
                  INNER JOIN Usuarios u ON (Yo.IdPropietario IS NOT NULL AND u.IdInquilino = a.IdInquilino)
                                        OR (Yo.IdInquilino IS NOT NULL AND u.IdPropietario = a.IdPropietario)
                  WHERE u.IdUsuario <> Yo.IdUsuario
              )
              INSERT INTO Notificaciones (IdUsuario, Texto)
              SELECT o.IdUsuario, LEFT(CONCAT(Yo.Nombre, ' ', Yo.Apellido, ' cambió ', @Detalle, '.'), 300)
              FROM OtraParte o CROSS JOIN Yo",
            new { IdUsuario = idUsuario, Detalle = detalle },
            transaction);
    }

    public Usuario? ObtenerUsuarioPorIdInquilino(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE IdInquilino = @IdInquilino";
        return connection.QueryFirstOrDefault<Usuario>(query, new { IdInquilino = idInquilino });
    }

    public bool VincularInquilinoAPropiedad(int idPropiedad, int idInquilino)
    {
        using SqlConnection connection = new(connectionString);

        int filasAfectadas = connection.Execute(
            @"UPDATE Propiedades
              SET IdInquilino = @IdInquilino,
                  Estado = 'Ocupada'
              WHERE IdPropiedad = @IdPropiedad
                AND IdInquilino IS NULL",
            new { IdPropiedad = idPropiedad, IdInquilino = idInquilino });

        return filasAfectadas > 0;
    }

    // ---- Terminar contrato ----

    // El contrato Confirmado de la propiedad (esté o no dentro de su plazo), si hay uno.
    public Contratos? ObtenerContratoConfirmadoDePropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<Contratos>(
            "SELECT TOP 1 * FROM Contratos WHERE IdPropiedad = @IdPropiedad AND Estado = 'Confirmado' ORDER BY FechaInicio DESC",
            new { IdPropiedad = idPropiedad });
    }

    // Ids de propiedad (de este propietario) que tienen un contrato Confirmado: ahí se muestra "Terminar contrato".
    public Dictionary<int, int> ObtenerContratosConfirmadosPorPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<(int IdPropiedad, int IdContrato)>(
            @"SELECT c.IdPropiedad, MAX(c.IdContrato) AS IdContrato
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              WHERE p.IdPropietario = @IdPropietario AND c.Estado = 'Confirmado'
              GROUP BY c.IdPropiedad",
            new { IdPropietario = idPropietario }).ToDictionary(x => x.IdPropiedad, x => x.IdContrato);
    }

    // Último contrato Finalizado de la propiedad (para que el propietario vea el motivo y la disconformidad).
    public Contratos? ObtenerUltimoContratoFinalizado(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QueryFirstOrDefault<Contratos>(
            "SELECT TOP 1 * FROM Contratos WHERE IdPropiedad = @IdPropiedad AND Estado = 'Finalizado' ORDER BY FechaFinReal DESC, IdContrato DESC",
            new { IdPropiedad = idPropiedad });
    }

    public List<Contratos> ObtenerContratosFinalizadosDeInquilino(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<Contratos>(
            @"SELECT c.*, p.Direccion
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              WHERE c.IdInquilino = @IdInquilino AND c.Estado = 'Finalizado'
              ORDER BY c.FechaFinReal DESC, c.IdContrato DESC",
            new { IdInquilino = idInquilino }).AsList();
    }

    // Termina el contrato: queda Finalizado, la propiedad vuelve a Disponible sin inquilino, se borran los eventos
    // del contrato posteriores a la fecha real de fin y se avisa al inquilino. Todo en una transacción.
    public bool TerminarContrato(int idContrato, string motivo, DateTime fechaFinReal, string? descripcion)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        string? descripcionLimpia = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        int filas = connection.Execute(
            @"UPDATE Contratos
              SET Estado = 'Finalizado', FechaFinReal = @FechaFinReal, MotivoFin = @Motivo, DescripcionFin = @Descripcion
              WHERE IdContrato = @IdContrato AND Estado = 'Confirmado'",
            new { IdContrato = idContrato, FechaFinReal = fechaFinReal.Date, Motivo = motivo, Descripcion = descripcionLimpia },
            transaction);

        if (filas == 0)
        {
            transaction.Rollback();
            return false;
        }

        var parametros = new { IdContrato = idContrato, FechaFinReal = fechaFinReal.Date };

        // La propiedad queda libre solo si el inquilino sigue siendo el de este contrato.
        connection.Execute(
            @"UPDATE p SET p.IdInquilino = NULL, p.Estado = 'Disponible'
              FROM Propiedades p
              INNER JOIN Contratos c ON c.IdPropiedad = p.IdPropiedad
              WHERE c.IdContrato = @IdContrato AND p.IdInquilino = c.IdInquilino",
            parametros,
            transaction);

        connection.Execute(
            @"DELETE n FROM Notificaciones n INNER JOIN Eventos e ON e.IdEvento = n.IdEvento
              WHERE e.IdContrato = @IdContrato AND e.Tipo <> 'Manual' AND e.Fecha > @FechaFinReal;
              DELETE FROM Eventos WHERE IdContrato = @IdContrato AND Tipo <> 'Manual' AND Fecha > @FechaFinReal;",
            parametros,
            transaction);

        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, Texto)
              SELECT u.IdUsuario,
                     LEFT(CONCAT('Terminó tu contrato de ', p.Direccion, ' (', @NombreMotivo, ') el ', CONVERT(varchar(10), @FechaFinReal, 103), '.',
                                 CASE WHEN @Descripcion IS NULL THEN '' ELSE CONCAT(' ', @Descripcion) END,
                                 ' Podés ver el contrato y tus gastos en modo lectura.'), 300)
              FROM Contratos c
              INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
              INNER JOIN Usuarios u ON u.IdInquilino = c.IdInquilino
              WHERE c.IdContrato = @IdContrato",
            new { IdContrato = idContrato, FechaFinReal = fechaFinReal.Date, NombreMotivo = ReglasContrato.NombreMotivoFin(motivo), Descripcion = descripcionLimpia },
            transaction);

        transaction.Commit();
        return true;
    }

    // El inquilino deja por escrito su disconformidad: una sola vez, sin edición, y solo en contratos que
    // terminaron por un motivo distinto del fin de plazo. Se avisa al propietario.
    public bool DejarDisconformidad(int idContrato, int idInquilino, string texto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int filas = connection.Execute(
            @"UPDATE Contratos SET DisconformidadInquilino = @Texto, FechaDisconformidad = GETDATE()
              WHERE IdContrato = @IdContrato AND IdInquilino = @IdInquilino AND Estado = 'Finalizado'
                AND MotivoFin <> 'FinDePlazo' AND DisconformidadInquilino IS NULL",
            new { IdContrato = idContrato, IdInquilino = idInquilino, Texto = texto.Trim() },
            transaction);

        if (filas > 0)
        {
            connection.Execute(
                @"INSERT INTO Notificaciones (IdUsuario, Texto)
                  SELECT u.IdUsuario, LEFT(CONCAT('El inquilino dejó constancia de su disconformidad con la terminación del contrato de ', p.Direccion, '. Podés leerla en la pantalla del contrato.'), 300)
                  FROM Contratos c
                  INNER JOIN Propiedades p ON p.IdPropiedad = c.IdPropiedad
                  INNER JOIN Usuarios u ON u.IdPropietario = p.IdPropietario
                  WHERE c.IdContrato = @IdContrato",
                new { IdContrato = idContrato },
                transaction);
        }

        transaction.Commit();
        return filas > 0;
    }

    // Desvincula al inquilino de una propiedad sin contrato confirmado y le avisa.
    public void QuitarInquilinoDePropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            @"INSERT INTO Notificaciones (IdUsuario, Texto)
              SELECT u.IdUsuario, LEFT(CONCAT('El propietario te desvinculó de ', p.Direccion, '.'), 300)
              FROM Propiedades p
              INNER JOIN Usuarios u ON u.IdInquilino = p.IdInquilino
              WHERE p.IdPropiedad = @IdPropiedad",
            new { IdPropiedad = idPropiedad },
            transaction);

        connection.Execute(
            @"UPDATE Propiedades
              SET IdInquilino = NULL,
                  Estado = 'Disponible'
              WHERE IdPropiedad = @IdPropiedad",
            new { IdPropiedad = idPropiedad },
            transaction);

        transaction.Commit();
    }

    public List<Evento> ObtenerEventosPorPropietario(int idPropietario, DateTime desde, DateTime hasta)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT e.*, p.Direccion AS NombrePropiedad
                               FROM Eventos e
                               LEFT JOIN Propiedades p ON p.IdPropiedad = e.IdPropiedad
                               WHERE e.IdPropietario = @IdPropietario AND e.Fecha >= @Desde AND e.Fecha < @Hasta
                               ORDER BY e.Fecha, e.HoraInicio";
        return connection.Query<Evento>(query, new { IdPropietario = idPropietario, Desde = desde.Date, Hasta = hasta.Date }).AsList();
    }

    public List<Evento> ObtenerProximosEventos(int idPropietario, DateTime desde, int cantidad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT TOP (@Cantidad) e.*, p.Direccion AS NombrePropiedad
                               FROM Eventos e
                               LEFT JOIN Propiedades p ON p.IdPropiedad = e.IdPropiedad
                               WHERE e.IdPropietario = @IdPropietario AND e.Fecha >= @Desde
                               ORDER BY e.Fecha, e.HoraInicio";
        return connection.Query<Evento>(query, new { IdPropietario = idPropietario, Desde = desde.Date, Cantidad = cantidad }).AsList();
    }

    // El inquilino sólo ve los eventos de las propiedades que alquila a los que fue invitado.
    public List<Evento> ObtenerEventosPorInquilino(int idInquilino, DateTime desde, DateTime hasta)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT e.*, p.Direccion AS NombrePropiedad
                               FROM Eventos e
                               INNER JOIN Propiedades p ON p.IdPropiedad = e.IdPropiedad
                               WHERE p.IdInquilino = @IdInquilino AND e.Invitado = 'Inquilino'
                                 AND e.Fecha >= @Desde AND e.Fecha < @Hasta
                               ORDER BY e.Fecha, e.HoraInicio";
        return connection.Query<Evento>(query, new { IdInquilino = idInquilino, Desde = desde.Date, Hasta = hasta.Date }).AsList();
    }

    public List<Evento> ObtenerProximosEventosInquilino(int idInquilino, DateTime desde, int cantidad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT TOP (@Cantidad) e.*, p.Direccion AS NombrePropiedad
                               FROM Eventos e
                               INNER JOIN Propiedades p ON p.IdPropiedad = e.IdPropiedad
                               WHERE p.IdInquilino = @IdInquilino AND e.Invitado = 'Inquilino' AND e.Fecha >= @Desde
                               ORDER BY e.Fecha, e.HoraInicio";
        return connection.Query<Evento>(query, new { IdInquilino = idInquilino, Desde = desde.Date, Cantidad = cantidad }).AsList();
    }

    public void AgregarEvento(Evento evento)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            @"INSERT INTO Eventos (IdPropietario, IdPropiedad, Titulo, Fecha, HoraInicio, HoraFin, Ubicacion, Invitado, Descripcion)
              VALUES (@IdPropietario, @IdPropiedad, @Titulo, @Fecha, @HoraInicio, @HoraFin, @Ubicacion, @Invitado, @Descripcion)",
            evento);
    }

    private const string SelectContactos = @"SELECT u.IdUsuario, u.Nombre, u.Apellido, u.FotoPerfil,
                                      ult.Texto AS UltimoMensaje, ult.FechaEnvio AS FechaUltimoMensaje,
                                      ult.IdEmisor AS IdEmisorUltimo, ISNULL(ult.Leido, 0) AS UltimoLeido,
                                      CAST(CASE WHEN ult.IdEmisor = @IdUsuario THEN 1 ELSE 0 END AS bit) AS UltimoEsMio,
                                      (SELECT COUNT(*) FROM Mensajes m
                                       WHERE m.IdEmisor = u.IdUsuario AND m.IdReceptor = @IdUsuario AND m.Leido = 0) AS NoLeidos
                               FROM Usuarios u
                               OUTER APPLY (SELECT TOP 1 m.Texto, m.FechaEnvio, m.IdEmisor, m.Leido
                                            FROM Mensajes m
                                            WHERE (m.IdEmisor = @IdUsuario AND m.IdReceptor = u.IdUsuario)
                                               OR (m.IdEmisor = u.IdUsuario AND m.IdReceptor = @IdUsuario)
                                            ORDER BY m.FechaEnvio DESC, m.IdMensaje DESC) ult
                               WHERE u.IdUsuario <> @IdUsuario
                                 AND (u.IdInquilino IN (SELECT p.IdInquilino FROM Propiedades p WHERE p.IdPropietario = @IdPropietario)
                                   OR u.IdPropietario IN (SELECT p.IdPropietario FROM Propiedades p WHERE p.IdInquilino = @IdInquilino)
                                   OR ult.FechaEnvio IS NOT NULL)";

    // Contactos = inquilinos de mis propiedades + propietarios de las propiedades que alquilo
    // + cualquiera con quien ya tenga mensajes (así la conversación no se pierde si se desvincula).
    public List<Contacto> ObtenerContactos(int idUsuario, int? idPropietario, int? idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        const string query = SelectContactos + @"
                               ORDER BY CASE WHEN ult.FechaEnvio IS NULL THEN 1 ELSE 0 END, ult.FechaEnvio DESC, u.Nombre, u.Apellido";
        return connection.Query<Contacto>(query, new { IdUsuario = idUsuario, IdPropietario = idPropietario, IdInquilino = idInquilino }).AsList();
    }

    // Devuelve el contacto sólo si el usuario puede chatear con él.
    public Contacto? ObtenerContacto(int idUsuario, int? idPropietario, int? idInquilino, int idContacto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = SelectContactos + " AND u.IdUsuario = @IdContacto";
        return connection.QueryFirstOrDefault<Contacto>(query, new { IdUsuario = idUsuario, IdPropietario = idPropietario, IdInquilino = idInquilino, IdContacto = idContacto });
    }

    public List<Mensaje> ObtenerConversacion(int idUsuario, int idContacto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT *, CAST(CASE WHEN IdEmisor = @IdUsuario THEN 1 ELSE 0 END AS bit) AS EsMio
                               FROM Mensajes
                               WHERE (IdEmisor = @IdUsuario AND IdReceptor = @IdContacto)
                                  OR (IdEmisor = @IdContacto AND IdReceptor = @IdUsuario)
                               ORDER BY FechaEnvio, IdMensaje";
        return connection.Query<Mensaje>(query, new { IdUsuario = idUsuario, IdContacto = idContacto }).AsList();
    }

    public void MarcarMensajesComoLeidos(int idUsuario, int idContacto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            @"UPDATE Mensajes SET Leido = 1 WHERE IdEmisor = @IdContacto AND IdReceptor = @IdUsuario AND Leido = 0;
              UPDATE Notificaciones SET Leida = 1 WHERE IdUsuario = @IdUsuario AND IdEmisor = @IdContacto AND Leida = 0;",
            new { IdUsuario = idUsuario, IdContacto = idContacto });
    }

    // Guarda el mensaje y avisa al receptor. Mientras no lea el chat hay una sola notificación
    // por remitente, que se actualiza con el último mensaje.
    public void EnviarMensaje(int idEmisor, int idReceptor, string texto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            @"INSERT INTO Mensajes (IdEmisor, IdReceptor, Texto, FechaEnvio, Leido)
              VALUES (@IdEmisor, @IdReceptor, @Texto, GETDATE(), 0)",
            new { IdEmisor = idEmisor, IdReceptor = idReceptor, Texto = texto },
            transaction);

        connection.Execute(
            @"DECLARE @Aviso VARCHAR(300) = LEFT(CONCAT('Nuevo mensaje de ',
                     (SELECT LTRIM(RTRIM(CONCAT(Nombre, ' ', Apellido))) FROM Usuarios WHERE IdUsuario = @IdEmisor),
                     ': ', CASE WHEN LEN(@Texto) > 120 THEN LEFT(@Texto, 117) + '...' ELSE @Texto END), 300);

              UPDATE Notificaciones SET Texto = @Aviso, FechaCreacion = GETDATE()
              WHERE IdUsuario = @IdReceptor AND IdEmisor = @IdEmisor AND Leida = 0;

              IF @@ROWCOUNT = 0
                  INSERT INTO Notificaciones (IdUsuario, IdEmisor, Texto) VALUES (@IdReceptor, @IdEmisor, @Aviso);",
            new { IdEmisor = idEmisor, IdReceptor = idReceptor, Texto = texto },
            transaction);

        transaction.Commit();
    }
}
