using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniTracker.Tests;

/// <summary>
/// The Codespaces entry point. With no screenshots in the README, the badge is the only way anyone
/// sees this app before cloning it, so the file behind it is worth pinning.
///
/// This asserts the config's own text rather than behaviour, which is usually a poor test — but
/// here the file *is* the deliverable, and there is no way to launch a Codespace from a test run.
/// </summary>
public class DevcontainerTests
{
    private static string Read()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".devcontainer", "devcontainer.json");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(".devcontainer/devcontainer.json not found above the test output.");
    }

    /// <summary>Strips // comments so the JSONC the devcontainer spec allows can be parsed.</summary>
    private static JsonDocument Parse() =>
        JsonDocument.Parse(Regex.Replace(Read(), @"^\s*//.*$", "", RegexOptions.Multiline));

    [Fact]
    public void The_app_is_started_bound_to_every_interface()
    {
        // It shipped binding to localhost. ASPNETCORE_URLS in containerEnv does not survive
        // `dotnet run`, which applies launchSettings.json and its applicationUrl of
        // http://localhost:5249 — so the port forwarder had nothing to reach.
        var attach = Parse().RootElement.GetProperty("postAttachCommand").GetString()!;

        Assert.Contains("--urls", attach);
        Assert.Contains("0.0.0.0", attach);
        Assert.DoesNotContain("--urls http://localhost", attach);
    }

    [Fact]
    public void Attaching_twice_does_not_start_a_second_copy()
    {
        // postAttachCommand runs on every attach, and a browser reconnecting counts. Unguarded,
        // the second run bound a port the first was holding and died with a page of stack trace —
        // which is what someone opening the badge actually saw.
        var attach = Parse().RootElement.GetProperty("postAttachCommand").GetString()!;

        Assert.Contains("||", attach);
        Assert.Contains("curl", attach);
        // Not pgrep: the shell running this command has the project name in its own command line,
        // so a process check would always match itself and never start the app.
        Assert.DoesNotContain("pgrep", attach);
    }

    [Fact]
    public void The_port_it_forwards_is_the_port_it_starts_on()
    {
        // Two numbers that have to agree; nothing but this notices when they stop.
        var root = Parse().RootElement;
        var forwarded = root.GetProperty("forwardPorts").EnumerateArray().Select(p => p.GetInt32()).ToArray();
        var attach = root.GetProperty("postAttachCommand").GetString()!;

        var started = int.Parse(Regex.Match(attach, @":(\d+)").Groups[1].Value,
                                System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(started, forwarded);
    }

    [Fact]
    public void Opening_a_codespace_restores_only_what_it_needs_to_run()
    {
        // Restoring the solution also pulls Playwright and the test SDK, which nobody opening a
        // Codespace to look at the board is waiting for on purpose.
        var create = Parse().RootElement.GetProperty("postCreateCommand").GetString()!;

        Assert.Contains("src/MiniTracker.Api", create);
    }
}
