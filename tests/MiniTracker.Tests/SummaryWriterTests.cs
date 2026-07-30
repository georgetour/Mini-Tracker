using System.Text.RegularExpressions;
using MiniTracker.Api.Backlog;
using MiniTracker.Api.Services;

namespace MiniTracker.Tests;

public class SummaryWriterTests
{
    private static string RealBacklogPath() => TestBacklogLocator.Resolve();
    private static string DemoTemplatePath() => TemplateLocator.Find("BACKLOG.template.md");

    private static string Lf(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

    /// <summary>
    /// The critical guarantee: given an existing BACKLOG.md's STATUS-SUMMARY block, the C# generator
    /// reproduces those exact bytes from the parsed board — proving the regenerator is byte-exact, not
    /// just "close enough." Covers both the test fixture and the shipped demo template — the template
    /// is hand-maintained (24 Acme App stories) and nothing else guards it against drifting out of sync
    /// with a hand-edited status.
    /// </summary>
    [Theory]
    [MemberData(nameof(BacklogPaths))]
    public void Reproduces_the_live_summary_block_byte_for_byte(string path)
    {
        var text = Lf(File.ReadAllText(path));

        var start = text.IndexOf(SummaryWriter.StartMarker, StringComparison.Ordinal);
        var end = text.IndexOf(SummaryWriter.EndMarker, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "STATUS-SUMMARY markers not found");
        var existingBlock = text.Substring(start, end + SummaryWriter.EndMarker.Length - start);

        // Use the date already baked into the live block so the comparison isn't a moving target.
        var date = DateOnly.ParseExact(
            Regex.Match(existingBlock, @"Synced — \d+ stories — (\d{4}-\d{2}-\d{2})").Groups[1].Value,
            "yyyy-MM-dd");

        var generated = SummaryWriter.Generate(BacklogParser.Parse(text), date);

        Assert.Equal(existingBlock, generated);
    }

    public static IEnumerable<object[]> BacklogPaths()
    {
        yield return new object[] { RealBacklogPath() };
        yield return new object[] { DemoTemplatePath() };
    }

    [Fact]
    public void Formats_a_small_board_exactly()
    {
        const string md =
            "| V0.1 | x |\n| V1 | y |\n" +
            "# Epic 0: Tooling\n" +
            "## US-90 · Alpha\n> **Status**: 🔄 In Progress · **Release**: V0.1\n" +
            "## US-91 · Beta\n> **Status**: ✅ Done · **Release**: V1\n";

        var block = SummaryWriter.Generate(BacklogParser.Parse(md), new DateOnly(2026, 7, 28));

        Assert.Contains("- [x] Synced — 2 stories — 2026-07-28", block);
        Assert.Contains("- 📍 Current epic: **Epic 0 — Tooling**", block); // In Progress = highest rank
        Assert.Contains("- 🔄 In Progress — 1", block);
        Assert.Contains("- ✅ Done — 1", block);
        Assert.Contains("- V0.1 — 1", block);
        Assert.Contains("- Epic 0 — Tooling — 2 (🔄 1 · ✅ 1) 📍", block);
    }
}
