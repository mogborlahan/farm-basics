<#
.SYNOPSIS
    Creates encrypted, verifiable archives of the local-only Doc/ and Archived/ Git repositories.

.DESCRIPTION
    Farm Basics keeps its specification set in local-only Git repositories with no
    remote configured, so that no third party (GitHub included) can be compelled
    or breached to disclose the documents. The trade-off is that there is no hosted
    copy, so durability depends on this script producing offsite archives.

    For each repository this script:
      1. Refuses to run if a remote is configured (guards against accidental publish).
      2. Refuses to run if the working tree is dirty (an archive of uncommitted work
         is not recoverable, which defeats the purpose).
      3. Creates a full git bundle of every ref.
      4. Verifies the bundle with 'git bundle verify'.
      5. Encrypts the bundle (age passphrase if available, else 7-Zip AES-256).
      6. Prints the SHA-256 of the encrypted artefact so it can be checked after restore.

    The passphrase is never written to disk by this script and is not accepted as a
    parameter, so it cannot leak through shell history or this file. Supply it via
    the FB_ARCHIVE_PASSWORD environment variable or the interactive prompt.

.PARAMETER Destination
    Directory to write encrypted archives into. Defaults to
    %USERPROFILE%\Farm-Basics-Archives. Copy the result to removable or offsite
    media; this script does not move it for you.

.PARAMETER KeepPlaintextBundle
    Retain the unencrypted .bundle after encryption. Off by default. A plaintext
    bundle is a complete, restorable copy of the specification set, so leaving it
    next to the ciphertext largely defeats the encryption.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Plan/tools/archive-specs.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Plan/tools/archive-specs.ps1 -Destination E:\Farm-Basics-Archives

.NOTES
    ASCII-only on purpose: PowerShell 5.1 parses .ps1 files as ANSI when there is no
    BOM, so a non-ASCII character in this file would be silently mangled - the same
    failure class as F-04.
#>

[CmdletBinding()]
param(
    [string] $Destination = (Join-Path $env:USERPROFILE 'Farm-Basics-Archives'),
    [switch] $KeepPlaintextBundle
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:RepoRoot  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

# The local-only repositories, each gitignored by the public repo above it.
# 'Path' is relative to the repo root; 'Label' is the filename-safe stem.
$script:ArchiveOf = @(
    @{ Path = 'Doc';           Label = 'doc' }
    @{ Path = 'Archived';      Label = 'archived' }
    @{ Path = 'Plan\sessions'; Label = 'sessions' }
)

function Write-Step { param([string] $Message) Write-Host "==> $Message" }
function Write-Ok   { param([string] $Message) Write-Host "    OK  $Message" }
function Write-Warn2{ param([string] $Message) Write-Host "    !!  $Message" }

# Set-StrictMode 2.0 treats an undefined env var as an error, and ProgramFiles(x86)
# does not exist on a 32-bit host, so every env read goes through this helper.
function Get-EnvSafe {
    param([string] $Name)
    $v = Get-Item -Path "Env:\$Name" -ErrorAction SilentlyContinue
    if ($v) { return $v.Value }
    return $null
}

function Assert-RepoIsLocalOnly {
    param([string] $RepoPath, [string] $Label)

    $remotes = @(git -C $RepoPath remote)
    if ($LASTEXITCODE -ne 0) { throw "Not a Git repository: $RepoPath" }
    if ($remotes.Count -gt 0) {
        throw ("{0} has a remote configured ({1}). This script only archives local-only " +
               "repositories. Remove the remote first." -f $Label, ($remotes -join ', '))
    }
    Write-Ok "$Label has no remote configured (local-only)"
}

function Assert-RepoIsClean {
    param([string] $RepoPath, [string] $Label)

    $dirty = @(git -C $RepoPath status --porcelain)
    if ($dirty.Count -gt 0) {
        throw ("{0} has {1} uncommitted change(s). Commit them before archiving, otherwise " +
               "they are absent from the bundle and would be lost." -f $Label, $dirty.Count)
    }
    Write-Ok "$Label working tree is clean"
}

function New-BundleFor {
    param([string] $RepoPath, [string] $Label, [string] $BundlePath)

    # --all captures every ref under refs/, including branches and tags.
    git -C $RepoPath bundle create $BundlePath --all 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git bundle create failed for $Label" }

    $verify = git -C $RepoPath bundle verify $BundlePath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ("git bundle verify failed for {0}: {1}" -f $Label, ($verify -join ' '))
    }

    $commits = git -C $RepoPath rev-list --all --count
    $sizeMb  = [math]::Round((Get-Item $BundlePath).Length / 1MB, 2)
    Write-Ok ("{0} bundle verified: {1} commit(s), {2} MB" -f $Label, $commits, $sizeMb)
}

function Get-Encryptor {
    $age  = Get-Command 'age' -ErrorAction SilentlyContinue
    $pf   = Get-EnvSafe 'ProgramFiles'
    $pf86 = Get-EnvSafe 'ProgramFiles(x86)'
    $candidates = @(, $pf, , $pf86) | Where-Object { $_ } | ForEach-Object { Join-Path $_ '7-Zip\7z.exe' }
    $sevenZip = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($age)      { return @{ Name = 'age'; Path = $age.Source; Strength = 'age (X25519/scrypt passphrase)' } }
    if ($sevenZip) { return @{ Name = '7zip'; Path = $sevenZip; Strength = '7-Zip AES-256' } }
    return $null
}

