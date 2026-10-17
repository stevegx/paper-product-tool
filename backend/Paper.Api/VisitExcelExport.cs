using System.Text.Json;
using ClosedXML.Excel;

namespace Paper.Api;

public static class VisitExcelExport
{
    private sealed record QuestionKey(Guid Id,string SectionKey,string SectionTitle,string Title);
    private sealed record ExportRow(Visit Visit,SavedAnswer[] Answers);

    public static byte[] Build(IReadOnlyCollection<Visit> source,IReadOnlyCollection<SurveyQuestion> catalog,bool latestOnly=false)
    {
        var visits=source.OrderByDescending(v=>v.OccurredAtUtc??v.CreatedAtUtc)
            .ThenByDescending(v=>v.CreatedAtUtc).ThenBy(v=>v.Id).ToList();
        // Customer code identifies a branch; the same AFM must never merge branches.
        if(latestOnly)visits=visits.DistinctBy(v=>v.BranchCode).ToList();
        var rows=visits.Select(v=>new ExportRow(v,JsonSerializer.Deserialize<SavedAnswer[]>(v.AnswersJson)??[])).ToList();
        var positions=catalog.ToDictionary(q=>q.Id,q=>q.Position);
        var keys=rows.SelectMany(row=>row.Answers).Where(a=>a.SelectedOptions.Length>0)
            .Select(Key).Distinct().OrderBy(k=>positions.GetValueOrDefault(k.Id,int.MaxValue))
            .ThenBy(k=>k.Id).ThenBy(k=>k.Title,StringComparer.Ordinal).ThenBy(k=>k.SectionTitle,StringComparer.Ordinal).ToList();
        var numbers=catalog.OrderBy(q=>q.Position).ThenBy(q=>q.Id)
            .Select((question,index)=>(question.Id,number:index+1)).ToDictionary(x=>x.Id,x=>x.number);
        foreach(var id in keys.Select(key=>key.Id).Distinct().Where(id=>!numbers.ContainsKey(id)).Order())
            numbers[id]=numbers.Count+1;
        var codes=new Dictionary<QuestionKey,string>();
        foreach(var group in keys.GroupBy(key=>key.Id))
        {
            var variants=group.ToList();
            for(var i=0;i<variants.Count;i++)codes[variants[i]]=$"Ε{numbers[group.Key]:00}"+(variants.Count>1?$".{i+1}":"");
        }
        using var book=new XLWorkbook();
        var sheet=book.AddWorksheet("Επισκέψεις");
        var legend=book.AddWorksheet("Ερωτήσεις & οδηγίες");
        string[] headers=["Ημερομηνία","Πελάτης","Κωδικός πελάτη","ΑΦΜ","Πωλητής","Σημειώσεις"];
        for(var col=0;col<headers.Length;col++)sheet.Cell(1,col+1).Value=headers[col];
        var answerColumn=keys.Select((key,index)=>(key,column:index+7)).ToDictionary(x=>x.key,x=>x.column);
        foreach(var key in keys)
        {
            var cell=sheet.Cell(1,answerColumn[key]);
            cell.Value=$"{codes[key]} · {ShortTitle(key.Title)}";
            cell.GetComment().AddText($"{key.SectionTitle}\n{key.Title}");
        }
        var technical=7+keys.Count;
        sheet.Cell(1,technical).Value="ID επίσκεψης";
        sheet.Cell(1,technical+1).Value="Έκδοση";
        sheet.Cell(1,technical+2).Value="Τελευταία διόρθωση";
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens");
        var index=2;
        foreach(var row in rows)
        {
            var visit=row.Visit;
            sheet.Cell(index,1).Value=LocalDate(visit.OccurredAtUtc??visit.CreatedAtUtc,zone);
            sheet.Cell(index,2).Value=visit.Branch.Name;
            sheet.Cell(index,3).Value=visit.BranchCode;
            sheet.Cell(index,4).Value=visit.Branch.TaxId;
            sheet.Cell(index,5).Value=visit.User.Name;
            sheet.Cell(index,6).Value=visit.Notes;
            foreach(var answer in row.Answers.Where(a=>a.SelectedOptions.Length>0))
                sheet.Cell(index,answerColumn[Key(answer)]).Value=string.Join(" | ",answer.SelectedOptions);
            sheet.Cell(index,technical).Value=visit.Id.ToString();
            sheet.Cell(index,technical+1).Value=visit.Version;
            if(visit.UpdatedAtUtc.HasValue)sheet.Cell(index,technical+2).Value=LocalDate(visit.UpdatedAtUtc.Value,zone);
            // Bound row height while keeping longer responses readable by expanding the row.
            var longest=Math.Max(visit.Notes.Length,row.Answers.Select(a=>string.Join(" | ",a.SelectedOptions).Length).DefaultIfEmpty().Max());
            sheet.Row(index).Height=longest>85?70:longest>38?44:30;
            index++;
        }
        FormatTable(sheet,rows.Count,technical+2,"Visits");
        sheet.Column(1).Width=19;sheet.Column(2).Width=32;sheet.Column(3).Width=18;
        sheet.Column(4).Width=14;sheet.Column(5).Width=22;sheet.Column(6).Width=38;
        sheet.Columns(3,4).Style.NumberFormat.Format="@";
        sheet.Column(1).Style.DateFormat.Format="dd/mm/yyyy hh:mm";
        sheet.Column(technical+2).Style.DateFormat.Format="dd/mm/yyyy hh:mm";
        if(keys.Count>0)sheet.Columns(7,technical-1).Width=29;
        sheet.Columns(technical,technical+2).Hide();
        sheet.SheetView.FreezeColumns(2);

        legend.Cell(1,1).Value="Στήλη";legend.Cell(1,2).Value="Ενότητα";
        legend.Cell(1,3).Value="Πλήρης ερώτηση";legend.Cell(1,4).Value="ID ερώτησης";
        for(var i=0;i<keys.Count;i++)
        {
            legend.Cell(i+2,1).Value=codes[keys[i]];
            legend.Cell(i+2,2).Value=keys[i].SectionTitle.Length>0?keys[i].SectionTitle:"Πρόσθετες ερωτήσεις";
            legend.Cell(i+2,3).Value=keys[i].Title;
            legend.Cell(i+2,4).Value=keys[i].Id.ToString();
            legend.Row(i+2).Height=60;
        }
        FormatTable(legend,keys.Count,4,"Questions");
        legend.Column(1).Width=12;legend.Column(2).Width=34;legend.Column(3).Width=78;legend.Column(4).Hide();
        legend.Column(6).Width=65;
        string[] guide=["Πώς διαβάζεται το αρχείο",
            latestOnly?"Μία γραμμή ανά κατάστημα: η πιο πρόσφατη επίσκεψη μέσα στα επιλεγμένα φίλτρα.":"Μία γραμμή ανά επίσκεψη. Κάθε υποκατάστημα διατηρεί τον δικό του κωδικό, ακόμη και όταν μοιράζεται ΑΦΜ.",
            "Οι στήλες Ε01, Ε02 κ.λπ. αντιστοιχούν στις πλήρεις ερωτήσεις αυτού του φύλλου. Ο πλήρης τίτλος υπάρχει και ως σχόλιο στην επικεφαλίδα.",
            "Εξάγονται μόνο ερωτήσεις που απαντήθηκαν σε τουλάχιστον μία από τις εξαγόμενες επισκέψεις. Κενό κελί = χωρίς απάντηση, όχι «Όχι».",
            "Χρησιμοποίησε τα φίλτρα των επικεφαλίδων για ταξινόμηση, σύγκριση και ανάλυση. Οι πολλαπλές επιλογές χωρίζονται με |.",
            "Ημερομηνίες και ώρες Ελλάδας. ΑΦΜ και κωδικοί αποθηκεύονται ως κείμενο ώστε να διατηρούν τα αρχικά μηδενικά.",
            "Δεν υπάρχουν συγχωνευμένα κελιά. Για μεγαλύτερα κείμενα, αύξησε το ύψος της γραμμής ή δες το πλήρες κείμενο στη γραμμή τύπων.",
            "Τα ID, η έκδοση και η ημερομηνία διόρθωσης είναι κρυμμένες στήλες δεξιά. Μπορούν να εμφανιστούν για συσχετίσεις δεδομένων. Οι κωδικοί Ε01 κ.λπ. είναι ονομασίες μέσα σε αυτό το αρχείο.",
            "Η επεξεργασία του αρχείου δεν ενημερώνει τις επισκέψεις στην εφαρμογή."];
        for(var i=0;i<guide.Length;i++){legend.Cell(i+1,6).Value=guide[i];legend.Cell(i+1,6).Style.Alignment.WrapText=true;legend.Row(i+1).Height=Math.Max(legend.Row(i+1).Height,i==0?40:65);}
        legend.Cell(1,6).Style.Font.Bold=true;legend.Cell(1,6).Style.Font.FontColor=XLColor.FromHtml("#254D93");
        using var stream=new MemoryStream();book.SaveAs(stream);return stream.ToArray();
    }

    private static QuestionKey Key(SavedAnswer answer)=>new(answer.QuestionId,answer.SectionKey??"",answer.SectionTitle??"",answer.Title);
    private static DateTime LocalDate(DateTime value,TimeZoneInfo zone)=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(value,DateTimeKind.Utc),zone);
    private static string ShortTitle(string title)=>title.Length<=48?title:title[..45]+"…";
    private static void FormatTable(IXLWorksheet sheet,int count,int columns,string name)
    {
        sheet.ShowGridLines=false;sheet.Style.Font.FontName="Calibri";sheet.Style.Font.FontSize=11;
        var range=sheet.Range(1,1,count+1,columns);
        range.Style.Alignment.Vertical=XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText=true;
        if(count>0){var table=range.CreateTable(name);table.Theme=XLTableTheme.TableStyleMedium2;table.ShowRowStripes=true;}
        else range.SetAutoFilter();
        var header=sheet.Range(1,1,1,columns);
        header.Style.Fill.BackgroundColor=XLColor.FromHtml("#263F68");header.Style.Font.FontColor=XLColor.White;header.Style.Font.Bold=true;
        sheet.Row(1).Height=54;sheet.SheetView.FreezeRows(1);
    }
}
