# Windows development setup and verification

Use this guide from a checkout of `codex/audit-repair-adminpanel`. These steps create a **local development database**. They do not deploy the application.

## Install prerequisites

- Install the **.NET 8 SDK**, not just the runtime. `global.json` permits 8.0.400 and later 8.0 feature bands. Use Microsoft's [.NET Windows installation instructions](https://learn.microsoft.com/en-us/dotnet/core/install/windows) or `winget install --id Microsoft.DotNet.SDK.8 --exact`.
- Use **PowerShell 7** for the commands below. Open a new terminal after installation so PATH is refreshed.
- Install **SQL Server Express LocalDB** using the SQL Server Express installer, or the SQL Server Express LocalDB component in Visual Studio Installer. See Microsoft's [LocalDB installation guidance](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb). LocalDB uses your Windows account and requires no SQL password. A dedicated SQL Server development instance also works; configure its connection privately.
- Git is required to obtain the checkout. No Node/npm installation is required by this application or its browser tests.

Verify the tools, then start LocalDB:

```powershell
git --version
dotnet --list-sdks
$PSVersionTable.PSVersion
SqlLocalDB info
SqlLocalDB start MSSQLLocalDB
```

If `MSSQLLocalDB` does not exist, create it explicitly with `SqlLocalDB create MSSQLLocalDB -s`. If the utility is not on PATH, a typical SQL Server 2022 location is `C:\Program Files\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe`. Use the installed version's actual path; do not replace an existing instance. The [SqlLocalDB utility reference](https://learn.microsoft.com/en-us/sql/tools/sqllocaldb-utility) documents instance management.

## Restore, configure and migrate

From the repository root:

```powershell
dotnet restore AdminPanel.sln
dotnet tool restore
dotnet build AdminPanel.sln --configuration Release
dotnet dev-certs https --trust

$jwtSecret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "JwtSettings:SecretKey" $jwtSecret --project AdminPanel
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=AdminPanelDev;Integrated Security=true;TrustServerCertificate=true" --project AdminPanel
Remove-Variable jwtSecret

dotnet ef migrations script --idempotent --project AdminPanel --output migrations.sql -- --environment Development
# Review migrations.sql and confirm this is your development database.
dotnet ef database update --project AdminPanel -- --environment Development
```

The generated SQL file is an inspection artifact. Do not commit connection strings or keys. The API validates JWT configuration at startup; `Issuer`, `Audience` and token lifetime have checked-in non-secret defaults. MVC has **no database connection**.

For an existing development database, `HardenDataIntegrity` rejects duplicate or overlong usernames/slugs. Resolve the reported data conflict deliberately, then retry. No startup migration or automatic record cleanup occurs. Phase 2/3 introduce no schema changes.

`TrustServerCertificate=true` above is specific to your local development SQL Server. It does not disable HTTPS validation between MVC and API. A shared SQL Server requires its own access and certificate configuration.

## Provision an administrator

After the development database has migrated:

```powershell
$env:BootstrapAdmin__Username = Read-Host "Administrator username"
$env:BootstrapAdmin__Email = Read-Host "Administrator email"
$securePassword = Read-Host "Administrator password (at least 12 characters)" -AsSecureString
$env:BootstrapAdmin__Password = [Net.NetworkCredential]::new("", $securePassword).Password
try {
    dotnet run --project AdminPanel --launch-profile https -- --bootstrap-admin
} finally {
    Remove-Item Env:BootstrapAdmin__Password
    Remove-Variable securePassword
}
```

This hashes the password and exits. It refuses to overwrite an existing username. Use `--reset-password` with the same input process to reset a legacy account; role and blocked status remain unchanged. There are no default credentials. Staff and Customer accounts cannot administer the system.

## Run the API and MVC client

Open two PowerShell terminals in the repository root:

```powershell
# Terminal 1
dotnet run --project AdminPanel --launch-profile https

# Terminal 2
dotnet run --project AdminPanel.MVC --launch-profile https
```

Open [MVC login](https://localhost:7189/Account/Login). The API listens on [https://localhost:7056](https://localhost:7056); development Swagger is at `/swagger`. Sign in with the account you just provisioned.

MVC `ApiSettings:BaseUrl` is `https://localhost:7056/api/`. If ports change, set `ApiSettings__BaseUrl` in the MVC terminal, including the trailing slash. Keep both HTTPS profiles: authentication cookies are Secure and HttpOnly. Close the terminals or press Ctrl+C to stop the applications.

Common problems:

| Symptom | Check |
| --- | --- |
| `dotnet` missing or SDK mismatch | Install the SDK, reopen the terminal, run `dotnet --list-sdks`, and check `global.json`. |
| SQL connection error | Run `SqlLocalDB info MSSQLLocalDB`; confirm the connection uses the same instance and Windows account. |
| JWT configuration startup error | Set user secrets on the **API project**, and use the Development HTTPS profile. |
| HTTPS trust/API unavailable message | Trust the development certificate, start the API, and verify MVC's base URL. Do not add a certificate-validation bypass. |
| Login succeeds over API but not MVC | Use the MVC HTTPS address, ensure the account has the Admin role and is not blocked. |
| Existing plaintext password fails | Use the explicit password-reset command after confirming the account. |
| Port already in use | Stop the previous local process or change the launch-profile ports and MVC base URL consistently. |

## Run the automated checks

```powershell
# Default: isolated relational SQLite, no application database access.
dotnet test AdminPanel.sln --configuration Release

# Install official Playwright browser binaries once after building.
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --install-browsers

# Runs Chromium and Firefox against disposable fixtures; optional -- --headed.
dotnet run --project AdminPanel.BrowserTests --configuration Release
```

The browser harness starts a loopback HTTPS transport into the real MVC/API ASP.NET pipelines, uses random test credentials and a fresh database for each browser, and destroys its fixtures afterwards. Its self-signed certificate exception exists only in the test browser. The application HTTP client retains normal TLS verification. Browser screenshots and a result JSON are saved under `AdminPanel.BrowserTests/TestResults/`; they are ignored by Git. Authentication traces, cookies and passwords are not written as artifacts.

Optional SQL Server test runs require `ADMINPANEL_TEST_SQLSERVER` to refer to a **disposable loopback SQL Server on port 1433**, with database create/drop permission. The fixture deliberately rejects arbitrary servers, ignores any supplied database name, generates unique `AdminPanel_Test_` databases, migrates and deletes them. Leave this variable unset to use SQLite. Normal development LocalDB configuration and the test fixture connection are separate.

CI runs the full integration and browser suites with SQLite and disposable SQL Server. A separate **Windows** CI job runs `scripts/Test-WindowsSetup.ps1`: it creates its own uniquely named LocalDB instance, applies migrations, bootstraps a random test administrator, starts both actual HTTPS application processes, verifies login, an MVC/API write and CSRF rejection, and cleans up its database/processes/test certificates. That script is restricted to disposable Windows CI runners; do not use it to manage your development or production instance.

See [AUDIT.md](AUDIT.md) for the actual completed-run evidence and limitations. Browser automation checks selected accessibility basics and layout bounds; it is not a full WCAG audit or a guarantee of identical rendering across all devices.
