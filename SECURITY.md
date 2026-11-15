# Security Policy

## Supported versions

Security fixes are released for the latest version only.

## Reporting a vulnerability

Please do not report security problems in public issues, pull requests, or discussions.

Report them privately through GitHub's
[private vulnerability reporting](https://github.com/sharp-space/validator/security/advisories/new): the
**Report a vulnerability** button under this repository's **Security** tab. Include the affected package and version,
a minimal reproduction, and the impact you see.

You can expect an acknowledgement within a few days. The fix is released together with a security advisory that
credits you, unless you prefer otherwise.

## Security-relevant areas

- The regex-based validators. A pattern the library builds gets a match timeout, and a timeout is treated as a
  validation failure by design, so input that makes a pattern backtrack excessively is reported as invalid rather
  than thrown.
- Input parsing in the format validators: `Email`, `CreditCard`, `PhoneNumber`, `Url`, and `Uri`.
