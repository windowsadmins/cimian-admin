namespace CimianAdmin.Core.Tests.Models;

using CimianAdmin.Core.Models.Packages;
using FluentAssertions;

public class PackageTests
{
    [Fact]
    public void Package_DefaultValues_ShouldBeCorrect()
    {
        var package = new Package();

        package.Name.Should().BeEmpty();
        package.Version.Should().BeEmpty();
        package.Catalogs.Should().BeEmpty();
        package.UnattendedInstall.Should().BeFalse();
        package.UnattendedUninstall.Should().BeFalse();
        package.OnDemand.Should().BeFalse();
    }

    [Fact]
    public void Package_EffectiveDisplayName_ShouldReturnDisplayName_WhenSet()
    {
        var package = new Package
        {
            Name = "firefox",
            DisplayName = "Mozilla Firefox"
        };

        package.EffectiveDisplayName.Should().Be("Mozilla Firefox");
    }

    [Fact]
    public void Package_EffectiveDisplayName_ShouldReturnName_WhenDisplayNameNotSet()
    {
        var package = new Package
        {
            Name = "firefox",
            DisplayName = null
        };

        package.EffectiveDisplayName.Should().Be("firefox");
    }

    [Fact]
    public void Package_WithFullData_ShouldStoreAllProperties()
    {
        var package = new Package
        {
            Name = "firefox",
            DisplayName = "Mozilla Firefox",
            Version = "120.0",
            Description = "A free and open-source web browser",
            Developer = "Mozilla",
            Category = "Browsers",
            Catalogs = ["testing", "production"],
            Installer = new Installer
            {
                Type = "exe",
                Location = "pkgs/Mozilla/Firefox-120.0.exe",
                Arguments = ["/S"]
            }
        };

        package.Name.Should().Be("firefox");
        package.Version.Should().Be("120.0");
        package.Catalogs.Should().HaveCount(2);
        package.Installer.Should().NotBeNull();
        package.Installer!.Type.Should().Be("exe");
    }
}
