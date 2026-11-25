using BlueID.Application.Features.Me;
using BlueID.Application.Features.Users;
using BlueID.Application.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BlueID.Api.Controllers;

/// <summary>"My account" (/me/v1): only the caller's own data; every handler scopes by the session's user id.</summary>
[ApiController]
[Route("me/v1")]
[SelfGuarded("acts only on the signed-in user's own profile and sessions")]
public sealed class MeController(ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ApiResponse<MeDto>> Get(CancellationToken ct) => ApiResponses.Ok(await mediator.Send(new GetMe(), ct));

    [HttpPatch("settings")]
    public async Task<ApiResponse<UserDto>> UpdateSettings([FromBody] UpdateMySettings body, CancellationToken ct) => ApiResponses.Ok(await mediator.Send(body, ct));

    [HttpGet("sessions")]
    public async Task<ApiResponse<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct) => ApiResponses.Ok(await mediator.Send(new ListMySessions(), ct));

    [HttpDelete("sessions/{id:guid}")]
    public async Task<ApiResponse<int>> RevokeSession(Guid id, CancellationToken ct) => ApiResponses.Ok(await mediator.Send(new RevokeMySession(id), ct));
}
