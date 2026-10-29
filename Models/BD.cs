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
        const string query = "SELECT * FROM Propiedades WHERE IdPropietario = @pIdPropietario";
        return connection.Query<Propiedades>(query, new { pIdPropietario = idPropietario }).AsList();
    }

    public int AgregarPropiedad(Propiedades propiedad)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        int idPropiedad = connection.ExecuteScalar<int>(
            @"INSERT INTO Propiedades (Direccion, Superficie, Ambientes, Foto, Estado, IdPropietario, FechaDeAlta, ResumenPropiedad, Alquiler)
              VALUES (@Direccion, @Superficie, @Ambientes, @Foto, @Estado, @IdPropietario, @FechaDeAlta, @ResumenPropiedad, @Alquiler);
              SELECT CAST(SCOPE_IDENTITY() AS int);",
            propiedad,
            transaction: transaction);

        int idServicio = connection.ExecuteScalar<int>(
            "INSERT INTO Servicios DEFAULT VALUES; SELECT CAST(SCOPE_IDENTITY() AS int);",
            transaction: transaction);

        int idResumen = connection.ExecuteScalar<int>(
            "INSERT INTO ResumenPropiedad (IdServicios) VALUES (@IdServicios); SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { IdServicios = idServicio },
            transaction: transaction);

        connection.Execute(
            "UPDATE Propiedades SET ResumenPropiedad = @IdResumen WHERE IdPropiedad = @IdPropiedad",
            new { IdResumen = idResumen, IdPropiedad = idPropiedad },
            transaction: transaction);

        transaction.Commit();
        return idPropiedad;
    }

    public Propiedades? ObtenerPropiedadPorId(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Propiedades WHERE IdPropiedad = @pIdPropiedad";
        return connection.QueryFirstOrDefault<Propiedades>(query, new { pIdPropiedad = idPropiedad });
    }

    public void AgregarExpensa(Expensas expensa, int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string insertExpensa = @"INSERT INTO Expensas (Monto, FechaVencimiento, FechaAlta, Estado, Comprobante, FechaPago)
                                        VALUES (@Monto, @FechaVencimiento, @FechaAlta, @Estado, @Comprobante, @FechaPago);
                                        SELECT CAST(SCOPE_IDENTITY() AS int);";

        int idExpensa = connection.ExecuteScalar<int>(insertExpensa, expensa);

        const string updatePropiedad = @"UPDATE Propiedades SET IdExpensas = @IdExpensa WHERE IdPropiedad = @IdPropiedad";
        connection.Execute(updatePropiedad, new { IdExpensa = idExpensa, IdPropiedad = idPropiedad });
    }

    public List<Expensas> ObtenerExpensasPorIdPropietario(int idPropietario)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT e.IdExpensas, e.Monto, e.FechaVencimiento, e.FechaAlta, e.Estado, e.Comprobante, e.FechaPago,
                                        p.Direccion AS NombrePropiedad
                              FROM Expensas e
                              INNER JOIN Propiedades p ON p.IdExpensas = e.IdExpensas
                              WHERE p.IdPropietario = @pIdPropietario
                              ORDER BY e.FechaAlta DESC";

        return connection.Query<Expensas>(query, new { pIdPropietario = idPropietario }).AsList();
    }

    public void EditarPropiedad(Propiedades propiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"UPDATE Propiedades
                               SET Direccion = @Direccion,
                                   Superficie = @Superficie,
                                   Ambientes = @Ambientes,
                                   Estado = @Estado,
                                   Foto = @Foto,
                                   Alquiler = @Alquiler
                               WHERE IdPropiedad = @IdPropiedad AND IdPropietario = @IdPropietario";

        connection.Execute(query, propiedad);
    }

    public List<Gastos> ObtenerTiposGasto()
    {
        using SqlConnection connection = new(connectionString);
        return connection.Query<Gastos>("SELECT * FROM Gastos ORDER BY TipoGasto").AsList();
    }

    public Gastos? ObtenerTipoGastoPorId(int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Gastos WHERE IdGasto = @IdGasto";
        return connection.QueryFirstOrDefault<Gastos>(query, new { IdGasto = idGasto });
    }

    public GastoXTipo? ObtenerGastoPorId(int idGastoXTipo)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT gxt.*, g.TipoGasto
                              FROM GastoXTipo gxt
                              INNER JOIN Gastos g ON g.IdGasto = gxt.IdGasto
                              WHERE gxt.IdGastoXTipo = @IdGastoXTipo";
        return connection.QueryFirstOrDefault<GastoXTipo>(query, new { IdGastoXTipo = idGastoXTipo });
    }

    public int? ObtenerIdResumenPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        return connection.QuerySingleOrDefault<int?>("SELECT ResumenPropiedad FROM Propiedades WHERE IdPropiedad = @IdPropiedad", new { IdPropiedad = idPropiedad });
    }

    public int ObtenerIdServicioDePropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        int? idServicio = connection.QuerySingleOrDefault<int?>(
            @"SELECT rp.IdServicios
              FROM Propiedades p
              LEFT JOIN ResumenPropiedad rp ON rp.IdResumenPropiedad = p.ResumenPropiedad
              WHERE p.IdPropiedad = @IdPropiedad",
            new { IdPropiedad = idPropiedad });

        if (idServicio.HasValue)
        {
            return idServicio.Value;
        }

        int nuevoServicioId = connection.ExecuteScalar<int>("INSERT INTO Servicios DEFAULT VALUES; SELECT CAST(SCOPE_IDENTITY() AS int);");
        int nuevoResumenId = connection.ExecuteScalar<int>(
            "INSERT INTO ResumenPropiedad (IdServicios) VALUES (@IdServicios); SELECT CAST(SCOPE_IDENTITY() AS int);",
            new { IdServicios = nuevoServicioId });

        connection.Execute(
            "UPDATE Propiedades SET ResumenPropiedad = @IdResumen WHERE IdPropiedad = @IdPropiedad",
            new { IdResumen = nuevoResumenId, IdPropiedad = idPropiedad });

        return nuevoServicioId;
    }

    public int CrearServicioSiNoExiste(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);

        int? idServicio = ObtenerIdServicioDePropiedad(idPropiedad);
        if (idServicio.HasValue)
        {
            return idServicio.Value;
        }

        int idNuevoServicio = connection.ExecuteScalar<int>("INSERT INTO Servicios DEFAULT VALUES; SELECT CAST(SCOPE_IDENTITY() AS int); ");
        int? idResumen = ObtenerIdResumenPropiedad(idPropiedad);

        if (!idResumen.HasValue)
        {
            int idNuevoResumen = connection.ExecuteScalar<int>("INSERT INTO ResumenPropiedad (IdServicios) VALUES (@IdServicios); SELECT CAST(SCOPE_IDENTITY() AS int);", new { IdServicios = idNuevoServicio });
            connection.Execute("UPDATE Propiedades SET ResumenPropiedad = @IdResumen WHERE IdPropiedad = @IdPropiedad", new { IdResumen = idNuevoResumen, IdPropiedad = idPropiedad });
            return idNuevoServicio;
        }

        connection.Execute("UPDATE ResumenPropiedad SET IdServicios = @IdServicios WHERE IdResumenPropiedad = @IdResumenPropiedad", new { IdServicios = idNuevoServicio, IdResumenPropiedad = idResumen.Value });
        return idNuevoServicio;
    }

    public void AgregarGasto(int idPropiedad, GastoXTipo gasto)
    {
        using SqlConnection connection = new(connectionString);
        int idServicio = CrearServicioSiNoExiste(idPropiedad);

        var existente = connection.QuerySingleOrDefault<int?>(
            @"SELECT IdGastoXTipo
              FROM GastoXTipo
              WHERE IdGasto = @IdGasto AND IdServicios = @IdServicios AND CAST(Mes AS date) = CAST(@Mes AS date)",
            new
            {
                IdGasto = gasto.IdGasto,
                IdServicios = idServicio,
                Mes = gasto.Mes
            });

        if (existente.HasValue)
        {
            connection.Execute(
                @"UPDATE GastoXTipo
                  SET Monto = @Monto,
                      FechaVencimiento = @FechaVencimiento,
                      FechaAlta = @FechaAlta,
                      Estado = @Estado,
                      Comprobante = @Comprobante,
                      FechaPago = @FechaPago
                  WHERE IdGastoXTipo = @IdGastoXTipo",
                new
                {
                    Monto = gasto.Monto,
                    FechaVencimiento = gasto.FechaVencimiento,
                    FechaAlta = gasto.FechaAlta,
                    Estado = gasto.Estado,
                    Comprobante = gasto.Comprobante,
                    FechaPago = gasto.FechaPago,
                    IdGastoXTipo = existente.Value
                });
            return;
        }

        const string query = @"INSERT INTO GastoXTipo (IdGasto, IdServicios, Mes, Monto, FechaVencimiento, FechaAlta, Estado, Comprobante, FechaPago)
                               VALUES (@IdGasto, @IdServicios, @Mes, @Monto, @FechaVencimiento, @FechaAlta, @Estado, @Comprobante, @FechaPago)";

        connection.Execute(query, new
        {
            IdGasto = gasto.IdGasto,
            IdServicios = idServicio,
            Mes = gasto.Mes,
            Monto = gasto.Monto,
            FechaVencimiento = gasto.FechaVencimiento,
            FechaAlta = gasto.FechaAlta,
            Estado = gasto.Estado,
            Comprobante = gasto.Comprobante,
            FechaPago = gasto.FechaPago
        });
    }

    public List<GastoXTipo> ObtenerGastosPorServicioYTipo(int idPropiedad, string tipoGasto)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT gxt.*, g.TipoGasto
                              FROM GastoXTipo gxt
                              INNER JOIN Gastos g ON g.IdGasto = gxt.IdGasto
                              INNER JOIN ResumenPropiedad rp ON rp.IdResumenPropiedad = @IdPropiedad
                              INNER JOIN Servicios s ON s.IdServicios = rp.IdServicios
                              WHERE gxt.IdServicios = s.IdServicios
                                AND g.TipoGasto = @TipoGasto
                              ORDER BY gxt.Mes DESC";

        return connection.Query<GastoXTipo>(query, new { IdPropiedad = idPropiedad, TipoGasto = tipoGasto }).AsList();
    }

    public List<GastoXTipo> ObtenerGastosPorServicio(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"SELECT gxt.*, g.TipoGasto
                              FROM GastoXTipo gxt
                              INNER JOIN Gastos g ON g.IdGasto = gxt.IdGasto
                              INNER JOIN ResumenPropiedad rp ON rp.IdResumenPropiedad = @IdPropiedad
                              INNER JOIN Servicios s ON s.IdServicios = rp.IdServicios
                              WHERE gxt.IdServicios = s.IdServicios
                              ORDER BY gxt.Mes DESC";

        return connection.Query<GastoXTipo>(query, new { IdPropiedad = idPropiedad }).AsList();
    }

    public void ActualizarEstadoGasto(int idGastoXTipo, string estado, DateTime? fechaPago)
    {
        using SqlConnection connection = new(connectionString);
        const string query = @"UPDATE GastoXTipo
                               SET Estado = @Estado,
                                   FechaPago = @FechaPago
                               WHERE IdGastoXTipo = @IdGastoXTipo";

        connection.Execute(query, new { Estado = estado, FechaPago = fechaPago, IdGastoXTipo = idGastoXTipo });
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

        if (propiedad.IdExpensas.HasValue)
        {
            connection.Execute(
                "DELETE FROM Expensas WHERE IdExpensas = @IdExpensas",
                new { IdExpensas = propiedad.IdExpensas.Value },
                transaction: transaction);
        }

        if (propiedad.ResumenPropiedad.HasValue)
        {
            var resumen = connection.QueryFirstOrDefault<ResumenPropiedad>(
                "SELECT * FROM ResumenPropiedad WHERE IdResumenPropiedad = @IdResumenPropiedad",
                new { IdResumenPropiedad = propiedad.ResumenPropiedad.Value },
                transaction: transaction);

            if (resumen != null && resumen.IdServicios.HasValue)
            {
                connection.Execute(
                    "DELETE FROM GastoXTipo WHERE IdServicios = @IdServicios",
                    new { IdServicios = resumen.IdServicios.Value },
                    transaction: transaction);

                connection.Execute(
                    "DELETE FROM Servicios WHERE IdServicios = @IdServicios",
                    new { IdServicios = resumen.IdServicios.Value },
                    transaction: transaction);
            }

            connection.Execute(
                "DELETE FROM ResumenPropiedad WHERE IdResumenPropiedad = @IdResumenPropiedad",
                new { IdResumenPropiedad = propiedad.ResumenPropiedad.Value },
                transaction: transaction);
        }

        connection.Execute(
            "DELETE FROM Propiedades WHERE IdPropiedad = @IdPropiedad AND IdPropietario = @IdPropietario",
            new { IdPropiedad = idPropiedad, IdPropietario = idPropietario },
            transaction: transaction);

        transaction.Commit();
    }

    public List<Gastos> ObtenerTiposGastoPorPropiedad(int idPropiedad)
    {
        using SqlConnection connection = new(connectionString);
        int idServicio = ObtenerIdServicioDePropiedad(idPropiedad);

        const string query = @"SELECT DISTINCT g.*
                              FROM GastoXTipo gxt
                              INNER JOIN Gastos g ON g.IdGasto = gxt.IdGasto
                              WHERE gxt.IdServicios = @IdServicio
                              UNION
                              SELECT g.*
                              FROM Gastos g
                              WHERE g.IdGasto IN (
                                  SELECT IdGasto FROM GastoXTipo WHERE IdServicios = @IdServicio
                              )
                              ORDER BY TipoGasto";

        return connection.Query<Gastos>(query, new { IdServicio = idServicio }).AsList();
    }

    public void RegistrarTipoGastoEnPropiedad(int idPropiedad, int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        int? idServicio = ObtenerIdServicioDePropiedad(idPropiedad);
        if (!idServicio.HasValue)
        {
            idServicio = CrearServicioSiNoExiste(idPropiedad);
        }

        int existe = connection.QuerySingleOrDefault<int>(
            @"SELECT COUNT(*)
              FROM GastoXTipo
              WHERE IdGasto = @IdGasto AND IdServicios = @IdServicios AND Comprobante = '__TIPO__'",
            new { IdGasto = idGasto, IdServicios = idServicio.Value });

        if (existe > 0)
        {
            return;
        }

        const string query = @"INSERT INTO GastoXTipo (IdGasto, IdServicios, Mes, Monto, FechaVencimiento, FechaAlta, Estado, Comprobante, FechaPago)
                               VALUES (@IdGasto, @IdServicios, @Mes, @Monto, @FechaVencimiento, @FechaAlta, @Estado, @Comprobante, @FechaPago)";

        connection.Execute(query, new
        {
            IdGasto = idGasto,
            IdServicios = idServicio.Value,
            Mes = new DateTime(1900, 1, 1),
            Monto = 0m,
            FechaVencimiento = new DateTime(1900, 1, 1),
            FechaAlta = DateTime.Today,
            Estado = "A pagar",
            Comprobante = "__TIPO__",
            FechaPago = (DateTime?)null
        });
    }

    public void EliminarTipoGastoDePropiedad(int idPropiedad, int idGasto)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();
        int? idServicio = ObtenerIdServicioDePropiedad(idPropiedad);

        if (!idServicio.HasValue)
        {
            return;
        }

        connection.Execute(
            "DELETE FROM GastoXTipo WHERE IdGasto = @IdGasto AND IdServicios = @IdServicios",
            new { IdGasto = idGasto, IdServicios = idServicio.Value });
    }

    public Usuario? ObtenerUsuarioPorId(int idUsuario)
    {
        using SqlConnection connection = new(connectionString);
        const string query = "SELECT * FROM Usuarios WHERE IdUsuario = @pIdUsuario";
        return connection.QueryFirstOrDefault<Usuario>(query, new { pIdUsuario = idUsuario });
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
}