# InstaGrabber

## Software Bill of Materials

Third-party attribution lives in [`NOTICE`](NOTICE). Version history is in
[`CHANGELOG.md`](CHANGELOG.md).

Component inventory is assembled from **two sources**, because no single scan covers
everything this project ships:

| Source | Covers | How |
| --- | --- | --- |
| Dependency-graph scan | NuGet packages (direct and transitive) | BomLens / Trivy over the restored solution |
| Hand-verified entries | Vendored front-end libraries in `wwwroot/lib/` | Read from the licence text and file headers shipped with each library, recorded in `NOTICE` |

The second source is not optional. Bootstrap, jQuery, jQuery Validation and jQuery
Unobtrusive Validation are committed as static files rather than pulled through a
package manager. A dependency-graph scanner sees them **partially and unreliably**: a
BomLens source scan detected `jquery` 1.12.4 but under the wrong package name for
jQuery Validation, and missed Bootstrap 2.3.2 entirely. Partial detection is not a
substitute for attribution, so all four are recorded by hand in `NOTICE`, and each
library's full licence text sits alongside its files under `InstaGrabber/wwwroot/lib/`.

**No SBOM document ships with 0.0.1.** Automated generation is blocked on an upstream
defect — see `CHANGELOG.md` under "Known issues". `NOTICE` is the sole compliance
artifact for this release.

Worth knowing when reading the inventory:

- The web application declares **no** `PackageReference`. Everything third-party that
  actually ships is a vendored front-end library plus the .NET shared framework.
- Test-only dependencies (`xunit`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`) are
  never distributed; `InstaGrabber.Tests` is marked `IsPackable=false`.
- Known advisories affecting the test-only graph are tracked in
  [`CHANGELOG.md`](CHANGELOG.md) under "Known issues".

Regenerate the dependency-graph portion with a restore first, so the scanner reads the
real package graph rather than compiled output:

```sh
dotnet restore InstaGrabber.sln
```

BomLens picks its scan mode from *how* it is invoked, not from a flag. Passing a
directory to `--target` forces filesystem (rootfs) mode, which catalogues `bin/` and
`obj/` and reports our own assemblies as packages. Source mode — the one that reads
`project.assets.json` and resolves NuGet — is selected by running **inside** the tree
with no `--target` at all:

```sh
cd <clean copy of the solution>   # must exclude the bomlens/ clone itself
/path/to/bomlens/scripts/scan-sbom.sh \
  --project InstaGrabber --version 0.0.1 --all --generate-only
```

Confirm the mode afterwards: `.scanmeta.json` must read `"source":"current-dir"`.
`"rootfs-dir"` means the scan is invalid regardless of how plausible its output looks.

A quick vulnerability check that needs no extra tooling:

```sh
dotnet list InstaGrabber.sln package --vulnerable --include-transitive
```

Raw scanner output lands in `bomlens/`, which is gitignored. `NOTICE` is the only
curated release artifact committed for 0.0.1.
