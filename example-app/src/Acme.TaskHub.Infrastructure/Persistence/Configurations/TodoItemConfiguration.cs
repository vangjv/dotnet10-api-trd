using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Acme.TaskHub.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for <see cref="TodoItem"/>.
/// </summary>
internal sealed class TodoItemConfiguration : IEntityTypeConfiguration<TodoItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TodoItem> builder)
    {
        builder.ToTable("TodoItems");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .HasMaxLength(TodoItem.MaxTitleLength)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(TodoItem.MaxDescriptionLength);

        builder.Property(x => x.Priority)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // SQLite does not support DateTimeOffset natively; store as UTC ticks.
        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            v => v.ToUnixTimeMilliseconds(),
            v => DateTimeOffset.FromUnixTimeMilliseconds(v));

        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : null,
            v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null);

        builder.Property(x => x.CreatedAtUtc)
            .HasConversion(dateTimeOffsetConverter)
            .IsRequired();

        builder.Property(x => x.DueDate)
            .HasConversion(nullableDateTimeOffsetConverter);

        builder.Property(x => x.CompletedAtUtc)
            .HasConversion(nullableDateTimeOffsetConverter);

        // Indexes for common query patterns.
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => x.DueDate);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
