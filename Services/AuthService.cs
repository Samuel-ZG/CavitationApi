// ============================================================
//  AUTH SERVICE — JWT + Refresh Tokens
//  Archivo: Services/AuthService.cs
// ============================================================

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using CavitationApi.Data;
using CavitationApi.DTOs;
using CavitationApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CavitationApi.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        IConfiguration config,
        IMapper mapper,
        ILogger<AuthService> logger)
    {
        _db = db;
        _config = config;
        _mapper = mapper;
        _logger = logger;
    }

    // ── Login ─────────────────────────────────────────────────

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var op = await _db.Operators
            .FirstOrDefaultAsync(o => o.Email == request.Email && o.IsActive);

        if (op is null || !BCrypt.Net.BCrypt.Verify(request.Password, op.PasswordHash))
        {
            _logger.LogWarning("Login fallido para {Email}", request.Email);
            return null;
        }

        return await BuildResponseAsync(op);
    }

    // ── Refresh token ─────────────────────────────────────────

    public async Task<LoginResponse?> RefreshTokenAsync(string refreshToken)
    {
        var stored = await _db.Set<RefreshTokenEntry>()
            .Include(r => r.Operator)
            .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.IsRevoked);

        if (stored is null || stored.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh token inválido o expirado");
            return null;
        }

        // Rotar el refresh token
        stored.IsRevoked = true;
        _db.Set<RefreshTokenEntry>().Update(stored);
        await _db.SaveChangesAsync();

        return await BuildResponseAsync(stored.Operator);
    }

    // ── Revocar ───────────────────────────────────────────────

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var stored = await _db.Set<RefreshTokenEntry>()
            .FirstOrDefaultAsync(r => r.Token == refreshToken);

        if (stored is not null)
        {
            stored.IsRevoked = true;
            await _db.SaveChangesAsync();
        }
    }

    // ── Operario actual ───────────────────────────────────────

    public async Task<OperatorDto?> GetCurrentOperatorAsync(int operatorId)
    {
        var op = await _db.Operators.FindAsync(operatorId);
        return op is null ? null : _mapper.Map<OperatorDto>(op);
    }

    // ── Helpers privados ──────────────────────────────────────

    private async Task<LoginResponse> BuildResponseAsync(Operator op)
    {
        var accessToken = GenerateAccessToken(op);
        var (refreshToken, refreshExpiry) = await GenerateAndSaveRefreshTokenAsync(op.Id);

        return new LoginResponse(
            Token: accessToken,
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddMinutes(
                int.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "60")),
            Operator: _mapper.Map<OperatorDto>(op)
        );
    }

    private string GenerateAccessToken(Operator op)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, op.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, op.Email),
            new Claim("operatorId", op.Id.ToString()),
            new Claim("name", op.Name),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                int.Parse(_config["Jwt:AccessTokenExpirationMinutes"] ?? "60")),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<(string token, DateTime expiry)> GenerateAndSaveRefreshTokenAsync(int operatorId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiry = DateTime.UtcNow.AddDays(
            int.Parse(_config["Jwt:RefreshTokenExpirationDays"] ?? "7"));

        _db.Set<RefreshTokenEntry>().Add(new RefreshTokenEntry
        {
            Token = token,
            OperatorId = operatorId,
            ExpiresAt = expiry,
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return (token, expiry);
    }
}
