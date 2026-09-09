using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>
/// Fiktiver ID.prove-Healthcheck zum Durchspielen der Server-Status-Kette —
/// NUR im Development-Environment aktiv (in Produktion 404). Test-Server
/// zeigen mit ihrer URL auf die Kundenzentrale selbst; die Mandanten-Kennung
/// steuert das Ergebnis:
///   *fail*  → nicht erreichbar (rote Ampel)
///   *conn*  → erreichbar, Connector inaktiv (gelbe Ampel)
///   sonst   → alles gut, Sanktionsliste von heute (grüne Ampel)
/// </summary>
[ApiController]
public class HealthCheckTestController(IWebHostEnvironment umgebung) : ControllerBase
{
    [HttpGet("/api/HealthCheck")]
    [AllowAnonymous]
    public IActionResult Get([FromQuery] string? customer)
    {
        if (!umgebung.IsDevelopment())
            return NotFound();

        if (string.IsNullOrWhiteSpace(customer))
            return Ok(new { isAlive = false, iDproveConnectorActive = false, sanctionListDate = "", errorMessage = "Mandant unbekannt" });

        if (customer.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return Ok(new { isAlive = false, iDproveConnectorActive = false, sanctionListDate = "", errorMessage = "Simulierter Ausfall (Testdaten)" });

        if (customer.Contains("conn", StringComparison.OrdinalIgnoreCase))
            return Ok(new
            {
                isAlive = true,
                iDproveConnectorActive = false,
                sanctionListDate = DateTime.Now.AddDays(-3).ToString("dd.MM.yyyy HH:mm"),
                errorMessage = "",
            });

        return Ok(new
        {
            isAlive = true,
            iDproveConnectorActive = true,
            sanctionListDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm"),
            errorMessage = "",
        });
    }
}
