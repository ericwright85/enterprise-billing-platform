using Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class DatabaseTests
{
    [Fact] public async Task PostgreSqlBatchAndReconciliationAreAtomicAndIdempotent()
    {
        // Explicit opt-in: never connect to an arbitrary default database in a test.
        var connection = Environment.GetEnvironmentVariable("BILLING_TEST_DB");
        Assert.False(string.IsNullOrEmpty(connection), "Set BILLING_TEST_DB to a dedicated disposable PostgreSQL database.");
        await using var db = new BillingDb(new DbContextOptionsBuilder<BillingDb>().UseNpgsql(connection).Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var service = new BillingService(db);
        await service.Seed();
        await Task.WhenAll(Enumerable.Range(0,4).Select(async _ =>
        {
            await using var concurrent = new BillingDb(new DbContextOptionsBuilder<BillingDb>().UseNpgsql(connection).Options);
            await new BillingService(concurrent).GenerateInvoices("2026-11");
        }));
        await service.GenerateInvoices("2026-11");
        Assert.Equal(2,await db.Invoices.CountAsync());
        var concurrentBatches = await Task.WhenAll(Enumerable.Range(0,4).Select(async _ =>
        {
            await using var concurrent = new BillingDb(new DbContextOptionsBuilder<BillingDb>().UseNpgsql(connection).Options);
            return await new BillingService(concurrent).Batch("2026-11",new(2026,11,6));
        }));
        Assert.Single(concurrentBatches.Select(x => x.Id).Distinct());
        var batch = await service.Batch("2026-11", new(2026,11,6));
        Assert.Equal(batch.Id,(await service.Batch("2026-11",new(2026,11,6))).Id);
        Assert.Equal(2,await db.Payments.CountAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => service.Reconcile(batch.Id,
            [new(batch.Payments[0].Id,"Settled",null),new("unknown-payment","Settled",null)]));
        db.ChangeTracker.Clear();
        batch = await db.Batches.Include(x => x.Payments).SingleAsync(x => x.Id == batch.Id);
        Assert.All(batch.Payments,p => Assert.Equal("Pending",p.Outcome));
        var results = batch.Payments.Select((p,i) => new Result(p.Id,i == 0 ? "Settled" : "Returned",i == 0 ? null : "R01")).ToArray();
        await service.Reconcile(batch.Id,results);
        var count = await db.Audits.CountAsync();
        await service.Reconcile(batch.Id,results);
        Assert.Equal(count,await db.Audits.CountAsync());
        Assert.Equal("Reconciled",batch.State);
        Assert.Equal(1,await db.Invoices.CountAsync(x => x.Status == "Paid"));
        Assert.Equal(1,await db.Invoices.CountAsync(x => x.Status == "Returned"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Reconcile(batch.Id,[new(results[0].PaymentId,"Returned","R02")]));
    }
}
