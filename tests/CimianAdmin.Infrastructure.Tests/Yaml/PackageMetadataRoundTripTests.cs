namespace CimianAdmin.Infrastructure.Tests.Yaml;

using CimianAdmin.Infrastructure.Yaml;
using FluentAssertions;

public sealed class PackageMetadataRoundTripTests
{
    [Fact]
    public void Roundtrip_PkgInfoWithMetadata_PreservesUnderscoreKey()
    {
        const string yaml = """
            name: SamplePkg
            version: '1.0'
            catalogs:
              - Production
            installer:
              type: msi
              hash: ABC123
            unattended_install: true
            unattended_uninstall: true
            _metadata:
              cimian-promoter_edit_date: '2026-04-29T18:01:30Z'
            """;

        var package = PackageYamlSerializer.Deserialize(yaml);
        package.Should().NotBeNull();
        package!.Metadata.Should().NotBeNull("_metadata block should be parsed via the representation model");
        package.Metadata!.Should().ContainKey("cimian-promoter_edit_date");
        package.Metadata!["cimian-promoter_edit_date"].Should().Be("2026-04-29T18:01:30Z");

        var rendered = PackageYamlSerializer.Serialize(package);
        rendered.Should().Contain("_metadata:", "the trailing block must round-trip");
        rendered.Should().Contain("cimian-promoter_edit_date", "the metadata key must survive serialization");
        rendered.Should().Contain("2026-04-29T18:01:30Z", "the metadata value must survive serialization");
    }
}
