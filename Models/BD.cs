namespace OSO.Models;
using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

public class BD
{
    private string connectionString = "Server=localhost;Database=OSO;Integrated Security=true;TrustServerCertificate=True;";

    public void RegistrarUsuario(Usuario usuario, bool esPropietario)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string storedProcedure = "sp_RegistrarUsuario";

            connection.Execute(
                storedProcedure,    
                new
                {
                    Nombre = usuario.Nombre,
                    Apellido = usuario.Apellido,
                    Correo = usuario.Correo,
                    Contrasena = usuario.Contrasena,
                    Telefono = usuario.Telefono,
                    esPropietario = esPropietario
                },
                commandType: CommandType.StoredProcedure
            );
        }
    }

     public Usuario verificarUsuario(Usuario usuario)
    {
        Usuario usuarioExistente = null;
        using(SqlConnection connection = new SqlConnection(connectionString)){
            string query = "SELECT * FROM Usuarios WHERE Correo = @pCorreo";
            usuarioExistente = connection.QueryFirstOrDefault<Usuario>(query, new {pCorreo = usuario.Correo});
        }
        return usuarioExistente;
    }

    public List<Propiedades> ObtenerPropiedadesPorIdPropietario(int idPropietario)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = "SELECT * FROM Propiedades WHERE IdPropietario = @pIdPropietario";
            return connection.Query<Propiedades>(query, new { pIdPropietario = idPropietario }).AsList();
        }
    }

    public void AgregarPropiedad(Propiedades propiedad)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = "INSERT INTO Propiedades (Direccion, Superficie, Ambientes, Foto, Estado, IdPropietario, FechaDeAlta) VALUES (@Direccion, @Superficie, @Ambientes, @Foto, @Estado, @IdPropietario, @FechaDeAlta)";
            connection.Execute(query, propiedad);
        }
    }

    public Propiedades ObtenerPropiedadPorId(int idPropiedad)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = "SELECT * FROM Propiedades WHERE IdPropiedad = @pIdPropiedad";
            return connection.QueryFirstOrDefault<Propiedades>(query, new { pIdPropiedad = idPropiedad });
        }
    }

    public void EditarPropiedad(Propiedades propiedad)
    {
        using (SqlConnection connection = new SqlConnection(connectionString))
        {
            string query = @"UPDATE Propiedades
                             SET Direccion = @Direccion,
                                 Superficie = @Superficie,
                                 Ambientes = @Ambientes,
                                 Estado = @Estado,
                                 Foto = @Foto
                             WHERE IdPropiedad = @IdPropiedad AND IdPropietario = @IdPropietario";

            connection.Execute(query, propiedad);
        }
    }
}