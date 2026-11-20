# Code signing

## Current status: self-signed

HT File Hash Checker 1.0.0 is signed with a **self-signed** HTREASURE LTD code-signing certificate:

| Field | Value |
|---|---|
| Subject / issuer | `CN=HTREASURE LTD, O=HTREASURE LTD, C=GB` |
| Purpose | Code signing (1.3.6.1.5.5.7.3.3) |
| Valid until | 20 November 2029 |
| SHA-1 thumbprint | `2F352812FE849999928F25F778A3D49AE5750EF2` |
| Public certificate | [`signing/HTREASURE-CodeSigning.cer`](../signing/HTREASURE-CodeSigning.cer) (also in the portable ZIP) |

Signed: `HTFileHashChecker.exe`, the product's DLLs (`HTFileHashChecker.*.dll`, `HTREASURE.*.dll`), Setup and the
uninstaller. Signatures use SHA-256 and an RFC 3161 timestamp from DigiCert's public timestamp service, so they stay
valid after the certificate expires. Only the file hash is sent to the timestamp service.

### What this does and does not mean

- **Does:** any change to a signed file breaks its signature, which Windows and `Get-AuthenticodeSignature` report.
- **Does not:** a self-signed certificate is not issued by a certificate authority that Windows trusts. Windows shows
  **"Unknown publisher"** in UAC and download prompts, `Get-AuthenticodeSignature` reports `UnknownError`
  ("terminated in a root certificate which is not trusted"), and Microsoft Defender SmartScreen may warn about the
  download. Do not describe the software as signed by a trusted or verified publisher.

The HTREASURE branding kit still records `codeSigning.configured: false` (meaning: no trusted, CA-issued certificate).

### Trusting the certificate inside an organisation

An administrator who has verified the certificate (compare the thumbprint above through a trusted channel) can make
Windows trust it on managed computers, for example by Group Policy, by importing `HTREASURE-CodeSigning.cer` into
both **Trusted Root Certification Authorities** and **Trusted Publishers**. This is a decision for each organisation;
the application and Setup never install the certificate themselves.

### Verifying a file

```powershell
Get-AuthenticodeSignature .\HTFileHashChecker_Setup_v1.0.0.exe | Format-List Status, SignerCertificate, TimeStamperCertificate
```

The signer thumbprint must be `2F352812FE849999928F25F778A3D49AE5750EF2`. Also compare the file with `SHA256SUMS.txt`
from the GitHub release.

## How signing runs

`scripts/sign-file.ps1` signs with the private key from `C:\HTSoftware\signing\HTREASURE-CodeSigning.pfx` (override
with `-SigningDir` or `HT_SIGNING_DIR`). The password file next to it is a Windows DPAPI-protected string, readable
only by the Windows account that created it. The key is loaded as an ephemeral key: nothing is added to the certificate
store, and the password is never shown or written in plain text. **The .pfx and its password must never be committed**
(`.gitignore` excludes `*.pfx`, `*.p12`, `*.key`, `*.pem`).

`scripts/build-release.ps1` signs the staged application files, then passes the same script to Inno Setup, which signs
Setup and its uninstaller. `-SkipCodeSigning` builds unsigned packages.

## Moving to a trusted certificate

To remove the "Unknown publisher" warnings, obtain an OV or EV code-signing certificate from a public certificate
authority, point `sign-file.ps1` at it (or replace it with `signtool sign /fd SHA256 /tr ... /td SHA256`), and only then
set `codeSigning.configured` to `true` in the branding kit's `brand.json`.
