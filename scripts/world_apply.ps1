Param(
  [string]$Name,
  [string]$Namespace = "creature-realms",
  [string]$Image = "igormendonca/world:latest",
  [int]$MinX,
  [int]$MaxX,
  [int]$MinY,
  [int]$MaxY,
  [string]$East = "",
  [string]$West = "",
  [string]$North = "",
  [string]$South = "",
  [int]$NodePortTcp = 0,
  [int]$NodePortHttp = 0
)

if ([string]::IsNullOrWhiteSpace($Name)) { Write-Host "Nome vazio"; exit 1 }

$dply = "world-$Name"
$svc = $dply

# Garantir namespace
$nsExists = (kubectl get namespace $Namespace --no-headers 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($nsExists)) {
  Write-Host "Criando namespace: $Namespace ..."
  kubectl create namespace $Namespace
  if ($LASTEXITCODE -ne 0) { Write-Host "Falha ao criar namespace."; exit $LASTEXITCODE }
}

$httpNodeLine = ""
if ($NodePortHttp -gt 0) { $httpNodeLine = "      nodePort: $NodePortHttp" }
$tcpNodeLine = ""
if ($NodePortTcp -gt 0) { $tcpNodeLine = "      nodePort: $NodePortTcp" }

$yaml = @"
apiVersion: apps/v1
kind: Deployment
 metadata:
  name: $dply
  namespace: $Namespace
  labels:
    app.kubernetes.io/name: $dply
    app.kubernetes.io/part-of: creature-realms
    app.kubernetes.io/component: backend
spec:
  replicas: 1
  selector:
    matchLabels:
      app.kubernetes.io/name: $dply
  template:
    metadata:
      labels:
        app.kubernetes.io/name: $dply
        app.kubernetes.io/part-of: creature-realms
        app.kubernetes.io/component: backend
    spec:
      containers:
        - name: world
          image: $Image
          imagePullPolicy: IfNotPresent
          ports:
            - name: http
              containerPort: 8082
            - name: tcp
              containerPort: 9090
              protocol: TCP
          env:
            - name: ASPNETCORE_URLS
              value: http://+:8082
            - name: MAP_DATA_DIR
              value: /app/data
            - name: PLAYER_MOVE_INTERVAL_MS
              value: "200"
            - name: OTEL__Endpoint
              value: http://otel-collector:4318
            - name: Logging__Elasticsearch__ShipTo__NodeUris__0
              value: http://elasticsearch:9200
            - name: Logging__Elasticsearch__Index
              value: dotnet-{0:yyyy.MM.dd}
            - name: REGION_NAME
              value: "$Name"
            - name: REGION_MIN_X
              value: "$MinX"
            - name: REGION_MAX_X
              value: "$MaxX"
            - name: REGION_MIN_Y
              value: "$MinY"
            - name: REGION_MAX_Y
              value: "$MaxY"
            - name: NEIGHBOR_EAST
              value: "$East"
            - name: NEIGHBOR_WEST
              value: "$West"
            - name: NEIGHBOR_NORTH
              value: "$North"
            - name: NEIGHBOR_SOUTH
              value: "$South"
          readinessProbe:
            httpGet:
              path: /healthz
              port: 8082
            initialDelaySeconds: 5
            periodSeconds: 10
          livenessProbe:
            httpGet:
              path: /healthz
              port: 8082
            initialDelaySeconds: 30
            periodSeconds: 10
---
apiVersion: v1
kind: Service
metadata:
  name: $svc
  namespace: $Namespace
  labels:
    app.kubernetes.io/name: $dply
    app.kubernetes.io/part-of: creature-realms
    app.kubernetes.io/component: backend
spec:
  selector:
    app.kubernetes.io/name: $dply
  type: NodePort
  ports:
    - name: http
      port: 8082
      targetPort: 8082
      protocol: TCP
__HTTP_NODEPORT__
    - name: tcp
      port: 9090
      targetPort: 9090
      protocol: TCP
__TCP_NODEPORT__
"@

$yaml = $yaml.Replace("__HTTP_NODEPORT__", $httpNodeLine).Replace("__TCP_NODEPORT__", $tcpNodeLine)
$yaml = $yaml.Replace("`r`n metadata:", "`r`nmetadata:").Replace("`n metadata:", "`nmetadata:")

$tmp = [System.IO.Path]::GetTempFileName()
Set-Content -Path $tmp -Value $yaml -Encoding UTF8
Write-Host "Aplicando recursos: $dply e $svc ..."
Write-Host "YAML gerado:"
Write-Host $yaml
kubectl apply -f $tmp
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-Item $tmp -Force
Write-Host "OK: $dply criado/atualizado."
exit 0
