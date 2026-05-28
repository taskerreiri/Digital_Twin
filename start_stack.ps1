# Digital Twin PoC スタック起動
# DTサーバー + PWA配信 + WebGL監視ビュー配信 を一括起動する
# Usage: .\start_stack.ps1 [-Simulate]
param([switch]$Simulate)

$root = $PSScriptRoot
$node = "node"

Write-Host "=== Digital Twin Stack ===" -ForegroundColor Cyan

# 1. DT Server (位置集約 + WS)
Write-Host "[1] DT Server (port 9300)..." -ForegroundColor Yellow
Start-Process $node -ArgumentList "server.js" -WorkingDirectory "$root\server" -WindowStyle Minimized

# 2. PWA 配信 (作業員端末用)
Write-Host "[2] PWA (port 8090)..." -ForegroundColor Yellow
Start-Process $node -ArgumentList "serve.js" -WorkingDirectory "$root\pwa" -WindowStyle Minimized

# 3. WebGL 監視ビュー配信
Write-Host "[3] WebGL monitor (port 8765)..." -ForegroundColor Yellow
Start-Process "python" -ArgumentList "serve-webgl.py" -WorkingDirectory $root -WindowStyle Minimized

Start-Sleep -Seconds 3

# 4. シミュレータ (任意)
if ($Simulate) {
    Write-Host "[4] Equipment simulator (3 entities)..." -ForegroundColor Yellow
    Start-Process $node -ArgumentList "equipment_simulator.js","--entities","3","--interval","500" -WorkingDirectory "$root\server" -WindowStyle Minimized
}

Write-Host ""
Write-Host "=== Endpoints ===" -ForegroundColor Cyan
Write-Host "  監視ビュー (Unity): http://localhost:8765/"
Write-Host "  作業員PWA:          http://localhost:8090/"
Write-Host "  API health:         http://localhost:9300/api/health"
Write-Host ""
Write-Host "停止: Get-Process node,python | Stop-Process" -ForegroundColor DarkGray
