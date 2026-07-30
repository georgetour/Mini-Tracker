using System.Diagnostics;
using System.Text;
using MiniTracker.Api.Backlog;
using MiniTracker.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var trackerConfigService = new TrackerConfigService(
    Path.Combine(builder.Environment.ContentRootPath, "tracker.config.json"));
var demoBacklogPath = Path.Combine(builder.Environment.ContentRootPath, "data", "BACKLOG.demo.md");
var overridePath = builder.Configuration["BacklogPath"];

string ResolveBacklogPath() => trackerConfigService.ResolveBacklogPath(overridePath, demoBacklogPath);

// CLI mode: `dotnet run -- sync-status` regenerates the STATUS-SUMMARY and exits.
// Pure C#, cross-platform — no external tooling required.
if (args.Length > 0 && args[0] == "sync-status")
{
    var cliService = new BacklogService(ResolveBacklogPath);
    cliService.SyncStatus();
    Console.WriteLine($"Status synced: {cliService.BacklogPath}");
    return;
}

builder.Services.AddSingleton(trackerConfigService);
builder.Services.AddSingleton(new BacklogService(ResolveBacklogPath));

var app = builder.Build();
// Dynamic data must never be cached: a stale board would show statuses that are no longer in the
// file. Without an explicit header a browser may heuristically cache a GET, so we say it outright.
// A Content-Security-Policy with no 'unsafe-eval' and no 'unsafe-inline' for scripts. This is why
// the front end uses Alpine's CSP build: it evaluates its bindings without new Function(), so an
// injected string can never become executable code — the browser refuses to run it, rather than us
// relying on having escaped every value correctly.
// 'unsafe-inline' remains for styles only: x-show and x-bind:style write the style attribute, and
// there is no script-execution risk in a style. Fonts come from Google, so those two hosts are
// named explicitly rather than opening style-src and font-src to everything.
const string ContentSecurityPolicy =
    "default-src 'self'; " +
    "script-src 'self'; " +
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
    "font-src https://fonts.gstatic.com; " +
    "img-src 'self' data:; " +
    "connect-src 'self'; " +
    "form-action 'self'; " +
    "base-uri 'none'; " +
    "object-src 'none'; " +
    "frame-ancestors 'none'";

app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers["Content-Security-Policy"] = ContentSecurityPolicy;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";

    if (ctx.Request.Path.StartsWithSegments("/api"))
        headers.CacheControl = "no-store";
    await next();
});

app.UseDefaultFiles();
// "no-cache" means revalidate before use — NOT "don't cache". Combined with the ETag that
// StaticFiles already sends, an unchanged file costs a 304 instead of a full download, while an
// edited app.js is picked up immediately. ("no-store" would forbid caching entirely and make the
// ETag pointless, which is what a stale-looking UI after an update taught us the first time.)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

app.MapGet("/api/board", (BacklogService svc) => Results.Json(svc.GetBoard()));

app.MapPost("/api/story/{code}/status", (string code, StatusRequest r, BacklogService svc) =>
    Results.Json(svc.SetStoryStatus(code, new StatusToken(r.Emoji, r.Label))));

app.MapPost("/api/story/{code}/task/{taskId}", (string code, string taskId, TaskRequest r, BacklogService svc) =>
    Results.Json(svc.SetTaskDone(code, taskId, r.Done)));

app.MapPost("/api/story/{code}/testcase/{tcId}", (string code, string tcId, StatusRequest r, BacklogService svc) =>
    Results.Json(svc.SetTestCaseStatus(code, tcId, new StatusToken(r.Emoji, r.Label))));

// Optional convenience: `git add` the resolved backlog file. Never commits.
app.MapPost("/api/git/stage", (BacklogService svc) =>
{
    try
    {
        var backlogPath = svc.BacklogPath; // resolve once — avoids acting on two different paths
                                            // if Configure changes it mid-request
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path.GetDirectoryName(backlogPath)!,
            RedirectStandardError = true, UseShellExecute = false
        };
        psi.ArgumentList.Add("add");
        psi.ArgumentList.Add(Path.GetFileName(backlogPath));
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode == 0 ? Results.Ok(new { staged = true })
                               : Results.Problem(p.StandardError.ReadToEnd());
    }
    catch (Exception e) { return Results.Problem(e.Message); }
});

