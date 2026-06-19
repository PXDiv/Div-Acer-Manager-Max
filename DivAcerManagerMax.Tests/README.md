# DivAcerManagerMax.Tests

Unit tests live under `units/`, mirroring the flat app project layout where practical.

Run tests:

```bash
dotnet test DivAcerManagerMax.Tests/DivAcerManagerMax.Tests.csproj
```

Run focused unit coverage:

```bash
dotnet test DivAcerManagerMax.Tests/DivAcerManagerMax.Tests.csproj --settings DivAcerManagerMax.Tests/coverage.runsettings --collect:"XPlat Code Coverage"
```

The focused coverage scope excludes Avalonia views, XAML-generated code, daemon socket code, and hardware probing code. Those areas need integration/UI seams before their coverage is meaningful in unit tests.
