---
name: build-feature
description: "Use when implementing a new feature or vertical slice in Cloudstrap after a _plans/<FeatureName>.md (repo root) is approved. Executes plan steps using Red-Green-Refactor. Each plan step is a vertical behavior slice that may touch multiple layers. Provides code templates for each layer as a reference catalog: Domain entity, Application command/query/handler, Persistence repository, Contracts DTOs, BFF controller, Database SQL, Presentation ViewModel/ServiceClient/Refit."
metadata:
  argument-hint: "Which plan step to implement, e.g. 'Step 1 — Display prescription list'"
---

# Build Feature — Implementation Skill

Execute approved `_plans/<FeatureName>.md` steps using Red-Green-Refactor, one step per reply, following existing patterns from the **reference feature** specified in the plan.

Each plan step is a **vertical behavior slice** that may touch multiple layers in one cycle. The Layer Templates below are a **reference catalog** — look up the layers your current step needs, not a sequential order to follow.

## Vertical Slice Strategy

Plans decompose features into vertical slices, not horizontal layers. Each slice follows a two-phase pattern:

1. **Stub phase** — Build the UI (page, ViewModel, form) with a mocked/stubbed service returning fake data. This lets the user validate the UI immediately.
2. **Wire phase** — Replace the stub with real production code from top to bottom: controller → application handler → domain → persistence → DB.

**Why this order?** Fail fast. By integrating all layers early, mismatches between contracts, DTOs, query shapes, and page expectations surface immediately — not at the end of a long horizontal build-out. The stub phase also catches UI/UX issues before investing in backend code.

When executing a plan step, check whether it is a stub step or a wire step, and apply the appropriate layers from the reference catalog below.

## Prerequisites

- A **`_plans/<FeatureName>.md`** (repo root) must exist and be **approved by the user** before using this skill.
- The plan's **Overview** section names the reference feature. If it doesn't, **ask the user** which existing feature to use as the pattern before proceeding.
- Read the plan step you're implementing. Read the reference feature files for **all layers** that step touches.
- **Do not skip ahead.** Complete one step, prove it, stop at the gate.

## Progress Tracking

The plan file is a **living document**. Track progress using the HUMAN GATE checkboxes:

- **Before starting**: read `_plans/<FeatureName>.md` and find the **first step with unchecked `[ ]`** checkboxes — that is the next step to implement. All steps with `[x]` are already completed and approved.
- **After user approves a step**: update `_plans/<FeatureName>.md` — change `[ ]` to `[x]` on every checkbox in that step's **🛑 HUMAN GATE** section.
- This ensures continuity across sessions — if the conversation restarts, any agent can read the plan and know exactly where to resume.

## Mandatory Workflow Per Step

Follow the **Red-Green-Refactor-Proof Loop** defined in `AGENTS.md` — one step per reply, RED before GREEN, stop at every 🛑 HUMAN GATE, and delegate code-analysis sweeps to `@code-analysis`.

Skill-specific notes:
- **READ** — find the first step with unchecked `[ ]` in `_plans/<FeatureName>.md`; read the reference feature for every layer that step touches before writing any code.
- **CODE ANALYSIS** — for non-trivial violation sweeps, invoke `@code-analysis` instead of running the grep manually; it owns the disabled-rule list and the common fix table.
- **MARK DONE** — after the user approves, change `[ ]` → `[x]` on this step's HUMAN GATE checkboxes in the plan file so the next session can resume.

> **Note**: If packages are missing, run `dotnet restore src/Cloudstrap.sln --interactive` first.

---

## Layer Templates — Reference Catalog

These are **not sequential steps**. Each plan step (a vertical behavior slice) may need code from one or more of these layers. Look up the layer(s) your current plan step touches and apply the matching template.

### Domain Entity

Location: `src/Core/Domain/Shared/Entities/<Entity>.cs`

```csharp
namespace Cloudstrap.Core.Domain.Shared.Entities;

public class <Entity> : IEntity
{
    public int Id { get; set; }
    // Properties — plain C#, no EF attributes, no dependencies
}
```

Interface: `src/Core/Domain/Shared/IEntity.cs` (already exists — `int Id { get; set; }`)

Test: construction + property assignment.

---

### Repository Interface + Implementation

**Interface** in `src/Core/Application/Shared/Interfaces/Persistence/Repositories/I<Entity>Repository.cs`:

```csharp
namespace Cloudstrap.Core.Application.Shared.Interfaces.Persistence.Repositories;

public interface I<Entity>Repository : IRepository<<Entity>>
{
    // Feature-specific query methods
    Task<IReadOnlyList<<Entity>>> Search<Entity>(string param1, string param2);
}
```

**Implementation** in `src/Core/Persistence/Repositories/<Entity>Repository.cs`:

```csharp
namespace Cloudstrap.Core.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

public class <Entity>Repository(CloudstrapDbContext dbContext)
    : BaseRepository<<Entity>>(dbContext), I<Entity>Repository
{
    public async Task<IReadOnlyList<<Entity>>> Search<Entity>(string param1, string param2)
    {
        var query = dbContext.<Entities>.AsQueryable();

        if (!string.IsNullOrWhiteSpace(param1))
            query = query.Where(e => e.Prop.Contains(param1));

        return await query.AsNoTracking().ToListAsync();  // Always AsNoTracking for reads
    }
}
```

