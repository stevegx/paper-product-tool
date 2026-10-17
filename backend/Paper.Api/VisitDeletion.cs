using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;

namespace Paper.Api;
public sealed record VisitVersionInput(int ExpectedVersion);
public static class VisitDeletion
{
    public static IResult Gone() => Results.Json(new {code="visit_deleted",
        message="Η επίσκεψη έχει διαγραφεί. Οι τοπικές αλλαγές δεν στάλθηκαν."},statusCode:410);

    public static void MapVisitDeletion(this WebApplication app)
    {
        var api=app.MapGroup("/api/visits").RequireAuthorization();
        api.MapDelete("/{id:guid}",async(Guid id,[FromBody] VisitVersionInput input,
            ClaimsPrincipal user,AppDbContext db,WriteGate gate,CancellationToken ct)=>
            await Change(id,input,user,db,gate,true,ct));
        api.MapPost("/{id:guid}/restore",async(Guid id,VisitVersionInput input,
            ClaimsPrincipal user,AppDbContext db,WriteGate gate,CancellationToken ct)=>
            await Change(id,input,user,db,gate,false,ct));
    }

    private static async Task<IResult> Change(Guid id,VisitVersionInput input,ClaimsPrincipal user,
        AppDbContext db,WriteGate gate,bool delete,CancellationToken ct)
    {
        await gate.Lock.WaitAsync(ct);
        try
        {
            var visit=await db.Visits.IgnoreQueryFilters().Include(v=>v.User).Include(v=>v.Branch)
                .SingleOrDefaultAsync(v=>v.Id==id,ct);
            if(visit is null)return Results.NotFound();
            if(visit.UserId!=UserId(user)&&!user.IsInRole("Admin"))return Results.Forbid();
            // A retry after a lost acknowledgement changes neither the version nor the audit again.
            if(visit.IsDeleted==delete)return delete?Results.NoContent():Results.Ok(VisitDto(visit));
            if(visit.Version!=input.ExpectedVersion)return Results.Conflict(new {code="conflict",
                message="Η επίσκεψη άλλαξε. Πάτησε «Ανανέωση» και έλεγξέ την πριν συνεχίσεις."});
            var before=JsonSerializer.Serialize(VisitDto(visit),VisitEndpoints.WebJson);
            visit.IsDeleted=delete;
            visit.DeletedAtUtc=delete?DateTime.UtcNow:null;
            visit.DeletedById=delete?UserId(user):null;
            visit.Version++;
            visit.UpdatedAtUtc=DateTime.UtcNow;
            visit.LastEditedById=UserId(user);
            db.VisitRevisions.Add(new VisitRevision {VisitId=id,EditorId=UserId(user),
                EditorName=user.Identity?.Name??"",BeforeJson=before,
                AfterJson=JsonSerializer.Serialize(VisitDto(visit),VisitEndpoints.WebJson)});
            await db.SaveChangesAsync(ct);
            return delete?Results.NoContent():Results.Ok(VisitDto(visit));
        }
        finally{gate.Lock.Release();}
    }
}
