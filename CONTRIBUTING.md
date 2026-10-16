# Contributing to Neo-Test

Thanks for contributing. Before proposing a change, please keep the extension's report formats, Visual Studio compatibility, and README examples in sync.

## Local setup

1. Install Visual Studio with the **Visual Studio extension development** workload.
2. Install the .NET Framework 4.8.1 targeting pack if it is not already available.
3. Restore packages and build from the repository root:

```powershell
dotnet restore NeoTest.slnx
dotnet build NeoTest.slnx --no-restore
```

The build produces `src/bin/<Configuration>/net481/Neo-Test.vsix`.

## Before submitting a change

- Build the solution successfully.
- Manually verify affected Visual Studio behavior, especially editor markers, report discovery, and tool-window changes.
- Update `README.md` when setup, supported formats, commands, settings, or output paths change.
- Keep [BACKLOG.md](BACKLOG.md) for planned work rather than mixing future work into completed behavior.

There is currently no automated test project in the solution. If you add parsing or coverage-calculation behavior, include automated tests where practical.
