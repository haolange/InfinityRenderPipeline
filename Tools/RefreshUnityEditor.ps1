# Refresh one explicitly selected running Editor through Unity's public AssetDatabase and CompilationPipeline APIs,
# then fail unless every package assembly in Library/ScriptAssemblies is newer than its sources.
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "../../.."),
    [ValidateRange(0, 60000)][int]$StableMs = 4000,
    [ValidateRange(1, 600000)][int]$TimeoutMs = 180000
)
$ErrorActionPreference = "Stop"
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Get-Command unity -ErrorAction SilentlyContinue)) { throw "Unity CLI is required." }
# A held assembly-reload lock keeps compiled assemblies out of Library/ScriptAssemblies: refuse to refresh under one.
$lock = $null
try { $lock = (& unity command eval --caller plugin --skill unity-cli --project-path $ProjectPath --format json 'var canReload = typeof(UnityEditor.EditorApplication).GetMethod("CanReloadAssemblies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static); if (canReload != null && !(bool)canReload.Invoke(null, null)) throw new System.InvalidOperationException("RELOAD_LOCKED"); return true;') | ConvertFrom-Json } catch { $lock = $null }
if ($null -eq $lock -or -not $lock.success -or -not $lock.data.result.success) {
    throw "RELOAD_LOCKED=1: an assembly-reload lock is held. Run infinity_reload_status, cancel its holder, then refresh again."
}
$request = (& unity command eval --caller plugin --skill unity-cli --project-path $ProjectPath --format json 'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceUpdate); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return true;') | ConvertFrom-Json
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
            $python = if (Get-Command python3 -ErrorAction SilentlyContinue) { "python3" } else { "python" }
            & $python (Join-Path $PSScriptRoot "VerifyScriptAssemblies.py") $ProjectPath (Join-Path $PSScriptRoot "..")
            if ($LASTEXITCODE -ne 0) { throw "A package assembly is older than its sources: the Editor did not compile them." }
            Write-Output "REFRESH_STABLE=1"
            return
        }
    } else { $since = $null }
    Start-Sleep -Milliseconds 500
}
throw "Editor did not finish refresh/reload within the deadline."
