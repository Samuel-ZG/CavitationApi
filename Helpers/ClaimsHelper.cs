// ============================================================
//  CLAIMS HELPER — extrae datos del operario del JWT
//  Archivo: Helpers/ClaimsHelper.cs
// ============================================================

using System.Security.Claims;

namespace CavitationApi.Helpers;

public static class ClaimsHelper
{
    /// <summary>Obtiene el ID del operario desde los claims del JWT.</summary>
    public static int GetOperatorId(ClaimsPrincipal user)
    {
        var claim = user.FindFirst("operatorId") ?? user.FindFirst(ClaimTypes.NameIdentifier);
        if (claim is null || !int.TryParse(claim.Value, out var id))
            throw new UnauthorizedAccessException("Token inválido: operatorId no encontrado.");
        return id;
    }

    /// <summary>Obtiene el email del operario desde los claims.</summary>
    public static string GetEmail(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value
            ?? string.Empty;
    }

    /// <summary>Obtiene el nombre del operario desde los claims.</summary>
    public static string GetName(ClaimsPrincipal user)
    {
        return user.FindFirst("name")?.Value ?? string.Empty;
    }
}
