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
    public async Task An_empty_epic_offers_a_way_to_add_the_first_story()
    {
        var (page, _) = await fx.NewPageAsync();
        await page.GotoAsync($"{fx.BaseUrl}/empty-epic");

        await Assertions.Expect(page.Locator(".empty button")).ToBeVisibleAsync();
        await page.Locator(".empty button").ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync($"{fx.BaseUrl}/add-story");
    }
}
