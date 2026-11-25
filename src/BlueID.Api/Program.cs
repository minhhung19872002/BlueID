using System.Net;
using BlueID.Api;
using BlueID.Application;
using BlueID.Application.Web;
using BlueID.Infrastructure;
using BlueID.Infrastructure.Configuration;
using BlueID.Infrastructure.Health;
using BlueID.Infrastructure.Localization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Serilog;

InfrastructureServices.UseInvariantProcessCulture();

var builder = WebApplication.CreateBuilder(args);
var settings = BidSettings.Load(builder.Configuration);
var testing = builder.Environment.IsEnvironment("Testing");
var plainHttpAllowed = testing || builder.Environment.IsDevelopment();

builder.AddBlueIdLogging(settings, "api");
builder.Services.AddBlueIdInfrastructure(settings);
builder.Services.AddBlueIdApplication();
builder.Services.AddBlueIdSessionCookie(plainHttpAllowed, isApi: true);
builder.Services.AddControllers()
    .AddJsonOptions(o => WebJson.Configure(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o =>
    {
        // Model binding errors use the same envelope and stable code as everything else.
        o.InvalidModelStateResponseFactory = ctx => new BadRequestObjectResult(ApiResponses.Error(ctx.HttpContext, "bad_request"));
    });
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var cidr in settings.TrustedProxies)
    {
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>("default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
app.UseMiddleware<InputFilterMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
}

app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture(JsonStringLocalizerFactory.Fallback);
    var cultures = JsonStringLocalizerFactory.Languages.Select(l => new System.Globalization.CultureInfo(l)).ToArray();
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.FallBackToParentCultures = true;
    // The user's language, then the tenant default (PROMPT 0.2), then Accept-Language.
    o.RequestCultureProviders.Insert(0, new UserCultureProvider());
});
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<TenantFromSessionMiddleware>();
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapControllers();
app.MapFallback((HttpContext http) => ApiResponses.WriteErrorAsync(http, HttpStatusCode.NotFound, "not_found")).AllowAnonymous();

app.Run();
