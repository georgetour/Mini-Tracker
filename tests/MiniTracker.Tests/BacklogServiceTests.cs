using MiniTracker.Api.Backlog;
using MiniTracker.Api.Services;

namespace MiniTracker.Tests;

public class BacklogServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_and_writes_whatever_path_the_resolver_currently_returns()
    {
        var pathA = Path.Combine(_dir, "a.md");
        var pathB = Path.Combine(_dir, "b.md");
        File.WriteAllText(pathA, "# Epic 0: A\n");
        File.WriteAllText(pathB, "# Epic 0: B\n");

        var current = pathA;
        var svc = new BacklogService(() => current);
        Assert.Equal("A", svc.GetBoard().Epics[0].Title);

        current = pathB; // simulates a Configure change — no restart needed
        Assert.Equal("B", svc.GetBoard().Epics[0].Title);
    }

    [Fact]
    public void SetStoryStatus_resolves_the_path_once_so_a_concurrent_Configure_cannot_redirect_the_write()
    {
        // Regression test for: a write-op used to call the resolver separately for its read and its
        // write, so a Configure that landed in between could make the write land on a different file
        // than the read — e.g. read the demo, then write over the user's real project backlog.
        var pathA = Path.Combine(_dir, "a.md");
        var pathB = Path.Combine(_dir, "b.md");
        File.WriteAllText(pathA, "# Epic 0: A\n## US-1 · Story\n> **Status**: ⬜ Not Yet Started · v1\n");
        File.WriteAllText(pathB, "SENTINEL — must never be written to");

        var resolveCalls = 0;
        // Simulates a Configure landing mid-operation: the resolver starts returning a different path
        // after its first call.
        var svc = new BacklogService(() => { resolveCalls++; return resolveCalls == 1 ? pathA : pathB; });

        svc.SetStoryStatus("US-1", new StatusToken("✅", "Done"));

        Assert.Equal(1, resolveCalls); // the whole operation resolved the path exactly once
        Assert.Equal("SENTINEL — must never be written to", File.ReadAllText(pathB));
        Assert.Contains("✅ Done", File.ReadAllText(pathA));
    }

    [Fact]
    public void BacklogPath_reflects_the_resolver_live()
    {
        var current = Path.Combine(_dir, "a.md");
        var svc = new BacklogService(() => current);

        Assert.Equal(current, svc.BacklogPath);

        current = Path.Combine(_dir, "b.md");
        Assert.Equal(current, svc.BacklogPath);
    }
}
