# Contributing to Ambient Director

Thanks for wanting to help! Ambient Director is a local **.NET 10 Minimal API + Blazor WASM** control
panel that switches a tabletop RPG's whole mood — lights, music, sound — and tracks the game layer
(party, bestiary, encounters). This guide takes you from clone to pull request **without** needing the
project's back-story.

New to the codebase? The single best map is **[CLAUDE.md](CLAUDE.md)**. It's written for AI agents, but
it's also the most complete architecture guide we have — every service, seam, and convention, with links
to the files. Read it once and most of the codebase will make sense.

## AI-assisted contributions are welcome

This project is built heavily with AI coding agents — that's why you'll see [CLAUDE.md](CLAUDE.md) and
`claude/*` branches. **AI-generated and AI-assisted pull requests are first-class here.** The bar is
exactly the same as for hand-written code:

1. **It passes all tests** — `dotnet test` is green, and so is CI.
2. **It keeps to the project's guidance** — the architecture and conventions in [CLAUDE.md](CLAUDE.md),
   the deep-dive docs linked below, and the style notes at the end of this file.

We only ask that you review and understand what you submit: you are the author on the PR, whoever — or
whatever — helped you write it.

## Prerequisites

- The **[.NET 10 SDK](https://dotnet.microsoft.com/download)**. That's the only hard requirement.
  [`global.json`](global.json) pins the major version, so you'll get a clear error (rather than a
  confusing one) if you're on .NET 9 or earlier.
- **No hardware required.** You do **not** need a smart bulb, a Philips Hue bridge, a Spotify account, an
  audio device, or a Stream Deck to build, run, test, or develop the app. See
  [Developing without hardware](#developing-without-hardware).

## Build, run, test

```bash
# Build — the API project also builds the referenced Blazor WASM UI.
dotnet build src/AmbientDirector.Api/AmbientDirector.Api.csproj

# Run — serves the API *and* the panel on http://localhost:5252
dotnet run --project src/AmbientDirector.Api

# Test — this is the gate. Keep it green.
dotnet test src/AmbientDirector.Tests/AmbientDirector.Tests.csproj
```

The test suite (~400 xUnit cases) runs **fully offline and cross-platform** — the same way CI runs it on
Linux. It uses hand-rolled fakes (`FakeHttpMessageHandler`, an in-memory SQLite database on real
migrations, `WebApplicationFactory`) so nothing ever touches a real bulb, Spotify, or audio device. When
you add a feature, add tests next to the closest existing ones in `src/AmbientDirector.Tests/`
(`Unit/`, `Integration/`, `Store/`, `Http/`) and copy the naming style you see.

## Developing without hardware

Every hardware/cloud integration sits behind a seam that **degrades cleanly** when it isn't configured:

- **Lights** (Tuya/Hue), **Spotify**, and the **audio output device** return a clean `503` — or a partial
  `207` on scene activation — when they're absent. The app never crashes, and the panel stays fully usable.
- The **local music library** and **Scryfall** image search work with no account (Scryfall just needs
  internet access).
- The first-run setup wizard is entirely skippable.

So `dotnet run`, open `http://localhost:5252`, and you have the whole panel to develop against.

## Making a change

1. **Find or open an issue.** Work is tracked in
   [GitHub Issues](https://github.com/Vegolas/ambient-director/issues) — look for
   [`good first issue`](https://github.com/Vegolas/ambient-director/labels/good%20first%20issue) and
   [`help wanted`](https://github.com/Vegolas/ambient-director/labels/help%20wanted). If the backlog is
   quiet, open an issue describing what you'd like to build (or comment on an existing one) **before**
   starting large work, so we can agree on the shape first.
2. **Branch.** Fork the repo and create a branch off `main` with a short, descriptive name. (Maintainers
   and in-repo agents use `claude/<slug>`; from a fork, any clear name is fine.)
3. **Make the change** following the [conventions below](#conventions-that-will-trip-you-up). Keep it
   focused — one logical change per PR.
4. **Build and test locally** with the commands above.
5. **Open a pull request.** Fill in the [PR template](.github/pull_request_template.md) and reference the
   issue with `Closes #NN`. CI ([build.yml](.github/workflows/build.yml)) runs `dotnet build` +
   `dotnet test` + a Windows publish smoke-test on every PR — keep it green.

## Conventions that will trip you up

These are the non-obvious ones. Fuller detail lives in [CLAUDE.md](CLAUDE.md).

- **Wire DTOs are duplicated by hand.** The API's `src/AmbientDirector.Api/Contracts/` and the UI's
  `src/AmbientDirector.Ui/Contracts/` keep *separate* copies — there is no shared contracts project.
  Change one, change its twin.
- **Schema changes need an EF migration.** After editing an entity, `AppDbContext`, or its
  `OnModelCreating`, restore the local tools and add a migration from the API project, then commit the
  generated files under `Data/Migrations/`:

  ```bash
  dotnet tool restore
  dotnet ef migrations add <Name> -o Data/Migrations --project src/AmbientDirector.Api
  ```

  The app only *applies* migrations at startup; it never creates tables from the model.
- **New user-facing text is localized.** A new error or validation message is a code-carrying
  `ValidationException` / `NotConfiguredException` with an `error.*` key added to **both**
  `Locales/en.json` and `Locales/pl.json`. New UI strings follow the same both-locales rule.
- **New protected routes** go into `IsProtectedPath` in `Program.cs`. The key-free player-facing TV
  surface must stay open.
- **Command endpoints accept both GET and POST** (`EndpointHelpers.GetOrPost`) so the Stream Deck
  *System → Website* action works with no plugin.
- **Don't edit generated CSS.** `wwwroot/css/app.css` is compiled from `Styles/*.scss` on build (and is
  gitignored) — edit the SCSS.
- Adding a **game system**? Follow [docs/GAME-SYSTEMS.md](docs/GAME-SYSTEMS.md): copy `Dnd5eSystem`, add
  its locale keys, register one line in `Program.cs`, and run `dotnet test` — the contract tests tell you
  what's missing.
- Touching **UI**? Follow the "Control Room" design system in
  [docs/design/STYLE-GUIDE.md](docs/design/STYLE-GUIDE.md).

## Code style

- **Use braces on all control-flow blocks and avoid one-liner bodies.** This is a project rule and lives
  in [`.editorconfig`](.editorconfig) (along with file-scoped namespaces, 4-space indent, `using`
  ordering).
- Style is **guidance for readability, not an automated gate** — CI does not fail on formatting, and we
  don't run `dotnet format` in CI. The gate is `dotnet test`. When in doubt, match the surrounding code.

## Reporting bugs and requesting features

Open an issue using the templates (Bug report / Feature request). For bugs, include your OS and — for
lights, music, or sound issues — which provider you're using.

## License

By contributing, you agree that your contributions are licensed under the repository's
[MIT License](LICENSE).
