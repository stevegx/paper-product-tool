using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Paper.Api.ApiHelpers;

namespace Paper.Api;
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapGet("/api/auth/status", async (AppDbContext db) => new { setupRequired = !await db.Users.AnyAsync() });
        app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, AppDbContext db) => PublicUser(await db.Users.SingleAsync(x => x.Id == UserId(principal)))).RequireAuthorization();
        app.MapPost("/api/auth/setup", async (CreateUserInput input, HttpContext context, AppDbContext db, PasswordHasher<AppUser> hasher, WriteGate gate) =>
        {
            if (!app.Environment.IsDevelopment() || context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address)) return Results.Json(new { message = "Η αρχική ρύθμιση γίνεται μόνο τοπικά σε περιβάλλον Development. Σε server χρησιμοποίησε Bootstrap__Email και Bootstrap__Password." }, statusCode: 403);
            await gate.Lock.WaitAsync(context.RequestAborted);
            try
            {
                if (await db.Users.AnyAsync()) return Results.Conflict(new { message = "Έχει ήδη δημιουργηθεί διαχειριστής." });
                var user = MakeUser(input with { Role = "Admin" }, hasher);
                db.Users.Add(user); await db.SaveChangesAsync(); await SignIn(context, user); return Results.Ok(PublicUser(user));
            }
            finally { gate.Lock.Release(); }
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/login", async (LoginInput input, HttpContext context, AppDbContext db, PasswordHasher<AppUser> hasher) =>
        {
            var email = (input.Email ?? "").Trim().ToLowerInvariant();
            if (email.Length > 254 || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128) return Results.Unauthorized();
            var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email && x.Active);
            var checkUser = user ?? new AppUser();
            var hash = user?.PasswordHash ?? hasher.HashPassword(checkUser, "unavailable-account-password");
            var result = hasher.VerifyHashedPassword(checkUser, hash, input.Password);
            if (user is null || result == PasswordVerificationResult.Failed) return Results.Unauthorized();
            if (result == PasswordVerificationResult.SuccessRehashNeeded) { user.PasswordHash = hasher.HashPassword(user, input.Password); await db.SaveChangesAsync(); }
            await SignIn(context, user); return Results.Ok(PublicUser(user));
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", async (HttpContext context) => { await context.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
    }
}
