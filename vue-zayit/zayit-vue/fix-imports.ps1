# Fix remaining import issues

$files = Get-ChildItem -Path "src" -Include "*.ts","*.vue" -Recurse -File

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $original = $content
    
    # Fix relative imports to stores
    $content = $content -replace "from ['""]\.\.\/stores\/", "from '@/data/stores/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/stores\/", "from '@/data/stores/"
    
    # Fix relative imports to services
    $content = $content -replace "from ['""]\.\.\/services\/", "from '@/data/services/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/services\/", "from '@/data/services/"
    
    # Fix relative imports to types
    $content = $content -replace "from ['""]\.\.\/types\/", "from '@/data/types/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/types\/", "from '@/data/types/"
    
    # Fix relative imports to composables
    $content = $content -replace "from ['""]\.\.\/composables\/", "from '@/composables/shared/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/composables\/", "from '@/composables/shared/"
    
    # Fix relative imports to utils
    $content = $content -replace "from ['""]\.\.\/utils\/", "from '@/utils/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/utils\/", "from '@/utils/"
    
    # Fix relative imports to config
    $content = $content -replace "from ['""]\.\.\/config\/", "from '@/config/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/config\/", "from '@/config/"
    
    # Fix common components
    $content = $content -replace "from ['""]\.\.\/common\/", "from '@/components/shared/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/common\/", "from '@/components/shared/"
    
    # Fix icon imports - handle both relative and @ paths
    $content = $content -replace "from ['""]@/components/icons/", "from '@/components/shared/icons/"
    $content = $content -replace "from ['""]\.\.\/icons\/", "from '@/components/shared/icons/"
    $content = $content -replace "from ['""]\.\.\/\.\.\/icons\/", "from '@/components/shared/icons/"
    
    if ($content -ne $original) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Updated: $($file.FullName)" -ForegroundColor Green
    }
}

Write-Host "Done!" -ForegroundColor Green
