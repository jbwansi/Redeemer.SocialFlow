#Requires -Version 5.1
# Frontend only: no API, worker, OAuth or local administration gateway started.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (Get-NetTCPConnection -State Listen -LocalPort 5173 -ErrorAction SilentlyContinue) {
    throw 'Port 5173 is already in use. Stop the existing Vite process before starting.'
}
Import-Module (Join-Path $PSScriptRoot 'LocalDevelopment.psm1') -Force
$directory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Redeemer.SocialFlow\LocalDevelopment'
$https = Export-LocalHttpsCertificate $directory
$webDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'Redeemer.SocialFlow.Web'
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = 'node'
$info.Arguments = 'node_modules/vite/bin/vite.js --mode development --host localhost --port 5173 --strictPort'
$info.WorkingDirectory = $webDirectory
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.EnvironmentVariables['SOCIALFLOW_HTTPS_CERT'] = $https.Certificate
$info.EnvironmentVariables['SOCIALFLOW_HTTPS_KEY'] = $https.Key
$info.EnvironmentVariables['NODE_EXTRA_CA_CERTS'] = $https.Certificate
$info.EnvironmentVariables['SOCIALFLOW_VISUAL_GATEWAY_ENABLED'] = 'false'
if ($info.EnvironmentVariables['NODE_TLS_REJECT_UNAUTHORIZED'] -eq '0') { throw 'TLS verification must not be disabled.' }
$frontend = [Diagnostics.Process]::Start($info)
try { $frontend.WaitForExit(); exit $frontend.ExitCode }
finally { if (!$frontend.HasExited) { $frontend.Kill() }; $frontend.Dispose() }
