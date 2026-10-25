#Requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Start')]
param(
    [Parameter(ParameterSetName = 'Initialize')][switch] $Initialize,
    [Parameter(ParameterSetName = 'Rotate')][switch] $RotateSecret
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalDevelopment.psm1') -Force
$root = Split-Path $PSScriptRoot -Parent
$directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Redeemer.SocialFlow\LocalDevelopment'
$api = $null
$web = $null
$pumps = @()
try {
    [void](Get-Command dotnet -ErrorAction Stop)
    $secret = Get-LocalAdminSecret -Directory $directory -Initialize:$Initialize -Rotate:$RotateSecret
    $certificate = Export-LocalCertificate $directory
    if ($Initialize -or $RotateSecret) {
        Write-Host 'Local configuration ready. No server started. Restart both servers after rotation.'
        return
    }
    [void](Get-Command node -ErrorAction Stop)
    $webDirectory = Join-Path $root 'Redeemer.SocialFlow.Web'
    if (!(Test-Path -LiteralPath (Join-Path $webDirectory 'node_modules\vite\bin\vite.js'))) {
        throw 'Frontend dependencies are missing. Run npm ci in Redeemer.SocialFlow.Web.'
    }
    foreach ($port in @(65474, 65475, 5173)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) {
            throw "Port $port is already in use. Stop the existing API/Vite before starting."
        }
    }
    $apiInfo = New-LocalProcessInfo 'dotnet' 'run --no-launch-profile' (Join-Path $root 'Redeemer.SocialFlow.Api') $secret $certificate
    # Ensure these safe diagnostics reach the console even with a restrictive local default level.
    foreach ($category in @('Redeemer.SocialFlow.Infrastructure.Publishing.Facebook.FacebookGraphClient', 'Redeemer.SocialFlow.Api.Controllers.FacebookOAuthController')) {
        $apiInfo.EnvironmentVariables['Logging__Console__LogLevel__' + $category] = 'Warning'
    }
    $apiInfo.EnvironmentVariables['Logging__Console__LogLevel__Redeemer.SocialFlow.Infrastructure.Publishing.Facebook.FacebookGraphClient'] = 'Information'
    $apiInfo.EnvironmentVariables['Logging__Console__LogLevel__Redeemer.SocialFlow.Api.Controllers.FacebookOAuthController'] = 'Information'
    $apiInfo.EnvironmentVariables['Logging__Console__LogLevel__Redeemer.SocialFlow.Api.Controllers.DevFacebookController'] = 'Information'
    $webInfo = New-LocalProcessInfo 'node' 'node_modules/vite/bin/vite.js --mode development --host 127.0.0.1 --port 5173 --strictPort' $webDirectory $secret $certificate
    $api = [Diagnostics.Process]::Start($apiInfo)
    $pumps += @(New-LocalOutputPump $api 'API')
    $web = [Diagnostics.Process]::Start($webInfo)
    $pumps += @(New-LocalOutputPump $web 'Vite')
    Write-Host "API launcher PID: $($api.Id). dotnet run builds current sources before starting (no --no-build)."
    Write-Host 'Accounts (built SPA): https://localhost:65474 | Local administration: http://127.0.0.1:5173'
    Write-Host 'Accounts require a manually initialized separate Identity database. No migration runs at startup.'
    Write-Host 'Automatic publication disabled. Keep this terminal open; Ctrl+C stops both servers.'
    while (!$api.HasExited -and !$web.HasExited) {
        Receive-LocalOutput $pumps
        Start-Sleep -Milliseconds 100
    }
    Receive-LocalOutput $pumps
    throw 'A server stopped. Review its output above; both processes will be stopped.'
} finally {
    foreach ($process in @($web, $api)) {
        if ($null -ne $process) {
            if (!$process.HasExited) { & taskkill.exe /PID $process.Id /T /F | Out-Null }
        }
    }
    $drainUntil = [DateTime]::UtcNow.AddSeconds(2)
    while (@($pumps | Where-Object { !$_.Closed }).Count -gt 0 -and [DateTime]::UtcNow -lt $drainUntil) {
        Receive-LocalOutput $pumps
        Start-Sleep -Milliseconds 20
    }
    foreach ($process in @($web, $api)) { if ($null -ne $process) { $process.Dispose() } }
    $secret = $null
}
