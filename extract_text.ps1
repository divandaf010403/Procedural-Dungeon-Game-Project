$file = "binus_article.html"
if (Test-Path $file) {
    $content = Get-Content $file -Raw
    $plainText = [regex]::Replace($content, '<[^>]+>', ' ')
    $plainText = [regex]::Replace($plainText, '\s+', ' ')
    Write-Output $plainText.Substring(0, [Math]::Min(15000, $plainText.Length))
} else {
    Write-Output "File not found. Downloading first..."
    Invoke-WebRequest -Uri "https://socs.binus.ac.id/game/2026/05/10/sekali-klik-langsung-jadi-kota-developer-ini-buat-procedural-city-generator-di-ue5/" -OutFile $file -UseBasicParsing
    if (Test-Path $file) {
        $content = Get-Content $file -Raw
        $plainText = [regex]::Replace($content, '<[^>]+>', ' ')
        $plainText = [regex]::Replace($plainText, '\s+', ' ')
        Write-Output $plainText.Substring(0, [Math]::Min(15000, $plainText.Length))
    }
}