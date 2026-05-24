// ============================================================
//  REGISTRO DE VALIDADORES — extensión para Program.cs
//  Archivo: Helpers/ValidatorRegistration.cs
//
//  Agregar en Program.cs, antes de builder.Build():
//  builder.Services.AddValidators();
// ============================================================

using CavitationApi.DTOs;
using CavitationApi.Helpers;
using FluentValidation;

namespace CavitationApi.Helpers;

public static class ValidatorRegistration
{
    public static IServiceCollection AddValidators(this IServiceCollection services)
    {
        // Auth
        services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<IValidator<CreateOperatorRequest>, CreateOperatorRequestValidator>();

        // Machines
        services.AddScoped<IValidator<CreateMachineRequest>, CreateMachineRequestValidator>();
        services.AddScoped<IValidator<UpdateMachineRequest>, UpdateMachineRequestValidator>();

        // Experiments
        services.AddScoped<IValidator<CreateExperimentRequest>, CreateExperimentRequestValidator>();
        services.AddScoped<IValidator<UpdateExperimentRequest>, UpdateExperimentRequestValidator>();

        // Measurements
        services.AddScoped<IValidator<CreateMeasurementRequest>, CreateMeasurementRequestValidator>();

        return services;
    }
}
