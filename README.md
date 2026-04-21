# FluGenPass

FluGenPass is a `net8.0-windows` WPF desktop app that generates secure passwords and stores them locally in an encrypted vault. The UI uses `WPF-UI` for a Fluent-style window, navigation shell, dialogs, and snackbar feedback, while the app logic follows an MVVM structure with `CommunityToolkit.Mvvm`.

## Features

- Fluent-style desktop shell with `Generator`, `Vault`, and `Settings` sections
- Real-time password generation with:
  - length slider from 8 to 64
  - uppercase, lowercase, numbers, and symbols toggles
  - strength indicator based on estimated entropy
  - copy to clipboard and save-to-vault actions
- Local encrypted vault:
  - site/service label per saved password
  - masked rows by default with reveal, copy, and delete actions
  - encrypted `vault.dat` stored under `%LocalAppData%\FluGenPass`
- Master password flow:
  - first-use setup when opening or saving to the vault
  - unlock for the current app session only
  - PBKDF2-SHA256 verification metadata in `settings.json`
- Theme settings with `System`, `Light`, and `Dark`

## Prerequisites

- Windows
- .NET 8 SDK or newer with Windows Desktop support

## NuGet Packages

The app project uses these NuGet packages:

```powershell
dotnet add FluGenPass\FluGenPass.csproj package WPF-UI
dotnet add FluGenPass\FluGenPass.csproj package CommunityToolkit.Mvvm
dotnet add FluGenPass\FluGenPass.csproj package Microsoft.Extensions.DependencyInjection
```

The test project uses `xunit`, `Microsoft.NET.Test.Sdk`, and `coverlet.collector`.

## Build and Run

```powershell
dotnet build FluGenPass.sln
dotnet run --project FluGenPass\FluGenPass.csproj
dotnet test FluGenPass.Tests\FluGenPass.Tests.csproj
```

## Project Layout

- `FluGenPass/MainWindow.xaml`: Fluent shell with `NavigationView`
- `FluGenPass/ViewModels/GeneratorViewModel.cs`: password generation logic and commands
- `FluGenPass/Services/VaultService.cs`: AES-GCM encrypted JSON vault persistence
- `FluGenPass/Services/MasterPasswordService.cs`: master password setup and unlock flow
- `FluGenPass/Views/Pages/`: generator, vault, and settings pages
- `FluGenPass.Tests/`: unit tests for generation and vault security

## Storage

FluGenPass stores its local data under:

```text
%LocalAppData%\FluGenPass
```

Files created there:

- `settings.json`: theme preference and master-password verification metadata
- `vault.dat`: encrypted vault contents

## Security Notes

- Passwords are generated with `System.Security.Cryptography.RandomNumberGenerator`.
- The master password is not stored directly.
- Verification metadata uses PBKDF2-SHA256 with a random 16-byte salt, 32-byte derived material, and 200,000 iterations.
- Vault contents are encrypted with AES-GCM.
- Unlocking keeps the derived vault key only in memory for the current app session.
- This is a local-only v1 app: no sync, export, or cloud backup is included
