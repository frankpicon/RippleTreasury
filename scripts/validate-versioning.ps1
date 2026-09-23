$ErrorActionPreference = 'Stop'
$base = 'http://localhost:8080'
$token = Invoke-RestMethod -Uri "$base/identity/realms/ticketing/protocol/openid-connect/token" -Method Post -ContentType 'application/x-www-form-urlencoded' -Body @{client_id='ticketing-cli';grant_type='password';username='admin';password='Demo123!'} -TimeoutSec 30
$headers = @{Authorization="Bearer $($token.access_token)"}
function Check($condition, $message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS: $message"
}
function Request($path, $method='GET', $body=$null, $extra=@{}) {
    $h = $headers.Clone()
    foreach ($key in $extra.Keys) { $h[$key] = $extra[$key] }
    $requestParams = @{Uri="$base$path";Method=$method;Headers=$h;TimeoutSec=30;SkipHttpErrorCheck=$true}
    if ($null -ne $body) { $requestParams.Body = $body | ConvertTo-Json -Depth 10; $requestParams.ContentType='application/json' }
    Invoke-WebRequest @requestParams
}
$v1 = Request '/api/v1/events'
Check ($v1.StatusCode -eq 200) 'Authenticated V1 event list returns 200'
$missingId = [guid]::NewGuid()
foreach ($resource in @('events','events/availability',"events/$missingId", "events/$missingId/availability", "events/$missingId/tickets/$missingId", "reports/events/$missingId/sales")) {
    Check ((Request "/api/$resource").StatusCode -eq 404) "Unversioned $resource returns 404"
}
Check (($v1.Headers['api-supported-versions'] -join ',') -match '1.0') 'V1 reports its supported version'
Check ((Request '/api/v2/events').StatusCode -eq 404) 'Unsupported V2 returns 404'
$anonymous = Invoke-WebRequest -Uri "$base/api/v1/events" -TimeoutSec 30 -SkipHttpErrorCheck
Check ($anonymous.StatusCode -eq 401) 'Anonymous V1 requests return 401'
foreach ($port in 5101,5102,5103) {
    $swagger = Invoke-RestMethod "http://localhost:$port/swagger/v1/swagger.json" -TimeoutSec 30
    Check (@($swagger.paths.PSObject.Properties.Name | Where-Object { $_ -like '/api/v1/*' }).Count -gt 0) "Service $port Swagger exposes concrete V1 paths"
    Check (@($swagger.paths.PSObject.Properties.Name | Where-Object { $_ -notlike '/api/v1/*' }).Count -eq 0) "Service $port Swagger contains only V1 endpoints"
}
$body = @{name="Versioning validation $([guid]::NewGuid().ToString('N').Substring(0,8))";description='Temporary local API validation';venue='Validation';startsAtUtc=[DateTime]::UtcNow.AddDays(30).ToString('o');totalCapacity=5;pricingTiers=@(@{name='General';price=10;capacity=5})}
$created = Request '/api/v1/events' 'POST' $body
Check ($created.StatusCode -eq 201) 'V1 event creation returns 201'
$event = $created.Content | ConvertFrom-Json
$id = $event.id
try {
    $location = [string]($created.Headers.Location | Select-Object -First 1)
    Check ($location -match '/api/v1') 'Created event has a versioned Location'
    if ($location -match '^https?://') { $location = ([uri]$location).PathAndQuery }
    Check ((Request $location).StatusCode -eq 200) 'Event Location can be followed through gateway'
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do { $available = Request "/api/v1/events/$id/availability"; if ($available.StatusCode -eq 200) { break }; Start-Sleep -Seconds 2 } while ([DateTime]::UtcNow -lt $deadline)
    Check ($available.StatusCode -eq 200) 'RabbitMQ event projection reaches ticketing inventory'
    $purchaseBody = @{pricingTierId=$event.pricingTiers[0].id;expectedUnitPrice=10;customerEmail='validation@example.com';quantity=2}
    $key = @{ 'Idempotency-Key'=[guid]::NewGuid().ToString() }
    $purchase = Request "/api/v1/events/$id/tickets" 'POST' $purchaseBody $key
    Check ($purchase.StatusCode -eq 201) 'V1 purchase returns 201'
    $receipt = $purchase.Content | ConvertFrom-Json
    $replay = Request "/api/v1/events/$id/tickets" 'POST' $purchaseBody $key
    Check ($replay.StatusCode -eq 201 -and ($replay.Content | ConvertFrom-Json).purchaseId -eq $receipt.purchaseId) 'V1 retry returns the original purchase'
    Check (($replay.Headers['Idempotency-Replayed'] -join ',') -eq 'true') 'Retry is marked as an idempotent replay'
    $remaining = (Request "/api/v1/events/$id/availability").Content | ConvertFrom-Json
    Check ($remaining.available -eq 3) 'Inventory is decremented exactly once'
    $purchaseLocation = [string]($purchase.Headers.Location | Select-Object -First 1)
    Check ($purchaseLocation -match '/api/v1') 'Purchase Location is versioned'
    if ($purchaseLocation -match '^https?://') { $purchaseLocation = ([uri]$purchaseLocation).PathAndQuery }
    Check ((Request $purchaseLocation).StatusCode -eq 200) 'Purchase Location can be followed through gateway'
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $report = Request "/api/v1/reports/events/$id/sales"
        $sales = if ($report.StatusCode -eq 200) { $report.Content | ConvertFrom-Json } else { $null }
        if ($sales.ticketsSold -eq 2) { break }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    Check ($sales.ticketsSold -eq 2 -and $sales.grossRevenue -eq 20) 'RabbitMQ sales projection reports the purchase exactly once'
    Check ((Request "/api/reports/events/$id/sales").StatusCode -eq 404) 'Unversioned sales report is unavailable'
} finally {
    if ($id) {
        $current = (Request "/api/v1/events/$id").Content | ConvertFrom-Json
        $deleted = Request "/api/v1/events/${id}?version=$($current.version)" 'DELETE'
        Check ($deleted.StatusCode -eq 204) 'Temporary validation event is deleted'
    }
}
