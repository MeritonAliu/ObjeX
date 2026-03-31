# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in ObjeX, please report it responsibly.

**Email:** meriton@centrolabs.ch

Do not open a public GitHub issue for security vulnerabilities.

## What to Include

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

## Response

We aim to acknowledge reports within 48 hours and provide a fix or mitigation plan within 7 days.

## Scope

- ObjeX application code
- S3 API authentication (SigV4)
- Blazor UI authentication
- Data access and authorization

Out of scope: third-party dependencies (report those upstream), self-hosted misconfiguration.

## Encryption

ObjeX does not encrypt blobs or metadata at the application level. For data at rest, rely on full-disk encryption (LUKS, BitLocker, encrypted cloud volumes). For data in transit, run behind a TLS-terminating reverse proxy (nginx, Caddy, Traefik).
