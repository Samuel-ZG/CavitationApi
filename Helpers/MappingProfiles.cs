// ============================================================
//  AUTOMAPPER PROFILES
//  Archivo: Helpers/MappingProfiles.cs
// ============================================================

using AutoMapper;
using CavitationApi.DTOs;
using CavitationApi.Models;

namespace CavitationApi.Helpers;

public class MappingProfiles : Profile
{
    public MappingProfiles()
    {
        // ── Operator ──────────────────────────────────────────
        CreateMap<Operator, OperatorDto>();

        CreateMap<CreateOperatorRequest, Operator>()
            .ForMember(dest => dest.PasswordHash, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow))
            .ForMember(dest => dest.IsActive, opt => opt.MapFrom(_ => true));

        // ── Machine ───────────────────────────────────────────
        CreateMap<Machine, MachineDto>()
            .ForMember(dest => dest.AssignedOperatorName,
                opt => opt.MapFrom(src => src.AssignedOperator != null
                    ? src.AssignedOperator.Name
                    : null));

        CreateMap<CreateMachineRequest, Machine>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(_ => MachineStatus.Available))
            .ForMember(dest => dest.IsConnected, opt => opt.MapFrom(_ => false))
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow))
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow));

        CreateMap<UpdateMachineRequest, Machine>()
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow));

        // ── Experiment ────────────────────────────────────────
        CreateMap<Experiment, ExperimentDto>()
            .ForMember(dest => dest.MachineName,
                opt => opt.MapFrom(src => src.Machine.Name))
            .ForMember(dest => dest.OperatorName,
                opt => opt.MapFrom(src => src.Operator.Name))
            .ForMember(dest => dest.PrecedentExperimentName,
                opt => opt.MapFrom(src => src.PrecedentExperiment != null
                    ? src.PrecedentExperiment.Name
                    : null));

        CreateMap<Experiment, ExperimentSummaryDto>()
            .ForMember(dest => dest.MachineName,
                opt => opt.MapFrom(src => src.Machine.Name))
            .ForMember(dest => dest.OperatorName,
                opt => opt.MapFrom(src => src.Operator.Name));

        CreateMap<CreateExperimentRequest, Experiment>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom(_ => ExperimentStatus.Pending))
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(_ => DateTime.UtcNow));

        // ── Measurement ───────────────────────────────────────
        CreateMap<Measurement, MeasurementDto>();

        CreateMap<CreateMeasurementRequest, Measurement>()
            .ForMember(dest => dest.Timestamp, opt => opt.MapFrom(_ => DateTime.UtcNow))
            .ForMember(dest => dest.FlowRateTarget, opt => opt.Ignore())
            .ForMember(dest => dest.FlowDeviation, opt => opt.Ignore())
            .ForMember(dest => dest.SubgroupMean, opt => opt.Ignore())
            .ForMember(dest => dest.SubgroupRange, opt => opt.Ignore())
            .ForMember(dest => dest.SubgroupNumber, opt => opt.Ignore());

        // ── Result ────────────────────────────────────────────
        CreateMap<ExperimentResult, ExperimentResultDto>()
            .ForMember(dest => dest.ExperimentName,
                opt => opt.MapFrom(src => src.Experiment.Name))
            .ForMember(dest => dest.MicroscopeImageBeforePath,
                opt => opt.MapFrom(src => src.Experiment.MicroscopeImageBeforePath))
            .ForMember(dest => dest.MicroscopeImageAfterPath,
                opt => opt.MapFrom(src => src.Experiment.MicroscopeImageAfterPath));

        // ── Alert ─────────────────────────────────────────────
        CreateMap<Alert, AlertDto>()
            .ForMember(dest => dest.MachineName,
                opt => opt.MapFrom(src => src.Machine.Name));
    }
}
