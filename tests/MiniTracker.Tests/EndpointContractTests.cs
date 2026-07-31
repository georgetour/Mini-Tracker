using MiniTracker.Api.Backlog;
using MiniTracker.Api.Services;

namespace MiniTracker.Tests;

/// <summary>
/// Every rule the API enforces regardless of what the browser did. The confirmation dialogs, the
/// disabled buttons and the client-side checks are all courtesies — anyone can send these requests
/// with curl, so the rules have to live here.
/// </summary>
public class EndpointContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mt-ep-" + Guid.NewGuid().ToString("N"));
    private readonly BacklogService _svc;

    private string Skills => Path.Combine(_root, "skills");

    public EndpointContractTests()
    {
        Directory.CreateDirectory(Path.Combine(Skills, "board"));
        File.WriteAllText(Path.Combine(_root, "BACKLOG.yaml"), """
            project: Test
            roadmap: [V1]
            epics:
              - number: 0
                title: Tooling
                stories:
                  - code: US-01
                    title: Board
                    status: Done
                    release: V1
                    folder: board
            """);

        _svc = new BacklogService(() => Path.Combine(_root, "BACKLOG.yaml"), () => Skills);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // ---------- status vocabulary ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Finished")]
    [InlineData("done")]                                  // right word, wrong case
    [InlineData("<script>alert(1)</script>")]
    public void Status_rejects_anything_outside_the_vocabulary(string status)
    {
        Assert.Throws<BacklogValidationException>(() => _svc.SetStoryStatus("US-01", status));
    }

    [Theory]
    [InlineData("Maybe")]
    [InlineData("passed")]
    [InlineData("")]
    public void Test_case_status_rejects_anything_outside_its_vocabulary(string status)
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.SetTestCases("US-01", new[] { new TestCase("Check it", status) }));
    }

    // ---------- size bounds ----------

    [Fact]
    public void Task_text_has_an_upper_bound()
    {
        Assert.Throws<BacklogValidationException>(
            () => _svc.SetTasks("US-01", new[] { new TaskItem(new string('x', 501), false) }));
    }

    [Fact]
    public void A_story_cannot_hold_an_unbounded_number_of_tasks()
    {
        // The UI cannot produce this, so a request that does is a bug or an attack. Before the cap,
        // one call wrote a 168 KB tasks.yaml.
        var many = Enumerable.Range(0, 201).Select(i => new TaskItem("Task " + i, false)).ToList();

        Assert.Throws<BacklogValidationException>(() => _svc.SetTasks("US-01", many));
    }

    [Fact]
    public void The_cap_is_generous_enough_for_real_work()
    {
        var many = Enumerable.Range(0, 200).Select(i => new TaskItem("Task " + i, false)).ToList();

        _svc.SetTasks("US-01", many);

        Assert.Equal(200, _svc.GetStory("US-01").Tasks.Count);
    }

    [Fact]
    public void A_story_cannot_hold_an_unbounded_number_of_test_cases()
    {
        var many = Enumerable.Range(0, 201).Select(i => new TestCase("Check " + i, "Not Run")).ToList();

        Assert.Throws<BacklogValidationException>(() => _svc.SetTestCases("US-01", many));
    }

    // ---------- deliberate emptiness is allowed ----------

    [Fact]
    public void Tasks_may_be_emptied_deliberately()
    {
        _svc.SetTasks("US-01", new[] { new TaskItem("One", false) });

        _svc.SetTasks("US-01", Array.Empty<TaskItem>());

        Assert.Empty(_svc.GetStory("US-01").Tasks);
    }

    // ---------- text is data, never markup ----------

    [Fact]
    public void Task_text_is_stored_verbatim_and_never_interpreted()
    {
        _svc.SetTasks("US-01", new[] { new TaskItem("<script>alert(1)</script>", false) });

        Assert.Equal("<script>alert(1)</script>", _svc.GetStory("US-01").Tasks[0].Text);
    }

    [Fact]
    public void Text_that_looks_like_yaml_cannot_change_the_files_structure()
    {
        _svc.SetTasks("US-01", new[]
        {
            new TaskItem("done: true\n- text: injected", false),
            new TaskItem("- leading dash: and a colon", true),
        });

        var tasks = _svc.GetStory("US-01").Tasks;
        Assert.Equal(2, tasks.Count);
        Assert.Equal("done: true\n- text: injected", tasks[0].Text);
        Assert.False(tasks[0].Done);
    }

    // ---------- unknown targets ----------

    [Fact]
    public void An_unknown_story_is_rejected_everywhere_it_can_be_named()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.GetStory("US-99"));
        Assert.Throws<BacklogValidationException>(() => _svc.SetStoryStatus("US-99", "Done"));
        Assert.Throws<BacklogValidationException>(() => _svc.SetTasks("US-99", Array.Empty<TaskItem>()));
        Assert.Throws<BacklogValidationException>(() => _svc.SetTestCases("US-99", Array.Empty<TestCase>()));
        Assert.Throws<BacklogValidationException>(() => _svc.DeleteStory("US-99"));
    }

    [Fact]
    public void An_unknown_epic_is_rejected_everywhere_it_can_be_named()
    {
        Assert.Throws<BacklogValidationException>(() => _svc.RenameEpic(99, "Nope"));
        Assert.Throws<BacklogValidationException>(() => _svc.DeleteEpic(99));
        Assert.Throws<BacklogValidationException>(() => _svc.AddStory(99, "US-02", "Nope", "V1"));
    }

    // ---------- deleting is not gated by the dialog ----------

    [Fact]
    public void Deleting_works_without_any_confirmation_because_the_dialog_is_only_UI()
    {
        // Worth stating outright: the confirm dialog protects against a slip, not against a request.
        // What the server owes is that the target exists and that nothing else is touched.
        _svc.DeleteStory("US-01");

        Assert.Empty(_svc.GetBoard().Epics[0].Stories);
        Assert.False(Directory.Exists(Path.Combine(Skills, "board")));
    }
}
