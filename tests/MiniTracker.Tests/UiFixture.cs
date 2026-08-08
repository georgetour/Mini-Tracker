using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace MiniTracker.Tests;

/// <summary>
/// Runs the real app on a real port and drives it with a real browser.
///
/// Everything else in this suite tests C# directly, which is why the UI was the one part with no
/// coverage — and where the bugs kept being found by hand. A menu that cannot render because a
/// parent is display:none, or an Alpine binding the CSP build rejects, is invisible to every other
/// kind of test here.
///
/// The app is started as a process rather than an in-memory TestServer because a browser needs
/// something listening on a socket, and because this exercises the same startup path a user gets.
/// </summary>
public sealed class UiFixture : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mt-ui-" + Guid.NewGuid().ToString("N"));
    private Process? _app;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        WriteSampleProject();

        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _app = StartApp(port);
        await WaitUntilServing();

        // Installs Chromium on first run, and on Linux the system libraries it needs — a bare
        // .NET SDK image has none of them, and the browser fails to launch with a wall of missing
        // libx11/libnss3 names rather than anything that reads like a cause.
        //
        // Done in-process on purpose: the installer Playwright ships is a PowerShell script, and
        // this repo keeps CI free of shell dependencies. Idempotent once installed.
        var exit = Microsoft.Playwright.Program.Main(["install", "--with-deps", "chromium"]);
        if (exit != 0)
            throw new InvalidOperationException(
                $"Playwright could not install Chromium (exit {exit}). On Linux this needs root or "
              + "passwordless sudo to add the browser's system libraries.");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <summary>A page sized like a phone or a desktop, with console errors collected. Anything
    /// Alpine's CSP build rejects surfaces here rather than as a silently dead button.</summary>
    public async Task<(IPage Page, List<string> Errors)> NewPageAsync(int width = 1280, int height = 800)
    {
        var context = await Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height } });
        var page = await context.NewPageAsync();

        var errors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        page.PageError += (_, e) => errors.Add(e);

        return (page, errors);
    }

    /// <summary>A path for a project that does not exist yet — adding it should create the backlog
    /// from the template, which is the behaviour Configure already has for a single project.</summary>
    public string UncreatedProjectPath => Path.Combine(_root, "second", "BACKLOG.yaml");

    /// <summary>The project every other test expects to be open.</summary>
    public string PrimaryBacklogPath => Path.Combine(_root, "BACKLOG.yaml");

    /// <summary>Switches back to the primary project. One app serves the whole collection, so a
    /// test that changes which project is open has to put it back — otherwise it decides what
    /// every test after it sees.</summary>
    public async Task UsePrimaryProjectAsync()
    {
        using var http = new HttpClient();
        var body = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { path = PrimaryBacklogPath }),
            System.Text.Encoding.UTF8, "application/json");

        var res = await http.PostAsync($"{BaseUrl}/api/projects/select", body);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Uploads a logo through the app's own endpoint, so the logo-set state is reached the
    /// way a person reaches it rather than by writing config behind the app's back.</summary>
    public async Task SetLogoAsync()
    {
        // A 1x1 PNG — the endpoint only cares that it is an image with an allowed extension.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        using var http = new HttpClient();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "logo", "logo.png");

        var res = await http.PostAsync($"{BaseUrl}/api/config/logo", form);
        res.EnsureSuccessStatusCode();
    }

    private void WriteSampleProject()
    {
        Directory.CreateDirectory(Path.Combine(_root, "skills", "board"));
        Directory.CreateDirectory(Path.Combine(_root, "skills", "write-back"));

        File.WriteAllText(Path.Combine(_root, "BACKLOG.yaml"), """
            project: UI Test
            roadmap: [V1]
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Backlog Board
                    status: Done
                    release: V1
                    folder: board
                  - code: US-02
                    title: Status Write Back
                    status: Not Yet Started
                    folder: write-back
              - number: 1
                title: Empty Epic
            """);

        // One story with a release and one without: that difference is what used to knock the
        // status column out of line.
        File.WriteAllText(Path.Combine(_root, "skills", "board", "SKILL.md"),
            "---\nname: board\n---\n\n# Backlog Board\n\n## Description\n\nThe board.\n");
        File.WriteAllText(Path.Combine(_root, "skills", "write-back", "SKILL.md"),
            "---\nname: write-back\n---\n\n# Status Write Back\n\n## Description\n\nWrites back.\n");
    }

    private Process StartApp(int port)
    {
        // A content root of our own, holding a copy of wwwroot. Pointing at the source project
        // would work, but tracker.config.json lives there — so the tests would inherit whatever
        // backlog and logo the developer happens to have configured, and pass or fail by machine.
        var dll = Path.Combine(AppContext.BaseDirectory, "MiniTracker.Api.dll");
        var contentRoot = Path.Combine(_root, "app");
        Directory.CreateDirectory(contentRoot);
        CopyDirectory(Path.Combine(FindRepoPath(Path.Combine("src", "MiniTracker.Api")), "wwwroot"),
                      Path.Combine(contentRoot, "wwwroot"));

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Seeded through tracker.config.json rather than --BacklogPath. A deploy-time override
        // always wins over configuration, which would pin the app and make project switching a
        // no-op — so driving it that way would test a mode nobody runs interactively.
        File.WriteAllText(Path.Combine(contentRoot, "tracker.config.json"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                BacklogPath = Path.Combine(_root, "BACKLOG.yaml"),
                SkillsPath = Path.Combine(_root, "skills"),
                LogoPath = (string?)null,
                IsDemo = false,
            }));

        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add($"--urls={BaseUrl}");
        psi.ArgumentList.Add($"--contentRoot={contentRoot}");

        var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the app.");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private async Task WaitUntilServing()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        for (var i = 0; i < 60; i++)
        {
            if (_app is { HasExited: true })
                throw new InvalidOperationException($"The app exited early with code {_app.ExitCode}.");
            try
            {
                var res = await http.GetAsync($"{BaseUrl}/api/board");
                if (res.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { /* not listening yet */ }
            catch (TaskCanceledException) { /* still starting */ }
            await Task.Delay(500);
        }
        throw new TimeoutException($"The app never served {BaseUrl}/api/board.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static string FindRepoPath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"{relative} not found above {AppContext.BaseDirectory}.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            _app.Kill(entireProcessTree: true);
            _app.WaitForExit(5000);
        }
        _app?.Dispose();

        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}

/// <summary>Binds the fixture to the "ui" collection, so one app and one browser serve every UI
/// test rather than being started per test.</summary>
[CollectionDefinition("ui")]
public sealed class UiCollectionDefinition : ICollectionFixture<UiFixture>;
