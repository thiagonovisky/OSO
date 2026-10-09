namespace OSO.Models;
using Dapper;
using Microsoft.Data.SqlClient;

public class BD
{
    private string connectionString = "Server=localhost;Database=OSO;User Id=sa;Password=123456789;TrustServerCertificate=True;";

    public void RegistrarUsuario(Usuario usuario)
    {
        // falta sp
    }
}