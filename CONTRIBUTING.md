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
