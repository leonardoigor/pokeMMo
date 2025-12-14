Param(
    [string]$File = "$PSScriptRoot\..\world.regions.json",
    [int]$BasePort = 9100,
    [int]$HttpBasePort = 18000,
    [switch]$Stop = $false,
    [string]$PidFile = "$env:TEMP\world_portforward.pids"
)

if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    Write-Host "kubectl nao encontrado no PATH."
    exit 1
}

if ($Stop) {
    if (Test-Path $PidFile) {
        $pids = Get-Content -Path $PidFile | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
        foreach ($pid in $pids) {
            Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
        }
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    }
    exit 0
}

if (-not (Test-Path $File)) {
    Write-Host "JSON de regioes nao encontrado: $File"
    exit 1
}

$cfg = Get-Content -Raw $File | ConvertFrom-Json
$ns = if ($cfg.namespace) { $cfg.namespace } else { 'creature-realms' }
$baseFromJson = if ($cfg.basePort) { [int]$cfg.basePort } else { $BasePort }
$image = if ($cfg.image) { $cfg.image } else { 'igormendonca/world:latest' }

$i = 0
$mappings = @()
foreach ($r in $cfg.regions) {
    $idx = $i
    $name = $r.name
    $localPort = if ($r.tcpPort) { [int]$r.tcpPort } else { $baseFromJson + $idx }
    $localHttp = $HttpBasePort + $idx
    $i++
    $svcName = ("world-{0}" -f $name)
    if ([string]::IsNullOrWhiteSpace($name)) { Write-Host "Aviso: nome de regiao vazio, pulando."; continue }
    if ([string]::IsNullOrWhiteSpace($svcName)) { Write-Host "Aviso: nome de service vazio, pulando."; continue }
    $svcExists = $false
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $null = kubectl get service $svcName -n $ns --no-headers 2>$null
        if ($?) { $svcExists = $true; break }
        Start-Sleep -Milliseconds 250
    }
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $localPort)
    try {
        $listener.Start()
        $listener.Stop()
    } catch {
        Write-Host "Aviso: porta $localPort em uso, pulando esta regiao."
        continue
    }
    $httpOk = $true
    try {
        $httpListener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $localHttp)
        $httpListener.Start()
        $httpListener.Stop()
    } catch {
        Write-Host "Aviso: porta HTTP $localHttp em uso, pulando forward HTTP desta regiao."
        $httpOk = $false
    }
    $target = $null
    if ($svcExists) {
        $target = "service/$svcName"
    } else {
        $minx = [int]$r.minX
        $maxx = [int]$r.maxX
        $miny = [int]$r.minY
        $maxy = [int]$r.maxY
        $east = if ($r.neighbors -and $r.neighbors.east) { "world-$($r.neighbors.east):9090" }  else { "" }
        $west = if ($r.neighbors -and $r.neighbors.west) { "world-$($r.neighbors.west):9090" }  else { "" }
        $north = if ($r.neighbors -and $r.neighbors.north) { "world-$($r.neighbors.north):9090" } else { "" }
        $south = if ($r.neighbors -and $r.neighbors.south) { "world-$($r.neighbors.south):9090" } else { "" }
        $applyPath = Join-Path $PSScriptRoot "world_apply.ps1"
        & $applyPath -Name $name -Namespace $ns -Image $image -MinX $minx -MaxX $maxx -MinY $miny -MaxY $maxy -East $east -West $west -North $north -South $south
        if (-not $?) { 
            $podName = kubectl get pod -l "app.kubernetes.io/name=$svcName" -n $ns -o jsonpath="{.items[0].metadata.name}" 2>$null
            if ($podName) { $target = "pod/$podName" }
        } else {
            for ($attempt = 0; $attempt -lt 8; $attempt++) {
                $null = kubectl get service $svcName -n $ns --no-headers 2>$null
                if ($?) { $target = "service/$svcName"; break }
                Start-Sleep -Milliseconds 250
            }
            if (-not $target) {
                $podName = kubectl get pod -l "app.kubernetes.io/name=$svcName" -n $ns -o jsonpath="{.items[0].metadata.name}" 2>$null
                if ($podName) { $target = "pod/$podName" }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($target)) {
        Write-Host "Aviso: nenhum alvo para port-forward encontrado (service ou pod), pulando regiao $name."
        continue
    }
    Write-Host "Forward: localhost:$localPort -> $target:9090 (ns=$ns)"
    $argsStr = "port-forward $target $localPort:9090 -n $ns"
    $proc = Start-Process -FilePath "kubectl" -ArgumentList $argsStr -WindowStyle Hidden -PassThru
    if ($proc -and $proc.Id) {
        if (-not (Test-Path $PidFile)) { New-Item -Path $PidFile -ItemType File -Force | Out-Null }
        Add-Content -Path $PidFile -Value $proc.Id
    }
    if ($httpOk) {
        Write-Host "Forward: localhost:$localHttp -> $target:8082 (ns=$ns)"
        $httpArgs = "port-forward $target $localHttp:8082 -n $ns"
        $procHttp = Start-Process -FilePath "kubectl" -ArgumentList $httpArgs -WindowStyle Hidden -PassThru
        if ($procHttp -and $procHttp.Id) {
            if (-not (Test-Path $PidFile)) { New-Item -Path $PidFile -ItemType File -Force | Out-Null }
            Add-Content -Path $PidFile -Value $procHttp.Id
        }
    }
    $mappings += [pscustomobject]@{
        name      = $name
        localPort = $localPort
        localHttp = if ($httpOk) { $localHttp } else { 0 }
        service   = "service/$svcName"
        namespace = $ns
    }
}

Write-Host "`nMapa de regioes (localhost):"
$mappings | ForEach-Object {
    if ($_.localHttp -gt 0) {
        Write-Host ("- {0} -> TCP localhost:{1} | HTTP localhost:{2}" -f $_.name, $_.localPort, $_.localHttp)
    } else {
        Write-Host ("- {0} -> TCP localhost:{1}" -f $_.name, $_.localPort)
    }
}

exit 0
