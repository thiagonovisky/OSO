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

    public Usuario? verificarUsuario(Usuario usuario)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE Correo = @pCorreo";
        return connection.QueryFirstOrDefault<Usuario>(query, new { pCorreo = usuario.Correo });
    }

    public List<Propiedades> ObtenerPropiedadesPorIdPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT p.*,
                                      (SELECT TOP 1 c.MontoAlquiler FROM Contratos c
                                       WHERE c.IdPropiedad = p.IdPropiedad
                                         AND c.FechaInicio <= CAST(GETDATE() AS date) AND c.FechaFin >= CAST(GETDATE() AS date)
                                       ORDER BY c.FechaInicio DESC) AS AlquilerActual
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
                                            WHERE c.IdPropiedad = p.IdPropiedad
                                              AND c.FechaInicio <= CAST(GETDATE() AS date) AND c.FechaFin >= CAST(GETDATE() AS date)
                                            ORDER BY c.FechaInicio DESC) contrato
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
                                       WHERE c.IdPropiedad = p.IdPropiedad
                                         AND c.FechaInicio <= CAST(GETDATE() AS date) AND c.FechaFin >= CAST(GETDATE() AS date)
                                       ORDER BY c.FechaInicio DESC) AS AlquilerActual
                               FROM Propiedades p
                               WHERE p.IdPropiedad = @pIdPropiedad";
        return connection.QueryFirstOrDefault<Propiedades>(query, new { pIdPropiedad = idPropiedad });
    }

    public Contratos? ObtenerContratoActivoPorPropiedad(int idPropiedad, DateTime? fechaReferencia = null)
    {
        using SqlConnection connection = new(connectionString);
        var fecha = fechaReferencia ?? DateTime.Today;

        const string query = @"SELECT *
                              FROM Contratos
                              WHERE IdPropiedad = @IdPropiedad
                                AND FechaInicio <= @FechaReferencia
                                AND FechaFin >= @FechaReferencia
                              ORDER BY FechaInicio DESC";

        return connection.QueryFirstOrDefault<Contratos>(query, new
        {
            IdPropiedad = idPropiedad,
            FechaReferencia = fecha.Date
        });
    }

    public bool ExisteContratoActivoParaPropiedad(int idPropiedad, DateTime? fechaReferencia = null)
    {
        return ObtenerContratoActivoPorPropiedad(idPropiedad, fechaReferencia) != null;
    }

    public void CrearContrato(Contratos contrato)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int contratoActivo = connection.QuerySingleOrDefault<int>(
            @"SELECT COUNT(*)
              FROM Contratos
              WHERE IdPropiedad = @IdPropiedad
                AND FechaInicio <= @FechaReferencia
                AND FechaFin >= @FechaReferencia",
            new
            {
                IdPropiedad = contrato.IdPropiedad,
                FechaReferencia = DateTime.Today.Date
            },
            transaction: transaction);

        if (contratoActivo > 0)
        {
            throw new InvalidOperationException("Ya existe un contrato activo para esta propiedad.");
        }

        connection.Execute(
            @"INSERT INTO Contratos (IdPropiedad, IdInquilino, FechaInicio, FechaFin, MontoAlquiler)
              VALUES (@IdPropiedad, @IdInquilino, @FechaInicio, @FechaFin, @MontoAlquiler)",
            new
            {
                IdPropiedad = contrato.IdPropiedad,
                IdInquilino = contrato.IdInquilino,
                FechaInicio = contrato.FechaInicio.Date,
                FechaFin = contrato.FechaFin.Date,
                MontoAlquiler = contrato.MontoAlquiler
            },
            transaction: transaction);

        connection.Execute(
            @"UPDATE Propiedades
              SET IdInquilino = @IdInquilino,
                  Estado = 'Ocupada'
              WHERE IdPropiedad = @IdPropiedad",
            new
            {
                IdInquilino = contrato.IdInquilino,
                IdPropiedad = contrato.IdPropiedad
            },
            transaction: transaction);

        transaction.Commit();
    }

    public List<Contratos> ObtenerContratosPorPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT *
                              FROM Contratos
                              WHERE IdPropiedad = @IdPropiedad
                              ORDER BY FechaInicio DESC";

        return connection.Query<Contratos>(query, new { IdPropiedad = idPropiedad }).AsList();
    }

    public void EliminarContrato(int idContrato)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute("DELETE FROM Contratos WHERE IdContrato = @IdContrato", new { IdContrato = idContrato });
    }

    public Usuario? ObtenerInquilinoPorId(int idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE IdUsuario = @IdUsuario";
        return connection.QueryFirstOrDefault<Usuario>(query, new { IdUsuario = idInquilino });
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
                                   Estado = @Estado,
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

    // Un gasto con la parte del propietario y lo que ya pagó.
    private const string SelectGastoPropietario = @"
        SELECT gp.*, g.TipoGasto, p.Direccion AS NombrePropiedad,
               gr.IdGastoResponsable, gr.Monto AS MontoResponsable,
               ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0) AS MontoPagado,
               (SELECT MAX(pg.Fecha) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable) AS FechaPago,
               (SELECT TOP 1 pg.Comprobante FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable ORDER BY pg.Fecha DESC, pg.IdPago DESC) AS Comprobante
        FROM GastosPropiedad gp
        INNER JOIN Gastos g ON g.IdGasto = gp.IdGasto
        INNER JOIN Propiedades p ON p.IdPropiedad = gp.IdPropiedad
        INNER JOIN GastoResponsables gr ON gr.IdGastoPropiedad = gp.IdGastoPropiedad AND gr.Responsable = 'Propietario'";

    private static DateTime PrimerDiaDelMes(DateTime fecha) => new(fecha.Year, fecha.Month, 1);

    // Crea o actualiza el gasto del período y deja al propietario como responsable del total.
    private static int GuardarGastoPropiedad(SqlConnection connection, SqlTransaction transaction, int idPropiedad, int idGasto, GastoPropiedad gasto)
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

        // Por ahora el propietario paga el 100%. Cuando haya reparto/IA, acá se escriben las demás partes.
        connection.Execute(
            @"MERGE GastoResponsables AS destino
              USING (SELECT @IdGastoPropiedad AS IdGastoPropiedad) AS origen
                 ON destino.IdGastoPropiedad = origen.IdGastoPropiedad AND destino.Responsable = 'Propietario'
              WHEN MATCHED THEN UPDATE SET Monto = @Monto, Porcentaje = 100
              WHEN NOT MATCHED THEN INSERT (IdGastoPropiedad, Responsable, Monto, Porcentaje, Origen)
                   VALUES (@IdGastoPropiedad, 'Propietario', @Monto, 100, 'Manual');",
            new { IdGastoPropiedad = idGastoPropiedad, gasto.Monto },
            transaction);

        return idGastoPropiedad;
    }

    // Registra un pago por lo que falta de la parte del propietario. Devuelve false si no corresponde.
    private static bool PagarParteDelPropietario(SqlConnection connection, SqlTransaction transaction, int idGastoResponsable, string? comprobante)
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
                AND gr.Responsable = 'Propietario'
                AND gr.Monto > ISNULL((SELECT SUM(pg.Monto) FROM Pagos pg WHERE pg.IdGastoResponsable = gr.IdGastoResponsable), 0)",
            new { IdGastoResponsable = idGastoResponsable, Fecha = DateTime.Today, Comprobante = comprobante },
            transaction);

        return filas > 0;
    }

    public void AgregarExpensa(GastoPropiedad expensa, int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int idGasto = ObtenerOCrearTipoGasto(connection, transaction, TipoExpensas, "Expensas de la propiedad");
        expensa.Periodo = PrimerDiaDelMes(expensa.FechaVencimiento);
        GuardarGastoPropiedad(connection, transaction, idPropiedad, idGasto, expensa);

        transaction.Commit();
    }

    public List<GastoPropiedad> ObtenerExpensasPorIdPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoPropietario + @"
            WHERE p.IdPropietario = @IdPropietario AND g.TipoGasto = @TipoGasto
            ORDER BY gp.FechaAlta DESC, gp.IdGastoPropiedad DESC";

        return connection.Query<GastoPropiedad>(query, new { IdPropietario = idPropietario, TipoGasto = TipoExpensas }).AsList();
    }

    // Paga la parte del propietario de un gasto (expensa), validando que la propiedad sea suya.
    public bool MarcarExpensaComoPagada(int idGastoResponsable, int idPropietario, string comprobante)
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

        bool pagado = esDelPropietario && PagarParteDelPropietario(connection, transaction, idGastoResponsable, comprobante);
        transaction.Commit();
        return pagado;
    }

    public List<Gastos> ObtenerTiposGasto()
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<Gastos>("SELECT * FROM Gastos ORDER BY TipoGasto").AsList();
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
        return ObtenerOCrearTipoGasto(connection, null, nombreTipo, "Gasto adicional");
    }

    public Gastos? ObtenerTipoGastoPorId(int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Gastos WHERE IdGasto = @IdGasto";
        return connection.QueryFirstOrDefault<Gastos>(query, new { IdGasto = idGasto });
    }

    // Crea (o actualiza) el gasto del mes. Si se indica que está pagado, registra el pago.
    public void AgregarGasto(int idPropiedad, int idGasto, GastoPropiedad gasto, bool pagado, string? comprobante)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int idGastoPropiedad = GuardarGastoPropiedad(connection, transaction, idPropiedad, idGasto, gasto);
        FijarEstadoPagado(connection, transaction, idGastoPropiedad, pagado, comprobante);

        transaction.Commit();
    }

    public List<GastoPropiedad> ObtenerGastosPorServicioYTipo(int idPropiedad, string tipoGasto)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoPropietario + @"
            WHERE gp.IdPropiedad = @IdPropiedad AND g.TipoGasto = @TipoGasto
            ORDER BY gp.Periodo DESC";

        return connection.Query<GastoPropiedad>(query, new { IdPropiedad = idPropiedad, TipoGasto = tipoGasto }).AsList();
    }

    public List<GastoPropiedad> ObtenerGastosPorServicio(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        string query = SelectGastoPropietario + @"
            WHERE gp.IdPropiedad = @IdPropiedad
            ORDER BY gp.Periodo DESC";

        return connection.Query<GastoPropiedad>(query, new { IdPropiedad = idPropiedad }).AsList();
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
            PagarParteDelPropietario(connection, transaction, idGastoResponsable, string.IsNullOrWhiteSpace(comprobante) ? null : comprobante);
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

        connection.Execute(
            $@"DELETE pg FROM Pagos pg
               INNER JOIN GastoResponsables gr ON gr.IdGastoResponsable = pg.IdGastoResponsable
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

    public void QuitarInquilinoDePropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            @"UPDATE Propiedades
              SET IdInquilino = NULL,
                  Estado = 'Disponible'
              WHERE IdPropiedad = @IdPropiedad",
            new { IdPropiedad = idPropiedad });
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

    // Contactos = inquilinos de mis propiedades + propietarios de las propiedades que alquilo
    // + cualquiera con quien ya tenga mensajes (así la conversación no se pierde si se desvincula).
    public List<Contacto> ObtenerContactos(int idUsuario, int? idPropietario, int? idInquilino)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT u.IdUsuario, u.Nombre, u.Apellido, u.FotoPerfil,
                                      ult.Texto AS UltimoMensaje, ult.FechaEnvio AS FechaUltimoMensaje,
                                      ult.IdEmisor AS IdEmisorUltimo, ISNULL(ult.Leido, 0) AS UltimoLeido,
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
                                   OR ult.FechaEnvio IS NOT NULL)
                               ORDER BY CASE WHEN ult.FechaEnvio IS NULL THEN 1 ELSE 0 END, ult.FechaEnvio DESC, u.Nombre, u.Apellido";
        return connection.Query<Contacto>(query, new { IdUsuario = idUsuario, IdPropietario = idPropietario, IdInquilino = idInquilino }).AsList();
    }

    public List<Mensaje> ObtenerConversacion(int idUsuario, int idContacto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT * FROM Mensajes
                               WHERE (IdEmisor = @IdUsuario AND IdReceptor = @IdContacto)
                                  OR (IdEmisor = @IdContacto AND IdReceptor = @IdUsuario)
                               ORDER BY FechaEnvio, IdMensaje";
        return connection.Query<Mensaje>(query, new { IdUsuario = idUsuario, IdContacto = idContacto }).AsList();
    }

    public void MarcarMensajesComoLeidos(int idUsuario, int idContacto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            "UPDATE Mensajes SET Leido = 1 WHERE IdEmisor = @IdContacto AND IdReceptor = @IdUsuario AND Leido = 0",
            new { IdUsuario = idUsuario, IdContacto = idContacto });
    }

    public void EnviarMensaje(int idEmisor, int idReceptor, string texto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Execute(
            @"INSERT INTO Mensajes (IdEmisor, IdReceptor, Texto, FechaEnvio, Leido)
              VALUES (@IdEmisor, @IdReceptor, @Texto, GETDATE(), 0)",
            new { IdEmisor = idEmisor, IdReceptor = idReceptor, Texto = texto });
    }
}