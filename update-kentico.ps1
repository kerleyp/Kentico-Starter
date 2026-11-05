<#
.SYNOPSIS
    Backs up the Kentico database, project folder, and assets folder, then
    runs the Xperience by Kentico package/database update.

.NOTES
    Kentico's own --kxp-update command only shows a confirmation PROMPT
    reminding you to back up -- it does not create a backup for you. This
    script does the actual backup work first, then runs the update with
    --skip-confirmation since a real backup already exists by that point.

    Adjust the CONFIG block below to match your environment before running.
#>

# ---------------- CONFIG -- edit these for your environment ----------------
$dbName       = "kentico"                          # SQL Server database name
$sqlInstance  = "localhost"                         # e.g. "localhost\SQLEXPRESS" if using a named instance
$projectPath  = "C:\Users\pkerley\Documents\Kentico" # project root (contains Program.cs, .csproj)
$assetsPath   = "$projectPath\assets"                # only zipped separately if it lives OUTSIDE $projectPath
$backupRoot   = "C:\KenticoBackups"                  # where timestamped backup folders get created
# -----------------------------------------------------------------------------

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupFolder = Join-Path $backupRoot $timestamp
New-Item -ItemType Directory -Path $backupFolder -Force | Out-Null

Write-Host "=== Backing up to $backupFolder ===" -ForegroundColor Cyan

# 1. Database backup (requires sqlcmd on PATH -- ships with SQL Server / SSMS tools)
$dbBackupFile = Join-Path $backupFolder "$dbName.bak"
Write-Host "Backing up database '$dbName' to $dbBackupFile ..."
sqlcmd -S $sqlInstance -Q "BACKUP DATABASE [$dbName] TO DISK = N'$dbBackupFile' WITH INIT, COMPRESSION"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Database backup FAILED (exit code $LASTEXITCODE). Stopping -- do not proceed to update." -ForegroundColor Red
    exit 1
}

# 2. Project folder backup (zip). If $assetsPath is inside $projectPath, this
#    already covers it -- the separate assets zip below only matters if your
#    assets live somewhere outside the project folder (e.g. a mapped network
#    path or separate storage volume).
$projectZip = Join-Path $backupFolder "project.zip"
Write-Host "Zipping project folder to $projectZip ..."
Compress-Archive -Path $projectPath -DestinationPath $projectZip -CompressionLevel Optimal

# 3. Assets folder backup (only if it's NOT already inside $projectPath)
if ((Test-Path $assetsPath) -and (-not $assetsPath.StartsWith($projectPath, [System.StringComparison]::OrdinalIgnoreCase))) {
    $assetsZip = Join-Path $backupFolder "assets.zip"
    Write-Host "Zipping assets folder to $assetsZip ..."
    Compress-Archive -Path $assetsPath -DestinationPath $assetsZip -CompressionLevel Optimal
}

Write-Host "=== Backup complete ===" -ForegroundColor Green

# ---------------- Run the update ----------------
Write-Host "=== Building project ===" -ForegroundColor Cyan
Push-Location $projectPath
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED. Stopping -- do not proceed to update." -ForegroundColor Red
    Pop-Location
    exit 1
}

Write-Host "=== Running Xperience database/file update ===" -ForegroundColor Cyan
dotnet run --no-build -- --kxp-update --skip-confirmation
$updateExitCode = $LASTEXITCODE
Pop-Location

if ($updateExitCode -ne 0) {
    Write-Host "Update command exited with code $updateExitCode. Check output above -- your backup is at $backupFolder if you need to restore." -ForegroundColor Yellow
} else {
    Write-Host "=== Update finished. Backup preserved at $backupFolder ===" -ForegroundColor Green
}
