// ============================================================
//  REPORT SERVICE
//  Archivo: Services/ReportService.cs
//
//  Orquesta:
//    1. ReportDataBuilder  → consolida datos del experimento
//    2. PdfReportGenerator → genera bytes del PDF con iText7
//    3. WordReportGenerator→ genera bytes del .docx con DocX
//    4. Guarda el archivo en FileStorage y retorna la ruta
// ============================================================

using CavitationApi.Data;
using CavitationApi.Reports;

namespace CavitationApi.Services;

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly IMeasurementService _measurementService;
    private readonly IFileStorageService _fileStorage;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        AppDbContext db,
        IMeasurementService measurementService,
        IFileStorageService fileStorage,
        IWebHostEnvironment env,
        IConfiguration config,
        ILogger<ReportService> logger)
    {
        _db                 = db;
        _measurementService = measurementService;
        _fileStorage        = fileStorage;
        _env                = env;
        _config             = config;
        _logger             = logger;
    }

    // ── PDF ───────────────────────────────────────────────────

    public async Task<string> GeneratePdfAsync(int experimentId)
    {
        _logger.LogInformation("Generando reporte PDF para experimento {Id}", experimentId);

        var data = await BuildReportDataAsync(experimentId);

        var generator = new PdfReportGenerator();
        var pdfBytes  = generator.Generate(data);

        var fileName  = BuildFileName(data, "pdf");
        var savedPath = await SaveReportAsync(pdfBytes, fileName, "reports/pdf");

        _logger.LogInformation(
            "Reporte PDF generado: {Path} ({Size} bytes)", savedPath, pdfBytes.Length);

        return savedPath;
    }

    // ── Word ──────────────────────────────────────────────────

    public async Task<string> GenerateWordAsync(int experimentId)
    {
        _logger.LogInformation("Generando reporte Word para experimento {Id}", experimentId);

        var data = await BuildReportDataAsync(experimentId);

        var generator  = new WordReportGenerator();
        var wordBytes  = generator.Generate(data);

        var fileName   = BuildFileName(data, "docx");
        var savedPath  = await SaveReportAsync(wordBytes, fileName, "reports/word");

        _logger.LogInformation(
            "Reporte Word generado: {Path} ({Size} bytes)", savedPath, wordBytes.Length);

        return savedPath;
    }

    // ── Helpers privados ──────────────────────────────────────

    private async Task<ReportData> BuildReportDataAsync(int experimentId)
    {
        var builder = new ReportDataBuilder(
            _db, _measurementService, _env, _config);

        return await builder.BuildAsync(experimentId);
    }

    private async Task<string> SaveReportAsync(byte[] bytes, string fileName, string folder)
    {
        // TODO NUBE: LocalFileStorageService.SaveImageAsync guarda en disco local.
        // Con Blob/S3 el mismo método subirá a nube sin cambiar este código.
        using var ms = new MemoryStream(bytes);
        return await _fileStorage.SaveImageAsync(ms, fileName, folder);
    }

    private static string BuildFileName(ReportData data, string extension)
    {
        // Nombre seguro: exp_{id}_{nombre sanitizado}_{fecha}.{ext}
        var safeName = string.Concat(
            data.ExperimentName
                .ToLower()
                .Replace(" ", "_")
                .Where(c => char.IsLetterOrDigit(c) || c == '_')
        ).Substring(0, Math.Min(30, data.ExperimentName.Length));

        return $"reporte_{safeName}_{data.GeneratedAt:yyyyMMdd_HHmmss}.{extension}";
    }
}
