using ClosedXML.Excel;
namespace Paper.Api.Tests;
public static class WorkbookFixture
{
    public static MemoryStream Create(bool duplicateCategory = false, bool conflictingBranch = false)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Πελάτες");
        string[] headers = ["ΚΩΔΙΚΟΣ","ΑΦΜ","ΠΩΛΗΤΗΣ","ΠΕΛΑΤΗΣ","ID ΚΑΤΗΓΟΡΙΑΣ","ΚΑΤΗΓΟΡΙΑ","ΤΖΙΡΟΣ"];
        for(var i=0;i<headers.Length;i++) sheet.Cell(1,i+1).Value=headers[i];
        object?[][] rows = [
            ["BR-01","012345678","Πωλητής Α","Κατάστημα Α",100,"Χαρτικά",25.12m],
            ["BR-01","012345678",conflictingBranch?"Πωλητής Β":"Πωλητής Α","Κατάστημα Α",duplicateCategory?100:200,duplicateCategory?"Χαρτικά":"Καθαριστικά",14.88m],
            ["BR-02","012345678","Πωλητής Β","Κατάστημα Β",100,"Χαρτικά",10m],
            ["BR-03","012345678","Πωλητής Γ","Κατάστημα Γ",null,null,null],
            ["BR-04","012345678","Πωλητής Δ","Κατάστημα Δ",200,"Καθαριστικά",8m]
        ];
        for(var r=0;r<rows.Length;r++) for(var c=0;c<7;c++) if(rows[r][c] is {} value) sheet.Cell(r+2,c+1).Value=XLCellValue.FromObject(value);
        var stream = new MemoryStream(); workbook.SaveAs(stream); stream.Position=0; return stream;
    }
}
