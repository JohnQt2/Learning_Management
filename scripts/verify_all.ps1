param([string]$DatabasePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$logRoot = Join-Path $repoRoot ('artifacts/verification/final-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

function Invoke-Checked([string]$Name, [string]$Executable, [string[]]$CommandArguments) {
    $log = Join-Path $logRoot ($Name + '.log')
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $Executable @CommandArguments *> $log
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $oldPreference
    if ($exitCode -ne 0) {
        Get-Content -LiteralPath $log -Tail 25
        throw "$Name failed (exit $exitCode). Log: $log"
    }
    Write-Output ($Name + ': exit 0; ' + (Get-Content -LiteralPath $log -Tail 1))
}

Push-Location $repoRoot
try {
    Invoke-Checked 'restore' 'dotnet' @('restore', 'BE/LMS.sln', '--disable-parallel', '--verbosity', 'quiet')
    Invoke-Checked 'build' 'dotnet' @('build', 'BE/LMS.sln', '-c', 'Release', '--disable-build-servers', '--no-restore', '-m:1', '-p:UseSharedCompilation=false', '--verbosity', 'quiet')
    foreach ($suite in @('LMS.SmokeTests', 'LMS.SecurityTests', 'LMS.IntegrationTests', 'LMS.HostingTests', 'LMS.ProfileTests', 'LMS.MaterialTests', 'LMS.AiAdapterTests', 'LMS.AiWorkflowTests', 'LMS.ProgressTests')) {
        Invoke-Checked $suite 'dotnet' @('run', '--project', ('BE/Tests/' + $suite), '-c', 'Release', '--no-build', '--no-restore', '--no-launch-profile')
    }
    Invoke-Checked 'database-helper-tests' 'python' @('-m', 'unittest', 'discover', '-s', 'scripts', '-p', 'test_*.py', '-v')
    Invoke-Checked 'legacy-upgrade' 'python' @('scripts/verify_legacy_upgrade.py', '--configuration', 'Release')
    if ($DatabasePath) {
        Invoke-Checked 'existing-database-copy-upgrade' 'python' @('scripts/verify_legacy_upgrade.py', '--configuration', 'Release', '--database', $DatabasePath)
    }
    Push-Location (Join-Path $repoRoot 'FE')
    try {
        Invoke-Checked 'frontend-tests' 'npm.cmd' @('test')
        Invoke-Checked 'frontend-build' 'npm.cmd' @('run', 'build')
    }
    finally { Pop-Location }
    Write-Output ('Verified local gates. Logs: ' + $logRoot)
    Write-Output 'This does not run live Gemini, browser UI, SQL Server runtime or deployment.'
}
finally { Pop-Location }
