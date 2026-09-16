param(
    [Parameter(Mandatory = $true)][string]$Zip,
    [string]$Author = 'Max',
    [string]$Community = 'valheim',
    [string[]]$Categories = @(),
    [string]$Site = 'https://hexium.gg'
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$tokenFile = Join-Path $PSScriptRoot 'hexium-token.txt'
if (-not (Test-Path $tokenFile)) { throw "No token file at $tokenFile" }
$token = ([IO.File]::ReadAllText($tokenFile)).Trim()
$H = @{ Authorization = "Bearer $token" }
$bytes = [IO.File]::ReadAllBytes($Zip); $name = [IO.Path]::GetFileName($Zip)
"uploading $name ($($bytes.Length) bytes) to $Site as $Author"
$init = Invoke-RestMethod -Method Post -Uri "$Site/api/experimental/usermedia/initiate-upload/" -Headers $H -ContentType 'application/json' -Body (@{ filename = $name; file_size_bytes = $bytes.Length } | ConvertTo-Json) -TimeoutSec 60
$uuid = $init.user_media.uuid
$parts = @()
foreach ($u in $init.upload_urls) {
    $chunk = New-Object byte[] $u.length
    [Array]::Copy($bytes, [int64]$u.offset, $chunk, 0, [int]$u.length)
    $resp = Invoke-WebRequest -Method Put -Uri $u.url -Body $chunk -ContentType 'application/octet-stream' -TimeoutSec 300 -UseBasicParsing
    $etag = "$($resp.Headers['ETag'])".Trim('"'); if (-not $etag) { throw "part $($u.part_number) returned no ETag" }
    $parts += @{ ETag = $etag; PartNumber = [int]$u.part_number }
}
$null = Invoke-RestMethod -Method Post -Uri "$Site/api/experimental/usermedia/$uuid/finish-upload/" -Headers $H -ContentType 'application/json' -Body (@{ parts = $parts } | ConvertTo-Json -Depth 4) -TimeoutSec 120
$meta = @{ author_name = $Author; communities = @($Community); categories = @($Categories); has_nsfw_content = $false; upload_uuid = $uuid; community_categories = @{ $Community = @($Categories) } }
try {
    $r = Invoke-RestMethod -Method Post -Uri "$Site/api/experimental/submission/submit/" -Headers $H -ContentType 'application/json' -Body ($meta | ConvertTo-Json -Depth 5) -TimeoutSec 300
} catch {
    $body = ''; try { $body = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch {}
    throw "submit failed: $($_.Exception.Message) $body"
}
$v = $r.package_version
"published: $($v.namespace)/$($v.name) $($v.version_number)  ->  $($v.download_url)"
$r | ConvertTo-Json -Depth 6
