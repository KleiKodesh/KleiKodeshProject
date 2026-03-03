# PowerShell script to update all import paths for layered architecture

Write-Host "Starting import path updates..." -ForegroundColor Green

# Get all TypeScript and Vue files
$files = Get-ChildItem -Path "src" -Include "*.ts","*.vue" -Recurse -File

$totalFiles = $files.Count
$currentFile = 0

foreach ($file in $files) {
    $currentFile++
    Write-Host "[$currentFile/$totalFiles] Processing: $($file.FullName.Replace($PWD, '.'))" -ForegroundColor Cyan
    
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Update imports for data layer
    # Stores
    $content = $content -replace "from ['""](\.\./)+stores/", "from '@/data/stores/"
    $content = $content -replace "from ['""]\./stores/", "from '@/data/stores/"
    $content = $content -replace "from ['""]@/stores/", "from '@/data/stores/"
    
    # Services
    $content = $content -replace "from ['""](\.\./)+services/", "from '@/data/services/"
    $content = $content -replace "from ['""]\./services/", "from '@/data/services/"
    $content = $content -replace "from ['""]@/services/", "from '@/data/services/"
    
    # Types
    $content = $content -replace "from ['""](\.\./)+types/", "from '@/data/types/"
    $content = $content -replace "from ['""]\./types/", "from '@/data/types/"
    $content = $content -replace "from ['""]@/types/", "from '@/data/types/"
    
    # Workers
    $content = $content -replace "from ['""](\.\./)+workers/", "from '@/data/workers/"
    $content = $content -replace "from ['""]\./workers/", "from '@/data/workers/"
    
    # Composables
    $content = $content -replace "from ['""](\.\./)+composables/", "from '@/composables/"
    $content = $content -replace "from ['""]\./composables/", "from '@/composables/"
    
    # Utils
    $content = $content -replace "from ['""](\.\./)+utils/", "from '@/utils/"
    $content = $content -replace "from ['""]\./utils/", "from '@/utils/"
    
    # Config
    $content = $content -replace "from ['""](\.\./)+config/", "from '@/config/"
    $content = $content -replace "from ['""]\./config/", "from '@/config/"
    
    # Components
    $content = $content -replace "from ['""](\.\./)+common/", "from '@/components/shared/"
    $content = $content -replace "from ['""]\./common/", "from '@/components/shared/"
    
    # Icons
    $content = $content -replace "from ['""](\.\./)+icons/", "from '@/components/shared/icons/"
    $content = $content -replace "from ['""]\./icons/", "from '@/components/shared/icons/"
    
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "  Updated" -ForegroundColor Green
    } else {
        Write-Host "  No changes" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Import path updates complete!" -ForegroundColor Green
Write-Host "Run npm run type-check to verify." -ForegroundColor Yellow
