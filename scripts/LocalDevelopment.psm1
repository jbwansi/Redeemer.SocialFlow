# Windows PowerShell 5.1. Secrets are returned only to the launcher, never printed.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

function Assert-RegularPath([string] $Path) {
    if ((Test-Path -LiteralPath $Path) -and
        ((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Local configuration cannot use a symbolic link or junction.'
    }
}

function Initialize-PrivateDirectory([string] $Path) {
    Assert-RegularPath $Path
    [void][IO.Directory]::CreateDirectory($Path)
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetOwner($sid)
    foreach ($identity in @($sid, [Security.Principal.SecurityIdentifier]'S-1-5-18')) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    [IO.Directory]::SetAccessControl($Path, $acl)
}

function Get-LocalAdminSecret([string] $Directory, [switch] $Initialize, [switch] $Rotate) {
    Initialize-PrivateDirectory $Directory
    $path = Join-Path $Directory 'visual-admin.dpapi'
    $lockPath = Join-Path $Directory 'configuration.lock'
    Assert-RegularPath $path
    Assert-RegularPath $lockPath
    $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        if ((Test-Path -LiteralPath $path) -and !$Rotate) {
            $bytes = $null
            try {
                $bytes = [Security.Cryptography.ProtectedData]::Unprotect(
                    [IO.File]::ReadAllBytes($path), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
                $secret = [Text.Encoding]::UTF8.GetString($bytes)
                if ($secret -cnotmatch '^[A-Za-z0-9+/]{43}=$') { throw 'Invalid secret.' }
                return $secret
            } catch { throw 'Cannot decrypt local configuration. Use the original Windows account or explicitly rotate the secret.' }
            finally { if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) } }
        }
        if (!$Initialize -and !$Rotate) { throw 'Local configuration is missing. Run Start-Local.ps1 -Initialize first.' }
        $random = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($random) } finally { $rng.Dispose() }
        $secret = [Convert]::ToBase64String($random)
        [Array]::Clear($random, 0, $random.Length)
        $plain = [Text.Encoding]::UTF8.GetBytes($secret)
        try { $encrypted = [Security.Cryptography.ProtectedData]::Protect($plain, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser) }
        finally { [Array]::Clear($plain, 0, $plain.Length) }
        $temporary = Join-Path $Directory ([Guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [IO.File]::WriteAllBytes($temporary, $encrypted)
            if (Test-Path -LiteralPath $path) { [IO.File]::Replace($temporary, $path, [NullString]::Value) }
            else { [IO.File]::Move($temporary, $path) }
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
        return $secret
    } finally { $lock.Dispose() }
}

function Export-LocalCertificate([string] $Directory) {
    # --check never creates/trusts a certificate. Export without a password exports public data only.
    & dotnet dev-certs https --check --trust --quiet
    if ($LASTEXITCODE -ne 0) { throw 'An existing trusted ASP.NET Core certificate is required. Run dotnet dev-certs https --trust separately.' }
    $path = Join-Path $Directory 'aspnetcore-public.pem'
    Assert-RegularPath $path
    & dotnet dev-certs https --export-path $path --format PEM --quiet
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Public certificate export failed.' }
    $pem = [IO.File]::ReadAllText($path)
    if ($pem -notmatch '-----BEGIN CERTIFICATE-----' -or $pem -match 'PRIVATE KEY') { throw 'Expected a public certificate only.' }
    return $path
}

function New-LocalProcessInfo([string] $Executable, [string] $Arguments, [string] $Directory,
    [string] $Secret, [string] $Certificate) {
    if (!(Test-Path -LiteralPath $Certificate -PathType Leaf)) { throw 'Public certificate is missing.' }
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = $Arguments
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables['SOCIALFLOW_VISUAL_ADMIN_SECRET'] = $Secret
    $info.EnvironmentVariables['SOCIALFLOW_VISUAL_GATEWAY_ENABLED'] = 'true'
    $info.EnvironmentVariables['NODE_EXTRA_CA_CERTS'] = $Certificate
    $info.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $info.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Development'
    $info.EnvironmentVariables['ASPNETCORE_URLS'] = 'https://localhost:65474;http://localhost:65475'
    $info.EnvironmentVariables['NODE_ENV'] = 'development'
    # This local launcher never starts automatic publication, regardless of inherited settings.
    $info.EnvironmentVariables['Facebook__WorkerEnabled'] = 'false'
    $info.EnvironmentVariables['LinkedIn__WorkerEnabled'] = 'false'
    if ($info.EnvironmentVariables['NODE_TLS_REJECT_UNAUTHORIZED'] -eq '0') { throw 'TLS verification must not be disabled.' }
    return $info
}

function New-LocalOutputPump([Diagnostics.Process] $Process, [string] $Name) {
    foreach ($stream in @('StandardOutput', 'StandardError')) {
        $reader = $Process.$stream
        [pscustomobject]@{ Name = $Name; Reader = $reader; Pending = $reader.ReadLineAsync(); Closed = $false }
    }
}

function Receive-LocalOutput($Pumps) {
    # Read both pipes concurrently; never block waiting for a partial line on either pipe.
    foreach ($pump in $Pumps) {
        for ($count = 0; $count -lt 200 -and !$pump.Closed -and $pump.Pending.IsCompleted; $count++) {
            $line = $pump.Pending.GetAwaiter().GetResult()
            if ($null -eq $line) { $pump.Closed = $true; break }
            Write-Host ('[{0}] {1}' -f $pump.Name, $line)
            $pump.Pending = $pump.Reader.ReadLineAsync()
        }
    }
}

Export-ModuleMember -Function Get-LocalAdminSecret, Export-LocalCertificate, New-LocalProcessInfo, New-LocalOutputPump, Receive-LocalOutput


