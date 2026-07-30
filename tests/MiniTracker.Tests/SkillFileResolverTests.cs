using MiniTracker.Api.Services;

namespace MiniTracker.Tests;

public class SkillFileResolverTests
{
    [Fact]
    public void Resolves_a_folder_path_by_appending_SKILL_md()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        var result = SkillFileResolver.Resolve(root, "skills/backlog-tooling/");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "skills/backlog-tooling/SKILL.md")), result);
    }

    [Fact]
    public void Leaves_a_path_that_already_ends_in_dot_md_alone()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        var result = SkillFileResolver.Resolve(root, "skills/backlog-tooling/SKILL.md");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "skills/backlog-tooling/SKILL.md")), result);
    }

    [Fact]
    public void Rejects_paths_that_escape_the_skills_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "proj");

        Assert.Null(SkillFileResolver.Resolve(root, "../../secrets.txt"));
    }

    [Fact]
    public void Returns_null_for_an_empty_path()
    {
        Assert.Null(SkillFileResolver.Resolve(Path.GetTempPath(), ""));
    }
}
