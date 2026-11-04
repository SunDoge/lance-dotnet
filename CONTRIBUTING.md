# Contributing

Commits follow Conventional Commits and are checked by cocogitto.

Install the configured tooling and commit-message hook after cloning:

```sh
mise install
cog install-hook --all
```

Stage changes, then create a commit with a type, description and optional scope:

```sh
git add <files>
cog commit fix "preserve snapshot lifetime" interop
cog commit docs "clarify supported platforms"
```

Use `feat` for new features, `fix` for bug fixes, and `build`, `chore`, `ci`,
`docs`, `perf`, `refactor`, `revert`, `style` or `test` as appropriate.
Run `cog check` to validate commit history.

## Releases

Release Please runs on pushes to `main` and can also be started manually from
GitHub Actions. It maintains a release PR with `CHANGELOG.md`, `version.txt`,
the NuGet version in `src/Lance/Lance.csproj`, and its release manifest.
Merging that PR creates a `v<version>` tag and a GitHub prerelease. The first
release is `0.1.0-preview.1`; subsequent releases use the prerelease versioning
strategy. The Python tooling and binding generator have independent versions.

Enable **Allow GitHub Actions to create and approve pull requests** in repository
Settings > Actions > General. The workflow uses `GITHUB_TOKEN` by default.
If other workflows must run in response to Release Please's PRs or tags, set
`RELEASE_PLEASE_TOKEN` to a suitable token; events created with `GITHUB_TOKEN`
do not start those workflows.

Review release PRs rather than running `cog bump`; cocogitto validates commits,
and Release Please owns release versions and tags. NuGet package upload is a
separate step and is not performed by this workflow. To switch to stable
releases, change the release configuration and review the resulting release PR
before merging it.
