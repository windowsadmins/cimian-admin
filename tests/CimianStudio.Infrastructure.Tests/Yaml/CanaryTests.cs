namespace CimianStudio.Infrastructure.Tests.Yaml;

using CimianStudio.Infrastructure.Yaml;
using FluentAssertions;

/// <summary>
/// Local-only canary: load real production pkginfo files from deployment/,
/// run them through PackageYaml twice, assert the output stabilises after the
/// first save. This is the actual contract — option-A canonical normalisation
/// rewrites quoting / key ordering / line endings to a cleaner form once on
/// first save (matching the user's stated preference for minimal quoting and
/// consistent layout), then never churns again. Byte-identity against
/// arbitrarily-formatted wild files is NOT a goal.
/// Skipped when deployment/ is absent (e.g. CI hosts).
/// </summary>
public class CanaryTests
{
    private const string DeploymentRoot =
        @"C:\Users\rchristiansen\Developer\AzDevOps\Devices\Cimian\deployment";

    public static IEnumerable<object[]> CanaryFiles =>
    [
        ["pkgsinfo/apps/dev/Git-x64-2.54.0.1.yaml"],
        ["pkgsinfo/printing/Printer-ECU_PhotoDeptInkjets.yaml"],
        ["pkgsinfo/apps/animation/Moho-14.4.yaml"],
        ["pkgsinfo/mgmt/ProvisioningManifestEnrollment.yaml"],
    ];

    [Theory]
    [MemberData(nameof(CanaryFiles))]
    public void RealDeploymentFile_StabilisesAfterFirstSave(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        var fullPath = Path.Combine(DeploymentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            // Not on a workstation with the deployment repo cloned alongside — skip gracefully.
            return;
        }

        var original = File.ReadAllText(fullPath);

        // First pass: option-A normalisation lands. Output may differ from original.
        var firstPkg = PackageYaml.Deserialize(original);
        firstPkg.Should().NotBeNull($"failed to parse {fullPath}");
        var firstSave = PackageYaml.Serialize(firstPkg!);

        // Second pass: should be a true no-op. If this ever diverges, we have a
        // genuine bug (the canonical form isn't its own fixed point).
        var secondPkg = PackageYaml.Deserialize(firstSave);
        secondPkg.Should().NotBeNull("re-parsing our own output must succeed");
        var secondSave = PackageYaml.Serialize(secondPkg!);

        // Drop side-by-side files for human inspection on failure.
        var tmp = Path.Combine(Path.GetTempPath(), "CimianStudio_Canary_" + Path.GetFileName(relativePath));
        File.WriteAllText(tmp + ".first.yaml", firstSave);
        File.WriteAllText(tmp + ".second.yaml", secondSave);

        secondSave.Should().Be(firstSave,
            $"second save must equal first save (idempotency). Compare: {tmp}.first.yaml vs {tmp}.second.yaml");
    }
}
