using BlueID.Application.Common;
using BlueID.Application.Directory;
using BlueID.Application.Features.Tenants;
using BlueID.Application.Features.Users;
using BlueID.Application.Security;
using BlueID.Application.Web;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BlueID.Api.Controllers;

/// <summary>Users of the administrator's tenant (/admin/v1, PROMPT 5). No logic here: MediatR features.</summary>
[ApiController]
[Route("admin/v1/users")]
public sealed class AdminUsersController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<ApiResponse<Page<UserDto>>> List([FromQuery] string? search, [FromQuery] int top = 100, [FromQuery] string? cursor = null,
        [FromQuery] bool count = false, CancellationToken ct = default) =>
        ApiResponses.Ok(await mediator.Send(new ListUsers(search, top, cursor, count), ct));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.UsersRead)]
    public async Task<ApiResponse<UserDto>> Get(Guid id, CancellationToken ct) => ApiResponses.Ok(await mediator.Send(new GetUser(id), ct));

    [HttpPost]
    [RequirePermission(Permissions.UsersWrite)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] CreateUser body, CancellationToken ct)
    {
        var user = await mediator.Send(body, ct);
        return Created($"/admin/v1/users/{user.Id}", ApiResponses.Ok(user));
    }

    [HttpPatch("{id:guid}")]
    [RequirePermission(Permissions.UsersWrite)]
    public async Task<ApiResponse<UserDto>> Update(Guid id, [FromBody] UserPatch body, CancellationToken ct) =>
        ApiResponses.Ok(await mediator.Send(new UpdateUser(id, body), ct));

    [HttpPost("{id:guid}/disable")]
    [RequirePermission(Permissions.UsersWrite)]
    public async Task<ApiResponse<object?>> Disable(Guid id, CancellationToken ct)
    {
        await mediator.Send(new SetUserEnabled(id, false), ct);
        return ApiResponses.Ok<object?>(null);
    }

    [HttpPost("{id:guid}/enable")]
    [RequirePermission(Permissions.UsersWrite)]
    public async Task<ApiResponse<object?>> Enable(Guid id, CancellationToken ct)
    {
        await mediator.Send(new SetUserEnabled(id, true), ct);
        return ApiResponses.Ok<object?>(null);
    }

    [HttpPost("{id:guid}/revoke-sessions")]
    [RequirePermission(Permissions.UsersSecurity)]
    public async Task<ApiResponse<int>> RevokeSessions(Guid id, CancellationToken ct) => ApiResponses.Ok(await mediator.Send(new RevokeUserSessions(id), ct));
}

/// <summary>Platform operators: tenants of the whole service.</summary>
[ApiController]
[Route("admin/v1/platform/tenants")]
public sealed class PlatformTenantsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.PlatformTenantsRead)]
    public async Task<ApiResponse<IReadOnlyList<TenantDto>>> List(CancellationToken ct) => ApiResponses.Ok(await mediator.Send(new ListTenants(), ct));

    [HttpPost]
    [RequirePermission(Permissions.PlatformTenantsWrite)]
    public async Task<ActionResult<ApiResponse<TenantDto>>> Create([FromBody] CreateTenant body, CancellationToken ct)
    {
        var t = await mediator.Send(body, ct);
        return Created($"/admin/v1/platform/tenants/{t.Id}", ApiResponses.Ok(t));
    }
}
