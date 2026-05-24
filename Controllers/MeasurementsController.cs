// ============================================================
//  MEASUREMENTS CONTROLLER
//  Archivo: Controllers/MeasurementsController.cs
//
//  Endpoints:
//    GET  /api/measurements/experiment/{experimentId}
//    POST /api/measurements
//    GET  /api/measurements/experiment/{experimentId}/control-chart
//    GET  /api/measurements/experiment/{experimentId}/control-chart?subgroupSize=5
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/measurements")]
[Authorize]
public class MeasurementsController : ControllerBase
{
    private readonly IMeasurementService _measurementService;
    private readonly IExperimentService _experimentService;
    private readonly IValidator<CreateMeasurementRequest> _createValidator;
    private readonly ILogger<MeasurementsController> _logger;

    public MeasurementsController(
        IMeasurementService measurementService,
        IExperimentService experimentService,
        IValidator<CreateMeasurementRequest> createValidator,
        ILogger<MeasurementsController> logger)
    {
        _measurementService = measurementService;
        _experimentService  = experimentService;
        _createValidator    = createValidator;
        _logger             = logger;
    }

    // GET /api/measurements/experiment/{experimentId}
    // Retorna todas las mediciones de un experimento en orden cronológico
    [HttpGet("experiment/{experimentId:int}")]
    public async Task<IActionResult> GetByExperiment(int experimentId)
    {
        var experiment = await _experimentService.GetByIdAsync(experimentId);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {experimentId} no encontrado." });

        var measurements = await _measurementService.GetByExperimentAsync(experimentId);
        return Ok(measurements);
    }

    // POST /api/measurements
    // Registro manual de medición (sin sensor físico)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMeasurementRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        try
        {
            var measurement = await _measurementService.RecordAsync(request);
            return CreatedAtAction(
                nameof(GetByExperiment),
                new { experimentId = request.ExperimentId },
                measurement);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET /api/measurements/experiment/{experimentId}/control-chart?subgroupSize=5
    // Retorna los datos calculados para el gráfico de control de medias X̄-R
    [HttpGet("experiment/{experimentId:int}/control-chart")]
    public async Task<IActionResult> GetControlChart(
        int experimentId,
        [FromQuery] int subgroupSize = 5)
    {
        if (subgroupSize < 2 || subgroupSize > 10)
            return BadRequest(new { message = "El tamaño de subgrupo debe estar entre 2 y 10." });

        var experiment = await _experimentService.GetByIdAsync(experimentId);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {experimentId} no encontrado." });

        var chartData = await _measurementService
            .GetControlChartDataAsync(experimentId, subgroupSize);

        return Ok(chartData);
    }
    
    // GET /api/measurements/experiment/{experimentId}/individual-chart
    // Carta de individuales X-MR para un experimento
    [HttpGet("experiment/{experimentId:int}/individual-chart")]
    public async Task<IActionResult> GetIndividualChart(int experimentId)
    {
        var experiment = await _experimentService.GetByIdAsync(experimentId);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {experimentId} no encontrado." });

        var chartData = await _measurementService
            .GetIndividualChartDataAsync(experimentId);

        return Ok(chartData);
    }

// GET /api/measurements/cross-experiment-chart
// Carta entre experimentos (por máquina, operario o todos)
// ?machineId=1  o  ?operatorId=2  o  ?maxExperiments=25
    [HttpGet("cross-experiment-chart")]
    public async Task<IActionResult> GetCrossExperimentChart(
        [FromQuery] int? machineId = null,
        [FromQuery] int? operatorId = null,
        [FromQuery] int maxExperiments = 25)
    {
        if (maxExperiments < 2 || maxExperiments > 100)
            return BadRequest(new { message = "maxExperiments debe estar entre 2 y 100." });

        var chartData = await _measurementService
            .GetCrossExperimentChartDataAsync(machineId, operatorId, maxExperiments);

        return Ok(chartData);
    }
}
