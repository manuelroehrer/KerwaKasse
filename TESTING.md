# Testing Strategy

## Structure

Two test projects, separated by dependency:

- **KerwaKasse.Core.Tests** — Tests for the data layer (services, models). No WPF dependency.
- **KerwaKasse.Tests** — Tests for ViewModels and helper classes of the WPF app.

Framework: xUnit. Mocking: NSubstitute.

## Unit Tests

ViewModels are tested against mocked service interfaces (`IProductService`, `IOrderService`, `ISettingsService`, `IDialogService`). This keeps each test independent of database and UI.

Core models (`Order`, `OrderPosition`) are tested directly where they contain computation logic (`Total`, `ShortDescription`).

## Integration Tests

The SQLite services (`SqliteProductService`, `SqliteOrderService`) are tested against real in-memory SQLite databases. Each test gets an isolated DB via shared-cache connections (`Data Source=...;Mode=Memory;Cache=Shared`). This tests actual SQL including transactions without any file system dependency.

`JsonSettingsService` is tested with temporary files (round-trip, missing file, corrupted JSON).

`EventCatalog` manages real database files (one per event), so its tests run against a temporary folder that is removed afterwards.

## Intentionally Not Tested

- **App.xaml.cs, MainWindow.xaml.cs, DialogService.cs** — Pure WPF wiring with no testable business logic. `DialogService` is a thin wrapper around `MessageBox.Show` and `SaveFileDialog`.
- **LiveCharts rendering** — `CreatePieChartData()` in `StatisticsViewModel` builds chart objects from already-tested data. Color mapping and rendering are framework responsibility.
- **DatabaseViewModel.BackupDatabase()** — A single `File.Copy` operation with a dialog. Introducing a file system abstraction just for this one call is not worth the effort.

## Running Tests

```
dotnet test KerwaKasse.sln
```

The full suite runs in under 15 seconds. A separate smoke layer is not needed at this project size.
