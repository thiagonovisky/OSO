using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OSO.Models;

namespace OSO.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    BD bd = new BD();

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Registro()
    {
        return View();
    }

    public IActionResult Registrar(Usuario usuario, bool inquilino, bool propietario)
    {
        bool esPropietario = false;

        if (inquilino)
        {
            esPropietario = false;
        }
        else if (propietario)
        {
            esPropietario = true;
        }

        bd.RegistrarUsuario(usuario, esPropietario);
        return View("Index");
    }

    public IActionResult Login()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
