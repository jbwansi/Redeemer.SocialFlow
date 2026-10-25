#Requires -Version 5.1
# Standalone tests: no Pester dependency, no API/worker/OAuth start, no real configuration touched.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'LocalDevelopment.psm1') -Force
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('socialflow-launcher-test-' + [Guid]::NewGuid().ToString('N'))
$script:checks = 0
function Assert([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function Assert-Fails([scriptblock] $Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert $failed 'Expected safe refusal.'
}
try {
    Assert-Fails { Get-LocalAdminSecret $testRoot }
    $first = Get-LocalAdminSecret $testRoot -Initialize
    Assert ($first.Length -eq 44) 'Expected 256-bit secret encoded as Base64.'
    $file = Join-Path $testRoot 'visual-admin.dpapi'
    $cipher = [IO.File]::ReadAllBytes($file)
    Assert (![Text.Encoding]::UTF8.GetString($cipher).Contains($first)) 'Secret persisted in cleartext.'
    $second = Get-LocalAdminSecret $testRoot -Initialize
    Assert ($first -ceq $second) 'Initialization must be idempotent.'
    Assert ([Convert]::ToBase64String($cipher) -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($file))) 'Initialization rewrote the secret.'
    Assert ((Get-Acl $testRoot).AreAccessRulesProtected) 'Directory ACL must not inherit access.'
    $allowed = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18')
    foreach ($rule in (Get-Acl $testRoot).Access) {
        Assert ($allowed -contains $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value) 'Unexpected directory access.'
    }
    $rotated = Get-LocalAdminSecret $testRoot -Rotate
    Assert ($rotated -cne $first) 'Rotation must generate a new secret.'
    Assert ((Get-LocalAdminSecret $testRoot) -ceq $rotated) 'Rotated secret did not persist.'
    [IO.File]::WriteAllText($file, 'corrupt')
    Assert-Fails { Get-LocalAdminSecret $testRoot -Initialize }
    Assert ([IO.File]::ReadAllText($file) -eq 'corrupt') 'Corruption must not trigger silent regeneration.'
    $secret = Get-LocalAdminSecret $testRoot -Rotate
    $cert = Join-Path $testRoot 'test-public.pem'
    Assert-Fails { New-LocalProcessInfo 'dotnet' 'run' $testRoot $secret $cert }
    [IO.File]::WriteAllText($cert, 'test certificate placeholder: no TLS connection in these tests')
    $parentSecret = $env:SOCIALFLOW_VISUAL_ADMIN_SECRET
    $api = New-LocalProcessInfo 'dotnet' 'run --no-launch-profile' $testRoot $secret $cert
    $web = New-LocalProcessInfo 'node' 'vite' $testRoot $secret $cert
    Assert ($api.EnvironmentVariables['SOCIALFLOW_VISUAL_ADMIN_SECRET'] -ceq $web.EnvironmentVariables['SOCIALFLOW_VISUAL_ADMIN_SECRET']) 'Process secrets differ.'
    Assert (!$api.Arguments.Contains($secret) -and !$web.Arguments.Contains($secret)) 'Secret exposed in process arguments.'
    Assert ($env:SOCIALFLOW_VISUAL_ADMIN_SECRET -ceq $parentSecret) 'Parent environment changed.'
    Assert ($web.EnvironmentVariables['NODE_EXTRA_CA_CERTS'] -eq $cert) 'Missing public certificate environment.'
    Assert ($web.EnvironmentVariables['SOCIALFLOW_VISUAL_GATEWAY_ENABLED'] -eq 'true') 'Missing gateway activation.'
    Assert ($api.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] -eq 'Development') 'Wrong API environment.'
    Assert ($api.EnvironmentVariables['Facebook__WorkerEnabled'] -eq 'false') 'Automatic Facebook publication must remain disabled.'
    # Two harmless child processes prove actual environment inheritance without printing the secret.
    $fingerprints = @()
    foreach ($info in @($api, $web)) {
        $info.FileName = Join-Path $PSHOME 'powershell.exe'
        $info.Arguments = '-NoProfile -Command "[Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($env:SOCIALFLOW_VISUAL_ADMIN_SECRET)))"'
        $info.RedirectStandardOutput = $true
        $child = [Diagnostics.Process]::Start($info)
        try {
            $fingerprints += $child.StandardOutput.ReadToEnd().Trim()
            $child.WaitForExit()
            Assert ($child.ExitCode -eq 0) 'Child environment verification failed.'
        } finally { $child.Dispose() }
    }
    Assert ($fingerprints[0].Length -eq 44 -and $fingerprints[0] -ceq $fingerprints[1]) 'Child processes did not receive identical secrets.'
    $outputInfo = New-LocalProcessInfo (Join-Path $PSHOME 'powershell.exe') '-NoProfile -Command "[Console]::Out.WriteLine(''stdout-marker''); [Console]::Error.WriteLine(''stderr-marker'')"' $testRoot $secret $cert
    Assert ($outputInfo.RedirectStandardOutput -and $outputInfo.RedirectStandardError) 'Both output streams must be captured.'
    $child = [Diagnostics.Process]::Start($outputInfo)
    try {
        $pumps = @(New-LocalOutputPump $child 'API')
        $captured = @()
        $until = [DateTime]::UtcNow.AddSeconds(10)
        while (@($pumps | Where-Object { !$_.Closed }).Count -gt 0 -and [DateTime]::UtcNow -lt $until) {
            $captured += @(Receive-LocalOutput $pumps 6>&1)
            Start-Sleep -Milliseconds 20
        }
        Assert (@($pumps | Where-Object { !$_.Closed }).Count -eq 0) 'Output pipes did not finish.'
        $output = ($captured | ForEach-Object { $_.ToString() }) -join "`n"
        Assert ($output.Contains('[API] stdout-marker') -and $output.Contains('[API] stderr-marker')) 'Missing stdout/stderr relay.'
        Assert (!$output.Contains($secret)) 'Secret leaked in output relay test.'
    } finally {
        if (!$child.HasExited) { $child.Kill() }
        $child.Dispose()
    }
    $tls = $env:NODE_TLS_REJECT_UNAUTHORIZED
    try {
        $env:NODE_TLS_REJECT_UNAUTHORIZED = '0'
        Assert-Fails { New-LocalProcessInfo 'node' 'vite' $testRoot $secret $cert }
    } finally { $env:NODE_TLS_REJECT_UNAUTHORIZED = $tls }
    # Validate certificate command selection using a fake executable function, never the actual store.
    $global:certificateCalls = @()
    function global:dotnet {
        $global:certificateCalls += ,$args
        $global:LASTEXITCODE = 0
        if ($args -contains '--export-path') {
            [IO.File]::WriteAllText($args[[Array]::IndexOf($args, '--export-path') + 1], "-----BEGIN CERTIFICATE-----`nTEST`n-----END CERTIFICATE-----")
        }
    }
    $export = Export-LocalCertificate $testRoot
    Assert (Test-Path -LiteralPath $export) 'Public certificate not exported.'
    Assert ($global:certificateCalls.Count -eq 2) 'Expected check then export.'
    Assert ($global:certificateCalls[0] -contains '--check' -and $global:certificateCalls[0] -contains '--trust') 'Trust must be checked.'
    Assert ($global:certificateCalls[1] -contains 'PEM' -and $global:certificateCalls[1] -notcontains '--no-password' -and $global:certificateCalls[1] -notcontains '--password') 'Private key export is prohibited.'
    function global:dotnet { $global:LASTEXITCODE = 1 }
    Assert-Fails { Export-LocalCertificate $testRoot }
    Write-Host "Passed: $script:checks launcher assertions (Windows PowerShell $($PSVersionTable.PSVersion))."
} finally {
    Remove-Item Function:\dotnet -ErrorAction SilentlyContinue
    # Only remove this unique, verified test directory.
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $resolved -Leaf) -like 'socialflow-launcher-test-*' -and (Test-Path -LiteralPath $resolved)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
