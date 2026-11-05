using Billing;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Billing") ?? throw new InvalidOperationException("Set ConnectionStrings__Billing (see .env.example).");
builder.Services.AddDbContext<BillingDb>(o => o.UseNpgsql(connection));
builder.Services.AddScoped<BillingService>();
builder.Services.AddHostedService<RecurringWorker>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (ArgumentException ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = ex.Message }); }
});
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<BillingDb>().Database.EnsureCreatedAsync();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", async (BillingDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
app.MapPost("/api/demo/seed", async (BillingService billing) => { await billing.Seed(); return Results.Ok(); });
app.MapPost("/api/invoices/{period}", async (string period, BillingService billing) => { await billing.GenerateInvoices(period); return Results.Ok(); });
app.MapGet("/api/invoices", async (BillingDb db) => await db.Invoices.OrderBy(x => x.Id).ToListAsync());
app.MapPost("/api/batches", async (BatchRequest request, BillingService billing) => await billing.Batch(request.Period, request.EffectiveDate));
app.MapGet("/api/batches/{id}/ach", async (string id, BillingDb db) =>
{
    var batch = await db.Batches.Include(x => x.Payments).SingleAsync(x => x.Id == id);
    return Results.Text(Nacha.Write(batch), "text/plain");
});
app.MapPost("/api/batches/{id}/reconcile", async (string id, Result[] results, BillingService billing) => { await billing.Reconcile(id, results); return Results.Ok(); });
app.MapGet("/api/audit", async (BillingDb db) => await db.Audits.OrderBy(x => x.Id).ToListAsync());
app.MapGet("/api/reports", async (BillingDb db) => new
{
    invoicedCents = await db.Invoices.SumAsync(x => x.Cents),
    settledCents = await db.Payments.Where(x => x.Outcome == "Settled").SumAsync(x => x.Cents),
    returnedCents = await db.Payments.Where(x => x.Outcome == "Returned").SumAsync(x => x.Cents),
    pendingCents = await db.Payments.Where(x => x.Outcome == "Pending").SumAsync(x => x.Cents)
});
app.Run();
