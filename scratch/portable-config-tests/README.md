# Portable Config Bundle Tests

This scratch project runs without a GUI, real plugin host, or real input.

- Default mode links the production `PortableConfigBundle.cs` and
  `SvgIconBatchImporter.cs` sources and uses `TestIconHelper.cs`.
- Candidate mode references only `$(StarPieCandidateDir)\StarPie.dll` (and the
  adjacent plugin abstractions when present), does not compile product sources
  or `TestIconHelper.cs`, and verifies the loaded `StarPie.dll` SHA-256 against
  the candidate.

The test process redirects `TEMP` and `TMP` to
`scratch\portable-config-tests\artifacts\temp` before running product code.
All run artifacts remain below `scratch\portable-config-tests\artifacts`.

Build:

```powershell
dotnet build scratch\portable-config-tests\PortableConfigTests.csproj -c Release --nologo -p:OutDir=scratch\portable-config-tests\build-out
```

Run:

```powershell
dotnet scratch\portable-config-tests\build-out\test_portable_config.dll
```

The test writes only beneath `scratch\portable-config-tests\artifacts`.

Candidate mode:

```powershell
dotnet build scratch\portable-config-tests\PortableConfigTests.csproj -c Release --nologo `
  -p:StarPieCandidateDir="C:\path\to\candidate" `
  -p:OutDir="G:\Users\2 Better\.codex\worktrees\profile-trigger-wheels\design\scratch\portable-config-tests\build-out-candidate-1937\"
$env:STARPIE_CANDIDATE_DLL = "C:\path\to\candidate\StarPie.dll"
dotnet scratch\portable-config-tests\build-out-candidate-1937\test_portable_config.dll
```

Candidate mode automatically uses a candidate-specific intermediate directory
under `scratch\portable-config-tests\obj-candidate-<candidate-leaf>\`, so it
does not reuse the source-linked `obj\` tree. Use a candidate path whose final
directory name is unique. Pass a new, empty `OutDir` for each candidate run;
never reuse a source-linked output directory.

Default None/Content discovery is disabled: test output DLLs must never enter
MSBuild's candidate-assembly search through unrelated directory contents.

Candidate mode fails before behavioral tests if the loaded DLL hash differs from
`STARPIE_CANDIDATE_DLL` (or `STARPIE_CANDIDATE_DIR\StarPie.dll`).
