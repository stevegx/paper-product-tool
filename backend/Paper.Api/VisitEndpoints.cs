using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;

namespace Paper.Api;
public sealed record SaveVisitInput(Guid MutationId, int ExpectedVersion, string BranchCode, string SurveyVersion, DateTime OccurredAtUtc, string? Notes, AnswerInput[]? Answers);
public static class VisitEndpoints
{
    public static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    public static async Task<object> PublishedSurvey(AppDbContext db)
    {
        var questions=await Questions(db);var version=SurveyVersion(questions);
        if (!await db.SurveyReleases.AnyAsync(x=>x.Id==version))
        {
            var release=new SurveyRelease{Id=version,QuestionsJson=JsonSerializer.Serialize(questions)};db.SurveyReleases.Add(release);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { db.Entry(release).State=EntityState.Detached;if(!await db.SurveyReleases.AnyAsync(x=>x.Id==version))throw; }
        }
        return new {version,questions};
    }
    public static async Task<List<SurveyQuestion>> VisitSurvey(AppDbContext db,Visit visit)
    {
        if(visit.SurveyJson.Length>0)return JsonSerializer.Deserialize<List<SurveyQuestion>>(visit.SurveyJson)!;
        var saved=JsonSerializer.Deserialize<SavedAnswer[]>(visit.AnswersJson)??[];
        var ids=saved.Select(x=>x.QuestionId).ToArray();
        var existing=await db.Questions.AsNoTracking().Include(q=>q.Options).Where(q=>ids.Contains(q.Id)).ToListAsync();
        return saved.Select((answer,index)=>
        {
            var current=existing.FirstOrDefault(q=>q.Id==answer.QuestionId);
            var options=current?.Options.Select(o=>new SurveyOption{Id=o.Id,Label=o.Label,Position=o.Position}).ToList()??[];
            foreach(var label in answer.SelectedOptions.Where(label=>options.All(o=>o.Label!=label)))
                options.Add(new SurveyOption{Id=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(answer.QuestionId+label))[..16]),Label=label,Position=options.Count});
            return new SurveyQuestion{Id=answer.QuestionId,Title=answer.Title,SectionKey=answer.SectionKey??"",SectionTitle=answer.SectionTitle??"",SelectionMode=current?.SelectionMode??(answer.SelectedOptions.Length>1?"Multiple":"Single"),Position=index,Options=options};
        }).ToList();
    }
    public static async Task<object> Editor(AppDbContext db,Visit visit)
    {
        var questions=await VisitSurvey(db,visit);var saved=JsonSerializer.Deserialize<SavedAnswer[]>(visit.AnswersJson)??[];
        return new { visit=VisitDto(visit),survey=new{version=SurveyVersion(questions),questions},answers=saved.ToDictionary(a=>a.QuestionId.ToString(),a=>questions.First(q=>q.Id==a.QuestionId).Options.Where(o=>a.SelectedOptions.Contains(o.Label)).Select(o=>o.Id).ToArray()) };
    }
    public static IQueryable<Visit> Filter(AppDbContext db,ClaimsPrincipal user,string? afm,Guid? sellerId,DateOnly? from,DateOnly? to)
    {
        if(afm is {Length:>0}&&!System.Text.RegularExpressions.Regex.IsMatch(afm,"^[0-9]{9}$")||from>to)throw new InputException("Έλεγξε τα φίλτρα αναζήτησης.");
        var query=db.Visits.AsNoTracking();
        if(!user.IsInRole("Admin")){var id=UserId(user);query=query.Where(v=>v.UserId==id);}else if(sellerId.HasValue)query=query.Where(v=>v.UserId==sellerId);
        if(!string.IsNullOrWhiteSpace(afm))query=query.Where(v=>v.Branch.TaxId==afm);
        if(from.HasValue){var start=AthensDate(from.Value);query=query.Where(v=>(v.OccurredAtUtc??v.CreatedAtUtc)>=start);}
        if(to.HasValue){if(to.Value.Year>=9999)throw new InputException("Μη έγκυρη ημερομηνία.");var end=AthensDate(to.Value.AddDays(1));query=query.Where(v=>(v.OccurredAtUtc??v.CreatedAtUtc)<end);}
        return query;
    }
    public static void MapVisitEndpoints(this WebApplication app)
    {
        var api=app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/visits/{id:guid}/editor",async(Guid id,ClaimsPrincipal user,AppDbContext db)=>
        {
            var visit=await db.Visits.IgnoreQueryFilters().Include(v=>v.User).Include(v=>v.Branch).SingleOrDefaultAsync(v=>v.Id==id);
            if(visit is null)return Results.NotFound();if(visit.UserId!=UserId(user)&&!user.IsInRole("Admin"))return Results.Forbid();
            if(visit.IsDeleted)return VisitDeletion.Gone();
            return Results.Ok(await Editor(db,visit));
        });
        api.MapPut("/visits/{id:guid}",async(Guid id,SaveVisitInput input,ClaimsPrincipal user,AppDbContext db,WriteGate gate,CancellationToken ct)=>
        {
            if(id==Guid.Empty||input.MutationId==Guid.Empty||input.ExpectedVersion<0)throw new InputException("Δεν αναγνωρίστηκε η καταχώριση. Άνοιξε ξανά την επίσκεψη.");
            await gate.Lock.WaitAsync(ct);
            try
            {
                var caller=UserId(user);var key=$"{caller:N}:{input.MutationId:N}";
                var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id+JsonSerializer.Serialize(input))));
                var visit=await db.Visits.IgnoreQueryFilters().Include(v=>v.User).Include(v=>v.Branch).SingleOrDefaultAsync(v=>v.Id==id,ct);
                if(visit is not null&&visit.UserId!=caller&&!user.IsInRole("Admin"))return Results.Forbid();
                if(visit?.IsDeleted==true)return VisitDeletion.Gone();
                var receipt=await db.VisitMutations.FindAsync([key],ct);
                if(receipt is not null)return receipt.BodyHash==hash?Results.Content(receipt.ResultJson,"application/json"):Results.Conflict(new{code="mutation_reused",message="Η ίδια αποστολή περιέχει διαφορετικά στοιχεία. Άνοιξε ξανά την επίσκεψη."});
                if((visit?.Version??0)!=input.ExpectedVersion)return Results.Conflict(new{code="conflict",message="Η επίσκεψη άλλαξε από άλλον χρήστη. Δες τη νεότερη καταχώριση πριν αποθηκεύσεις.",current=visit is null?null:VisitDto(visit)});
                if(visit is not null&&visit.BranchCode!=input.BranchCode)throw new InputException("Δεν μπορεί να αλλάξει το υποκατάστημα μιας επίσκεψης.");
                List<SurveyQuestion> questions;
                if(visit is not null)questions=await VisitSurvey(db,visit);
                else
                {
                    var release=await db.SurveyReleases.FindAsync([input.SurveyVersion],ct);
                    if(release is null)return Results.Conflict(new{code="survey_missing",message="Σύνδεσε τη συσκευή για να ανανεωθεί το ερωτηματολόγιο. Οι απαντήσεις σου παραμένουν αποθηκευμένες."});
                    questions=JsonSerializer.Deserialize<List<SurveyQuestion>>(release.QuestionsJson)!;
                }
                if(SurveyVersion(questions)!=input.SurveyVersion)throw new InputException("Άνοιξε το ερωτηματολόγιο της συγκεκριμένης επίσκεψης πριν τη διόρθωση.");
                if((input.Notes??"").Length>2000)throw new InputException("Οι σημειώσεις δέχονται έως 2.000 χαρακτήρες.");
                var answers=SurveyCatalog.ValidateAnswers(questions,input.Answers??[]);
                if(!answers.Any(a=>a.SelectedOptions.Length>0)&&string.IsNullOrWhiteSpace(input.Notes))throw new InputException("Συμπλήρωσε τουλάχιστον μία ενότητα ή γράψε σημειώσεις. Δεν αποστέλλεται κενή επίσκεψη.");
                var before=visit is null?null:JsonSerializer.Serialize(VisitDto(visit),WebJson);
                if(visit is null)
                {
                    if(input.OccurredAtUtc.ToUniversalTime()>DateTime.UtcNow.AddMinutes(15)||input.OccurredAtUtc.Year<2020)throw new InputException("Έλεγξε την ημερομηνία και ώρα της συσκευής.");
                    var branch=await db.Branches.FindAsync([input.BranchCode],ct);if(branch is null)return Results.NotFound();
                    visit=new Visit{Id=id,UserId=caller,User=await db.Users.SingleAsync(x=>x.Id==caller,ct),BranchCode=branch.Code,Branch=branch,SubmissionId=id,OccurredAtUtc=input.OccurredAtUtc.ToUniversalTime(),Version=1};db.Visits.Add(visit);
                }
                else {visit.Version++;visit.UpdatedAtUtc=DateTime.UtcNow;visit.LastEditedById=caller;}
                visit.SurveyJson=JsonSerializer.Serialize(questions);visit.AnswersJson=JsonSerializer.Serialize(answers);visit.Notes=(input.Notes??"").Trim();
                var result=JsonSerializer.Serialize(VisitDto(visit),WebJson);
                db.VisitMutations.Add(new VisitMutation{Id=key,BodyHash=hash,ResultJson=result});
                if(before is not null)db.VisitRevisions.Add(new VisitRevision{VisitId=id,EditorId=caller,EditorName=user.Identity?.Name??"",BeforeJson=before,AfterJson=result});
                await db.SaveChangesAsync(ct);
                return Results.Content(result,"application/json");
            }
            finally{gate.Lock.Release();}
        });
        api.MapGet("/visits/{id:guid}/revisions",async(Guid id,ClaimsPrincipal user,AppDbContext db)=>
        {
            var visit=await db.Visits.FindAsync(id);if(visit is null)return Results.NotFound();if(visit.UserId!=UserId(user)&&!user.IsInRole("Admin"))return Results.Forbid();
            return Results.Ok(await db.VisitRevisions.AsNoTracking().Where(r=>r.VisitId==id).OrderByDescending(r=>r.ChangedAtUtc).Select(r=>new{r.Id,r.EditorName,r.ChangedAtUtc,r.BeforeJson,r.AfterJson}).ToListAsync());
        });
        api.MapGet("/offline/snapshot",async(ClaimsPrincipal user,AppDbContext db)=>
        {
            var survey=await PublishedSurvey(db);var latest=await db.Imports.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).FirstOrDefaultAsync();var year=latest?.Year??2026;
            var branches=await db.Branches.AsNoTracking().OrderBy(b=>b.Code).ToListAsync();var categories=await db.Categories.AsNoTracking().OrderBy(c=>c.Id).ToListAsync();
            var sales=(await db.Turnovers.AsNoTracking().Where(s=>s.Year==year).ToListAsync()).ToLookup(s=>s.BranchCode);
            var caller=UserId(user);var visits=await db.Visits.AsNoTracking().Include(v=>v.User).Include(v=>v.Branch).Where(v=>v.UserId==caller).OrderByDescending(v=>v.CreatedAtUtc).Take(100).ToListAsync();
            var editors=new List<object>();foreach(var visit in visits)editors.Add(await Editor(db,visit));
            return Results.Ok(new{generatedAtUtc=DateTime.UtcNow,survey,overview=new{customers=branches.Count,latestImport=latest},customers=branches.Select(branch=>new{branch,year,total=sales[branch.Code].Sum(s=>s.Amount),categories=categories.Select(c=>new{c.Id,c.Name,amount=sales[branch.Code].FirstOrDefault(s=>s.CategoryId==c.Id)?.Amount??0m}),history=visits.Where(v=>v.BranchCode==branch.Code).Take(10).Select(VisitDto)}),editors});
        });
        api.MapGet("/admin/visits/export",async(ClaimsPrincipal user,AppDbContext db,string? afm,Guid? sellerId,DateOnly? from,DateOnly? to,bool latestOnly=false)=>
        {
            var visits=await Filter(db,user,afm,sellerId,from,to).Include(v=>v.User).Include(v=>v.Branch).ToListAsync();
            var catalog=await db.Questions.AsNoTracking().ToListAsync();
            var bytes=VisitExcelExport.Build(visits,catalog,latestOnly);
            return Results.File(bytes,"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"visits-{(latestOnly?"latest-":"")}{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
        }).RequireAuthorization("Admin");
    }
}
