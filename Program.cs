using MaximoROWeb.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RathenaStatusService>();
builder.Services.AddScoped<AccountRegistrationService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("registration", httpContext =>
    {
        var clientIp =
            httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain";

        await context.HttpContext.Response.WriteAsync(
            "Too many registration attempts. Please wait one minute and try again.",
            cancellationToken);
    };
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthorization();
app.MapStaticAssets();

app.MapGet(
    "/api/server-status",
    async (
        RathenaStatusService statusService,
        CancellationToken cancellationToken) =>
    {
        var status =
            await statusService.GetStatusAsync(cancellationToken);

        return Results.Ok(new
        {
            online = status.IsOnline,
            loginServerOnline = status.LoginServerOnline,
            charServerOnline = status.CharServerOnline,
            mapServerOnline = status.MapServerOnline,
            onlinePlayers = status.OnlinePlayers,
            checkedAtUtc = status.CheckedAtUtc
        });
    });

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();