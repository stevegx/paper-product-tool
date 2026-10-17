using System.Text.Json;
using ClosedXML.Excel;
using Xunit;

namespace Paper.Api.Tests;
public sealed class VisitExcelExportTests
{
    static readonly Guid Q1=Guid.Parse("10000000-0000-0000-0000-000000000001");
    static readonly Guid Q2=Guid.Parse("10000000-0000-0000-0000-000000000002");
    static readonly Guid Q3=Guid.Parse("10000000-0000-0000-0000-000000000003");
    static SurveyQuestion[] Catalog()=>[
        new(){Id=Q1,Title="Πώς καθαρίζει ο barman τον πάγκο του;",Position=1},
        new(){Id=Q2,Title="Κενή ερώτηση",Position=2},
        new(){Id=Q3,Title="Υπάρχει σύστημα αρωματισμού χώρου;",Position=3}];
    static Visit Visit(string code,int day,params SavedAnswer[] answers)=>new(){
        BranchCode=code,Branch=new(){Code=code,Name="Δοκιμαστικό κατάστημα "+code,TaxId="012345678"},
        User=new(){Name="Πωλητής δοκιμής"},OccurredAtUtc=new DateTime(2026,10,day,9,0,0,DateTimeKind.Utc),
        CreatedAtUtc=new DateTime(2026,10,day,9,0,0,DateTimeKind.Utc),Notes="Σημείωση επίσκεψης",
        AnswersJson=JsonSerializer.Serialize(answers)};
    static SavedAnswer Bar(params string[] options)=>new(Q1,"Πώς καθαρίζει ο barman τον πάγκο του;",options,"bar","Bar & Σταθμός Σέρβις");
    static Visit[] Rows()=>[
        Visit("BR-01",15,Bar("Πανί μικροϊνών"),new(Q2,"Κενή ερώτηση",[],"bar","Bar & Σταθμός Σέρβις")),
        Visit("BR-01",17,Bar(),new(Q3,"Υπάρχει σύστημα αρωματισμού χώρου;",["Όχι"],"wc","Τουαλέτες")),
        Visit("BR-02",16,Bar("Πανί μικροϊνών","Επαγγελματικό χαρτί"))];

    [Fact] public void CompactRowsHaveOnlyAnsweredColumnsAndExcelFiltering()
    {
        var bytes=VisitExcelExport.Build(Rows(),Catalog());
        using var stream=new MemoryStream(bytes);using var book=new XLWorkbook(stream);
        var sheet=book.Worksheet("Επισκέψεις");
        Assert.Equal(4,sheet.LastRowUsed()!.RowNumber());Assert.Equal(11,sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("012345678",sheet.Cell(2,4).GetString());Assert.Equal(XLDataType.Text,sheet.Cell(2,4).DataType);
        Assert.Equal(XLDataType.DateTime,sheet.Cell(2,1).DataType);
        Assert.Equal("Όχι",sheet.Cell(2,8).GetString());Assert.True(sheet.Cell(2,7).IsEmpty());
        Assert.Equal("Πανί μικροϊνών | Επαγγελματικό χαρτί",sheet.Cell(3,7).GetString());
        Assert.Single(sheet.Tables);Assert.True(sheet.Table(0).ShowAutoFilter);
        Assert.True(sheet.Column(9).IsHidden);Assert.True(sheet.Column(10).IsHidden);Assert.True(sheet.Column(11).IsHidden);
        Assert.Equal(1,sheet.SheetView.SplitRow);Assert.Equal(2,sheet.SheetView.SplitColumn);
        Assert.Empty(sheet.MergedRanges);
        var legend=book.Worksheet("Ερωτήσεις & οδηγίες");
        Assert.Equal(3,legend.Table(0).RangeAddress.LastAddress.RowNumber);
        Assert.Equal(Q1.ToString(),legend.Cell(2,4).GetString());Assert.Equal(Catalog()[0].Title,legend.Cell(2,3).GetString());
        Assert.DoesNotContain("Κενή ερώτηση",legend.Range(2,3,3,3).Cells().Select(cell=>cell.GetString()));
        var examplePath=Environment.GetEnvironmentVariable("PAPER_EXPORT_SAMPLE_PATH");
        if(!string.IsNullOrEmpty(examplePath))File.WriteAllBytes(examplePath,bytes);
    }
    [Fact] public void LatestMeansOneVisitPerBranchEvenWhenBranchesShareTaxId()
    {
        using var stream=new MemoryStream(VisitExcelExport.Build(Rows(),Catalog(),true));using var book=new XLWorkbook(stream);
        var sheet=book.Worksheet(1);Assert.Equal(3,sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("BR-01",sheet.Cell(2,3).GetString());Assert.Equal("BR-02",sheet.Cell(3,3).GetString());
        Assert.True(sheet.Cell(2,7).IsEmpty());Assert.Equal("Όχι",sheet.Cell(2,8).GetString());
    }
    [Fact] public void HistoricalQuestionTitleChangesRemainSeparateInsteadOfMixingMeanings()
    {
        var changed=Visit("BR-01",17,new SavedAnswer(Q1,"Νέα διαφορετική ερώτηση",["Άλλη απάντηση"],"bar","Bar"));
        using var stream=new MemoryStream(VisitExcelExport.Build([Rows()[0],changed],Catalog()));using var book=new XLWorkbook(stream);
        var legend=book.Worksheet(2);Assert.Equal(3,legend.Table(0).RangeAddress.LastAddress.RowNumber);
        Assert.Contains("Νέα διαφορετική ερώτηση",legend.Range(2,3,3,3).Cells().Select(cell=>cell.GetString()));
        Assert.Contains(Catalog()[0].Title,legend.Range(2,3,3,3).Cells().Select(cell=>cell.GetString()));
    }
    [Fact] public void EmptyExportHasHeadersWithoutInventedVisitAndNotesCannotBecomeFormulas()
    {
        using var emptyStream=new MemoryStream(VisitExcelExport.Build([],Catalog()));using var empty=new XLWorkbook(emptyStream);
        Assert.Equal(1,empty.Worksheet(1).LastRowUsed()!.RowNumber());
        var row=Visit("BR-01",17);row.Notes="=1+1";
        using var stream=new MemoryStream(VisitExcelExport.Build([row],Catalog()));using var book=new XLWorkbook(stream);
        Assert.Equal("=1+1",book.Worksheet(1).Cell(2,6).GetString());Assert.False(book.Worksheet(1).Cell(2,6).HasFormula);
    }
}