**EF Configuration** in `src/Core/Persistence/EntityTypeConfigurations/<Entity>Configuration.cs`:

```csharp
namespace Cloudstrap.Core.Persistence.EntityTypeConfigurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class <Entity>Configuration : IEntityTypeConfiguration<<Entity>>
{
    public void Configure(EntityTypeBuilder<<Entity>> builder)
    {
        builder.ToTable("<Entity>", schema: "<Schema>");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(50);
        // No EF attributes on entity — all config here via fluent API
    }
}
```

DI: auto-registered by `PersistenceModule` (suffix `Repository` → Scoped).

---

### Command + Handler + Query

**Command** in `src/Core/Application/Functionalities/<Feature>/Commands/<Action>/Add<Entity>Command.cs`:

```csharp
namespace Cloudstrap.Core.Application.Functionalities.<Feature>.Commands.<Action>;

public record Add<Entity>Command(string Name, string Email)
{
    public (bool IsValid, List<string> Errors) Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(Name)) errors.Add("Name cannot be empty");
        return (!errors.Any(), errors);
    }
}
```

**Handler** in same folder `Add<Entity>CommandHandler.cs`:

```csharp
namespace Cloudstrap.Core.Application.Functionalities.<Feature>.Commands.<Action>;

using Cloudstrap.Functional;

public class Add<Entity>CommandHandler(I<Entity>Repository repository)
    : ICommandHandler<Add<Entity>Command, Result<Unit>>
{
    private readonly I<Entity>Repository _repository = repository
        ?? throw new ArgumentNullException(nameof(repository));

    public async Task<Result<Unit>> Execute(Add<Entity>Command command)
    {
        Result<Unit> validateResult = ValidateCommand(command);
        if (!validateResult.IsSuccess)
            return validateResult;

        var entity = new <Entity> { Name = command.Name };
        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        return new Result<Unit>(Unit.Default());
    }

    private static Result<Unit> ValidateCommand(Add<Entity>Command command)
    {
        (bool isValid, List<string> errors) = command.Validate();
        if (!isValid)
        {
            var sb = new StringBuilder();
            errors.ForEach(err => sb.Append($"{err} {Environment.NewLine}"));
            return new Result<Unit>(sb.ToString());
        }
        return new Result<Unit>(Unit.Default());
    }
}
```

DI: auto-registered by `ApplicationModule` (suffix `Handler` → Scoped).

**Query** in `src/Core/Application/Functionalities/<Feature>/Queries/<Action>/`:

```csharp
// Interface
public interface IGet<Entity>ListQuery
{
    Task<List<<Entity>>> Execute(string param1, string param2);
}

// Implementation
public class Get<Entity>ListQuery(I<Entity>Repository repository) : IGet<Entity>ListQuery
{
    private readonly I<Entity>Repository _repository = repository
        ?? throw new ArgumentNullException(nameof(repository));

    public async Task<List<<Entity>>> Execute(string param1, string param2)
        => [.. await _repository.Search<Entity>(param1, param2)];
}
```

DI: auto-registered by `ApplicationModule` (suffix `Query` → Scoped).

Test: mock `I<Entity>Repository`, verify handler returns `Result<Unit>` success/failure.

---

### Contracts (DTOs)

Location: `src/Contracts/<Feature>/Api/`

```csharp
// Read DTO
namespace Cloudstrap.Contracts.<Feature>.Api;
public record <Entity>Dto(int Id, string Name, string Email);

// Write DTO
public record Add<Entity>Dto(string Name, string Email);
```

DTOs are `record` types. No logic. No domain dependencies.

---

### BFF Controller

Location: `src/Host/BFF/Controllers/<Feature>Controller.cs`:

```csharp
namespace Cloudstrap.Host.Bff.Controllers;

[ApiController]
[Route("api/<feature-kebab>")]
public class <Feature>Controller(
    IGet<Entity>ListQuery getQuery,
    ICommandHandler<Add<Entity>Command, Result<Unit>> addCommand,
    ILogger<<Feature>Controller> logger,
    IAuditLogger auditLogger,
    ICorrelationContextAccessor correlationContextAccessor,
    IUserContextAccessor userContextAccessor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<<Entity>Dto>>> Get(
        CancellationToken cancellationToken,
        [FromQuery] string name = null)
    {
        try
        {
            var entities = await getQuery.Execute(name, ...);
            // Audit logging
            return Ok(entities.Select(e => new <Entity>Dto(e.Id, e.Name, e.Email)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Controller} has throw an exception.", nameof(<Feature>Controller));
            throw;
        }
    }

    [HttpPost]
    public async Task<ActionResult> Add([FromBody] Add<Entity>Dto dto, CancellationToken cancellationToken)
    {
        try
        {
            var result = await addCommand.Execute(new Add<Entity>Command(dto.Name, dto.Email));
            if (!result.IsSuccess)
                return BadRequest(result.Error);
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Controller} has throw an exception.", nameof(<Feature>Controller));
            throw;
        }
    }
}
```

