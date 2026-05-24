// ============================================================
//  RESULTS CONTROLLER
//  Archivo: Controllers/ResultsController.cs
//
//  Endpoints:
//    GET  /api/results/experiment/{experimentId}
//    POST /api/results/experiment/{experimentId}/generate
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Helpers;
using CavitationApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/results")]
[Authorize]
public class ResultsController : ControllerBase
{
    private readonly IResultService _resultService;
    private readonly IExperimentService _experimentService;
    private readonly ILogger<ResultsController> _logger;

    public ResultsController(
        IResultService resultService,
        IExperimentService experimentService,
        ILogger<ResultsController> logger)
    {
        _resultService     = resultService;
        _experimentService = experimentService;
        _logger            = logger;
    }

    // GET /api/results/experiment/{experimentId}
    [HttpGet("experiment/{experimentId:int}")]
    public async Task<IActionResult> GetByExperiment(int experimentId)
    {
        var experiment = await _experimentService.GetByIdAsync(experimentId);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {experimentId} no encontrado." });

        var result = await _resultService.GetByExperimentAsync(experimentId);
        if (result is null)
            return NotFound(new
            {
                message = "Aún no se han generado resultados para este experimento."
            });

        return Ok(result);
    }

    // POST /api/results/experiment/{experimentId}/generate
    // Calcula y persiste los resultados finales a partir de las mediciones
    [HttpPost("experiment/{experimentId:int}/generate")]
    public async Task<IActionResult> Generate(
        int experimentId,
        [FromBody] GenerateResultRequest? request = null)
    {
        // Verificar que el operario es dueño del experimento
        var operatorId = ClaimsHelper.GetOperatorId(User);
        var experiment = await _experimentService.GetByIdAsync(experimentId);

        if (experiment is null)
            return NotFound(new { message = $"Experimento {experimentId} no encontrado." });

        if (experiment.OperatorId != operatorId)
            return Forbid();

        try
        {
            var result = await _resultService.GenerateAsync(experimentId, request?.Observations);
            return CreatedAtAction(
                nameof(GetByExperiment),
                new { experimentId },
                result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

// DTO local para el body de generación de resultados
public record GenerateResultRequest(string? Observations);
