using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;
namespace Paper.Api;
public sealed record EditOption(Guid Id,string Label);
public sealed record EditQuestionInput(string ExpectedSurveyVersion,string Title,bool Required,string SelectionMode,string? SectionKey,EditOption[] Options);
public static class QuestionEditing
{
    public static void MapQuestionEditing(this WebApplication app)
    {
        app.MapPut("/api/admin/questions/{id:guid}",async(Guid id,EditQuestionInput input,AppDbContext db,WriteGate gate,CancellationToken ct)=>
        {
            await gate.Lock.WaitAsync(ct);
            try
            {
                var active=await Questions(db);
                if(SurveyVersion(active)!=input.ExpectedSurveyVersion)return Results.Conflict(new{message="Το ερωτηματολόγιο άλλαξε. Ανανέωσε τη λίστα πριν τη διόρθωση."});
                var question=await db.Questions.Include(q=>q.Options).SingleOrDefaultAsync(q=>q.Id==id,ct);if(question is null)return Results.NotFound();
                var title=(input.Title??"").Trim();var options=input.Options??[];
                if(title.Length is 0 or >256||input.SelectionMode is not("Single" or "Multiple")||options.Length is <1 or >20||options.Any(o=>o is null||string.IsNullOrWhiteSpace(o.Label)||o.Label.Trim().Length>128)||options.Select(o=>o.Label.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=options.Length)
                    throw new InputException("Έλεγξε τον τίτλο και τις διαφορετικές επιλογές της ερώτησης.");
                var retained=options.Where(o=>o.Id!=Guid.Empty).Select(o=>o.Id).ToArray();
                if(retained.Distinct().Count()!=retained.Length||retained.Any(optionId=>question.Options.All(o=>o.Id!=optionId)))throw new InputException("Μη έγκυρη επιλογή ερώτησης.");
                var sectionKey=(input.SectionKey??"").Trim();var section=sectionKey.Length==0?null:active.FirstOrDefault(q=>q.SectionKey==sectionKey);
                if(sectionKey.Length>0&&section is null)throw new InputException("Η ενότητα δεν βρέθηκε.");
                await VisitEndpoints.PublishedSurvey(db);
                if(question.SectionKey!=sectionKey)question.Position=section is null?10000:active.Where(q=>q.SectionKey==sectionKey).Max(q=>q.Position)+1;
                question.Title=title;question.Required=section is null&&input.Required;question.SelectionMode=input.SelectionMode;question.SectionKey=sectionKey;question.SectionTitle=section?.SectionTitle??"";
                foreach(var option in question.Options.Where(o=>!retained.Contains(o.Id)).ToList())db.Remove(option);
                for(var i=0;i<options.Length;i++)
                {
                    var option=question.Options.FirstOrDefault(o=>o.Id==options[i].Id&&o.Id!=Guid.Empty);
                    if(option is null)
                    {
                        option=new SurveyOption{SurveyQuestionId=id};
                        question.Options.Add(option);
                        // The constructor assigns a Guid. Explicitly mark the child as new;
                        // relationship discovery otherwise treats that key as an existing row.
                        db.Add(option);
                    }
                    option.Label=options[i].Label.Trim();option.Position=i;
                }
                await db.SaveChangesAsync(ct);await VisitEndpoints.PublishedSurvey(db);return Results.Ok(new{id=question.Id});
            }
            finally{gate.Lock.Release();}
        }).RequireAuthorization("Admin");
    }
}
