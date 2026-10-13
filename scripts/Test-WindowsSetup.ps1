# CI-only smoke check of the documented Windows setup, using a fresh LocalDB instance.
# It never accepts an external connection string and never touches an existing database.
$ErrorActionPreference = 'Stop'
if ($env:CI -ne 'true' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'This smoke script is restricted to disposable Windows CI runners. Follow docs/WINDOWS-SETUP.md locally.'
}
$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo
$instance = 'AdminPanelCi_' + [Guid]::NewGuid().ToString('N')
$connection = "Server=(localdb)\$instance;Database=$instance;Integrated Security=true;TrustServerCertificate=true"
$processes = @()
$certificate = $null
$certificatePath = Join-Path $env:RUNNER_TEMP ($instance + '.pfx')
$publicCertificatePath = Join-Path $env:RUNNER_TEMP ($instance + '.cer')
$logs = Join-Path $repo 'artifacts/windows-smoke'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
$localdb = (Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue).Source
if (!$localdb) {
    $localdb = 'C:\Program Files\Microsoft SQL Server\160\Tools\Binn\SqlLocalDB.exe'
}
if (!(Test-Path -LiteralPath $localdb)) { throw 'SQL Server Express LocalDB is not installed on this runner.' }

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed with exit code $LASTEXITCODE." }
}
function Wait-Endpoint([string]$url, [int]$expected) {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $url -SkipHttpErrorCheck -TimeoutSec 2
            if ($response.StatusCode -eq $expected) { return }
        } catch { }
        Start-Sleep -Seconds 1
    }
    throw "Local endpoint did not become ready: $url"
}
function Get-Token([string]$html) {
    $token = [regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
    if (!$token) { throw 'Antiforgery token missing from the MVC form.' }
    return [Net.WebUtility]::HtmlDecode($token)
}
function Start-App([string]$project, [string]$assembly, [string]$url) {
    $env:ASPNETCORE_URLS = $url
    $dll = Join-Path $repo "$project/bin/Release/net8.0/$assembly.dll"
    return Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('"' + $dll + '"') -WorkingDirectory (Join-Path $repo $project) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs "$project.log") -RedirectStandardError (Join-Path $logs "$project-error.log")
}

