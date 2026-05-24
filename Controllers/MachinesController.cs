// ============================================================
//  MACHINES CONTROLLER
//  Archivo: Controllers/MachinesController.cs
//
//  Endpoints:
//    GET    /api/machines                       (todas)
//    GET    /api/machines/my                    (del operario autenticado)
//    GET    /api/machines/{id}
//    POST   /api/machines
//    PUT    /api/machines/{id}
//    PATCH  /api/machines/{id}/status
//    POST   /api/machines/{id}/command          (start/stop/emergency_stop)
//    POST   /api/machines/{id}/assign/{opId}
//    DELETE /api/machines/{id}
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Helpers;
using CavitationApi.Models;
using CavitationApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/machines")]
[Authorize]
public class MachinesController : ControllerBase
{
    private readonly IMachineService _machineService;
    private readonly IValidator<CreateMachineRequest> _createValidator;
    private readonly IValidator<UpdateMachineRequest> _updateValidator;
    private readonly ILogger<MachinesController> _logger;

    public MachinesController(
        IMachineService machineService,
        IValidator<CreateMachineRequest> createValidator,
        IValidator<UpdateMachineRequest> updateValidator,
        ILogger<MachinesController> logger)
    {
        _machineService = machineService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    // GET /api/machines
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var machines = await _machineService.GetAllAsync();
        return Ok(machines);
    }

    // GET /api/machines/my
    // Retorna solo las máquinas asignadas al operario autenticado
    [HttpGet("my")]
    public async Task<IActionResult> GetMine()
    {
        var operatorId = ClaimsHelper.GetOperatorId(User);
        var machines = await _machineService.GetByOperatorAsync(operatorId);
        return Ok(machines);
    }

    // GET /api/machines/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var machine = await _machineService.GetByIdAsync(id);
        if (machine is null) return NotFound(new { message = $"Máquina {id} no encontrada." });
        return Ok(machine);
    }

    // POST /api/machines
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMachineRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var machine = await _machineService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = machine.Id }, machine);
    }

    // PUT /api/machines/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMachineRequest request)
    {
        var validation = await _updateValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var machine = await _machineService.UpdateAsync(id, request);
        if (machine is null) return NotFound(new { message = $"Máquina {id} no encontrada." });
        return Ok(machine);
    }

    // PATCH /api/machines/{id}/status
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] MachineStatusUpdateRequest request)
    {
        var success = await _machineService.UpdateStatusAsync(id, request.Status);
        if (!success) return NotFound(new { message = $"Máquina {id} no encontrada." });
        return NoContent();
    }

    // POST /api/machines/{id}/command
    // El operario envía un comando: "start", "stop" o "emergency_stop"
    [HttpPost("{id:int}/command")]
    public async Task<IActionResult> SendCommand(int id, [FromBody] MachineCommandRequest request)
    {
        var validCommands = new[] { "start", "stop", "emergency_stop" };
        if (!validCommands.Contains(request.Command))
            return BadRequest(new { message = $"Comando inválido. Use: {string.Join(", ", validCommands)}" });

        // Verificar que el operario tiene esta máquina asignada
        var operatorId = ClaimsHelper.GetOperatorId(User);
        var machine = await _machineService.GetByIdAsync(id);

        if (machine is null)
            return NotFound(new { message = $"Máquina {id} no encontrada." });

        if (machine.AssignedOperatorId != operatorId)
            return Forbid(); // El operario no tiene permiso sobre esta máquina

        var success = await _machineService.SendCommandAsync(id, request.Command);
        if (!success)
            return StatusCode(503, new { message = "No se pudo enviar el comando. Verifique la conexión MQTT." });

        _logger.LogInformation(
            "Operario {OperatorId} envió comando '{Command}' a máquina {MachineId}",
            operatorId, request.Command, id);

        return Ok(new { message = $"Comando '{request.Command}' enviado correctamente." });
    }

    // POST /api/machines/{id}/assign/{operatorId}
    [HttpPost("{id:int}/assign/{operatorId:int}")]
    public async Task<IActionResult> AssignOperator(int id, int operatorId)
    {
        var success = await _machineService.AssignOperatorAsync(id, operatorId);
        if (!success)
            return BadRequest(new { message = "No se pudo asignar el operario. Verifique los IDs." });

        return Ok(new { message = "Operario asignado correctamente." });
    }

    // DELETE /api/machines/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _machineService.DeleteAsync(id);
        if (!success)
            return BadRequest(new
            {
                message = "No se puede eliminar la máquina. Puede tener experimentos activos."
            });

        return NoContent();
    }
}
