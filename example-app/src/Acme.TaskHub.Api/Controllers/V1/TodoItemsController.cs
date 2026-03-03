using System.Diagnostics;
using Acme.TaskHub.Application.Models;
using Acme.TaskHub.Application.Services;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Acme.TaskHub.Api.Controllers.V1;

/// <summary>
/// Manages to-do items — full CRUD with filtering, paging, and status transitions.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("v{version:apiVersion}/todoitems")]
[Produces("application/json")]
[Authorize] // TRD §8.2: default deny — every endpoint requires auth.
public sealed class TodoItemsController : ControllerBase
{
    private readonly ITodoItemService _service;
    private readonly IValidator<CreateTodoItemRequest> _createValidator;
    private readonly IValidator<UpdateTodoItemRequest> _updateValidator;
    private readonly IValidator<UpdateTodoStatusRequest> _statusValidator;
    private readonly IValidator<TodoItemQueryParameters> _queryValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TodoItemsController"/> class.
    /// </summary>
    public TodoItemsController(
        ITodoItemService service,
        IValidator<CreateTodoItemRequest> createValidator,
        IValidator<UpdateTodoItemRequest> updateValidator,
        IValidator<UpdateTodoStatusRequest> statusValidator,
        IValidator<TodoItemQueryParameters> queryValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _statusValidator = statusValidator;
        _queryValidator = queryValidator;
    }

    /// <summary>
    /// Lists to-do items with optional filtering and paging.
    /// </summary>
    /// <param name="query">Query parameters for filtering and paging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paged list of to-do items.</returns>
    /// <response code="200">Returns the paged list.</response>
    /// <response code="400">Validation failed.</response>
    [HttpGet]
    [Authorize(Policy = "Api.Read")]
    [ProducesResponseType(typeof(PagedResult<TodoItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] TodoItemQueryParameters query,
        CancellationToken cancellationToken)
    {
        var validation = await _queryValidator.ValidateAsync(query, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(validation);
        }

        var result = await _service.GetPagedAsync(query, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Gets a single to-do item by its identifier.
    /// </summary>
    /// <param name="id">The to-do item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The to-do item.</returns>
    /// <response code="200">Returns the item.</response>
    /// <response code="404">Item not found.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Api.Read")]
    [ProducesResponseType(typeof(TodoItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var item = await _service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return NotFoundProblem(id);
        }

        return Ok(item);
    }

    /// <summary>
    /// Creates a new to-do item.
    /// </summary>
    /// <param name="request">The creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created to-do item.</returns>
    /// <response code="201">Item created successfully.</response>
    /// <response code="400">Validation failed.</response>
    [HttpPost]
    [Authorize(Policy = "Api.Write")]
    [ProducesResponseType(typeof(TodoItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(validation);
        }

        var created = await _service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return CreatedAtAction(nameof(GetById), new { id = created.Id, version = "1.0" }, created);
    }

    /// <summary>
    /// Updates an existing to-do item.
    /// </summary>
    /// <param name="id">The to-do item id.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated to-do item.</returns>
    /// <response code="200">Item updated successfully.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">Item not found.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Api.Write")]
    [ProducesResponseType(typeof(TodoItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _updateValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(validation);
        }

        var updated = await _service.UpdateAsync(id, request, cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            return NotFoundProblem(id);
        }

        return Ok(updated);
    }

    /// <summary>
    /// Transitions the status of a to-do item (e.g., Pending -> InProgress -> Completed).
    /// </summary>
    /// <param name="id">The to-do item id.</param>
    /// <param name="request">The status transition request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated to-do item.</returns>
    /// <response code="200">Status updated successfully.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="404">Item not found.</response>
    /// <response code="409">Invalid status transition.</response>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "Api.Write")]
    [ProducesResponseType(typeof(TodoItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateTodoStatusRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _statusValidator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return ValidationProblem(validation);
        }

        var updated = await _service.UpdateStatusAsync(id, request, cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            return NotFoundProblem(id);
        }

        return Ok(updated);
    }

    /// <summary>
    /// Deletes a to-do item.
    /// </summary>
    /// <param name="id">The to-do item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content.</returns>
    /// <response code="204">Item deleted successfully.</response>
    /// <response code="404">Item not found.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "Api.Write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        bool deleted = await _service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        if (!deleted)
        {
            return NotFoundProblem(id);
        }

        return NoContent();
    }

    /// <summary>
    /// Builds a standard 404 ProblemDetails response.
    /// </summary>
    private ObjectResult NotFoundProblem(Guid id)
    {
        var problem = new ProblemDetails
        {
            Title = "Todo item not found.",
            Status = StatusCodes.Status404NotFound,
            Detail = $"No todo item with id '{id}' was found.",
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        problem.Extensions["errorCode"] = "TODO_ITEM_NOT_FOUND";

        return StatusCode(StatusCodes.Status404NotFound, problem);
    }

    /// <summary>
    /// Converts FluentValidation results into a RFC 7807 ValidationProblemDetails response.
    /// </summary>
    private ObjectResult ValidationProblem(FluentValidation.Results.ValidationResult validationResult)
    {
        var errors = validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest,
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return StatusCode(StatusCodes.Status400BadRequest, problem);
    }
}
