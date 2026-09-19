# Release signing

Prompt Helper release workflows fail closed unless the published executable can be
Authenticode-signed and the resulting signature can be verified.

## Required repository secrets

- `WINDOWS_SIGNING_CERTIFICATE_PFX`: Base64 representation of the complete PFX file.
- `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`: Password protecting that PFX file.

Generate the Base64 value locally without committing the certificate:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('PromptHelper-signing.pfx')) |
  Set-Clipboard
```

Add both values under **Repository settings → Secrets and variables → Actions**.
The certificate and password must never be committed, added to workflow artifacts, or
printed in workflow output.

## Release guarantees

For tag and manually dispatched release builds, the workflow:

1. publishes the self-contained Windows application;
2. signs `PromptHelper.exe` with SHA-256 and a trusted timestamp;
3. verifies the Authenticode signature;
4. creates an SPDX SBOM with Microsoft's pinned `sbom-tool`;
5. writes `SHA256SUMS.txt` for every distributed file;
6. creates a GitHub build-provenance attestation; and
7. uploads the release artifact only after every preceding gate succeeds.

If signing is not configured or fails, no release artifact is uploaded. Test evidence is
still retained to make the failure diagnosable.
