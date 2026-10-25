# Refresh one explicitly selected running Editor through Unity's public AssetDatabase API.
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "../../.."),
    [ValidateRange(0, 60000)][int]$StableMs = 4000,
    [ValidateRange(1, 600000)][int]$TimeoutMs = 180000
)
$ErrorActionPreference = "Stop"
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Get-Command unity -ErrorAction SilentlyContinue)) { throw "Unity CLI is required." }
$request = (& unity command eval --caller plugin --skill unity-cli --project-path $ProjectPath --format json 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); return true;') | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $request.success -or -not $request.data.result.success) { throw "Editor refresh request failed." }
$deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
$since = $null
while ([DateTime]::UtcNow -lt $deadline) {
    $ready = $false
    try {
        $reply = (& unity command editor_status --caller plugin --skill unity-cli --project-path $ProjectPath --format json) | ConvertFrom-Json
        $state = $reply.data.result
        $ready = $LASTEXITCODE -eq 0 -and $reply.success -and $state.status -eq "ready" -and -not $state.compiling -and -not $state.domainReloadInProgress
    } catch { $ready = $false }
    if ($ready) {
        if ($null -eq $since) { $since = [DateTime]::UtcNow }
        if (([DateTime]::UtcNow - $since).TotalMilliseconds -ge $StableMs) {
            Write-Output "REFRESH_STABLE=1"
            return
        }
    } else { $since = $null }
    Start-Sleep -Milliseconds 500
}
throw "Editor did not finish refresh/reload within the deadline."
