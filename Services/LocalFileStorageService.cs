// ============================================================
//  FILE STORAGE SERVICE — almacenamiento local
//  Archivo: Services/LocalFileStorageService.cs
//
//  TODO NUBE: Crear AzureBlobStorageService o S3StorageService
//  implementando la misma interfaz IFileStorageService y
//  registrarlo en Program.cs en lugar de este servicio.
// ============================================================

using CavitationApi.Services;

namespace CavitationApi.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalFileStorageService> _logger;

    // TODO NUBE: El IWebHostEnvironment ya no aplica con blob storage.
    // Inyectar en su lugar el SDK del proveedor de nube (BlobServiceClient, IAmazonS3).
    public LocalFileStorageService(
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<LocalFileStorageService> logger)
    {
        _config = config;
        _env = env;
        _logger = logger;
    }

    public async Task<string> SaveImageAsync(Stream imageStream, string fileName, string folder)
    {
        // TODO NUBE: Reemplazar con upload a container de Blob / bucket de S3
        var basePath = Path.Combine(
            _env.ContentRootPath,
            _config["FileStorage:BasePath"] ?? "uploads",
            folder
        );

        Directory.CreateDirectory(basePath);

        var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
        var fullPath = Path.Combine(basePath, safeFileName);

        await using var fileStream = new FileStream(fullPath, FileMode.Create);
        await imageStream.CopyToAsync(fileStream);

        _logger.LogInformation("Imagen guardada en {Path}", fullPath);

        // Retorna la ruta relativa para guardar en BD
        return Path.Combine(folder, safeFileName).Replace("\\", "/");
    }

    public Task<bool> DeleteAsync(string filePath)
    {
        // TODO NUBE: Reemplazar con DeleteBlobAsync / DeleteObjectAsync
        try
        {
            var fullPath = Path.Combine(
                _env.ContentRootPath,
                _config["FileStorage:BasePath"] ?? "uploads",
                filePath
            );

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando archivo {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    public string GetPublicUrl(string filePath)
    {
        // TODO NUBE: Retornar URL de Azure CDN / CloudFront / firma de S3
        return $"/uploads/{filePath.Replace("\\", "/")}";
    }
}
