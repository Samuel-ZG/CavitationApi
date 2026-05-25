// ============================================================
//  PROGRAM.CS — VERSIÓN FINAL
//  Integra todas las partes (1-5) del sistema de cavitación
//  Archivo: Program.cs
// ============================================================

using System.Text;
using CavitationApi.Data;
using CavitationApi.Helpers;
using CavitationApi.Hubs;
using CavitationApi.Services;
using CavitationApi.BackgroundServices;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// LOGGING — Serilog
// ============================================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    // TODO NUBE: Agregar sink para Azure Application Insights / AWS CloudWatch
    // .WriteTo.ApplicationInsights(builder.Configuration["ApplicationInsights:ConnectionString"],
    //     TelemetryConverter.Traces)
    .WriteTo.File("logs/cavitation-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ============================================================
// BASE DE DATOS — Entity Framework Core
// ============================================================
builder.Services.AddDbContext<AppDbContext>(options =>
{
    // TODO NUBE: Cambiar UseSqlServer por:
    //   PostgreSQL → options.UseNpgsql(connectionString)
    //   Azure SQL  → mismo UseSqlServer pero con connection string de Azure
    //   MySQL      → options.UseMySql(connectionString, ServerVersion.AutoDetect(...))
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    );
});

// ============================================================
// AUTENTICACIÓN — JWT
// ============================================================
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "JWT Secret no configurado. Agrégalo en appsettings.json o como variable de entorno.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey         = new SymmetricSecurityKey(
                                       Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer           = true,
        ValidIssuer              = builder.Configuration["Jwt:Issuer"],
        ValidateAudience         = true,
        ValidAudience            = builder.Configuration["Jwt:Audience"],
        ValidateLifetime         = true,
        ClockSkew                = TimeSpan.FromMinutes(1)
    };

    // Necesario para que SignalR autentique con token en query string
    // Flutter lo envía como: /hubs/cavitation?access_token=xxx
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            var path  = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hubs"))
                context.Token = token;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ============================================================
// CORS
// ============================================================
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[] { "http://localhost" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("CavitationPolicy", policy =>
    {
        // TODO NUBE: Reemplazar con el dominio real del frontend en producción
        // Ejemplo: policy.WithOrigins("https://cavitation.tudominio.com")
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Requerido para SignalR
    });
});

// ============================================================
// SIGNALR — Tiempo real hacia Flutter
// ============================================================
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors  = builder.Environment.IsDevelopment();
    options.KeepAliveInterval     = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});
// TODO NUBE: Para escalar con múltiples instancias del servidor:
// .AddAzureSignalR(builder.Configuration["SignalR:AzureSignalRConnectionString"]);
// o .AddStackExchangeRedis(redisConnectionString);

// ============================================================
// AUTOMAPPER
// ============================================================
//builder.Services.AddAutoMapper(new[] { typeof(MappingProfiles) });
builder.Services.AddAutoMapper(typeof(MappingProfiles));

// ============================================================
// VALIDADORES — FluentValidation
// ============================================================
builder.Services.AddValidators();

// ============================================================
// SERVICIOS DE NEGOCIO
// ============================================================
builder.Services.AddScoped<IAuthService,        AuthService>();
builder.Services.AddScoped<IMachineService,     MachineService>();
builder.Services.AddScoped<IExperimentService,  ExperimentService>();
builder.Services.AddScoped<IMeasurementService, MeasurementService>();
builder.Services.AddScoped<IResultService,      ResultService>();
builder.Services.AddScoped<IAlertService,       AlertService>();
builder.Services.AddScoped<IReportService,      ReportService>();

// ============================================================
// ALMACENAMIENTO DE ARCHIVOS
// ============================================================
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
// TODO NUBE: Reemplazar por uno de estos según el proveedor:
// builder.Services.AddScoped<IFileStorageService, AzureBlobStorageService>();
// builder.Services.AddScoped<IFileStorageService, S3StorageService>();
// builder.Services.AddScoped<IFileStorageService, GcsStorageService>();

// ============================================================
// SERVICIOS EN BACKGROUND
// ============================================================

// 1. Cliente MQTT singleton — compartido por MachineService y los hosted services
builder.Services.AddSingleton<IMqttClientService, MqttClientService>();

// 2. Arranca la conexión MQTT al iniciar el servidor
builder.Services.AddHostedService<MqttStartupService>();

// 3. Escucha mensajes del broker, guarda mediciones y emite a SignalR
builder.Services.AddHostedService<MqttBackgroundService>();

// 4. Evalúa límites de temperatura y caudal, ejecuta apagado de emergencia
builder.Services.AddHostedService<EmergencyMonitorService>();

// ============================================================
// CONTROLLERS + JSON
// ============================================================
builder.Services.AddControllers();

// ============================================================
// SWAGGER
// ============================================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opts =>
{
    opts.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "Cavitation Control API",
        Version     = "v1",
        Description = "API para control de máquinas de cavitación — Sistema completo"
    });

    opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "Bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Ingresa el token JWT. Ejemplo: Bearer eyJhbGci..."
    });

    opts.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ============================================================
// OTROS SERVICIOS
// ============================================================
builder.Services.AddHttpContextAccessor();

// Límite de tamaño para subida de imágenes de microscopio (10 MB)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opts =>
{
    opts.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

// ============================================================
// BUILD
// ============================================================
var app = builder.Build();

// ============================================================
// MIDDLEWARE PIPELINE
// ============================================================

// 1. Manejo global de excepciones — siempre primero
app.UseMiddleware<GlobalExceptionMiddleware>();

// 2. Swagger — solo en desarrollo
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Cavitation API v1");
    c.RoutePrefix = "swagger";
});

// 3. Logging de requests
app.UseSerilogRequestLogging();

// 4. HTTPS redirect
// TODO NUBE: Descomentar en producción con certificado SSL válido
// app.UseHttpsRedirection();

// 5. CORS — antes de Auth
app.UseCors("CavitationPolicy");

// 6. Autenticación y autorización
app.UseAuthentication();
app.UseAuthorization();

// 7. Archivos estáticos (imágenes y reportes guardados en disco)
// TODO NUBE: Eliminar cuando se use Blob Storage / S3 / GCS
app.UseStaticFiles();

// ============================================================
// ENDPOINTS
// ============================================================
app.MapControllers();

// SignalR hub — Flutter se conecta aquí con WebSocket
// URL: ws://localhost:5000/hubs/cavitation?access_token={jwt}
app.MapHub<CavitationHub>("/hubs/cavitation");

// ============================================================
// MIGRACIÓN AUTOMÁTICA AL INICIAR
// ============================================================
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Migraciones de base de datos aplicadas correctamente.");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Error aplicando migraciones. Verifica la connection string.");
        throw;
    }
}

// ============================================================
// ARRANQUE
// ============================================================
Log.Information("╔══════════════════════════════════════════╗");
Log.Information("║     CavitationApi iniciada correctamente  ║");
Log.Information("╠══════════════════════════════════════════╣");
Log.Information("║  Swagger : http://localhost:5000/swagger  ║");
Log.Information("║  SignalR : ws://localhost:5000/hubs/      ║");
Log.Information("║           cavitation                      ║");
Log.Information("║  MQTT    : broker.emqx.io:1883            ║");
// TODO NUBE: Actualizar estas URLs con el dominio de producción
Log.Information("╚══════════════════════════════════════════╝");

await app.RunAsync();
