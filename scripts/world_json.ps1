Param(
    [string]$File = "$PSScriptRoot\..\world.regions.json",
    [string]$Mode = "up",
    [string]$Opt = ""
)

if (-not (Test-Path $File)) {
    Write-Host "JSON de regioes nao encontrado: $File"
    exit 1
}

$cfg = Get-Content -Raw $File | ConvertFrom-Json
$image = if ($cfg.image) { $cfg.image } else { 'igormendonca/world:latest' }
$nsDefault = if ($cfg.namespace) { $cfg.namespace } else { 'creature-realms' }

if ($Mode -eq 'up' -and $Opt -ne '--skip-build') {
    $dockerfile = Join-Path $PSScriptRoot "..\World\Dockerfile"
    $repoRoot = Join-Path $PSScriptRoot ".."
    if (Test-Path $dockerfile) {
        Write-Host "Construindo e enviando imagem: $image"
        docker build -t $image -f $dockerfile $repoRoot
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        docker push $image
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

foreach ($r in $cfg.regions) {
    $name = $r.name
    $minx = [int]$r.minX
    $maxx = [int]$r.maxX
    $miny = [int]$r.minY
    $maxy = [int]$r.maxY
    $east = if ($r.neighbors -and $r.neighbors.east) { "world-$($r.neighbors.east):9090" }  else { "" }
    $west = if ($r.neighbors -and $r.neighbors.west) { "world-$($r.neighbors.west):9090" }  else { "" }
    $north = if ($r.neighbors -and $r.neighbors.north) { "world-$($r.neighbors.north):9090" } else { "" }
    $south = if ($r.neighbors -and $r.neighbors.south) { "world-$($r.neighbors.south):9090" } else { "" }

    $applyPath = Join-Path $PSScriptRoot "world_apply.ps1"
    if ($Mode -eq 'up') {
        & $applyPath -Name $name -Namespace $nsDefault -Image $image -MinX $minx -MaxX $maxx -MinY $miny -MaxY $maxy -East $east -West $west -North $north -South $south
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    else {
        $dply = "world-$name"
        $ns = $nsDefault
        Write-Host "Removendo recursos: $dply ..."
        kubectl delete deployment $dply -n $ns --ignore-not-found
        kubectl delete service $dply -n $ns --ignore-not-found
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}

exit 0
