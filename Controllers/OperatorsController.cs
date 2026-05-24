// ============================================================
//  OPERATORS CONTROLLER
//  Archivo: Controllers/OperatorsController.cs
//
//  Endpoints:
//    GET    /api/operators              (listar todos)
//    GET    /api/operators/{id}
//    POST   /api/operators              (crear)
//    PUT    /api/operators/{id}         (actualizar)
//    PATCH  /api/operators/{id}/active  (activar/desactivar)
// ============================================================

using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Helpers;
using CavitationApi.Models;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/operators")]
[Authorize]
public class OperatorsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMapper _mapper;
    private readonly IValidator<CreateOperatorRequest> _createValidator;
    private readonly ILogger<OperatorsController> _logger;

    public OperatorsController(
        AppDbContext db,
        IMapper mapper,
        IValidator<CreateOperatorRequest> createValidator,
        ILogger<OperatorsController> logger)
    {
        _db = db;
        _mapper = mapper;
        _createValidator = createValidator;
        _logger = logger;
    }

    // GET /api/operators
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var operators = await _db.Operators
            .AsNoTracking()
            .Select(o => new OperatorDto(
                o.Id, o.Name, o.Email, o.IsActive, o.CreatedAt))
            .ToListAsync();

        return Ok(operators);
    }

    // GET /api/operators/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var op = await _db.Operators
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (op is null) return NotFound(new { message = $"Operario {id} no encontrado." });
        return Ok(_mapper.Map<OperatorDto>(op));
    }

    // GET /api/operators/{id}/machines
    // Muestra las máquinas asignadas a un operario
    [HttpGet("{id:int}/machines")]
    public async Task<IActionResult> GetMachines(int id)
    {
        var exists = await _db.Operators.AnyAsync(o => o.Id == id);
        if (!exists) return NotFound(new { message = $"Operario {id} no encontrado." });

        var machines = await _db.Machines
            .Where(m => m.AssignedOperatorId == id)
            .AsNoTracking()
            .Select(m => new
            {
                m.Id,
                m.Name,
                m.SerialNumber,
                m.Status,
                m.IsConnected,
                m.TargetFlowRate,
                m.TemperatureLimitWarning,
                m.TemperatureLimitCritical
            })
            .ToListAsync();

        return Ok(machines);
    }

    // POST /api/operators
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOperatorRequest request)
    {
        var validation = await _createValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var emailExists = await _db.Operators.AnyAsync(o => o.Email == request.Email);
        if (emailExists)
            return Conflict(new { message = "Ya existe un operario con ese email." });

        var op = new Operator
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Operators.Add(op);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Nuevo operario creado: {Email}", op.Email);
        return CreatedAtAction(nameof(GetById), new { id = op.Id }, _mapper.Map<OperatorDto>(op));
    }

    // PUT /api/operators/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateOperatorRequest request)
    {
        var op = await _db.Operators.FindAsync(id);
        if (op is null) return NotFound(new { message = $"Operario {id} no encontrado." });

        // No puede actualizarse el email a uno ya en uso por otro operario
        var emailTaken = await _db.Operators
            .AnyAsync(o => o.Email == request.Email && o.Id != id);
        if (emailTaken)
            return Conflict(new { message = "El email ya está en uso por otro operario." });

        op.Name = request.Name;
        op.Email = request.Email;
        op.IsActive = request.IsActive;
        await _db.SaveChangesAsync();

        return Ok(_mapper.Map<OperatorDto>(op));
    }

    // PATCH /api/operators/{id}/active
    [HttpPatch("{id:int}/active")]
    public async Task<IActionResult> ToggleActive(int id, [FromBody] bool isActive)
    {
        var op = await _db.Operators.FindAsync(id);
        if (op is null) return NotFound(new { message = $"Operario {id} no encontrado." });

        // No desactivarse a sí mismo
        var currentOperatorId = ClaimsHelper.GetOperatorId(User);
        if (op.Id == currentOperatorId && !isActive)
            return BadRequest(new { message = "No puedes desactivar tu propio usuario." });

        op.IsActive = isActive;
        await _db.SaveChangesAsync();

        return Ok(new { message = $"Operario {(isActive ? "activado" : "desactivado")} correctamente." });
    }
}
