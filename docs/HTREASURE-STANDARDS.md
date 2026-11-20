# HTREASURE standards in HT File Hash Checker

This product applies [`HTREASURE-LTD/ht-software-standards`](https://github.com/HTREASURE-LTD/ht-software-standards)
and the branding kit from [`HTREASURE-LTD/HT-Software-Template`](https://github.com/HTREASURE-LTD/HT-Software-Template).
Both are git submodules under `external/`, pinned to a reviewed commit:

```bash
git submodule update --init
```

## What is used unchanged

| Standard | How it is applied |
|---|---|
| `HTREASURE.Branding.props` | Imported by `Directory.Build.props`: Company, Authors, `Copyright © <year> HTREASURE LTD. All rights reserved.` |
| `HTREASURE.Branding.targets` | Imported by `Directory.Build.targets`: executable icon |
| Brand assets and `brand.json` | Embedded from the kit at build time (logos, corporate icon, splash backgrounds, Inter font). Nothing is redrawn |
| `HTREASURE.Standards.targets` | Imported by `Directory.Build.targets`; `HTAppId=ht-file-hash-checker`. Generates `security/advisory-snapshot.json` and `ht-compatibility.json`, embeds `trusted-keys.json`, and fails Release builds whose snapshot is not signed |
| Advisory registry, schemas, fixtures | Read from the submodule; the client must pass `security/test-fixtures/expected-statuses.json` (21 cases, all passing) |

## .NET Framework ports (Windows 7 and 8)

The template and standards libraries target .NET 10, which cannot run on Windows 7 or 8. These projects reproduce
their behaviour and public names on .NET Framework 4.6.1 and 4.8:

| Port | Original | Notes |
|---|---|---|
| `src/HTREASURE.Branding.NetFramework` | `HTREASURE.Branding.Core` + `HTREASURE.Branding.WinForms` | `BrandInfo`, `BrandColors`, `BrandAssets`, `BrandWinForms`, `BrandFonts`, `BrandImageBox`, `BrandSplashForm`, `BrandAboutForm` |
| `src/HTREASURE.Standards.NetFramework` | `HTREASURE.Standards.Security`, `HTStandardsRuntime`, `HTREASURE.Standards.WinForms` | Same status rules, validation, feed reading, rollback protection and screens. ECDSA P-256 signatures are verified with Windows CNG (`ECDsaCng`), available on Windows 7 and later. Strict JSON (unknown members rejected) uses the framework's own `JavaScriptSerializer`: no third-party libraries |

Not ported, because the services do not exist yet (see *What is not done yet* in the standards guide):

- the HTTPS advisory transport: **Check for Advisories** reports "The advisory service is not configured for this product";
- the Software Update Service: **Check for Security Updates** says the update service is not configured and never downloads anything.

When `HTAdvisoryServiceUrl` and `HTReleaseFeedUrl` are provisioned, these two pieces must be added to the port.
The ports are candidates for moving into `ht-software-standards`, so that other .NET Framework products (for example
HT ISO Ripper) can share them.

## Where the standards appear in the application

- **Help > About > About this application**: standard HTREASURE About box (product, version, copyright, publisher,
  company number, registered address, logo).
- **Help > About > Security Advisory History** and **Help > Security Advisories**: Security Advisory History screen,
  working offline from the bundled snapshot.
- Splash screen at start-up; Deep Navy product header; footer with `© <year> HTREASURE LTD · An HTREASURE LTD product`.

## Release signing

Release builds need the offline advisory signing key (key id `htreasure-advisory-2026`, public key in
`external/ht-software-standards/security/keys/trusted-keys.json`):

```bash
dotnet build -c Release -p:HTAdvisorySigningKey=<path to offline pem> -p:HTAdvisoryKeyId=htreasure-advisory-2026
```

The key must never be committed. Internal test builds may use `-p:HTRequireSignedSnapshot=false`; the application
then shows "Development build: the bundled advisory snapshot is not signed".

## Product icon

The kit generates product icons with `Source/build/build_brand.py`; they are never drawn by hand per app. There is no
`file-hash-checker` icon in the kit yet, so the corporate HTREASURE icon is used (the documented fallback when
`HTProductId` is not set). To add one: write a glyph in `build_brand.py`, run it, then set
`<HTProductId>file-hash-checker</HTProductId>` in `src/HTFileHashChecker.UI/HTFileHashChecker.UI.csproj`.

## Code signing

`brand.json` records `codeSigning.configured: false`. The executables are not digitally signed, and no documentation
may say that they are.