Test: `Test/Integration/Endpoints/` — HTTP tests with `CustomWebApplicationFactory`.

---

### Database SQL

Location: `src/Database/Tables/<Entity>.sql`

```sql
CREATE TABLE [<Schema>].[<Entity>] (
    [Id]     INT           IDENTITY (1, 1) NOT NULL,
    [Name]   VARCHAR (50)  NOT NULL,
    -- Encrypted columns use: COLLATE Latin1_General_BIN2 ENCRYPTED WITH (...)
    CONSTRAINT [PK_<Entity>] PRIMARY KEY CLUSTERED ([Id] ASC)
);
```

**User deploys via Schema Compare** — never modify `.sqlproj` directly.

---

### Presentation Layer

**Refit Client** in `src/Presentation/Shared/ServiceClients/Bff/Clients/I<Feature>Client.cs`:

```csharp
namespace Cloudstrap.Presentation.Shared.ServiceClients.Bff.Clients;

using Refit;

public interface I<Feature>Client
{
    [Get("/api/<feature-kebab>")]
    Task<ICollection<<Entity>Dto>> Get<Feature>Async(
        [Query] string name = null,
        [AliasAs("api-version")][Query] string apiVersion = null,
        CancellationToken cancellationToken = default);

    [Post("/api/<feature-kebab>")]
    Task Add<Entity>Async(
        [Body] Add<Entity>Dto dto,
        [AliasAs("api-version")][Query] string apiVersion = null,
        CancellationToken cancellationToken = default);
}
```

**Feature ServiceClient** in `src/Presentation/<Feature>/ServiceClients/<Feature>ServiceClient.cs`:

```csharp
namespace Cloudstrap.Presentation.<Feature>.ServiceClients;

using Cloudstrap.Presentation.Shared.ServiceClients.Bff;

public class <Feature>ServiceClient(I<Feature>Client client) : I<Feature>ServiceClient
{
    private readonly I<Feature>Client _client = client
        ?? throw new ArgumentNullException(nameof(client));

    public async Task<IEnumerable<<Entity>Model>> GetAllAsync()
    {
        var items = await _client.Get<Feature>Async(null, ApiConstants.ApiVersion);
        return items.Select(x => new <Entity>Model(x.Name, x.Email));
    }

    public async Task<Result<Unit>> AddAsync(string name, string email)
    {
        try
        {
            await _client.Add<Entity>Async(new Add<Entity>Dto(name, email), ApiConstants.ApiVersion);
        }
        catch (ApiException ex)
        {
            return ex.ConvertApiExceptionToResult<Unit>();
        }
        return new Result<Unit>(Unit.Default());
    }
}
```

DI: auto-registered by `PresentationModule` (suffix `ServiceClient` → Transient).

**ViewModel** in `src/Presentation/<Feature>/ViewModels/<Feature>ViewModel.cs`:

```csharp
namespace Cloudstrap.Presentation.<Feature>.ViewModels;

public class <Feature>ViewModel(I<Feature>ServiceClient serviceClient) : I<Feature>ViewModel
{
    private readonly I<Feature>ServiceClient _serviceClient = serviceClient;

    public bool IsBusy { get; set; }
    public IList<<Entity>Model> Items { get; set; }

    public async Task InitializeAsync(IErrorComponent errorComponent)
    {
        IsBusy = true;
        try
        {
            var items = await _serviceClient.GetAllAsync();
            Items = [.. items];
        }
        catch (Exception ex)
        {
            errorComponent.ProcessError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

DI: auto-registered by `PresentationModule` (suffix `ViewModel` → Transient).

---

## Key Reminders

- **ICommandHandler<TCommand, TResult>** is the Application-layer command interface. Handlers are auto-scanned.
- **Result<T>** from `Cloudstrap.Functional` — constructor `new Result<T>(value)` for success, `new Result<T>("error msg")` for failure. Check `result.IsSuccess`.
- **Unit** from `Cloudstrap.Functional` — use `Unit.Default()` for void-equivalent returns.
- **ApiException** — catch in ServiceClient, convert via `ex.ConvertApiExceptionToResult<T>()`.
- **BaseRepository<T>** — provides `AddAsync`, `GetByIdAsync`, `ListAllAsync`, `Update`, `Delete`, `SaveChangesAsync`.
- **DbSet registration** — add `DbSet<<Entity>>` to `CloudstrapDbContext`.
- **Copyright header** — follow the header convention defined in the repo's StyleCop settings (MIT-licensed project; no company-specific header).
- **CancellationToken** — propagate on every async call in controllers. Handlers may omit if not needed by repository.

## Related Skills & Agents

| When your step touches… | Use |
|-------------------------|-----|
| Refit client interface design | **`refit`** skill — error handling, file uploads, auth headers, retry |
| Messaging events / handlers (Wolverine) | *(skill to be authored during the Cloudstrap.Messaging port)* |
| Violation sweeps during CODE ANALYSIS | **`@code-analysis`** agent — owns the disabled-rule list |
| Browser smoke test after wire step | **`webapp-testing`** skill (.NET Playwright) |
