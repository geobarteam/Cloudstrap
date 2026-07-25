---
name: webapp-testing
description: "Use when testing the running Cloudstrap web application in a browser. Covers writing and running .NET Playwright tests with MSTest, capturing screenshots, verifying page content, debugging UI behavior, and checking browser console logs. Complements the e2e-test skill (which handles full-stack launch) by focusing on writing the actual browser automation and assertions. Use for: smoke-testing pages, verifying UI after implementation, capturing screenshots, checking rendered content."
metadata:
  argument-hint: "Describe the test scenario, e.g. 'verify the login page renders correctly' or 'check that the product list loads'"
---

# Web Application Testing with Playwright (.NET)

## Quick Decision

| Situation | Approach |
|-----------|----------|
| **Full-stack launch + test** (BlazorServer/BlazorWasm templates) | Use the **`/e2e-test`** skill — starts the identity provider, app hosts, proxy hosts if applicable, and Azurite |
| **Server already running, persisted .NET tests** | Use this skill — `PageTest` base, MSTest, runs in CI |
| **Ad-hoc Node.js browser script** | Use the **`playwright-skill`** — writes to `/tmp`, not persisted |
| **Static HTML check** | Read the HTML file, then write Playwright assertions |
| **API-only integration tests** | Use `WebApplicationFactory` in `Test/Integration/` |

This skill focuses on **writing .NET Playwright browser tests** against a running application.

---

## Setup

```bash
dotnet add package Microsoft.Playwright
dotnet add package Microsoft.Playwright.MSTest
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

---

## Basic Page Test (MSTest + PageTest Base)

```csharp
using Microsoft.Playwright.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class ProductPageTests : PageTest
{
    [TestMethod]
    public async Task ProductsPage_ShowsHeading()
    {
        await Page.GotoAsync("https://localhost:7259/products");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(Page.Locator("h1")).ToHaveTextAsync("Products");
    }

    [TestMethod]
    public async Task ProductsPage_TableHasRows()
    {
        await Page.GotoAsync("https://localhost:7259/products");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var rows = Page.Locator("table tbody tr");
        await Expect(rows).ToHaveCountAsync(greaterThan: 0);
    }
}
```

---

## Screenshot Capture

```csharp
[TestMethod]
public async Task CapturePageScreenshot()
{
    await Page.GotoAsync("https://localhost:7259");
    await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    await Page.ScreenshotAsync(new()
    {
        Path = "screenshot.png",
        FullPage = true
    });
}
```

---

## Form Interaction

```csharp
[TestMethod]
public async Task CreateProduct_FillsFormAndSubmits()
{
    await Page.GotoAsync("https://localhost:7259/products/new");
    await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Fill form fields
    await Page.FillAsync("input[label='Name']", "Test Product");
    await Page.FillAsync("input[label='Description']", "A test product");
    await Page.Locator("select[label='Category']").SelectOptionAsync("Electronics");

    // Submit
    await Page.ClickAsync("button:has-text('Save')");
    await Page.WaitForURLAsync("**/products");

    // Verify redirect
    await Expect(Page.Locator("h1")).ToHaveTextAsync("Products");
}
```

---

## Authenticated Testing

```csharp
[TestMethod]
public async Task ProtectedPage_RequiresLogin()
{
    await Page.GotoAsync("https://localhost:7259/admin");
    await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Should redirect to login
    StringAssert.Contains(Page.Url, "login");
}
```

---

## Console Log Capture

```csharp
[TestMethod]
public async Task CaptureBrowserConsoleLogs()
{
    var logs = new List<string>();
    Page.Console += (_, msg) => logs.Add($"[{msg.Type}] {msg.Text}");

    await Page.GotoAsync("https://localhost:7259");
    await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    foreach (var log in logs)
    {
        Console.WriteLine(log);
    }
}
```

---

## Custom Browser Context (Cookies, Storage)

```csharp
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class AuthenticatedTests : BrowserTest
{
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    [TestInitialize]
    public async Task SetUp()
    {
        _context = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });
        _page = await _context.NewPageAsync();
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await _context.CloseAsync();
    }

    [TestMethod]
    public async Task WithStorageState_AccessesProtectedPage()
    {
        // Load saved auth state
        _context = await Browser.NewContextAsync(new()
        {
            StorageStatePath = "auth-state.json",
            IgnoreHTTPSErrors = true
        });
        _page = await _context.NewPageAsync();

        await _page.GotoAsync("https://localhost:7259/admin");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(_page.Locator("h1")).ToHaveTextAsync("Admin");
    }
}
```

---

## Reconnaissance Pattern

When testing an unfamiliar page, inspect before asserting:

1. **Navigate and wait** for `networkidle`
2. **Screenshot** to see what rendered
3. **Inspect DOM** to find selectors:
   ```csharp
   var content = await Page.ContentAsync();
   var buttons = await Page.Locator("button").AllTextContentsAsync();
   var links = await Page.Locator("a").AllTextContentsAsync();
   var inputs = await Page.Locator("input").EvaluateAllAsync<object[]>(
       "els => els.map(e => ({name: e.name, type: e.type, id: e.id}))");
   ```
4. **Write assertions** using discovered selectors

## Best Practices

- **Always wait for `NetworkIdle`** before inspecting dynamic pages (Blazor, SPA).
- **Use headless mode** (default) — headed is only for debugging.
- **Take screenshots** at key points for visual verification.
- **Use descriptive selectors**: `text=`, `role=`, CSS selectors, or IDs.
- **Use `PageTest` base class** — it manages browser lifecycle automatically.
- **Use `Expect()`** for assertions — it auto-retries until timeout, handling Blazor re-renders.

## Dependencies

| Package | Install | Use case |
|---------|---------|----------|
| Microsoft.Playwright | `dotnet add package Microsoft.Playwright` | Core Playwright API |
| Microsoft.Playwright.MSTest | `dotnet add package Microsoft.Playwright.MSTest` | `PageTest` / `BrowserTest` base classes |
| Chromium | `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` | Browser binary |
