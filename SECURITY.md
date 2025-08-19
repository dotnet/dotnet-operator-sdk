# Security Policy

## Supported Versions

By default, the latest major version of the project is supported. Information about supported versions and their security update status will be documented here as the project matures and establishes a formal release cadence.

| Version        | Supported          |
| -------------- | ------------------ |
| Latest Major   | :white_check_mark: |
| < Latest Major | :x:                |

## Dependency Updates

We use [Renovate Bot](https://renovatebot.com) to automatically update all dependencies to the newest versions. This ensures that our project remains secure by incorporating the latest security patches. Renovate Bot only merges updates when all checks pass successfully.

## Reporting a Vulnerability

The KubeOps team and community take security bugs seriously. We appreciate your efforts to responsibly disclose your findings, and will make every effort to acknowledge your contributions.

To report a security vulnerability, please use the [GitHub Security Advisory "Report a Vulnerability" tab](https://github.com/dotnet/dotnet-operator-sdk/security/advisories/new).

Please include the requested information listed below (as much as you can provide) to help us better understand the nature and scope of the possible issue:

- Type of issue (e.g., buffer overflow, SQL injection, cross-site scripting, etc.)
- Full paths of source file(s) related to the manifestation of the issue
- The location of the affected source code (tag/branch/commit or direct URL)
- Any special configuration required to reproduce the issue
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the issue, including how an attacker might exploit the issue

Please **do not** report security vulnerabilities through public GitHub issues. Use the security dashboard of the repository exclusively.
