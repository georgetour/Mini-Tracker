using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MiniTracker.Api.Backlog;

/// <summary>
/// Reads and writes BACKLOG.yaml.
///
/// Whole-file deserialize → mutate → serialize. The app is the only writer, so there is nothing to
/// preserve between writes and no surgical text editing to get wrong — which is what let
/// BacklogWriter, BacklogGenerator and SummaryWriter be deleted outright. Serialization is
/// deterministic, so changing one status still shows up as one line in a diff.
/// </summary>
public static class YamlIndex
{
    // Shapes YamlDotNet binds to, kept separate from the domain records so a file missing a field
    // is a default rather than an exception, and so the domain never has to model "absent".
    private sealed class IndexDto
    {
        public string Project { get; set; } = "";
        public List<string> Roadmap { get; set; } = new();
        public List<EpicDto> Epics { get; set; } = new();
    }

    private sealed class EpicDto
    {
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public List<StoryDto> Stories { get; set; } = new();
    }

    private sealed class StoryDto
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Status { get; set; } = "Not Yet Started";
        public string Release { get; set; } = "";
        public string Folder { get; set; } = "";
    }

    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static Board Parse(string yaml)
    {
        var dto = (string.IsNullOrWhiteSpace(yaml) ? null : Reader.Deserialize<IndexDto>(yaml))
                  ?? new IndexDto();

        var epics = dto.Epics.Select(e => new Epic(
            e.Number,
            e.Title ?? "",
            (e.Stories ?? new List<StoryDto>())
                .Select(s => new Story(s.Code ?? "", s.Title ?? "",
                                       string.IsNullOrWhiteSpace(s.Status) ? "Not Yet Started" : s.Status,
                                       s.Release ?? "", s.Folder ?? ""))
                .ToList()
        )).ToList();

        return new Board(dto.Project ?? "", dto.Roadmap ?? new List<string>(), AssignSlugs(epics));
    }

    public static string Write(Board board) => Writer.Serialize(new IndexDto
    {
        Project = board.Project,
        Roadmap = board.Roadmap.ToList(),
        Epics = board.Epics.Select(e => new EpicDto
        {
            Number = e.Number,
            Title = e.Title,
            Stories = e.Stories.Select(s => new StoryDto
            {
                Code = s.Code,
                Title = s.Title,
                Status = s.Status,
                Release = s.Release,
                Folder = s.Folder,
            }).ToList(),
        }).ToList(),
    });

    /// <summary>
    /// Epic slugs are unique board-wide and steer clear of the app's own paths; story slugs only
    /// need to be unique inside their epic, because a story URL is always reached through one —
    /// /core-application/checkout-and-payment.
    /// </summary>
    private static List<Epic> AssignSlugs(List<Epic> epics)
    {
        var epicSlugs = Slugs.Unique(
            epics.Select(e => e.Title).ToList(),
            epics.Select(e => $"epic-{e.Number}").ToList(),
            topLevel: true);

        return epics.Select((epic, i) =>
        {
            var storySlugs = Slugs.Unique(
                epic.Stories.Select(s => s.Title).ToList(),
                epic.Stories.Select(s => s.Code).ToList(),
                topLevel: false);

            var stories = epic.Stories.Select((s, j) => s with { Slug = storySlugs[j] }).ToList();
            return epic with { Slug = epicSlugs[i], Stories = stories };
        }).ToList();
    }
}
