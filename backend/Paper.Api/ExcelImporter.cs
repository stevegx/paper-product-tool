using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Paper.Api;

public sealed class InputException(string message) : Exception(message);
public sealed record ParsedDatabase(List<Branch> Branches, List<ProductCategory> Categories, List<CategoryTurnover> Turnovers);
public sealed record ImportSummary(int Year, int Branches, int Categories, int TurnoverRows, int CustomersWithoutPurchases);

public sealed class ExcelImporter
{
    private static readonly string[] Headers = ["ΚΩΔΙΚΟΣ", "ΑΦΜ", "ΠΩΛΗΤΗΣ", "ΠΕΛΑΤΗΣ", "ID ΚΑΤΗΓΟΡΙΑΣ", "ΚΑΤΗΓΟΡΙΑ", "ΤΖΙΡΟΣ"];
    public ParsedDatabase Parse(Stream stream, int year)
    {
        if (year is < 2000 or > 2100) throw new InputException("Το έτος εισαγωγής δεν είναι έγκυρο.");
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        for (var i = 0; i < Headers.Length; i++)
            if (sheet.Cell(1, i + 1).GetString().Trim() != Headers[i])
                throw new InputException($"Η στήλη {i + 1} πρέπει να λέγεται «{Headers[i]}».");
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (last > 50001) throw new InputException("Το αρχείο ξεπερνά τις 50.000 γραμμές.");
        var branches = new Dictionary<string, Branch>(StringComparer.Ordinal);
        var categories = new Dictionary<int, ProductCategory>();
        var turnovers = new Dictionary<(string, int), CategoryTurnover>();
        for (var r = 2; r <= last; r++)
        {
            var row = sheet.Row(r);
            if (row.Cells(1, 7).All(cell => cell.IsEmpty())) continue;
            if (row.Cells(1, 7).Any(cell => cell.HasFormula)) throw new InputException($"Γραμμή {r}: χρησιμοποίησε τιμές, όχι τύπους Excel.");
            var code = row.Cell(1).GetString().Trim();
            var tax = row.Cell(2).DataType == XLDataType.Number
                ? row.Cell(2).GetDouble().ToString("000000000", CultureInfo.InvariantCulture)
                : row.Cell(2).GetString().Trim();
            var seller = row.Cell(3).GetString().Trim();
            var name = row.Cell(4).GetString().Trim();
            if (code.Length is 0 or > 64 || !Regex.IsMatch(tax, "^[0-9]{9}$") || name.Length is 0 or > 512 || seller.Length is 0 or > 128)
                throw new InputException($"Γραμμή {r}: έλεγξε κωδικό, εννιαψήφιο ΑΦΜ, όνομα και πωλητή.");
            if (branches.TryGetValue(code, out var existing))
            {
                if (existing.TaxId != tax || existing.Name != name || existing.AssignedSeller != seller)
                    throw new InputException($"Γραμμή {r}: ο κωδικός {code} έχει αντικρουόμενα στοιχεία.");
            }
            else branches[code] = new Branch { Code = code, TaxId = tax, Name = name, AssignedSeller = seller };
            var missing = row.Cells(5, 7).Count(cell => cell.IsEmpty());
            if (missing == 3) continue; // Confirmed by the owner: no purchases, not missing sales.
            if (missing != 0) throw new InputException($"Γραμμή {r}: συμπλήρωσε μαζί ID κατηγορίας, κατηγορία και τζίρο.");
            if (!row.Cell(5).TryGetValue<int>(out var categoryId) || categoryId <= 0)
                throw new InputException($"Γραμμή {r}: μη έγκυρο ID κατηγορίας.");
            var categoryName = row.Cell(6).GetString().Trim();
            if (categoryName.Length is 0 or > 256 || !row.Cell(7).TryGetValue<decimal>(out var amount) || Math.Abs(amount) > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
                throw new InputException($"Γραμμή {r}: έλεγξε κατηγορία και αριθμητικό τζίρο έως 2 δεκαδικών.");
            if (categories.TryGetValue(categoryId, out var category) && category.Name != categoryName)
                throw new InputException($"Γραμμή {r}: το ID {categoryId} έχει διαφορετικές ονομασίες.");
            categories[categoryId] = new ProductCategory { Id = categoryId, Name = categoryName };
            if (!turnovers.TryAdd((code, categoryId), new CategoryTurnover { BranchCode = code, CategoryId = categoryId, Year = year, Amount = amount }))
                throw new InputException($"Γραμμή {r}: διπλή εγγραφή για τον κωδικό {code} και την κατηγορία {categoryId}.");
        }
        if (branches.Count == 0) throw new InputException("Το αρχείο δεν περιέχει πελάτες.");
        return new ParsedDatabase([.. branches.Values], [.. categories.Values], [.. turnovers.Values]);
    }

    public async Task<ImportSummary> ImportAsync(AppDbContext db, Stream stream, int year, CancellationToken ct = default)
    {
        var data = Parse(stream, year); // Validate the complete workbook before touching the database.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existingBranches = await db.Branches.ToDictionaryAsync(x => x.Code, ct);
        foreach (var branch in data.Branches)
        {
            if (existingBranches.TryGetValue(branch.Code, out var current))
            {
                current.Name = branch.Name;
                current.TaxId = branch.TaxId;
                current.AssignedSeller = branch.AssignedSeller;
            }
            else db.Branches.Add(branch);
        }
        var existingCategories = await db.Categories.ToDictionaryAsync(x => x.Id, ct);
        foreach (var category in data.Categories)
        {
            if (existingCategories.TryGetValue(category.Id, out var current)) current.Name = category.Name;
            else db.Categories.Add(category);
        }
        // Re-import is a full turnover snapshot for this year. Visits and other years survive.
        await db.Turnovers.Where(x => x.Year == year).ExecuteDeleteAsync(ct);
        db.Turnovers.AddRange(data.Turnovers);
        db.Imports.Add(new ImportRun { Year = year, BranchCount = data.Branches.Count, TurnoverCount = data.Turnovers.Count });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var purchasingCodes = data.Turnovers.Select(x => x.BranchCode).ToHashSet();
        return new ImportSummary(year, data.Branches.Count, data.Categories.Count, data.Turnovers.Count, data.Branches.Count(x => !purchasingCodes.Contains(x.Code)));
    }
}
