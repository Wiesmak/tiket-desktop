param([switch]$Upstream)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $root 'contracts/snapshot.json') -Raw | ConvertFrom-Json
$files = $manifest.files.PSObject.Properties
foreach ($file in $files) {
    $local = Join-Path $root ('contracts/upstream/' + $file.Name)
    $hash = (Get-FileHash -LiteralPath $local -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $file.Value) { throw "Local snapshot changed: $($file.Name)" }
}
if ($Upstream) {
    $head = (Invoke-RestMethod 'https://api.github.com/repos/Wiesmak/tiket/commits/master').sha
    Write-Output "Pinned: $($manifest.commit); upstream master: $head"
    foreach ($file in $files) {
        $temp = [IO.Path]::GetTempFileName()
        try {
            $url = "https://raw.githubusercontent.com/Wiesmak/tiket/$head/tiket-contracts/src/main/proto/$($file.Name)"
            Invoke-WebRequest $url -OutFile $temp
            $hash = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne $file.Value) { throw "Upstream contract differs: $($file.Name). Review manually; nothing replaced." }
        } finally { Remove-Item -LiteralPath $temp }
    }
    if ($head -ne $manifest.commit) { throw 'Upstream moved; contracts match, but review the new commit before updating the pin.' }
}
Write-Output 'Four contract checksums match. No files replaced.'