try {
    & $localdb create $instance -s
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the disposable LocalDB instance.' }
    $env:ConnectionStrings__DefaultConnection = $connection
    $env:JwtSettings__SecretKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    $env:BootstrapAdmin__Username = 'windows-smoke'
    $env:BootstrapAdmin__Email = 'windows-smoke@example.test'
    $env:BootstrapAdmin__Password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:ASPNETCORE_ENVIRONMENT = 'Testing'
    Write-Output "::add-mask::$env:JwtSettings__SecretKey"
    Write-Output "::add-mask::$env:BootstrapAdmin__Password"
    Invoke-Dotnet tool restore
    Invoke-Dotnet ef database update --project AdminPanel --no-build --configuration Release
    # Splat an array to preserve the literal "--"; PowerShell otherwise consumes it at a function boundary.
    $bootstrapArguments = @('run', '--project', 'AdminPanel', '--no-build', '--configuration', 'Release', '--no-launch-profile', '--', '--bootstrap-admin')
    Invoke-Dotnet @bootstrapArguments

    $certificate = New-SelfSignedCertificate -DnsName localhost -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddHours(2) -KeyExportPolicy Exportable
    # CurrentUser Root imports display a trust dialog even with certutil -f.
    # The disposable hosted runner is elevated: import this short-lived test root
    # into LocalMachine, then remove that exact certificate in finally.
    [IO.File]::WriteAllBytes($publicCertificatePath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    & certutil -f -addstore Root $publicCertificatePath
    if ($LASTEXITCODE -ne 0) { throw 'Could not trust the temporary CI localhost certificate.' }
    Write-Output 'WINDOWS SMOKE: migrations and bootstrap complete; temporary HTTPS certificate trusted.'
    $certificatePassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    Write-Output "::add-mask::$certificatePassword"
    [IO.File]::WriteAllBytes($certificatePath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $certificatePassword))
    $env:ASPNETCORE_Kestrel__Certificates__Default__Path = $certificatePath
    $env:ASPNETCORE_Kestrel__Certificates__Default__Password = $certificatePassword
    $env:ApiSettings__BaseUrl = 'https://localhost:17556/api/'
    $env:PublicCatalog__ProductIds__0 = '1'
    $processes += Start-App 'AdminPanel' 'AdminPanel.API' 'https://localhost:17556'
    $processes += Start-App 'AdminPanel.MVC' 'AdminPanel.MVC' 'https://localhost:17889'
    $env:ApiSettings__BaseUrl = 'https://localhost:17556/api/catalog/'
    $processes += Start-App 'AdminPanel.Storefront' 'AdminPanel.Storefront' 'https://localhost:17989'
    Wait-Endpoint 'https://localhost:17556/api/categories' 401
    Wait-Endpoint 'https://localhost:17889/Account/Login' 200
    Wait-Endpoint 'https://localhost:17989/' 200

    $login = Invoke-WebRequest 'https://localhost:17889/Account/Login' -SessionVariable session
    $signedIn = Invoke-WebRequest 'https://localhost:17889/Account/Login' -Method Post -WebSession $session -Body @{
        Username = $env:BootstrapAdmin__Username
        Password = $env:BootstrapAdmin__Password
        __RequestVerificationToken = Get-Token $login.Content
    }
    if ($signedIn.Content -notmatch '<h1>Dashboard</h1>') { throw 'Windows MVC/API administrator login failed.' }
    $form = Invoke-WebRequest 'https://localhost:17889/ShippingOptions/Create' -WebSession $session
    $created = Invoke-WebRequest 'https://localhost:17889/ShippingOptions/Create' -Method Post -WebSession $session -Body @{
        Name = 'Windows smoke shipping'; Area = 'Test only'; Price = '3.25'
        __RequestVerificationToken = Get-Token $form.Content
    }
    if ($created.Content -notmatch 'Windows smoke shipping') { throw 'Windows MVC-to-API write did not appear in the list.' }
    $csrf = Invoke-WebRequest 'https://localhost:17889/ShippingOptions/Create' -Method Post -WebSession $session -Body @{Name='Rejected';Area='Test';Price='1'} -SkipHttpErrorCheck
    if ($csrf.StatusCode -ne 400) { throw 'Windows CSRF protection failed.' }
    $apiLogin = Invoke-RestMethod 'https://localhost:17556/api/auth/login' -Method Post -ContentType 'application/json' -Body (@{username=$env:BootstrapAdmin__Username;password=$env:BootstrapAdmin__Password} | ConvertTo-Json)
    $adminHeaders = @{Authorization='Bearer ' + $apiLogin.token}
    Invoke-RestMethod 'https://localhost:17556/api/categories' -Method Post -Headers $adminHeaders -ContentType 'application/json' -Body '{"name":"Windows catalog"}' | Out-Null
    Invoke-RestMethod 'https://localhost:17556/api/products' -Method Post -Headers $adminHeaders -Form @{Name='Windows catalog product';CategoryId='1';Price='1250.50';StockQuantity='2';IsAvailable='true';Unit='Piece'} | Out-Null
    $storePage = Invoke-WebRequest 'https://localhost:17989/' -SessionVariable storeSession
    if ($storePage.Content -notmatch 'lang="ar" dir="rtl"' -or $storePage.Content -notmatch 'Windows catalog product') { throw 'Arabic storefront catalog failed.' }
    $english = Invoke-WebRequest 'https://localhost:17989/Language/Change' -Method Post -WebSession $storeSession -Body @{culture='en';returnUrl='/Catalog/Details/1';__RequestVerificationToken=(Get-Token $storePage.Content)}
    if ($english.Content -notmatch 'lang="en" dir="ltr"' -or $english.Content -notmatch 'Windows catalog product') { throw 'English storefront details failed.' }
    $cartToken = Get-Token $english.Content
    $cart = Invoke-WebRequest 'https://localhost:17989/Cart/Add' -Method Post -WebSession $storeSession -Body @{ProductId='1'; Quantity='1'; __RequestVerificationToken=$cartToken}
    if ($cart.Content -notmatch 'data-product-id="1"') { throw 'Guest cart add failed.' }
    $register = Invoke-WebRequest 'https://localhost:17989/Account/Register' -WebSession $storeSession
    $customerPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $merged = Invoke-WebRequest 'https://localhost:17989/Account/Register' -Method Post -WebSession $storeSession -Body @{
        DisplayName='Windows customer'; Email='windows-customer@example.test'; Password=$customerPassword
        ConfirmPassword=$customerPassword; __RequestVerificationToken=(Get-Token $register.Content)
    }
    if ($merged.Content -notmatch 'data-product-id="1"') { throw 'Customer registration and cart merge failed.' }
    Invoke-WebRequest 'https://localhost:17989/Account/Logout' -Method Post -WebSession $storeSession -Body @{__RequestVerificationToken=(Get-Token $merged.Content)} | Out-Null
    $customerLogin = Invoke-WebRequest 'https://localhost:17989/Account/Login' -WebSession $storeSession
    Invoke-WebRequest 'https://localhost:17989/Account/Login' -Method Post -WebSession $storeSession -Body @{
        Email='windows-customer@example.test'; Password=$customerPassword; __RequestVerificationToken=(Get-Token $customerLogin.Content)
    } | Out-Null
    $saved = Invoke-WebRequest 'https://localhost:17989/Cart' -WebSession $storeSession
    if ($saved.Content -notmatch 'data-product-id="1"') { throw 'Saved customer cart did not persist.' }
    Remove-Variable customerPassword
    Write-Output 'WINDOWS SMOKE PASS: isolated migrations, three HTTPS apps, admin CRUD/CSRF, bilingual catalog, customer registration/login/logout and guest/saved cart merging.'
} finally {
    foreach ($process in $processes) {
        if (!$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    # The generated connection is the only drop target this script can use.
    if ($env:ConnectionStrings__DefaultConnection -eq $connection) {
        & dotnet ef database drop --force --project AdminPanel --no-build --configuration Release
    }
    & $localdb stop $instance -k
    & $localdb delete $instance
    if ($certificate) {
        foreach ($target in @(@{Name='Root'; Location='LocalMachine'}, @{Name='My'; Location='CurrentUser'})) {
            $store = [Security.Cryptography.X509Certificates.X509Store]::new($target.Name, $target.Location)
            $store.Open('ReadWrite')
            try { $store.Remove($certificate) } finally { $store.Close() }
        }
        $certificate.Dispose()
    }
    $resolvedTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
    foreach ($temporaryFile in @($certificatePath, $publicCertificatePath)) {
        $resolvedCertificate = [IO.Path]::GetFullPath($temporaryFile)
        if ($resolvedCertificate.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedCertificate)) {
            Remove-Item -LiteralPath $resolvedCertificate
        }
    }
    foreach ($setting in @('JwtSettings__SecretKey','ConnectionStrings__DefaultConnection','BootstrapAdmin__Username','BootstrapAdmin__Email','BootstrapAdmin__Password','ASPNETCORE_Kestrel__Certificates__Default__Password','ASPNETCORE_Kestrel__Certificates__Default__Path','ASPNETCORE_URLS','ASPNETCORE_ENVIRONMENT','ApiSettings__BaseUrl','PublicCatalog__ProductIds__0')) {
        [Environment]::SetEnvironmentVariable($setting, $null, 'Process')
    }
}
