using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardManagement.Infrastructure.Persistence.Configurations;

public class ProcessorSessionConfiguration : IEntityTypeConfiguration<ProcessorSession>
{
    public void Configure(EntityTypeBuilder<ProcessorSession> builder)
    {
        builder.ToTable("ProcessorSessions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ProcessorType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.Endpoint)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(p => p.IsSignedOn)
            .IsRequired();

        builder.Property(p => p.LastSignOnUtc)
            .HasPrecision(3);

        builder.Property(p => p.LastHeartbeatUtc)
            .HasPrecision(3);
    }
}
