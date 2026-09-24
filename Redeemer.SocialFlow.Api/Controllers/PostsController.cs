using Microsoft.AspNetCore.Mvc;
using Redeemer.SocialFlow.Application.SocialPosts;

namespace Redeemer.SocialFlow.Api.Controllers;

[ApiController]
[Route("api/posts")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class PostsController(ISocialPostService posts) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Create a draft post")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SocialPostDto>> Create([FromBody] CreatePostRequest request, CancellationToken cancellationToken)
    {
        var post = await posts.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = post.Id }, post);
    }

    [HttpGet("{id}")]
    [EndpointSummary("Get a post by ID")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.GetByIdAsync(id, cancellationToken) ?? throw new PostNotFoundException(id));

    [HttpGet]
    [EndpointSummary("List posts with optional platform, status, and creation date filters")]
    [EndpointDescription("Filters combine with AND. CreatedFrom and CreatedTo are inclusive DateTimeOffset instants on CreatedAt. Results are ordered newest first, then by ID. No pagination.")]
    [ProducesResponseType<IReadOnlyList<SocialPostDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SocialPostDto>>> List([FromQuery] ListPostsRequest request, CancellationToken cancellationToken) =>
        Ok(await posts.ListAsync(request, cancellationToken));

    [HttpPut("{id}")]
    [EndpointSummary("Replace a draft or rejected post's editable fields")]
    [EndpointDescription("Null optional values clear those fields.")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> Update(Guid id, [FromBody] UpdatePostRequest request, CancellationToken cancellationToken) =>
        Ok(await posts.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id}")]
    [EndpointSummary("Delete a draft or rejected post")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await posts.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id}/submit-for-review")]
    [EndpointSummary("Submit a post for review")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> SubmitForReview(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.SubmitForReviewAsync(id, cancellationToken));

    [HttpPost("{id}/approve")]
    [EndpointSummary("Approve a post awaiting review")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> Approve(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.ApproveAsync(id, cancellationToken));

    [HttpPost("{id}/reject")]
    [EndpointSummary("Reject a post awaiting review")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> Reject(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.RejectAsync(id, cancellationToken));

    [HttpPost("{id}/schedule")]
    [EndpointSummary("Schedule an approved post for a future instant")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> Schedule(Guid id, [FromBody] SchedulePostRequest request, CancellationToken cancellationToken) =>
        Ok(await posts.ScheduleAsync(id, request, cancellationToken));

    [HttpPost("{id}/cancel")]
    [EndpointSummary("Cancel a scheduled post")]
    [ProducesResponseType<SocialPostDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SocialPostDto>> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await posts.CancelAsync(id, cancellationToken));
}
