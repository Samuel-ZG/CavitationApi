// ============================================================
//  REPORTS CONTROLLER
//  Archivo: Controllers/ReportsController.cs
//
//  Endpoints:
//    POST /api/reports/experiment/{id}/pdf
//      → Genera el PDF y lo devuelve como descarga directa
//
//    POST /api/reports/experiment/{id}/word
//      → Genera el .docx y lo devuelve como descarga directa
//
//    GET  /api/reports/experiment/{id}/pdf
//      → Descarga el último PDF generado (si existe en disco)
//
//    GET  /api/reports/experiment/{id}/word
//      → Descarga el último Word generado (si existe en disco)
// ============================================================

using CavitationApi.Data;
using CavitationApi.Helpers;
using CavitationApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IExperimentService _experimentService;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportService reportService,
        IExperimentService experimentService,
        IWebHostEnvironment env,
        IConfiguration config,
        AppDbContext db,
        ILogger<ReportsController> logger)
    {
        _reportService     = reportService;
        _experimentService = experimentService;
        _env               = env;
        _config            = config;
        _db                = db;
        _logger            = logger;
    }

    // ── POST — Generar y descargar PDF ────────────────────────

    // POST /api/reports/experiment/{id}/pdf
    [HttpPost("experiment/{id:int}/pdf")]
    public async Task<IActionResult> GeneratePdf(int id)
    {
        var (authorized, error) = await AuthorizeOperatorAsync(id);
        if (!authorized) return error!;

        try
        {
            var filePath = await _reportService.GeneratePdfAsync(id);
            return await BuildFileDownloadResponse(filePath, "application/pdf", $"reporte_exp_{id}.pdf");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── POST — Generar y descargar Word ───────────────────────

    // POST /api/reports/experiment/{id}/word
    [HttpPost("experiment/{id:int}/word")]
    public async Task<IActionResult> GenerateWord(int id)
    {
        var (authorized, error) = await AuthorizeOperatorAsync(id);
        if (!authorized) return error!;

        try
        {
            var filePath = await _reportService.GenerateWordAsync(id);
            return await BuildFileDownloadResponse(filePath,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                $"reporte_exp_{id}.docx");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── GET — Descargar PDF existente ─────────────────────────

    // GET /api/reports/experiment/{id}/pdf
    [HttpGet("experiment/{id:int}/pdf")]
    public async Task<IActionResult> DownloadLatestPdf(int id)
        => await DownloadLatestReport(id, "pdf",
            "application/pdf",
            $"reporte_exp_{id}.pdf");

    // ── GET — Descargar Word existente ────────────────────────

    // GET /api/reports/experiment/{id}/word
    [HttpGet("experiment/{id:int}/word")]
    public async Task<IActionResult> DownloadLatestWord(int id)
        => await DownloadLatestReport(id, "docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"reporte_exp_{id}.docx");

    // ── Helpers privados ──────────────────────────────────────

    /// <summary>
    /// Verifica que el experimento existe y que el operario autenticado
    /// tiene permiso para acceder (es el dueño o tiene la máquina asignada).
    /// </summary>
    private async Task<(bool ok, IActionResult? error)> AuthorizeOperatorAsync(int experimentId)
    {
        var experiment = await _experimentService.GetByIdAsync(experimentId);

        if (experiment is null)
            return (false, NotFound(new { message = $"Experimento {experimentId} no encontrado." }));

        var operatorId = ClaimsHelper.GetOperatorId(User);
        if (experiment.OperatorId != operatorId)
            return (false, Forbid());

        return (true, null);
    }

    /// <summary>
    /// Lee el archivo generado desde disco y lo devuelve como FileResult.
    /// TODO NUBE: Reemplazar la lectura de disco por un stream desde Blob/S3.
    /// </summary>
    private async Task<IActionResult> BuildFileDownloadResponse(
        string savedPath, string contentType, string downloadName)
    {
        // savedPath es relativo a FileStorage:BasePath
        var basePath = _config["FileStorage:BasePath"] ?? "uploads";
        var fullPath = Path.Combine(_env.ContentRootPath, basePath, savedPath);

        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { message = "El archivo generado no se encontró en disco." });

        var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
        return File(bytes, contentType, downloadName);
    }

    /// <summary>
    /// Busca el reporte más reciente generado para el experimento por extensión.
    /// </summary>
    private async Task<IActionResult> DownloadLatestReport(
        int experimentId, string extension, string contentType, string downloadName)
    {
        var (authorized, error) = await AuthorizeOperatorAsync(experimentId);
        if (!authorized) return error!;

        var basePath    = _config["FileStorage:BasePath"] ?? "uploads";
        var reportDir   = Path.Combine(
            _env.ContentRootPath, basePath,
            extension == "pdf" ? "reports/pdf" : "reports/word");

        if (!Directory.Exists(reportDir))
            return NotFound(new { message = "No se han generado reportes aún." });

        // Buscar el archivo más reciente del experimento
        var files = Directory.GetFiles(reportDir, $"reporte_*{experimentId}*.{extension}")
            .Concat(Directory.GetFiles(reportDir, $"*.{extension}"))
            .OrderByDescending(f => System.IO.File.GetCreationTime(f))
            .ToList();

        // Filtrar por prefijo de nombre incluyendo el ID del experimento
        var experiment = await _experimentService.GetByIdAsync(experimentId);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {experimentId} no encontrado." });

        if (!files.Any())
            return NotFound(new
            {
                message = $"No existe un reporte .{extension} para este experimento. " +
                          $"Use POST /api/reports/experiment/{experimentId}/{extension} para generarlo."
            });

        var bytes = await System.IO.File.ReadAllBytesAsync(files.First());
        return File(bytes, contentType, downloadName);
    }
}
