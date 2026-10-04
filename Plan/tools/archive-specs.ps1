<#
.SYNOPSIS
    Creates encrypted, verifiable archives of the local-only Doc/, Archived/ and
    Plan/sessions/ Git repositories.

.DESCRIPTION
    Farm Basics keeps its specification set in local-only Git repositories with no
    remote configured, so that no third party (GitHub included) can be compelled
    or breached to disclose the documents. The trade-off is that there is no hosted
    copy, so durability depends on this script producing offsite archives.

    For each repository this script:
      1. Refuses to run if a remote is configured (guards against accidental publish).
      2. Refuses to run if the working tree is dirty (an archive of uncommitted work
         is not recoverable, which defeats the purpose).
      3. Writes a full git bundle of every ref, using a native redirect so the
         binary payload is never passed through PowerShell text handling.
      4. Verifies the bundle with 'git bundle verify' before encrypting.
      5. Encrypts it with age, using an X25519 keypair rather than a passphrase.
      6. Deletes the plaintext bundle and prints the SHA-256 of the ciphertext.

    Why a keypair and not a passphrase: age's '-p' reads the passphrase from an
    interactive console only. Piping it on stdin does not work - age blocks
    indefinitely - and age 1.3.2 has no '--passphrase-file' flag. An identity file
    is therefore the only non-interactive option age offers, and it is also the
    better design: the public key can be recorded and shared, while only the holder
    of the private key can decrypt.

    The plaintext bundle exists only briefly. It is written to a directory whose
    ACL is stripped down to the current user and SYSTEM, and removed in a finally
    block. It is a complete restorable copy of the specification set, so leaving
    it behind would defeat the encryption.

.PARAMETER Destination
    Directory to write encrypted archives into. Defaults to
    %USERPROFILE%\Farm-Basics-Archives. Copy the result to removable or offsite
    media; this script does not move it for you.

.PARAMETER IdentityFile
    Path to the age identity file holding the secret key. Defaults to
    %LOCALAPPDATA%\Farm-Basics\archive-identity.txt. If it does not exist the
    script prints the exact age-keygen command to create it and exits.

.PARAMETER KeepPlaintextBundle
    Retain the unencrypted .bundle. Off by default, for the reason above.

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
    [string] $Destination      = (Join-Path $env:USERPROFILE 'Farm-Basics-Archives'),
    [string] $IdentityFile     = (Join-Path $env:LOCALAPPDATA 'Farm-Basics\archive-identity.txt'),
    [switch] $KeepPlaintextBundle
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

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

