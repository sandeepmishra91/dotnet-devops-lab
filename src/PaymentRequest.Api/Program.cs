using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentRequest.Api;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("LabDb")
    ?? throw new InvalidOperationException("Set ConnectionStrings__LabDb.");
builder.Services.AddDbContext<LabDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddProblemDetails();
var app = builder.Build();

// Explicit schema initialization, used locally and by a Kubernetes Job.
// EnsureCreated is for this fixed-schema lab, not a production migration strategy.
if (args.Contains("--init-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
    await db.Database.EnsureCreatedAsync();
    Console.WriteLine("Lab schema initialized.");
    return;
}

app.UseExceptionHandler();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async Task<IResult> (LabDbContext db, ILogger<Program> logger, CancellationToken token) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(2));
    try
    {
        await db.PaymentRequests.AsNoTracking().Select(x => x.Id).Take(1)
            .ToListAsync(timeout.Token);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database readiness check failed.");
        return Results.StatusCode(503);
    }
});
app.MapGet("/version", () => Results.Ok(new
{
    release = app.Configuration["APP_RELEASE"] ?? "local-v1",
    message = "docker-cache-v2",
    instance = Environment.MachineName
}));

app.MapPost("/api/payment-requests", async Task<IResult>
    (PaymentInput input, HttpRequest request, LabDbContext db,
     ILogger<Program> logger, CancellationToken token) =>
{
    var validation = PaymentRules.Validate(input);
    if (validation is not null)
        return Results.BadRequest(new { error = validation });

    var key = request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || key.Contains(','))
        return Results.BadRequest(new { error = "Supply one Idempotency-Key of 1 to 100 characters." });
    input = PaymentRules.Normalize(input);

    var existing = await db.PaymentRequests.AsNoTracking().SingleOrDefaultAsync(
        x => x.MerchantReference == input.MerchantReference && x.IdempotencyKey == key, token);
    if (existing is not null)
        return PaymentRules.SamePayload(existing, input)
            ? Results.Ok(ToResponse(existing))
            : Results.Conflict(new { error = "This key was already used for a different request." });

    var payment = new PaymentRecord
    {
        Id = Guid.NewGuid(), MerchantReference = input.MerchantReference,
        IdempotencyKey = key, Amount = input.Amount, Currency = input.Currency,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
    db.PaymentRequests.Add(payment);
    try
    {
        await db.SaveChangesAsync(token);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
        pg.SqlState == PostgresErrorCodes.UniqueViolation &&
        pg.ConstraintName == "IX_PaymentRequests_MerchantReference_IdempotencyKey")
    {
        // Another replica may win the insert after the initial read.
        db.ChangeTracker.Clear();
        existing = await db.PaymentRequests.AsNoTracking().SingleAsync(
            x => x.MerchantReference == input.MerchantReference && x.IdempotencyKey == key, token);
        return PaymentRules.SamePayload(existing, input)
            ? Results.Ok(ToResponse(existing))
            : Results.Conflict(new { error = "This key was already used for a different request." });
    }
    logger.LogInformation("Recorded request {PaymentId} for merchant {MerchantReference}",
        payment.Id, payment.MerchantReference);
    return Results.Created($"/api/payment-requests/{payment.Id}", ToResponse(payment));
});

app.MapGet("/api/payment-requests/{id:guid}", async Task<IResult>
    (Guid id, LabDbContext db, CancellationToken token) =>
{
    var payment = await db.PaymentRequests.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == id, token);
    return payment is null ? Results.NotFound() : Results.Ok(ToResponse(payment));
});



app.Run();

static object ToResponse(PaymentRecord p) => new
{
    p.Id, p.MerchantReference, p.Amount, p.Currency, p.Status, p.CreatedAtUtc
};
