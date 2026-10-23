# 检查创意工坊描述文件的 UTF-8 字节数（Steam 上限 8000 字节，BBCode 也计入）
# 用法: powershell -File Workshop\check_description_size.ps1

$limit = 8000
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = Get-ChildItem $root -Filter "Description*.txt" | Sort-Object Name

$fail = 0
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $pct = [math]::Round($bytes.Length / $limit * 100, 1)
    $status = "OK"
    if ($bytes.Length -gt $limit) { $status = "TOO LONG"; $fail++ }
    "{0,-28} {1,6} bytes  {2,5}% of limit  {3}" -f $f.Name, $bytes.Length, $pct, $status
}

if ($fail -gt 0) {
    Write-Host "$fail file(s) exceed the $limit byte limit." -ForegroundColor Red
    exit 1
}
Write-Host "All description files fit within $limit bytes." -ForegroundColor Green
exit 0