# Several git subcommands report success on stderr - 'git bundle verify' prints
# "<path> is okay" there and exits 0. With $ErrorActionPreference = 'Stop' that
# stderr line becomes a terminating NativeCommandError, so a successful command
# looks like a failure. Every native call therefore goes through here, which
# captures both streams and judges success by exit code alone.
function Invoke-Native {
    param([string] $FilePath, [string[]] $Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $FilePath @Arguments 2>&1
        $code   = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    return [pscustomobject]@{
        Output   = @($output | ForEach-Object { "$_" })
        ExitCode = $code
    }
}

function Resolve-AgeTool {
    param([string] $ExeName)

    $onPath = Get-Command $ExeName -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    # winget installs portable packages into a versioned directory and relies on the
    # persisted user PATH, which is not visible until a new shell starts.
    $packages = Get-EnvSafe 'LOCALAPPDATA'
    if ($packages) {
        $hit = Get-ChildItem (Join-Path $packages 'Microsoft\WinGet\Packages') `
                            -Filter $ExeName -Recurse -ErrorAction SilentlyContinue |
                            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

# Creates a directory that only the current user and SYSTEM can read. Used for the
# transient plaintext bundle.
function New-LockedDirectory {
    param([string] $Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null

    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe $Path /inheritance:r /grant:r "${user}:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' | Out-Null

    return $Path
}

function Assert-RepoIsLocalOnly {
    param([string] $RepoPath, [string] $Label)

    $r = Invoke-Native 'git' @('-C', $RepoPath, 'remote')
    if ($r.ExitCode -ne 0) { throw "Not a Git repository: $RepoPath" }
    if ($r.Output.Count -gt 0) {
        throw ("{0} has a remote configured ({1}). This script only archives local-only " +
               "repositories. Remove the remote first." -f $Label, ($r.Output -join ', '))
    }
    Write-Ok "$Label has no remote configured (local-only)"
}

function Assert-RepoIsClean {
    param([string] $RepoPath, [string] $Label)

    $r = Invoke-Native 'git' @('-C', $RepoPath, 'status', '--porcelain')
    if ($r.ExitCode -ne 0) { throw "git status failed in $Label" }
    if ($r.Output.Count -gt 0) {
        throw ("{0} has {1} uncommitted change(s). Commit them before archiving, otherwise " +
               "they are absent from the bundle and would be lost." -f $Label, $r.Output.Count)
    }
    Write-Ok "$Label working tree is clean"
}

function New-BundleFor {
    param([string] $RepoPath, [string] $Label, [string] $BundlePath)

    # 'git bundle create -' writes the bundle to stdout. The redirect is performed by
    # cmd.exe rather than PowerShell, because piping binary through the PowerShell
    # pipeline would decode and re-encode it.
    $cmdLine = 'git -C "{0}" bundle create - --all > "{1}"' -f $RepoPath, $BundlePath
    $create  = Invoke-Native 'cmd.exe' @('/c', $cmdLine)
    if ($create.ExitCode -ne 0) {
        throw "git bundle create failed for ${Label}: $($create.Output -join ' ')"
    }
    if (-not (Test-Path $BundlePath)) { throw "git bundle produced no file for $Label" }

    $verify = Invoke-Native 'git' @('-C', $RepoPath, 'bundle', 'verify', $BundlePath)
    if ($verify.ExitCode -ne 0) {
        throw ("git bundle verify failed for {0}: {1}" -f $Label, ($verify.Output -join ' '))
    }

    # @() is required: a pipeline returning a single match yields a scalar, and a
    # scalar has no .Count property under Set-StrictMode.
    $complete = @($verify.Output | Where-Object { $_ -match 'complete history' }).Count -gt 0
    if (-not $complete) {
        throw ("{0}: bundle did not report a complete history, so it would not be a " +
               "faithful archive. Refusing to record it." -f $Label)
    }

    $commits = (Invoke-Native 'git' @('-C', $RepoPath, 'rev-list', '--all', '--count')).Output -join ''
    $sizeMb  = [math]::Round((Get-Item $BundlePath).Length / 1MB, 2)
    Write-Ok ("{0} bundle verified, complete history: {1} commit(s), {2} MB" -f $Label, $commits, $sizeMb)
}

function Assert-NotRepositoryRoot {
    param([string] $Path, [string] $Label)

    # 'git bundle create' run at a repository root refuses a destination inside that
    # same repository, because git would treat the bundle as an object to pack.
    $root = (Invoke-Native 'git' @('-C', $Path, 'rev-parse', '--show-toplevel')).Output -join ''
    if ($root -and $Path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw ("{0} lives inside the repository at {1}, whose bundles must be written " +
               "outside it. Choose another location." -f $Label, $root)
    }
}

# ---------------------------------------------------------------------------

$scratch = $null
try {
    Write-Step 'Farm Basics - encrypted spec archive'

    # ---- toolchain -------------------------------------------------------
    $ageExe    = Resolve-AgeTool 'age.exe'
    $keygenExe = Resolve-AgeTool 'age-keygen.exe'
    if (-not $ageExe -or -not $keygenExe) {
        # Note: 'throw (' would be parsed as invoking a function named throw, so the
        # message is assembled in a variable instead of inline.
        $msg = 'age not found. Install it with:  winget install --id FiloSottile.age -e' +
               ' then open a new shell.'
        throw $msg
    }
    $ageVersion = (Invoke-Native $ageExe @('--version')).Output -join ' '
    Write-Ok "age $ageVersion"

    # ---- identity --------------------------------------------------------
    if (-not (Test-Path $IdentityFile)) {
        $dir = Split-Path -Parent $IdentityFile
        New-LockedDirectory -Path $dir | Out-Null
        $msg = "No age identity file at $IdentityFile. Create one with:" + "`n" +
               ('  age-keygen -o "' + $IdentityFile + '"') + "`n" +
               'age-keygen prints the public key and writes the secret key to that file.' + "`n" +
               'Record the public key in your password manager alongside the archives, and keep ' +
               'a copy of the secret key somewhere separate. Without it the archives cannot be ' +
               'decrypted.'
        throw $msg
    }

    $publicKey = ((Invoke-Native $keygenExe @('-y', $IdentityFile)).Output | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $publicKey) { throw "Could not read a public key from $IdentityFile" }
    Write-Ok "recipient $publicKey"

    # ---- destination -----------------------------------------------------
    if (-not (Test-Path $Destination)) {
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        Write-Ok "created destination $Destination"
    }

    $destFull = (Resolve-Path $Destination).Path
    if ($destFull.StartsWith($script:RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Destination is inside the repository ($script:RepoRoot). Choose a location outside it."
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $scratch = New-LockedDirectory -Path (Join-Path ([System.IO.Path]::GetTempPath()) "fb-archive-$stamp")

    # ---- archive ---------------------------------------------------------
    $results = @()
    foreach ($target in $script:ArchiveOf) {

        $name     = $target.Label
        $repoPath = Join-Path $script:RepoRoot $target.Path
        if (-not (Test-Path (Join-Path $repoPath '.git'))) {
            throw "'$($target.Path)' is not a Git repository at $repoPath"
        }

        Write-Step "Archiving $name"
        Assert-RepoIsLocalOnly -RepoPath $repoPath -Label $name
        Assert-RepoIsClean    -RepoPath $repoPath -Label $name
        Assert-NotRepositoryRoot -Path $repoPath -Label $name

        $plain  = Join-Path $scratch "$name.bundle"
        $cipher = Join-Path $destFull "farm-basics-$name-$stamp.bundle.age"

        try {
            New-BundleFor -RepoPath $repoPath -Label $name -BundlePath $plain

            $enc = Invoke-Native $ageExe @('-r', $publicKey, '-o', $cipher, $plain)
            if ($enc.ExitCode -ne 0 -or -not (Test-Path $cipher)) {
                throw "age encryption failed for ${name}: $($enc.Output -join ' ')"
            }

            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $cipher).Hash
            Write-Ok ("encrypted -> {0} ({1} MB)" -f $cipher, [math]::Round((Get-Item $cipher).Length / 1MB, 2))
            Write-Ok "SHA-256 $hash"

            $results += [pscustomobject]@{
                Repo       = $name
                File       = $cipher
                Bytes      = (Get-Item $cipher).Length
                Sha256     = $hash
                Recipient  = $publicKey
                Commits    = (Invoke-Native 'git' @('-C', $repoPath, 'rev-list', '--all', '--count')).Output -join ''
            }
        }
        finally {
            # The plaintext bundle is a complete restorable copy of the documents.
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

    # ---- manifest --------------------------------------------------------
    $manifest = Join-Path $destFull "farm-basics-archive-$stamp.manifest.csv"
    $results | Export-Csv -Path $manifest -NoTypeInformation -Encoding UTF8
    Write-Ok "manifest -> $manifest"

    Write-Step 'Done'
    Write-Host "  Encrypted archives are in: $destFull"
    Write-Host '  Copy them to removable or offsite media. This script does not move them for you.'
    Write-Host '  To restore, decrypt with the age identity, then clone from the bundle:'
    Write-Host '      age -d -i <identity> -o doc.bundle farm-basics-doc-<stamp>.bundle.age'
    Write-Host '      git clone doc.bundle <target>'
    Write-Host '  Verify the ciphertext against the SHA-256 in the manifest before trusting it.'
    Write-Host ''
    Write-Warn2 "Back up the age secret key separately. The archives are unrecoverable without it."
}
finally {
    if ($scratch -and (Test-Path $scratch)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}