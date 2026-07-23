using System.Threading.RateLimiting;
using MaximoROWeb.Services;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

builder.Services.Configure<EmailVerificationOptions>(
    builder.Configuration.GetSection(
        EmailVerificationOptions.SectionName));

builder.Services.AddSingleton<RathenaStatusService>();
builder.Services.AddSingleton<EmailVerificationService>();
builder.Services.AddSingleton<
    IVerificationEmailSender,
    SmtpVerificationEmailSender>();

builder.Services.AddHostedService<
    EmailVerificationSchemaInitializer>();

builder.Services.AddScoped<AccountRegistrationService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddPolicy("registration", httpContext =>
        CreateFixedWindowLimiter(
            httpContext,
            permitLimit: 3,
            window: TimeSpan.FromMinutes(1)));

    options.AddPolicy("verification-resend", httpContext =>
        CreateFixedWindowLimiter(
            httpContext,
            permitLimit: 5,
            window: TimeSpan.FromMinutes(15)));

    options.AddPolicy("verification-link", httpContext =>
        CreateFixedWindowLimiter(
            httpContext,
            permitLimit: 30,
            window: TimeSpan.FromMinutes(1)));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "text/plain";

        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please wait and try again.",
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
app.UseStaticFiles();
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

static RateLimitPartition<string> CreateFixedWindowLimiter(
    HttpContext httpContext,
    int permitLimit,
    TimeSpan window)
{
    var clientIp =
        httpContext.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: clientIp,
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
}