function Invoke-Encrypt {
    param([hashtable] $Encryptor, [string] $PlainPath, [string] $CipherPath, [string] $Passphrase)

    switch ($Encryptor.Name) {

        'age' {
            # Passphrase goes in on stdin, so it never appears in the process list.
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName               = $Encryptor.Path
            $psi.Arguments              = '-p -o "{0}" "{1}"' -f $CipherPath, $PlainPath
            $psi.UseShellExecute        = $false
            $psi.RedirectStandardInput  = $true
            $psi.RedirectStandardError   = $true

            $proc = [System.Diagnostics.Process]::Start($psi)
            $proc.StandardInput.WriteLine($Passphrase)
            $proc.StandardInput.Close()
            $err = $proc.StandardError.ReadToEnd()
            $proc.WaitForExit()
            if ($proc.ExitCode -ne 0) { throw "age failed: $err" }
        }

        '7zip' {
            # 7-Zip takes the passphrase only as a command-line argument, so it is
            # briefly visible in the process list. That is a real weakness of this
            # backend and the reason age is preferred when both are installed.
            $proc = Start-Process -FilePath $Encryptor.Path `
                                  -ArgumentList @('a', '-t7z', '-mhe=on', '-mx=9', "-p$Passphrase", "`"$CipherPath`"", "`"$PlainPath`"") `
                                  -NoNewWindow -Wait -PassThru
            if ($proc.ExitCode -ne 0) { throw "7-Zip failed with exit code $($proc.ExitCode)" }
        }
    }
}

function Get-Passphrase {
    $fromEnv = Get-EnvSafe 'FB_ARCHIVE_PASSWORD'
    if ($fromEnv) { return $fromEnv }

    if (-not [Environment]::UserInteractive) {
        throw 'No passphrase available. Set FB_ARCHIVE_PASSWORD in a non-interactive session.'
    }

    $secure = Read-Host 'Passphrase for the spec archives' -AsSecureString
    $bstr   = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try   { return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# ---------------------------------------------------------------------------

try {
    Write-Step 'Farm Basics - encrypted spec archive'

    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        Write-Ok "created destination $Destination"
    }

    # Never write archives inside the repository they archive.
    $destFull = (Resolve-Path $Destination).Path
    if ($destFull.StartsWith($script:RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Destination is inside the repository ($script:RepoRoot). Choose a location outside it."
    }

    $encryptor = Get-Encryptor
    if (-not $encryptor) {
        throw 'No encryptor found. Install age (https://github.com/FiloSottile/age) or 7-Zip, then retry.'
    }
    Write-Ok "encryptor: $($encryptor.Name) - $($encryptor.Strength)"

    $stamp     = Get-Date -Format 'yyyyMMdd-HHmmss'
    $passphrase = Get-Passphrase
    if ([string]::IsNullOrWhiteSpace($passphrase)) { throw 'Empty passphrase; refusing to write an unencrypted archive under an encrypted name.' }

    $results = @()
    foreach ($target in $script:ArchiveOf) {

        $name    = $target.Label
        $repoPath = Join-Path $script:RepoRoot $target.Path
        if (-not (Test-Path (Join-Path $repoPath '.git'))) {
            throw "'$($target.Path)' is not a Git repository at $repoPath"
        }

        Write-Step "Archiving $name"
        Assert-RepoIsLocalOnly -RepoPath $repoPath -Label $name
        Assert-RepoIsClean    -RepoPath $repoPath -Label $name

        $plain = Join-Path $env:TEMP "farm-basics-$name-$stamp.bundle"
        $cipher = Join-Path $destFull "farm-basics-$name-$stamp.bundle.$($encryptor.Name).enc"

        try {
            New-BundleFor -RepoPath $repoPath -Label $name -BundlePath $plain
            Invoke-Encrypt -Encryptor $encryptor -PlainPath $plain -CipherPath $cipher -Passphrase $passphrase

            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cipher).Hash
            $size = [math]::Round((Get-Item $cipher).Length / 1MB, 2)

            Write-Ok ("encrypted -> {0} ({1} MB)" -f $cipher, $size)
            Write-Ok "SHA-256 $hash"

            $results += [pscustomobject]@{
                Repo     = $name
                File     = $cipher
                Bytes    = (Get-Item $cipher).Length
                Sha256   = $hash
                Commits  = (git -C $repoPath rev-list --all --count)
            }
        }
        finally {
            if (Test-Path $plain) {
                if ($KeepPlaintextBundle) {
                    Move-Item -LiteralPath $plain -Destination (Join-Path $destFull "farm-basics-$name-$stamp.bundle") -Force
                    Write-Warn2 "plaintext bundle retained at the destination, as requested"
                } else {
                    Remove-Item -LiteralPath $plain -Force
                }
            }
        }
    }

    # Write the manifest unencrypted: it holds only paths, sizes and digests.
    $manifest = Join-Path $destFull "farm-basics-archive-$stamp.manifest.csv"
    $results | Export-Csv -Path $manifest -NoTypeInformation -Encoding UTF8
    Write-Ok "manifest -> $manifest"

    Write-Step 'Done'
    Write-Host "  Encrypted archives are in: $destFull"
    Write-Host '  Copy them to removable or offsite media. This script does not move them for you.'
    Write-Host '  Verify after restore with the SHA-256 values in the manifest.'
    Write-Host '  Restore with:  git clone <bundle> <target>   (a bundle is a valid git remote source)'
    Write-Host ''
    Write-Warn2 'Record the passphrase in a password manager. There is no recovery if it is lost.'
}
finally {
    if (Get-EnvSafe 'FB_ARCHIVE_PASSWORD') { Remove-Item Env:\FB_ARCHIVE_PASSWORD -ErrorAction SilentlyContinue }
}