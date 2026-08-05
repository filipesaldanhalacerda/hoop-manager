param(
    [string]$Version = "1.51.2"
)

$ErrorActionPreference = "Stop"

# Define path variables.
# Reference: https://github.com/hoophq/hoop/releases
$hoopDownloadUrl = "https://releases.hoop.dev/release/$Version/hoop_${Version}_Windows_x86_64.tar.gz"
$hoopDir = Join-Path $HOME "hoop"
$hoopTempDir = Join-Path $hoopDir "tmp_install"
$hoopTarFile = Join-Path $hoopTempDir "hoop_${Version}_Windows_x86_64.tar.gz"
$hoopExe = Join-Path $hoopDir "hoop.exe"
$hoopExtractedExe = Join-Path $hoopTempDir "hoop.exe"

function Test-PathEntry {
    param(
        [string]$PathValue,
        [string]$Directory
    )

    $normalizedDirectory = $Directory.TrimEnd('\')
    return @(($PathValue -split ';') | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and $_.Trim().TrimEnd('\') -ieq $normalizedDirectory
    }).Count -gt 0
}

try {
    # Create the hoop directory if it does not exist.
    if (-not (Test-Path -LiteralPath $hoopDir)) {
        New-Item -Path $hoopDir -ItemType Directory | Out-Null
    }

    # Recreate the temporary directory for a clean installation.
    if (Test-Path -LiteralPath $hoopTempDir) {
        Remove-Item -LiteralPath $hoopTempDir -Recurse -Force
    }
    New-Item -Path $hoopTempDir -ItemType Directory | Out-Null

    # Download the archive to the temporary directory.
    Write-Host "Downloading Hoop $Version..."
    Invoke-WebRequest -Uri $hoopDownloadUrl -OutFile $hoopTarFile

    # Extract the archive to the temporary directory.
    tar -xf $hoopTarFile -C $hoopTempDir
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to extract $hoopTarFile. tar exited with code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $hoopExtractedExe)) {
        throw "Expected hoop.exe was not found after extraction."
    }

    # Move the executable to the hoop directory.
    Move-Item -LiteralPath $hoopExtractedExe -Destination $hoopExe -Force

    # Add hoop to the user PATH.
    $envPath = [System.Environment]::GetEnvironmentVariable("PATH", "User")
    if (-not (Test-PathEntry -PathValue $envPath -Directory $hoopDir)) {
        if ([string]::IsNullOrWhiteSpace($envPath)) {
            $newPath = $hoopDir
        }
        else {
            $newPath = "$envPath;$hoopDir"
        }

        [System.Environment]::SetEnvironmentVariable("PATH", $newPath, "User")
        if (-not (Test-PathEntry -PathValue $env:PATH -Directory $hoopDir)) {
            if ([string]::IsNullOrWhiteSpace($env:PATH)) {
                $env:PATH = $hoopDir
            }
            else {
                $env:PATH = "$env:PATH;$hoopDir"
            }
        }

        Write-Host "Hoop added to the user PATH."
    }
    else {
        Write-Host "Hoop is already in the user PATH."
    }

    Write-Host "Hoop $Version installed successfully!"
    Write-Host "Run 'hoop version' in your terminal to verify the installation."
}
catch {
    Write-Host "An error occurred during installation: $_"
    exit 1
}
finally {
    try {
        # Remove temporary installation files.
        if (Test-Path -LiteralPath $hoopTempDir) {
            Remove-Item -LiteralPath $hoopTempDir -Recurse -Force -ErrorAction Stop
        }
    }
    catch {
        Write-Warning "Temporary installation files could not be removed from ${hoopTempDir}: $_"
    }
}
