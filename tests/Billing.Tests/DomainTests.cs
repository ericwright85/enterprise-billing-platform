using Billing;
using Xunit;

public class DomainTests
{
    private static PaymentBatch Batch() => new() { Id = "batch", EffectiveDate = new(2026,11,6), Payments = [new() { Id = Rules.StableId("one"), InvoiceId = "a", Cents = 12900 }, new() { Id = Rules.StableId("two"), InvoiceId = "b", Cents = 24900 }] };
    [Fact] public void FileIsDeterministicAndTotalsValidate()
    {
        var batch = Batch();
        var file = Nacha.Write(batch);
        batch.Payments.Reverse();
        Assert.Equal(file, Nacha.Write(batch));
        Assert.All(file.TrimEnd('\n').Split('\n'), line => Assert.Equal(94,line.Length));
        Assert.Contains("000000037800",file);
        Nacha.Validate(file);
    }
    [Fact] public void CorruptedAmountFailsControlValidation()
    {
        var file = Nacha.Write(Batch()).Replace("0000012900","0000012901");
        Assert.Throws<ArgumentException>(() => Nacha.Validate(file));
    }
    [Fact] public void RepeatedResultIsSafeButConflictIsRejected()
    {
        var p = new Payment();
        Rules.Reconcile(p,"Returned","R01");
        Rules.Reconcile(p,"Returned","R01");
        Assert.Throws<InvalidOperationException>(() => Rules.Reconcile(p,"Settled",null));
    }
    [Theory] [InlineData("2026-13")] [InlineData("2026-1")] [InlineData("oops")]
    public void InvalidPeriodsRejected(string period) => Assert.Throws<ArgumentException>(() => Rules.Period(period));
    [Fact] public void InvalidOutcomeRejected() => Assert.Throws<ArgumentException>(() => Rules.Reconcile(new(),"Returned","R99"));
}
