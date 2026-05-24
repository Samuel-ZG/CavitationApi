// ============================================================
//  ALERTS CONTROLLER
//  Archivo: Controllers/AlertsController.cs
//
//  Endpoints:
//    GET  /api/alerts/experiment/{experimentId}
//    GET  /api/alerts/active              (alertas activas del operario)
//    POST /api/alerts/{id}/acknowledge
// ============================================================

using CavitationApi.Helpers;
using CavitationApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly IAlertService _alertService;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(
        IAlertService alertService,
        ILogger<AlertsController> logger)
    {
        _alertService = alertService;
        _logger       = logger;
    }

    // GET /api/alerts/experiment/{experimentId}
    // Historial completo de alertas de un experimento
    [HttpGet("experiment/{experimentId:int}")]
    public async Task<IActionResult> GetByExperiment(int experimentId)
    {
        var alerts = await _alertService.GetByExperimentAsync(experimentId);
        return Ok(alerts);
    }

    // GET /api/alerts/active
    // Alertas no reconocidas de las máquinas asignadas al operario autenticado
    // La app Flutter llama a este endpoint al arrancar y periódicamente
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var operatorId = ClaimsHelper.GetOperatorId(User);
        var alerts = await _alertService.GetActiveByOperatorAsync(operatorId);
        return Ok(alerts);
    }

    // POST /api/alerts/{id}/acknowledge
    // El operario marca la alerta como vista desde la app
    [HttpPost("{id:int}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id)
    {
        var operatorId = ClaimsHelper.GetOperatorId(User);

        try
        {
            var success = await _alertService.AcknowledgeAsync(id, operatorId);
            if (!success)
                return NotFound(new { message = $"Alerta {id} no encontrada." });

            return Ok(new { message = "Alerta reconocida correctamente." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }
}
