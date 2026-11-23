$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$base = "http://localhost:5052"
$out  = "d:\New folder (2)\SRC"

function Check($label, $value) { Write-Output ("{0,-36}: {1}" -f $label, $value) }

# Grab a real user id from the API.
$api = Invoke-RestMethod -Uri "http://localhost:5051/api/v1/users?page=1&pageSize=1" -TimeoutSec 25
$id = $api.items[0].id
Write-Output ("using user id: {0}" -f $id)
Write-Output ""

$r = Invoke-WebRequest -Uri "$base/Users/Details/$id" -UseBasicParsing -TimeoutSec 25
$html = $r.Content
[System.IO.File]::WriteAllText("$out\p-details-real.html", $html, [System.Text.Encoding]::UTF8)

Write-Output "---- DETAILS page (real user) ----"
Check "status"                     ($r.StatusCode)
Check "hero gradient band"         ($html -match 'bg-brand-gradient')
Check "avatar rendered"            ($html -match 'avatar w-20')
Check "personal info section"      ($html -match 'detail-grid')
Check "detail-label count"         ([regex]::Matches($html, 'detail-label').Count)
Check "edit button"                ($html -match 'asp-route-id|/Users/Edit/')
Check "delete button"              ($html -match 'data-confirm-delete')
Check "documents section"          ($html -match 'user.IdentityDocuments.Count')
Check "no empty-state"             (-not ($html -match 'Chưa có người dùng nào'))
Check "name present (decoded)"     ([System.Net.WebUtility]::HtmlDecode($html).Contains($api.items[0].fullName))
Check "email present"              ($html.Contains($api.items[0].email))
Check "phone present"              ($html.Contains($api.items[0].phoneNumber))
Check "created date rendered"      ($html -match 'Ngày tạo')
Check "updated date rendered"      ($html -match 'Cập nhật lần cuối')
