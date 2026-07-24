---
description: "Public API design conventions for library types: XML documentation, sealed-by-default, guard clauses, CancellationToken, deprecation via ObsoleteAttribute, EditorBrowsable for infrastructure types. Activates when editing library source files."
applyTo: "src/Cloudstrap*/**"
---
# Public API Design Conventions

## Visibility

- `internal` by default. Only types and members intended for consumer use are `public`.
- Seal classes unless designed for inheritance (`sealed class` by default).
- Mark `virtual` only what is intended to be overridden. Prefer abstract base classes over interfaces when the API is expected to evolve (adding members to an interface is a binary breaking change).
- Use `EditorBrowsable(EditorBrowsableState.Never)` to hide internal extension methods or infrastructure types from IntelliSense when they must be `public` for technical reasons (e.g., DI registration internals).

## XML Documentation

Every public type and member requires XML documentation:

```csharp
/// <summary>
/// Validates security tokens against configured policies.
/// </summary>
public interface ITokenValidator
{
    /// <summary>
    /// Validates the specified token asynchronously.
    /// </summary>
    /// <param name="token">The token string to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating success or failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="token"/> is null.</exception>
    Task<ValidationResult> ValidateAsync(string token, CancellationToken ct);
}
```

Required tags:
- `<summary>` on every public type and member.
- `<param>` for every parameter.
- `<returns>` for non-void methods.
- `<exception>` for documented thrown exceptions.
- `<remarks>` when behavior is non-trivial or has important caveats.

## Nullable Reference Types

- Nullable reference types are enabled project-wide.
- Never return `null` from a public method without an explicit `T?` return type.
- Use `[NotNullWhen(true)]`, `[MaybeNullWhen(false)]` attributes where applicable.

## Guard Clauses

All public method parameters must be validated:

```csharp
public async Task<Result> ProcessAsync(string input, CancellationToken ct)
{
    ArgumentNullException.ThrowIfNull(input);
    ArgumentException.ThrowIfNullOrWhiteSpace(input);
    // ...
}
```

## CancellationToken

All async public methods must accept `CancellationToken` as the last parameter. Propagate it through every async call in the chain.

## Deprecation

- Never remove a public API member without a deprecation cycle.
- First, add `[Obsolete("Use X instead. Will be removed in vN.")]`.
- Ship a minor version with both old (deprecated) and new API.
- Remove the old API in the next major version.

## Return Types

- Return interfaces or abstract types from public API for extensibility.
- Avoid returning concrete collection types — prefer `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, or `IEnumerable<T>`.

## All public API changes are a risk area — require human review.
