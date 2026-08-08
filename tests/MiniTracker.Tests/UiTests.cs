using Microsoft.Playwright;

namespace MiniTracker.Tests;

/// <summary>
/// What the page actually does, in a browser, at a real size.
///
/// Each of these corresponds to something that was broken and found by hand. The point is not
/// coverage for its own sake — it is that none of them could have been caught by any other test in
/// this suite, because they are all consequences of CSS and of Alpine binding to the DOM.
/// </summary>
[Collection("ui")]
public class UiTests(UiFixture fx)
{
    [Fact]
    public async Task The_board_renders_its_stories()
    {
        // A smoke test that means more than it looks: the rows only exist if Alpine started, which
        // it will not do under the strict CSP if a binding uses syntax the CSP build rejects.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator(".story-row").First).ToBeVisibleAsync();
        Assert.Equal(2, await page.Locator(".story-row:visible").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task The_add_menu_opens_from_the_plus_button_on_a_phone()
    {
        // This is the bug. #addMenu is a child of .addwrap, and hiding .addwrap on mobile removed
        // the menu with it, so the bottom bar's + toggled state nothing could see. No C# test could
        // have seen that, because nothing about the markup or the CSS text is wrong on its face.
        var (page, errors) = await fx.NewPageAsync(390, 760);
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator("#addMenu")).Not.ToBeVisibleAsync();
        await page.Locator(".mnav-add").ClickAsync();

        await Assertions.Expect(page.Locator("#addMenu")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#addMenu button", new() { HasTextString = "New epic" })).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task The_add_menu_still_opens_from_the_pill_on_a_desktop()
    {
        var (page, _) = await fx.NewPageAsync(1280, 800);
        await page.GotoAsync(fx.BaseUrl);

        await page.Locator("#btnAdd").ClickAsync();
        await Assertions.Expect(page.Locator("#addMenu")).ToBeVisibleAsync();

        // Anchored under the pill rather than floating at the bottom of the viewport.
        var menu = await page.Locator("#addMenu").BoundingBoxAsync();
        var pill = await page.Locator("#btnAdd").BoundingBoxAsync();
        Assert.True(menu!.Y > pill!.Y, "The desktop menu should sit below the Add pill.");
    }

    [Fact]
    public async Task The_logo_slot_becomes_a_real_link_once_a_logo_is_set()
    {
        // One test rather than two, because setting a logo changes shared state — split across two
        // tests, whichever ran first decided the other's result.
        //
        // The transition is the point: with nothing to link to it is the button that opens
        // Configure; with a logo it must be an anchor, because no amount of JavaScript makes a
        // <button> ctrl-clickable into a new tab.
        // Located by tag deliberately, and asserted with Expect so it retries: the config arrives
        // asynchronously, so checking the element the instant the page loads is a race.
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator("button#logoSlot")).ToBeVisibleAsync();
        await page.Locator("#logoSlot").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/configure");

        await fx.SetLogoAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator("a#logoSlot")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#logoSlot")).ToHaveAttributeAsync("href", "/");

        // And prove the point: ctrl-click opens a second tab.
        await page.GotoAsync($"{fx.BaseUrl}/tooling");
        await Assertions.Expect(page.Locator("a#logoSlot")).ToBeVisibleAsync();
        var opened = await page.Context.RunAndWaitForPageAsync(async () =>
            await page.Locator("#logoSlot").ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] }));
        Assert.NotNull(opened);
    }

    [Fact]
    public async Task Status_chips_and_release_tags_line_up_into_columns()
    {
        // One story has a release and one does not — the case that used to leave the slot collapsed
        // and every chip after it at a different x.
        var (page, _) = await fx.NewPageAsync(1280, 800);
        await page.GotoAsync(fx.BaseUrl);

        var lefts = await page.Locator(".story-row:visible .chip.row-badge")
                              .EvaluateAllAsync<double[]>("els => els.map(e => Math.round(e.getBoundingClientRect().left))");

        Assert.True(lefts.Length >= 2, "Expected at least two visible story rows.");
        Assert.Single(lefts.Distinct());
    }

    [Fact]
    public async Task The_header_names_the_current_project_but_is_not_a_control()
    {
        // Orientation only — it says which board you are on. Switching is a page, not a header
        // click, because a name that silently navigates is not discoverable as a control.
        var (page, errors) = await fx.NewPageAsync();
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator(".app-name")).ToContainTextAsync("UI Test");
        Assert.Equal("span", await page.Locator(".app-name").EvaluateAsync<string>(
            "e => e.tagName.toLowerCase()"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Projects_is_reachable_from_the_bottom_bar_on_a_phone()
    {
        // The header is not a control any more, and the top-bar button is hidden at this width, so
        // the bottom bar is the only way in. It was not findable before.
        var (page, _) = await fx.NewPageAsync(390, 760);
        await page.GotoAsync(fx.BaseUrl);

        await Assertions.Expect(page.Locator("#btnProjects")).Not.ToBeVisibleAsync();
        // Still five, so every label stays readable rather than ellipsising.
        Assert.Equal(5, await page.Locator(".mnav:visible").CountAsync());

        await page.Locator(".mnav:has-text('Projects')").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
        await Assertions.Expect(page.Locator(".projrow").First).ToBeVisibleAsync();

        // And Configure is still reachable, now via the project you picked.
        await page.Locator("button:has-text('Configure this project')").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/configure");
    }

    [Fact]
    public async Task Clicking_a_project_row_does_not_switch_project()
    {
        // Switching reloads the whole board, which is too much to happen from a stray tap on a
        // name. It is a button you choose, not the row you happened to touch.
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/projects");

        await page.Locator(".projrow .projname").First.ClickAsync();
        await page.Locator(".projrow .projpath").First.ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
        await Assertions.Expect(page.Locator(".projrow.on")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Removing_a_project_makes_you_type_its_name_and_says_the_files_are_safe()
    {
        var (page, _) = await fx.NewPageAsync();
        try
        {
            // Add one to remove, so the fixture's own project is never the subject.
            await page.GotoAsync($"{fx.BaseUrl}/add-project");
            await page.Locator("#projBacklog").FillAsync(fx.UncreatedProjectPath);
            await page.Locator("button:has-text('Add project')").ClickAsync();
            await Assertions.Expect(page.Locator(".projrow")).ToHaveCountAsync(2);

            var name = await page.Locator(".projrow.on .projname").InnerTextAsync();
            await page.Locator(".projrow.on .iconbin").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/remove-project");

            // The reassurance is the point of the page — it must actually be on it.
            await Assertions.Expect(page.Locator(".safenote")).ToContainTextAsync("not touched");
            await Assertions.Expect(page.Locator(".safenote")).ToContainTextAsync("bring it back");

            // A near-miss is refused rather than accepted.
            await page.Locator("#rmConfirm").FillAsync(name.ToLowerInvariant() + "x");
            await page.Locator("button:has-text('Remove project')").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/remove-project");
            await Assertions.Expect(page.Locator("#rmConfirm ~ .field-err")).ToBeVisibleAsync();

            await page.Locator("#rmConfirm").FillAsync(name);
            await page.Locator("button:has-text('Remove project')").ClickAsync();

            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
            await Assertions.Expect(page.Locator(".projrow")).ToHaveCountAsync(1);
            Assert.True(File.Exists(fx.UncreatedProjectPath),
                "Removing a project must not delete its backlog — that is what the note promises.");
        }
        finally
        {
            await fx.UsePrimaryProjectAsync();
        }
    }

    [Fact]
    public async Task The_current_project_is_labelled_in_words_not_only_by_colour()
    {
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/projects");

        await Assertions.Expect(page.Locator(".projrow.on .projbadge")).ToHaveTextAsync("Current");
    }

    [Fact]
    public async Task Adding_a_project_happens_on_its_own_page_and_returns_to_the_list()
    {
        // The form used to sit under the list, where its fields read as editing the selected
        // project rather than creating a new one.
        var (page, _) = await fx.NewPageAsync();
        try
        {
            await page.GotoAsync($"{fx.BaseUrl}/projects");
            // Not absent from the DOM — every page lives in index.html and is toggled with x-show —
            // but not on screen, which is what "the form is not on this page" means to a reader.
            await Assertions.Expect(page.Locator("#projBacklog")).Not.ToBeVisibleAsync();

            await page.Locator("button:has-text('Add a project')").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/add-project");

            await page.Locator("#projBacklog").FillAsync(fx.UncreatedProjectPath);
            await page.Locator("button:has-text('Add project')").ClickAsync();

            // Back to the list, with the new project created, listed and marked Current.
            await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/projects");
            await Assertions.Expect(page.Locator(".projrow")).ToHaveCountAsync(2);
            await Assertions.Expect(page.Locator(".projrow.on .projbadge")).ToHaveCountAsync(1);
            Assert.True(File.Exists(fx.UncreatedProjectPath), "Adding a project should create its backlog.");
        }
        finally
        {
            // Adding switches to the new project, and one app serves every test in this collection.
            await fx.UsePrimaryProjectAsync();
        }
    }

    [Fact]
    public async Task Text_and_controls_are_bigger_on_a_phone_than_on_a_desktop()
    {
        // px does not scale with the screen, so the desktop sizes were the phone sizes. Asserted
        // by measuring what the browser computes rather than by matching stylesheet text.
        var (desktop, _) = await fx.NewPageAsync(1280, 800);
        await desktop.GotoAsync($"{fx.BaseUrl}/add-project");
        var deskInput = await desktop.Locator("#projBacklog").EvaluateAsync<string>(
            "e => getComputedStyle(e).fontSize");

        var (phone, _) = await fx.NewPageAsync(390, 760);
        await phone.GotoAsync($"{fx.BaseUrl}/add-project");
        var phoneInput = await phone.Locator("#projBacklog").EvaluateAsync<string>(
            "e => getComputedStyle(e).fontSize");
        var phoneBody = await phone.Locator("body").EvaluateAsync<string>("e => getComputedStyle(e).fontSize");

        Assert.True(Px(phoneInput) > Px(deskInput), "Inputs should be larger on a phone.");
        Assert.True(Px(phoneInput) >= 16, "16px is the comfortable minimum for a touch control.");
        Assert.True(Px(phoneBody) >= 15, "Body text should be raised on a phone.");
    }

    private static double Px(string computed) => double.Parse(computed.Replace("px", ""),
        System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task An_empty_epic_offers_a_way_to_add_the_first_story()
    {
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/empty-epic");

        await Assertions.Expect(page.Locator(".empty button")).ToBeVisibleAsync();
        await page.Locator(".empty button").ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/add-story");
    }
}
