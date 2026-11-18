# NativeShellKit source pin

`SecretRedactor.cs` is the standalone BCL-only redactor from NativeShellKit commit
`89147f1058f853eb423624d468780aab1e064e65`, at `src/NativeShellKit.Security/SecretRedactor.cs`.
The source is vendored so a fresh BackThread checkout needs neither a sibling clone
nor an unpublished package. The NativeShellKit MIT license is included here and
copied into the portable distribution. This integration adds no NuGet dependency.

Update only by choosing a reviewed commit and running the log redaction regression.
The upstream file SHA-256 (normalized to LF) is `acbeb702d0b3a0c7872061562d971dfb8bc07e74e8c9a12140dc71212efa63bb`.
