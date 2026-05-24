// ============================================================
//  AUTH CONTROLLER
//  Archivo: Controllers/AuthController.cs
//
//  Endpoints:
//    POST /api/auth/login
//    POST /api/auth/refresh
//    POST /api/auth/logout
//    GET  /api/auth/me
//    POST /api/auth/operators          (crear operario)
//    PUT  /api/auth/operators/{id}     (actualizar operario)
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Helpers;
using CavitationApi.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CavitationApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<CreateOperatorRequest> _createOpValidator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IValidator<LoginRequest> loginValidator,
        IValidator<CreateOperatorRequest> createOpValidator,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _loginValidator = loginValidator;
        _createOpValidator = createOpValidator;
        _logger = logger;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var validation = await _loginValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        var response = await _authService.LoginAsync(request);
        if (response is null)
            return Unauthorized(new { message = "Credenciales incorrectas." });

        _logger.LogInformation("Login exitoso: {Email}", request.Email);
        return Ok(response);
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { message = "Refresh token requerido." });

        var response = await _authService.RefreshTokenAsync(request.RefreshToken);
        if (response is null)
            return Unauthorized(new { message = "Refresh token inválido o expirado." });

        return Ok(response);
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        await _authService.RevokeRefreshTokenAsync(request.RefreshToken);
        return NoContent();
    }

    // GET /api/auth/me
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var operatorId = ClaimsHelper.GetOperatorId(User);
        var op = await _authService.GetCurrentOperatorAsync(operatorId);
        if (op is null) return NotFound();
        return Ok(op);
    }

    // POST /api/auth/operators
    [HttpPost("operators")]
    [Authorize]
    public async Task<IActionResult> CreateOperator([FromBody] CreateOperatorRequest request)
    {
        var validation = await _createOpValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        // El servicio de creación de operario se maneja en AuthService
        // (extender AuthService con CreateOperatorAsync si se necesita)
        return StatusCode(501, new { message = "Implementar en AuthService.CreateOperatorAsync" });
    }
}
