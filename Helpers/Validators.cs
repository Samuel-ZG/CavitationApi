// ============================================================
//  VALIDACIONES — FluentValidation
//  Archivo: Helpers/Validators.cs
// ============================================================

using FluentValidation;
using CavitationApi.DTOs;

namespace CavitationApi.Helpers;

// ── Auth ──────────────────────────────────────────────────────

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El email es requerido.")
            .EmailAddress().WithMessage("Formato de email inválido.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es requerida.")
            .MinimumLength(6).WithMessage("La contraseña debe tener al menos 6 caracteres.");
    }
}

public class CreateOperatorRequestValidator : AbstractValidator<CreateOperatorRequest>
{
    public CreateOperatorRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El email es requerido.")
            .EmailAddress().WithMessage("Formato de email inválido.")
            .MaximumLength(200);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contraseña es requerida.")
            .MinimumLength(8).WithMessage("Mínimo 8 caracteres.")
            .Matches(@"[A-Z]").WithMessage("Debe contener al menos una mayúscula.")
            .Matches(@"[0-9]").WithMessage("Debe contener al menos un número.")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Debe contener al menos un caracter especial.");
    }
}

// ── Machine ───────────────────────────────────────────────────

public class CreateMachineRequestValidator : AbstractValidator<CreateMachineRequest>
{
    public CreateMachineRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.SerialNumber)
            .MaximumLength(50);

        RuleFor(x => x.TemperatureLimitWarning)
            .GreaterThan(0).WithMessage("El límite de advertencia debe ser mayor a 0.")
            .LessThan(x => x.TemperatureLimitCritical)
                .WithMessage("El límite de advertencia debe ser menor al límite crítico.");

        RuleFor(x => x.TemperatureLimitCritical)
            .GreaterThan(0).WithMessage("El límite crítico debe ser mayor a 0.")
            .LessThanOrEqualTo(200).WithMessage("El límite crítico no puede superar 200°C.");

        RuleFor(x => x.TargetFlowRate)
            .GreaterThan(0).WithMessage("El caudal objetivo debe ser mayor a 0.");

        RuleFor(x => x.FlowRateTolerance)
            .InclusiveBetween(0.01, 0.5)
                .WithMessage("La tolerancia de caudal debe estar entre 1% y 50%.");

        RuleFor(x => x.SensorIpAddress)
            .MaximumLength(50);

        RuleFor(x => x.SensorMqttTopic)
            .MaximumLength(200);
    }
}

public class UpdateMachineRequestValidator : AbstractValidator<UpdateMachineRequest>
{
    public UpdateMachineRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es requerido.")
            .MaximumLength(100);

        RuleFor(x => x.TemperatureLimitWarning)
            .GreaterThan(0)
            .LessThan(x => x.TemperatureLimitCritical)
                .WithMessage("El límite de advertencia debe ser menor al límite crítico.");

        RuleFor(x => x.TemperatureLimitCritical)
            .GreaterThan(0)
            .LessThanOrEqualTo(200);

        RuleFor(x => x.TargetFlowRate)
            .GreaterThan(0);

        RuleFor(x => x.FlowRateTolerance)
            .InclusiveBetween(0.01, 0.5);
    }
}

// ── Experiment ────────────────────────────────────────────────

public class CreateExperimentRequestValidator : AbstractValidator<CreateExperimentRequest>
{
    public CreateExperimentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre del experimento es requerido.")
            .MaximumLength(200);

        RuleFor(x => x.SampleName)
            .NotEmpty().WithMessage("El nombre de la muestra es requerido.")
            .MaximumLength(200);

        RuleFor(x => x.SampleDescription)
            .MaximumLength(1000);

        RuleFor(x => x.InitialTemperature)
            .InclusiveBetween(-50, 200)
                .WithMessage("La temperatura inicial debe estar entre -50°C y 200°C.");

        RuleFor(x => x.PrecedentExperimentId)
            .NotNull().When(x => x.HasPrecedent)
                .WithMessage("Debe indicar el experimento antecedente si HasPrecedent es true.");

        RuleFor(x => x.StartTime)
            .GreaterThanOrEqualTo(DateTime.UtcNow.AddMinutes(-5))
                .WithMessage("La fecha de inicio no puede ser en el pasado.");

        RuleFor(x => x.PlannedDuration)
            .Must(d => d.TotalMinutes >= 1)
                .WithMessage("La duración mínima es 1 minuto.")
            .Must(d => d.TotalHours <= 72)
                .WithMessage("La duración máxima es 72 horas.");

        RuleFor(x => x.TargetFlowRate)
            .GreaterThan(0).WithMessage("El caudal objetivo debe ser mayor a 0.");

        RuleFor(x => x.MachineId)
            .GreaterThan(0).WithMessage("Debe seleccionar una máquina válida.");
    }
}

public class UpdateExperimentRequestValidator : AbstractValidator<UpdateExperimentRequest>
{
    public UpdateExperimentRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().MaximumLength(200);

        RuleFor(x => x.SampleDescription)
            .MaximumLength(1000);

        RuleFor(x => x.TargetFlowRate)
            .GreaterThan(0);

        RuleFor(x => x.PlannedDuration)
            .Must(d => d.TotalMinutes >= 1)
            .Must(d => d.TotalHours <= 72);
    }
}

// ── Measurement ───────────────────────────────────────────────

public class CreateMeasurementRequestValidator : AbstractValidator<CreateMeasurementRequest>
{
    public CreateMeasurementRequestValidator()
    {
        RuleFor(x => x.ExperimentId)
            .GreaterThan(0);

        RuleFor(x => x.Temperature)
            .InclusiveBetween(-50, 300)
                .WithMessage("Temperatura fuera de rango razonable (-50 a 300°C).");

        RuleFor(x => x.FlowRate)
            .GreaterThanOrEqualTo(0)
                .WithMessage("El caudal no puede ser negativo.");

        RuleFor(x => x.Pressure)
            .GreaterThanOrEqualTo(0).When(x => x.Pressure.HasValue)
                .WithMessage("La presión no puede ser negativa.");
    }
}
