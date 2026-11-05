using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Billing;

public class Customer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long MonthlyCents { get; set; }
}
public class Invoice
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string Period { get; set; } = "";
    public long Cents { get; set; }
    public string Status { get; set; } = "Open";
}
public class PaymentBatch
{
    public string Id { get; set; } = "";
    public string Period { get; set; } = "";
    public DateOnly EffectiveDate { get; set; }
    public string State { get; set; } = "Generated";
    public List<Payment> Payments { get; set; } = [];
}
public class Payment
{
    public string Id { get; set; } = "";
    public string PaymentBatchId { get; set; } = "";
    public string InvoiceId { get; set; } = "";
    public long Cents { get; set; }
    public string Outcome { get; set; } = "Pending";
    public string? ReturnCode { get; set; }
}
public class Audit
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = "";
    public string EntityId { get; set; } = "";
}
public class BillingDb(DbContextOptions<BillingDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<PaymentBatch> Batches => Set<PaymentBatch>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Audit> Audits => Set<Audit>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Invoice>().HasIndex(x => new { x.CustomerId, x.Period }).IsUnique();
        m.Entity<Invoice>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        m.Entity<Payment>().HasIndex(x => x.InvoiceId).IsUnique();
        m.Entity<Payment>().HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId);
        m.Entity<PaymentBatch>().HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.PaymentBatchId);
    }
}
public static class Rules
{
    public static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    public static void Period(string period)
    {
        if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Period must be yyyy-MM.");
    }
    public static void Reconcile(Payment payment, string outcome, string? code)
    {
        if (outcome is not ("Settled" or "Returned") || (outcome == "Returned" && code is not ("R01" or "R02" or "R03")))
            throw new ArgumentException("Use Settled or Returned with R01/R02/R03.");
        if (outcome == "Settled" && code is not null) throw new ArgumentException("Settled has no return code.");
        if (payment.Outcome != "Pending" && (payment.Outcome != outcome || payment.ReturnCode != code))
            throw new InvalidOperationException("Conflicting terminal outcome.");
        payment.Outcome = outcome;
        payment.ReturnCode = code;
    }
}
public class BillingService(BillingDb db)
{
    // Transaction-scoped advisory lock serializes schedulers and API mutations across instances.
    private async Task Lock() => await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8102026)");
    public async Task Seed()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock();
        if (!await db.Customers.AnyAsync())
        {
            db.Customers.AddRange(new Customer { Id = "fiction-001", Name = "Fictional Cedar Studio", MonthlyCents = 12900 },
                new Customer { Id = "fiction-002", Name = "Fictional Harbor Workshop", MonthlyCents = 24900 });
            db.Audits.Add(new() { Action = "Synthetic customers seeded", EntityId = "demo" });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }
    public async Task GenerateInvoices(string period)
    {
        Rules.Period(period);
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock();
        foreach (var c in await db.Customers.OrderBy(x => x.Id).ToListAsync())
        {
            var id = Rules.StableId($"invoice:{c.Id}:{period}");
            if (await db.Invoices.AnyAsync(x => x.Id == id)) continue;
            db.Invoices.Add(new() { Id = id, CustomerId = c.Id, Period = period, Cents = c.MonthlyCents });
            db.Audits.Add(new() { Action = "Invoice generated", EntityId = id });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
    public async Task<PaymentBatch> Batch(string period, DateOnly effectiveDate)
    {
        Rules.Period(period);
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock();
        var id = Rules.StableId($"batch:{period}");
        var existing = await db.Batches.Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id);
        if (existing is not null)
        {
            if (existing.EffectiveDate != effectiveDate) throw new InvalidOperationException("Period already batched with another date.");
            await tx.CommitAsync();
            return existing;
        }
        var invoices = await db.Invoices.Where(x => x.Period == period && x.Status == "Open").OrderBy(x => x.Id).ToListAsync();
        if (invoices.Count == 0) throw new InvalidOperationException("Generate invoices first.");
        var batch = new PaymentBatch { Id = id, Period = period, EffectiveDate = effectiveDate };
        foreach (var i in invoices)
        {
            if (i.Cents <= 0 || i.Cents > 9999999999L) throw new ArgumentException("ACH amount outside supported range.");
            batch.Payments.Add(new() { Id = Rules.StableId($"payment:{i.Id}"), InvoiceId = i.Id, Cents = i.Cents });
            i.Status = "Processing";
        }
        db.Batches.Add(batch);
        db.Audits.Add(new() { Action = "Payment batch generated", EntityId = id });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return batch;
    }
    public async Task Reconcile(string batchId, Result[] results)
    {
        if (results.Length == 0 || results.Select(x => x.PaymentId).Distinct().Count() != results.Length)
            throw new ArgumentException("Provide distinct payment results.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock();
        var batch = await db.Batches.Include(x => x.Payments).SingleAsync(x => x.Id == batchId);
        foreach (var result in results)
        {
            var payment = batch.Payments.SingleOrDefault(x => x.Id == result.PaymentId) ?? throw new ArgumentException("Unknown payment.");
            var previous = payment.Outcome;
            Rules.Reconcile(payment, result.Outcome, result.ReturnCode);
            if (previous == payment.Outcome) continue;
            var invoice = await db.Invoices.SingleAsync(x => x.Id == payment.InvoiceId);
            invoice.Status = payment.Outcome == "Settled" ? "Paid" : "Returned";
            db.Audits.Add(new() { Action = $"Payment {payment.Outcome} {payment.ReturnCode}".Trim(), EntityId = payment.Id });
        }
        batch.State = batch.Payments.All(x => x.Outcome != "Pending") ? "Reconciled" : "Partial";
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
public record Result(string PaymentId, string Outcome, string? ReturnCode);
public record BatchRequest(string Period, DateOnly EffectiveDate);
public class RecurringWorker(IServiceScopeFactory scopes, ILogger<RecurringWorker> log, IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Billing:RecurringEnabled")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<BillingService>().GenerateInvoices(DateTime.UtcNow.ToString("yyyy-MM"));
            }
            catch (Exception ex) { log.LogError(ex, "Recurring invoice generation failed; next tick will retry"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
