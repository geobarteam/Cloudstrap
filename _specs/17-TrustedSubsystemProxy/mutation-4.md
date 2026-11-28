# Mutation check: 4

mutation: **not run** — Stryker.NET could not analyze the slice's projects (exit 4).

- Base: `96da9d0`
- Threshold: 80%
- Command: `node scripts/mutation-check.mjs --slice 4 --base 96da9d0 --feature 17-TrustedSubsystemProxy` (2026-11-28, job `01M8GT8NGG37JJVR8HERQZDG2J`)
- Output: `mutation-check: Stryker.NET failed for Cloudstrap.Demo.Api.csproj (exit 1, no report)`; Stryker: "Could not find an assembly reference to a mutable assembly for project … Cloudstrap.Demo.E2E.Tests.csproj" (and every other test project), "No project found … Failed to analyze project builds."

## Why

Slice 4 changed demo code only (`src/demo/Api/Controllers/DownstreamController.cs`, `src/demo/BlazorWasm/Bff/Program.cs`, `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs`, `src/demo/Shared/Contracts/DownstreamInboundDto.cs`). No test project of Pack values **Test projects** references a demo host: the E2E suite boots the demo apps as separate processes from their own build output, so Stryker has no test project whose in-process run could see a mutant. This is the same gap Q-2 recorded (the demo apps have no host-level integration test harness), and the reason the slice's outer test is the E2E test.

## What proves the slice's tests can fail instead

Each of the four `TrustedSubsystemProxyTests` but the last was seen red before the demo was wired, on its assertion (the SPA fallback answered `index.html`: 200 `text/html` instead of 401 / the Api's JSON). `AnonymousCall_ToTheProxiedRoute_NeverReachesTheApi` was written after the wiring (green on arrival, T-2); it carries its own positive control (a signed-in probe path that must appear in the Api's console telemetry), so it can see a hit.

## Survivors

None scored. For the owner: accept this slice without mutation evidence, or decide that demo-only slices need a host-level harness (Q-2's rejected option) before the next demo slice.
