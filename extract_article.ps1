$file = "binus_article.html"
$content = Get-Content $file -Raw

# Try to find main content
if ($content -match '<div[^>]*class="[^"]*wp-content[^"]*"[^>]*>(.*?)</div>') {
    Write-Output "FOUND WP-CONTENT"
    $main = $matches[1]
} elseif ($content -match '<article[^>]*>(.*?)</article>') {
    Write-Output "FOUND ARTICLE"
    $main = $matches[1]
} elseif ($content -match '<main[^>]*>(.*?)</main>') {
    Write-Output "FOUND MAIN"
    $main = $matches[1]
} else {
    Write-Output "Trying entry-content..."
    if ($content -match 'entry-content[^"]*"[^>]*>(.*?)</div>\s*<footer') {
        $main = $matches[1]
    } else {
        $main = $content
    }
}

# Strip tags
$plainText = [regex]::Replace($main, '<script[^>]*>.*?</script>', ' ', 'Singleline')
$plainText = [regex]::Replace($plainText, '<style[^>]*>.*?</style>', ' ', 'Singleline')
$plainText = [regex]::Replace($plainText, '<[^>]+>', ' ')
$plainText = [regex]::Replace($plainText, '&[a-z]+;', ' ')
$plainText = [regex]::Replace($plainText, '\s+', ' ')

Write-Output ""
Write-Output "=== ARTICLE TEXT ==="
Write-Output $plainText