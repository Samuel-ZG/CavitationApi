// ============================================================
//  EXPERIMENTS CONTROLLER
//  Archivo: Controllers/ExperimentsController.cs
//
//  Endpoints:
//    GET    /api/experiments                        (paginado, filtros)
//    GET    /api/experiments/{id}
//    GET    /api/experiments/machine/{machineId}
//    POST   /api/experiments
//    PUT    /api/experiments/{id}
//    PATCH  /api/experiments/{id}/status
//    POST   /api/experiments/{id}/image/before      (multipart)
//    POST   /api/experiments/{id}/image/after       (multipart)
//    DELETE /api/experiments/{id}
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Helpers;
using CavitationApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/experiments")]
[Authorize]
public class ExperimentsController : ControllerBase
{
    private readonly IExperimentService _experimentService;
    private readonly IFileStorageService _fileStorage;
    private readonly IValidator<CreateExperimentRequest> _createValidator;
    private readonly IValidator<UpdateExperimentRequest> _updateValidator;
    private readonly ILogger<ExperimentsController> _logger;

    // Tipos de imagen permitidos para el microscopio
    private static readonly string[] AllowedImageTypes =
        { "image/jpeg", "image/png", "image/tiff", "image/bmp" };

    private const long MaxImageSizeBytes = 10 * 1024 * 1024; // 10 MB

    public ExperimentsController(
        IExperimentService experimentService,
        IFileStorageService fileStorage,
        IValidator<CreateExperimentRequest> createValidator,
        IValidator<UpdateExperimentRequest> updateValidator,
        ILogger<ExperimentsController> logger)
    {
        _experimentService = experimentService;
        _fileStorage = fileStorage;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    // GET /api/experiments?page=1&pageSize=20&machineId=1
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] int? machineId = null,
        [FromQuery] bool myOnly = false)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { message = "Parámetros de paginación inválidos." });

        var operatorId = myOnly ? ClaimsHelper.GetOperatorId(User) : (int?)null;
        var result = await _experimentService.GetPagedAsync(page, pageSize, operatorId, machineId);
        return Ok(result);
    }

    // GET /api/experiments/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var experiment = await _experimentService.GetByIdAsync(id);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {id} no encontrado." });

        return Ok(experiment);
    }

    // GET /api/experiments/machine/{machineId}
    [HttpGet("machine/{machineId:int}")]
    public async Task<IActionResult> GetByMachine(int machineId)
    {
        var experiments = await _experimentService.GetByMachineAsync(machineId);
        return Ok(experiments);
    }

    // POST /api/experiments
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExperimentRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var operatorId = ClaimsHelper.GetOperatorId(User);

        try
        {
            var experiment = await _experimentService.CreateAsync(request, operatorId);
            return CreatedAtAction(nameof(GetById), new { id = experiment.Id }, experiment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
    }

    // PUT /api/experiments/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateExperimentRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        try
        {
            var experiment = await _experimentService.UpdateAsync(id, request);
            if (experiment is null)
                return NotFound(new { message = $"Experimento {id} no encontrado." });

            return Ok(experiment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // PATCH /api/experiments/{id}/status
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id, [FromBody] UpdateExperimentStatusRequest request)
    {
        try
        {
            var success = await _experimentService.UpdateStatusAsync(id, request);
            if (!success)
                return NotFound(new { message = $"Experimento {id} no encontrado." });

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // POST /api/experiments/{id}/image/before
    // Sube la imagen de microscopio ANTES del proceso
    [HttpPost("{id:int}/image/before")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImageBefore(int id, IFormFile file)
    {
        return await UploadMicroscopeImage(id, file, isBefore: true);
    }

    // POST /api/experiments/{id}/image/after
    // Sube la imagen de microscopio DESPUÉS del proceso
    [HttpPost("{id:int}/image/after")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImageAfter(int id, IFormFile file)
    {
        return await UploadMicroscopeImage(id, file, isBefore: false);
    }

    // DELETE /api/experiments/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var success = await _experimentService.DeleteAsync(id);
            if (!success)
                return NotFound(new { message = $"Experimento {id} no encontrado." });

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Helper privado para subida de imágenes ────────────────

    private async Task<IActionResult> UploadMicroscopeImage(int id, IFormFile file, bool isBefore)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No se recibió ningún archivo." });

        if (file.Length > MaxImageSizeBytes)
            return BadRequest(new { message = "La imagen no puede superar 10 MB." });

        if (!AllowedImageTypes.Contains(file.ContentType.ToLower()))
            return BadRequest(new
            {
                message = $"Tipo de archivo no permitido. Use: {string.Join(", ", AllowedImageTypes)}"
            });

        var experiment = await _experimentService.GetByIdAsync(id);
        if (experiment is null)
            return NotFound(new { message = $"Experimento {id} no encontrado." });

        // Verificar que el operario es dueño del experimento
        var operatorId = ClaimsHelper.GetOperatorId(User);
        if (experiment.OperatorId != operatorId)
            return Forbid();

        var extension   = Path.GetExtension(file.FileName);
        var fileName    = $"exp_{id}_{(isBefore ? "before" : "after")}_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
        var folder      = "microscope";

        await using var stream = file.OpenReadStream();
        var savedPath = await _fileStorage.SaveImageAsync(stream, fileName, folder);

        await _experimentService.SetMicroscopeImageAsync(id, savedPath, isBefore);

        var publicUrl = _fileStorage.GetPublicUrl(savedPath);

        _logger.LogInformation(
            "Imagen de microscopio {Phase} subida para experimento {Id}: {Url}",
            isBefore ? "ANTES" : "DESPUÉS", id, publicUrl);

        return Ok(new
        {
            message  = "Imagen guardada correctamente.",
            path     = savedPath,
            url      = publicUrl,
            phase    = isBefore ? "before" : "after"
        });
    }
}
