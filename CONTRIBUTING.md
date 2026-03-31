# Contributing to ObjeX

Thanks for your interest in contributing!

## Getting Started

```bash
git clone https://github.com/centrolabs/ObjeX.git
cd ObjeX/src/ObjeX.Api
dotnet run
```

UI at http://localhost:9001 (admin / admin), S3 API at http://localhost:9000.

## Development

- **.NET 10** — check `global.json` for exact version
- **EF Migrations** — generate for both SQLite and PostgreSQL when changing models (see [CLAUDE.md](CLAUDE.md#ef-migrations))
- **No tests yet** — CI is build-only; don't break the build

## Pull Requests

- One feature per PR
- Keep changes focused — don't refactor unrelated code
- Ensure `dotnet build` passes
- Describe what and why in the PR description

## Reporting Issues

Use [GitHub Issues](https://github.com/centrolabs/ObjeX/issues). Include:
- What you did, what you expected, what happened
- ObjeX version, OS, database provider (SQLite/PostgreSQL)
- Logs if applicable

## License

By contributing, you agree your code is licensed under the [MIT License](LICENSE).