app.MapGet("/api/config", (TrackerConfigService cfg) => Results.Json(cfg.Load()));

app.MapPost("/api/config/backlog", (PathRequest r, TrackerConfigService cfg) =>
{
    try { return Results.Json(cfg.SetBacklogPath(r.Path)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
    catch (IOException) { return Results.BadRequest("That file could not be created. Check the folder exists and is writable."); }
    catch (UnauthorizedAccessException) { return Results.BadRequest("That location is not writable."); }
});

app.MapPost("/api/config/skills", (PathRequest r, TrackerConfigService cfg) =>
{
    try { return Results.Json(cfg.SetSkillsPath(r.Path)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/config/logo", async (HttpRequest req, TrackerConfigService cfg, IWebHostEnvironment env) =>
{
    if (!req.HasFormContentType) return Results.BadRequest("Expected multipart/form-data");
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("logo");
    if (file is null || file.Length == 0) return Results.BadRequest("No file uploaded");
    if (file.Length > 2 * 1024 * 1024) return Results.BadRequest("Logo must be under 2 MB");

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowedExts = new[] { ".png", ".jpg", ".jpeg", ".svg", ".webp" };
    if (!allowedExts.Contains(ext)) return Results.BadRequest("Logo must be PNG, JPG, SVG, or WebP");

    var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
    Directory.CreateDirectory(uploadsDir);
    var savedName = "logo" + ext;
    await using (var stream = File.Create(Path.Combine(uploadsDir, savedName)))
        await file.CopyToAsync(stream);

    return Results.Json(cfg.SetLogoPath($"/uploads/{savedName}"));
});

app.MapGet("/api/skill", (string path, TrackerConfigService cfg) =>
{
    var skillsRoot = cfg.Load().SkillsPath;
    if (string.IsNullOrWhiteSpace(skillsRoot)) return Results.NotFound("No skills folder configured.");

    var resolved = SkillFileResolver.Resolve(skillsRoot, path);
    if (resolved is null || !File.Exists(resolved)) return Results.NotFound("SKILL.md not found.");

    return Results.Text(File.ReadAllText(resolved), "text/plain");
});

// Saves an edited SKILL.md. Only writes inside the configured skills folder, and only over a file
// that already exists — this endpoint edits specs, it does not create arbitrary files on disk.
app.MapPost("/api/skill", (SaveSkillRequest r, TrackerConfigService cfg) =>
{
    var skillsRoot = cfg.Load().SkillsPath;
    if (string.IsNullOrWhiteSpace(skillsRoot)) return Results.BadRequest("No skills folder is configured yet.");

    var resolved = SkillFileResolver.Resolve(skillsRoot, r.Path);
    if (resolved is null) return Results.BadRequest("That path is outside the skills folder.");
    if (!File.Exists(resolved)) return Results.BadRequest("That SKILL.md does not exist.");

    File.WriteAllText(resolved, r.Content ?? "", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    return Results.Ok(new { saved = true });
});

// Clears the logo. The uploaded file is left on disk — harmless, gitignored, and it means an
// accidental removal can be undone by re-uploading rather than losing the original.
app.MapDelete("/api/config/logo", (TrackerConfigService cfg) => Results.Json(cfg.SetLogoPath(null)));

// Creating epics and stories. Validation failures come back as 400 with a message written for
// the person using the app, so the form can show it verbatim.
app.MapPost("/api/epic", (AddEpicRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.AddEpic(r.Number, r.Title)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/story", (AddStoryRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.AddStory(r.EpicNumber, r.Code, r.Title, r.Release, r.SkillPath)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapPost("/api/epic/{number:int}", (int number, RenameRequest r, BacklogService svc) =>
{
    try { return Results.Json(svc.RenameEpic(number, r.Title)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapDelete("/api/epic/{number:int}", (int number, BacklogService svc) =>
{
    try { return Results.Json(svc.DeleteEpic(number)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

app.MapDelete("/api/story/{code}", (string code, BacklogService svc) =>
{
    try { return Results.Json(svc.DeleteStory(code)); }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
});

// Gives a story a SKILL.md when it has none: creates the file from the template, then records the
// path in BACKLOG.md. Idempotent — if the story already points at a file that exists, nothing is
// written and the existing path comes back, so this can never overwrite someone's spec.
app.MapPost("/api/story/{code}/skill", (string code, BacklogService svc, TrackerConfigService cfg) =>
{
    var skillsRoot = cfg.Load().SkillsPath;
    if (string.IsNullOrWhiteSpace(skillsRoot))
        return Results.BadRequest("Set a skills folder in Configure first, then try again.");

    var stories = svc.GetBoard().Epics.SelectMany(e => e.Stories).ToList();
    var story = stories.FirstOrDefault(s => s.Code == code);
    if (story is null) return Results.BadRequest($"There is no story {code}.");

    var relative = string.IsNullOrWhiteSpace(story.SkillPath)
        ? $"{SkillFolderPrefix(stories)}{Slug(code, story.Title)}/SKILL.md"
        : story.SkillPath;

    var resolved = SkillFileResolver.Resolve(skillsRoot, relative);
    if (resolved is null) return Results.BadRequest("That skill path points outside the skills folder.");

    try
    {
        if (!File.Exists(resolved))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
            var template = File.ReadAllText(TemplateLocator.Find("SKILL.template.md"))
                .Replace("skill-name-here", Slug(code, story.Title))
                .Replace("# [Skill Name]", $"# {story.Code} · {story.Title}");
            File.WriteAllText(resolved, template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        var board = string.IsNullOrWhiteSpace(story.SkillPath)
            ? svc.SetStorySkill(code, relative)
            : svc.GetBoard();
        return Results.Json(new { path = relative, board });
    }
    catch (BacklogValidationException e) { return Results.BadRequest(e.Message); }
    catch (IOException) { return Results.BadRequest("The skill file could not be created. Check the folder is writable."); }
    catch (UnauthorizedAccessException) { return Results.BadRequest("The skills folder is not writable."); }
});

// A new description file should sit beside the existing ones. Backlogs written from the template
// record paths as "skills/<name>/SKILL.md", but a project may use its own folder — so the prefix is
// copied from whatever the other stories already use, and only falls back to "skills/".
static string SkillFolderPrefix(IEnumerable<Story> stories)
{
    foreach (var path in stories.Select(s => s.SkillPath).Where(p => !string.IsNullOrWhiteSpace(p)))
    {
        var segments = path!.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3) return segments[0] + "/";   // <folder>/<name>/SKILL.md
        if (segments.Length == 2) return "";                  // <name>/SKILL.md — already flat
    }
    return "skills/";
}

// "US-07", "Checkout basket" -> "us-07-checkout-basket" — a folder name that stays readable and
// sorts with the backlog.
static string Slug(string code, string title)
{
    var text = $"{code} {title}".ToLowerInvariant();
    var sb = new StringBuilder();
    foreach (var ch in text)
    {
        if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
    }
    return sb.ToString().Trim('-') is { Length: > 0 } s ? s : "skill";
}

// Real page URLs (/add-epic, /add-story, /configure) are handled by the client router, so a
// direct visit or a refresh has to return index.html rather than a 404. API routes are matched
// first, so this never shadows them.
app.MapFallbackToFile("index.html");

app.Run();

record StatusRequest(string Emoji, string Label);
record TaskRequest(bool Done);
record PathRequest(string Path);
record AddEpicRequest(int Number, string Title);
record AddStoryRequest(int EpicNumber, string Code, string Title, string? Release, string? SkillPath);
record SaveSkillRequest(string Path, string Content);
record RenameRequest(string Title);
