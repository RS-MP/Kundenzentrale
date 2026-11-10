using Microsoft.AspNetCore.Mvc;
using Rausoft.Erp.Api.Dienste;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Controllers;

/// <summary>Kennzeichnung der Instanz fürs Frontend (Band „Testsystem").</summary>
[ApiController]
[Route("api/instanz")]
public class InstanzController(InstanzOptionen instanz) : ControllerBase
{
    [HttpGet]
    public ActionResult<InstanzInfo> Info() => Ok(new InstanzInfo(
        instanz.Testsystem,
        instanz.Testsystem && string.IsNullOrWhiteSpace(instanz.Bezeichnung) ? "Testsystem" : instanz.Bezeichnung.Trim()));
}
