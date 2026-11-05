using System.Globalization;

namespace Billing;

// Fictional, unbalanced PPD debit file. All account details are deliberately synthetic.
public static class Nacha
{
    private const string Routing = "091000019";
    private static string N(long n, int size)
    {
        var value = n.ToString(CultureInfo.InvariantCulture);
        if (n < 0 || value.Length > size) throw new ArgumentException("Numeric field overflow.");
        return value.PadLeft(size, '0');
    }
    private static string A(string value, int size) => value.Length <= size ? value.PadRight(size) : throw new ArgumentException("Text field overflow.");
    public static string Write(PaymentBatch batch)
    {
        if (batch.Payments.Count is 0 or > 9999999) throw new ArgumentException("Unsupported entry count.");
        var entries = batch.Payments.OrderBy(x => x.InvoiceId, StringComparer.Ordinal).ToArray();
        long total = entries.Sum(x => checked(x.Cents));
        long hash = entries.Length * 9100001L % 10000000000L;
        var date = batch.EffectiveDate.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            "101 " + Routing + " 123456789" + date + "0000A094101" + A("FICTIONAL BANK",23) + A("FICTIONAL BILLING",23) + "        ",
            "5225" + A("FICTION BILLING",16) + new string(' ',20) + "1123456789PPD" + A("DEMO DEBIT",10) + "      " + date + "   1" + "09100001" + "0000001"
        };
        for (var i = 0; i < entries.Length; i++)
        {
            var p = entries[i];
            if (p.Cents <= 0) throw new ArgumentException("Positive debit required.");
            lines.Add("627" + Routing + A("DEMOACCOUNT" + N(i+1,4),17) + N(p.Cents,10) + A(p.Id[..15],15) + A("FICTIONAL CUSTOMER " + (i+1),22) + "  0" + "09100001" + N(i+1,7));
        }
        lines.Add("8225" + N(entries.Length,6) + N(hash,10) + N(total,12) + N(0,12) + "1123456789" + new string(' ',25) + "09100001" + "0000001");
        var blocks = (int)Math.Ceiling((lines.Count+1)/10.0);
        lines.Add("9" + N(1,6) + N(blocks,6) + N(entries.Length,8) + N(hash,10) + N(total,12) + N(0,12) + new string(' ',39));
        while (lines.Count % 10 != 0) lines.Add(new string('9',94));
        var file = string.Join('\n', lines) + "\n";
        Validate(file);
        return file;
    }
    public static void Validate(string file)
    {
        var lines = file.TrimEnd('\n').Split('\n');
        if (lines.Length % 10 != 0 || lines.Any(x => x.Length != 94 || x.Any(c => c is < ' ' or > '~')))
            throw new ArgumentException("Expected ASCII 94-character records in blocks of 10.");
        if (!lines[0].StartsWith("101") || !lines[1].StartsWith("5225")) throw new ArgumentException("Unsupported header.");
        var details = lines.Skip(2).TakeWhile(x => x[0] == '6').ToArray();
        if (details.Length == 0) throw new ArgumentException("No entries.");
        long total = 0, hash = 0;
        foreach (var d in details)
        {
            if (!d.StartsWith("627") || d[78] != '0') throw new ArgumentException("Only debit details without addenda supported.");
            var routing = d.Substring(3,9);
            var digits = routing.Select(c => c - '0').ToArray();
            if (digits.Any(x => x is < 0 or > 9) || (3*(digits[0]+digits[3]+digits[6])+7*(digits[1]+digits[4]+digits[7])+digits[2]+digits[5]+digits[8])%10 != 0)
                throw new ArgumentException("Routing checksum invalid.");
            var cents = long.Parse(d.Substring(29,10), CultureInfo.InvariantCulture);
            if (cents <= 0) throw new ArgumentException("Invalid debit.");
            total = checked(total+cents);
            hash += long.Parse(d.Substring(3,8), CultureInfo.InvariantCulture);
        }
        hash %= 10000000000L;
        var control = lines[details.Length+2];
        var footer = lines[details.Length+3];
        if (!control.StartsWith("8225") || control.Substring(4,6) != N(details.Length,6) || control.Substring(10,10) != N(hash,10) || control.Substring(20,12) != N(total,12) || control.Substring(32,12) != N(0,12))
            throw new ArgumentException("Batch control mismatch.");
        if (footer[0] != '9' || footer.Substring(1,6) != N(1,6) || footer.Substring(7,6) != N(lines.Length/10,6) || footer.Substring(13,8) != N(details.Length,8) || footer.Substring(21,10) != N(hash,10) || footer.Substring(31,12) != N(total,12) || footer.Substring(43,12) != N(0,12))
            throw new ArgumentException("File control mismatch.");
        if (lines.Skip(details.Length+4).Any(x => x != new string('9',94))) throw new ArgumentException("Invalid padding.");
    }
}
