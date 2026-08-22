---
description: "NuGet packaging conventions for library projects: MSBuild-based packaging with GeneratePackageOnBuild, GitVersion for SemVer versioning, SourceLink. Activates when editing .csproj files."
applyTo: "**/*.csproj"
---
# NuGet Packaging Conventions

## MSBuild-Based Packaging

NuGet packages are produced by MSBuild during the build on the build server. The `.csproj` enables this with two minimal properties:

```xml
<GeneratePackageOnBuild>true</GeneratePackageOnBuild>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

- These two properties are the **minimum requirement** for a library to produce NuGet packages.
- **No `dotnet pack` step** — the build pipeline produces `.nupkg` files automatically when building in Release configuration.
- `GenerateDocumentationFile` ensures XML doc comments are included in the package for IntelliSense.

## Versioning

SemVer 2.0.0 versioning is handled by **GitVersion**:

- Versions are derived from git tags on `main`. Developers never set `<PackageVersion>` or `<Version>` manually in the `.csproj`.
- `dev` branch builds automatically get `-dev.N` prerelease suffixes.
- Tagging: `git tag v1.2.3` on `main` sets the version baseline.
- See `@git` agent for the full branching and tagging strategy.

**Never add these properties to a `.csproj`:**
```xml
<!-- DON'T — GitVersion handles these -->
<Version>1.0.0</Version>
<PackageVersion>1.0.0</PackageVersion>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
```

## Optional NuGet Metadata

Additional metadata can be added to the `.csproj` when needed — none of these are strictly required for packaging:

```xml
<PropertyGroup>
  <PackageReadmeFile>README.md</PackageReadmeFile>
  <Description>Brief description of the library's purpose</Description>
  <Authors>Cloudstrap</Authors>
  <PackageTags>cloudstrap;library</PackageTags>
</PropertyGroup>

<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="" />
</ItemGroup>
```

## SourceLink (Recommended)

SourceLink enables consumers to step into the library source during debugging:

```xml
<PropertyGroup>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
```

## Target Framework

- Target framework: `net10.0` — Cloudstrap internal libraries, no multi-targeting for legacy consumers.
- The target framework is set in `Directory.Build.props`, not per-project.

## Rules

- Do not add `<Version>` or `<PackageVersion>` to any `.csproj` — GitVersion injects them.
- Do not run `dotnet pack` manually — `GeneratePackageOnBuild` handles it.
- Every packaged `.csproj` must have `GeneratePackageOnBuild` and `GenerateDocumentationFile` set to `true`.
