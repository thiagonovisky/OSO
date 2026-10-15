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
                    EsPropietario = esPropietario
                },
                commandType: CommandType.StoredProcedure
            );
        }
    }
}