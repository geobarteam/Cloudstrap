---
applyTo: "src/Cloudstrap.Functional/**"
description: "Functional utilities: Result<T>, Unit, Preconditions, and ICommandHandler conventions."
---

# Cloudstrap.Functional Conventions

## Key Types

| Type | Purpose |
|------|---------|
| `Result<T>` | Success/failure monad. Use `Result<T>.Success(value)` / `Result<T>.Failure(error)` factory methods. |
| `Unit` | Void equivalent for functional returns. Use `Unit.Default()`. |
| `ICommandHandler<TCommand, TResult>` | Async command pattern: `Task<TResult> Execute(TCommand command)`. |
| `Preconditions` | Static guard helpers: `NotNull`, `NotEmpty`, etc. |

## Rules

- **No external dependencies** — pure .NET library.
- `Result<T>` is immutable (init-only properties). Constructors are `[Obsolete]` — always use factory methods.
- `[DebuggerStepThrough]` on utility types for cleaner stack traces.
- No `CancellationToken` overloads — kept intentionally simple.
