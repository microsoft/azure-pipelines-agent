$ErrorActionPreference = 'Stop'

# Block destructive commands. Extend this list as needed.
$blockedPatterns = @(
    'git reset --hard',
    'rm -rf /',
    'DROP DATABASE',
    'format C:',
    'mkfs'
)

$inputCommand = $args -join ' '
foreach ($pattern in $blockedPatterns) {
    if ($inputCommand -match [regex]::Escape($pattern)) {
        Write-Error "Blocked: destructive pattern detected: $pattern"
        exit 1
    }
}
