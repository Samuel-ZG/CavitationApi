// ============================================================
//  DATABASE CONTEXT — Entity Framework Core
//  Archivo: Data/AppDbContext.cs
// ============================================================

using Microsoft.EntityFrameworkCore;
using CavitationApi.Models;

namespace CavitationApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Experiment> Experiments => Set<Experiment>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<ExperimentResult> ExperimentResults => Set<ExperimentResult>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<RefreshTokenEntry> RefreshTokens => Set<RefreshTokenEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Operator ──────────────────────────────────────────
        modelBuilder.Entity<Operator>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Name).HasMaxLength(100).IsRequired();
            e.Property(o => o.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(o => o.Email).IsUnique();
        });

        // ── Machine ───────────────────────────────────────────
        modelBuilder.Entity<Machine>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).HasMaxLength(100).IsRequired();
            e.Property(m => m.SerialNumber).HasMaxLength(50);
            e.Property(m => m.SensorIpAddress).HasMaxLength(50);
            e.Property(m => m.SensorMqttTopic).HasMaxLength(200);
            e.Property(m => m.TemperatureLimitWarning).HasPrecision(6, 2);
            e.Property(m => m.TemperatureLimitCritical).HasPrecision(6, 2);
            e.Property(m => m.TargetFlowRate).HasPrecision(8, 3);
            e.Property(m => m.FlowRateTolerance).HasPrecision(5, 4);

            e.HasOne(m => m.AssignedOperator)
             .WithMany(o => o.AssignedMachines)
             .HasForeignKey(m => m.AssignedOperatorId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Experiment ────────────────────────────────────────
        modelBuilder.Entity<Experiment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.SampleName).HasMaxLength(200).IsRequired();
            e.Property(x => x.SampleDescription).HasMaxLength(1000);
            e.Property(x => x.InitialTemperature).HasPrecision(6, 2);
            e.Property(x => x.TargetFlowRate).HasPrecision(8, 3);
            e.Property(x => x.MicroscopeImageBeforePath).HasMaxLength(500);
            e.Property(x => x.MicroscopeImageAfterPath).HasMaxLength(500);
            e.Property(x => x.AbortReason).HasMaxLength(500);

            e.HasOne(x => x.PrecedentExperiment)
             .WithMany()
             .HasForeignKey(x => x.PrecedentExperimentId)
             .OnDelete(DeleteBehavior.NoAction); // ← fix: era SetNull

            e.HasOne(x => x.Machine)
             .WithMany(m => m.Experiments)
             .HasForeignKey(x => x.MachineId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Operator)
             .WithMany(o => o.Experiments)
             .HasForeignKey(x => x.OperatorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Measurement ───────────────────────────────────────
        modelBuilder.Entity<Measurement>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Temperature).HasPrecision(6, 2);
            e.Property(m => m.FlowRate).HasPrecision(8, 3);
            e.Property(m => m.FlowRateTarget).HasPrecision(8, 3);
            e.Property(m => m.FlowDeviation).HasPrecision(6, 4);
            e.Property(m => m.Pressure).HasPrecision(6, 2);
            e.Property(m => m.SubgroupMean).HasPrecision(8, 4);
            e.Property(m => m.SubgroupRange).HasPrecision(8, 4);

            e.HasIndex(m => new { m.ExperimentId, m.Timestamp });

            e.HasOne(m => m.Experiment)
             .WithMany(x => x.Measurements)
             .HasForeignKey(m => m.ExperimentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ExperimentResult ──────────────────────────────────
        modelBuilder.Entity<ExperimentResult>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.FinalTemperature).HasPrecision(6, 2);
            e.Property(r => r.AverageFlowRate).HasPrecision(8, 3);
            e.Property(r => r.FlowRateCompliance).HasPrecision(5, 2);
            e.Property(r => r.FlowRateControlMean).HasPrecision(8, 4);
            e.Property(r => r.FlowRateUpperControlLimit).HasPrecision(8, 4);
            e.Property(r => r.FlowRateLowerControlLimit).HasPrecision(8, 4);
            e.Property(r => r.Observations).HasMaxLength(2000);

            e.HasOne(r => r.Experiment)
             .WithOne(x => x.Result)
             .HasForeignKey<ExperimentResult>(r => r.ExperimentId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Alert ─────────────────────────────────────────────
        modelBuilder.Entity<Alert>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Message).HasMaxLength(500);
            e.Property(a => a.TriggerValue).HasPrecision(8, 3);
            e.Property(a => a.ThresholdValue).HasPrecision(8, 3);

            e.HasIndex(a => new { a.ExperimentId, a.TriggeredAt });

            e.HasOne(a => a.Experiment)
             .WithMany(x => x.Alerts)
             .HasForeignKey(a => a.ExperimentId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.Machine)
             .WithMany(m => m.Alerts)
             .HasForeignKey(a => a.MachineId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── RefreshToken ──────────────────────────────────────
        modelBuilder.Entity<RefreshTokenEntry>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Token).HasMaxLength(200).IsRequired();
            e.HasIndex(r => r.Token).IsUnique();

            e.HasOne(r => r.Operator)
             .WithMany(o => o.RefreshTokens)
             .HasForeignKey(r => r.OperatorId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Seed data ─────────────────────────────────────────
        modelBuilder.Entity<Operator>().HasData(new Operator
        {
            Id = 1,
            Name = "Operario Demo",
            Email = "operario@demo.com",
            PasswordHash = "$2a$11$TqUBSVuVFpSXNMQWpuMBEe5QbbUTKDpGNOhPaOPNEJXepiUFvL.Im",
            IsActive = true,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        for (int i = 1; i <= 4; i++)
        {
            modelBuilder.Entity<Machine>().HasData(new Machine
            {
                Id = i,
                Name = $"Máquina Cavitación #{i}",
                SerialNumber = $"CAV-{i:D4}",
                Status = MachineStatus.Available,
                TemperatureLimitWarning = 70.0,
                TemperatureLimitCritical = 85.0,
                TargetFlowRate = 5.0,
                FlowRateTolerance = 0.05,
                SensorIpAddress = $"192.168.1.{10 + i}",
                SensorMqttTopic = $"cavitation/machine/{i}/sensors",
                AssignedOperatorId = 1,
                IsConnected = false,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }
    }
}