<#
.SYNOPSIS
    Authenticode-signs files with the HTREASURE LTD code-signing certificate (SHA-256, RFC 3161 timestamp).

.DESCRIPTION
    The certificate is read from a .pfx file OUTSIDE the repository (default C:\HTSoftware\signing). Its password
    file holds a Windows DPAPI-protected SecureString (ConvertFrom-SecureString), which only the Windows account that
    created it can decrypt. The password is never shown or written in plain text, and the key is loaded as an
    ephemeral key: nothing is added to the Windows certificate store.

    The current certificate is SELF-SIGNED (CN=HTREASURE LTD). Signed files are tamper-evident, but Windows does not
    trust the certificate unless it is installed as a trusted root and trusted publisher on the computer (see
    docs/CODE-SIGNING.md). Do not describe the files as signed by a trusted certificate authority.

    Used directly, and by Inno Setup (ISCC /Shtsign=...) for Setup and the uninstaller.
#>
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Path,
    [string]$SigningDir = $(if ($env:HT_SIGNING_DIR) { $env:HT_SIGNING_DIR } else { 'C:\HTSoftware\signing' }),
    [string]$TimestampServer = 'http://timestamp.digicert.com',
    [switch]$NoTimestamp
)
$ErrorActionPreference = 'Stop'
$pfx = Join-Path $SigningDir 'HTREASURE-CodeSigning.pfx'
$pwdFile = Join-Path $SigningDir 'HTREASURE-CodeSigning.pfx.password'
if (-not (Test-Path $pfx) -or -not (Test-Path $pwdFile)) { throw "Signing certificate not found in $SigningDir." }

$secure = (Get-Content -Raw $pwdFile).Trim() | ConvertTo-SecureString
$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $secure, $flags)
try {
    if (-not $cert.HasPrivateKey) { throw 'The .pfx has no private key.' }
    if ($cert.NotAfter -lt (Get-Date)) { throw "The signing certificate expired on $($cert.NotAfter)." }
    foreach ($p in ($Path -split ';' | Where-Object { $_ })) {
        $args = @{ FilePath = $p; Certificate = $cert; HashAlgorithm = 'SHA256' }
        if (-not $NoTimestamp) { $args.TimestampServer = $TimestampServer }
        $result = Set-AuthenticodeSignature @args
        # A self-signed certificate is not trusted by Windows, so Status is UnknownError rather than Valid.
        # Accept that, but only when this certificate actually signed the file.
        if ($result.SignerCertificate -eq $null -or $result.SignerCertificate.Thumbprint -ne $cert.Thumbprint) {
            throw "Signing failed for ${p}: $($result.Status) $($result.StatusMessage)"
        }
        $ts = if ($result.TimeStamperCertificate) { 'timestamped' } else { 'not timestamped' }
        Write-Host "Signed ($ts): $p"
    }
}
finally {
    $cert.Dispose()
}
