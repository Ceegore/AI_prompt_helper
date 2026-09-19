# Optional release signing

Prompt Helper is a hobby project and does not require a paid code-signing certificate.
Without signing secrets, the release workflow deliberately publishes an unsigned build.
It still runs the full test and security gates, creates an SPDX SBOM and SHA-256 checksums,
and adds a GitHub build-provenance attestation.

Windows may show an "Unknown publisher" or Microsoft Defender SmartScreen warning for an
unsigned executable. That is expected. A self-signed certificate does not meaningfully
improve this experience for other users, so it is not required or recommended here.

## Default: free unsigned releases

No setup is required. Leave both signing secrets absent and run the **Release** workflow.
The workflow will upload the `PromptHelper-release` artifact after all other checks pass.

Users can verify the downloaded files against `SHA256SUMS.txt`. The GitHub attestation also
links the artifact to the repository workflow that produced it.

Signing remains available as a purely optional upgrade if a suitable certificate is ever
available at no additional cost.

## Optional repository secrets

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

Configure either both secrets or neither. Supplying only one is treated as an error so the
workflow cannot silently run with a broken signing configuration.

## Release guarantees

For tag and manually dispatched release builds, the workflow:

1. publishes the self-contained Windows application;
2. signs and verifies `PromptHelper.exe` when both optional secrets are configured;
3. otherwise explicitly continues with an unsigned executable;
4. creates an SPDX SBOM with Microsoft's pinned `sbom-tool`;
5. writes `SHA256SUMS.txt` for every distributed file;
6. creates a GitHub build-provenance attestation; and
7. uploads the release artifact only after every applicable gate succeeds.

If optional signing is enabled but signing or verification fails, no release artifact is
uploaded. Test evidence is still retained to make the failure diagnosable.
