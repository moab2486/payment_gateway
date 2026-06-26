using System.Text.Json;
using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for SagaState.
/// Persists saga steps as a JSON-serialized column (JSONB in PostgreSQL).
/// </summary>
public class SagaStateConfiguration : IEntityTypeConfiguration<SagaState>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Configure(EntityTypeBuilder<SagaState> builder)
    {
        builder.ToTable("SagaStates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.TransactionReference)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(s => s.TransactionReference)
            .IsUnique();

        builder.Property(s => s.CurrentStepIndex)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.CreatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        builder.Property(s => s.UpdatedAtUtc)
            .IsRequired()
            .HasPrecision(3);

        // Store Steps as JSON column
        builder.Property(s => s.Steps)
            .HasColumnType("jsonb")
            .HasConversion(
                steps => JsonSerializer.Serialize(steps, JsonOptions),
                json => JsonSerializer.Deserialize<List<SagaStep>>(json, JsonOptions) ?? new List<SagaStep>());

        // Index on Status for querying sagas that need manual intervention
        builder.HasIndex(s => s.Status);
    }
}
