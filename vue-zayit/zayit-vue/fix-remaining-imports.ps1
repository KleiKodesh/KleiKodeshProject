# Fix remaining import issues found by TypeScript

Write-Host "Fixing remaining import issues..." -ForegroundColor Green

$files = Get-ChildItem -Path "src" -Include "*.ts","*.vue" -Recurse -File

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Fix composables - add /shared/ path
    $content = $content -replace "from ['""]@/composables/use", "from '@/composables/shared/use"
    
    # Fix icons - add /shared/icons/ path
    $content = $content -replace "from ['""]@/components/icons/", "from '@/components/shared/icons/"
    
    # Fix relative component imports within same feature
    # These need manual review but let's handle common patterns
    
    # Fix imports from ../pages/ (workspace components importing pages)
    $content = $content -replace "from ['""]\./pages/HomePage\.vue", "from '@/components/home/HomePage.vue"
    $content = $content -replace "from ['""]\./pages/WorkspacesPage\.vue", "from '@/components/workspace/WorkspacesPage.vue"
    $content = $content -replace "from ['""]\./pages/BookViewPage\.vue", "from '@/components/book/BookViewPage.vue"
    $content = $content -replace "from ['""]\./pages/PdfViewPage\.vue", "from '@/components/pdf/PdfViewPage.vue"
    $content = $content -replace "from ['""]\./pages/HebrewBooksViewPage\.vue", "from '@/components/hebrew-books/HebrewBooksViewPage.vue"
    $content = $content -replace "from ['""]\./pages/KezayitSearchPage\.vue", "from '@/components/kezayitdb-search/KezayitSearchPage.vue"
    $content = $content -replace "from ['""]\./pages/SettingsPage\.vue", "from '@/components/settings/SettingsPage.vue"
    $content = $content -replace "from ['""]\./pages/HebrewbooksPage\.vue", "from '@/components/hebrew-books/HebrewbooksPage.vue"
    $content = $content -replace "from ['""]\./pages/KezayitOpenFilePage\.vue", "from '@/components/kezayitdb-fs/KezayitOpenFilePage.vue"
    
    # Fix relative imports within components (../)
    $content = $content -replace "from ['""]\.\./AppTile\.vue", "from '@/components/shared/AppTile.vue"
    $content = $content -replace "from ['""]\.\./HebrewbooksListItem\.vue", "from '@/components/hebrew-books/HebrewbooksListItem.vue"
    $content = $content -replace "from ['""]\.\./FsTree\.vue", "from '@/components/kezayitdb-fs/FsTree.vue"
    $content = $content -replace "from ['""]\.\./FsTreeSearch\.vue", "from '@/components/kezayitdb-fs/FsTreeSearch.vue"
    $content = $content -replace "from ['""]\.\./FsCheckedTree\.vue", "from '@/components/kezayitdb-search/FsCheckedTree.vue"
    
    # Fix imports within book feature
    $content = $content -replace "from ['""]\.\./TocTreeView\.vue", "from '@/components/book/TocTreeView.vue"
    $content = $content -replace "from ['""]\.\./LineView\.vue", "from '@/components/book/LineView.vue"
    $content = $content -replace "from ['""]\.\./CommentaryView\.vue", "from '@/components/commentary/CommentaryView.vue"
    $content = $content -replace "from ['""]\.\./LineViewToolbar\.vue", "from '@/components/book/LineViewToolbar.vue"
    
    # Fix imports within settings feature
    $content = $content -replace "from ['""]\.\./ThemePreviewDropdown\.vue", "from '@/components/settings/ThemePreviewDropdown.vue"
    $content = $content -replace "from ['""]\.\./ThemeCreator\.vue", "from '@/components/settings/ThemeCreator.vue"
    $content = $content -replace "from ['""]\.\./ReadingBackgroundDropdown\.vue", "from '@/components/settings/ReadingBackgroundDropdown.vue"
    $content = $content -replace "from ['""]\./ThemeToggleButton\.vue", "from '@/components/settings/ThemeToggleButton.vue"
    $content = $content -replace "from ['""]\.\./DiacriticsDropdownItem\.vue", "from '@/components/settings/DiacriticsDropdownItem.vue"
    $content = $content -replace "from ['""]\./DiacriticsDropdownItem\.vue", "from '@/components/settings/DiacriticsDropdownItem.vue"
    
    # Fix remaining relative imports to data layer
    $content = $content -replace "from ['""]\.\./\.\./stores/", "from '@/data/stores/"
    $content = $content -replace "from ['""]\.\./\.\./services/", "from '@/data/services/"
    $content = $content -replace "from ['""]\.\./\.\./utils/", "from '@/utils/"
    
    # Fix imports in App.vue
    $content = $content -replace "from ['""]\.\/components\/TabControl\.vue", "from '@/components/workspace/TabControl.vue"
    
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Updated: $($file.FullName.Replace($PWD, '.'))" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Fixes applied! Run npm run type-check again." -ForegroundColor Yellow
