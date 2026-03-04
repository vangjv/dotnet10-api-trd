using Acme.TaskHub.Domain.Entities;
using Acme.TaskHub.Application.Models;

namespace Acme.TaskHub.Application.Mapping;

/// <summary>
/// Extension methods for mapping between <see cref="TodoItem"/> domain entities and DTOs.
/// </summary>
public static class TodoItemMappingExtensions
{
    /// <summary>
    /// Maps a <see cref="TodoItem"/> entity to a <see cref="TodoItemResponse"/> DTO.
    /// </summary>
    public static TodoItemResponse ToResponse(this TodoItem entity)
    {
        return new TodoItemResponse
        {
            Id = entity.Id,
            Title = entity.Title,
            Description = entity.Description,
            Priority = entity.Priority,
            Status = entity.Status,
            DueDate = entity.DueDate,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc,
            CompletedAtUtc = entity.CompletedAtUtc,
        };
    }

    /// <summary>
    /// Maps a collection of <see cref="TodoItem"/> entities to response DTOs.
    /// </summary>
    public static IReadOnlyList<TodoItemResponse> ToResponseList(this IEnumerable<TodoItem> entities)
    {
        return entities.Select(e => e.ToResponse()).ToList().AsReadOnly();
    }
}
