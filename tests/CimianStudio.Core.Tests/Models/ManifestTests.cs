namespace CimianStudio.Core.Tests.Models;

using CimianStudio.Core.Models.Manifests;
using FluentAssertions;

public class ManifestTests
{
    [Fact]
    public void Manifest_DefaultValues_ShouldBeCorrect()
    {
        var manifest = new Manifest();

        manifest.Catalogs.Should().BeEmpty();
        manifest.DisplayName.Should().BeNull();
        manifest.ManagedInstalls.Should().BeNull();
        manifest.ManagedUninstalls.Should().BeNull();
    }

    [Fact]
    public void Manifest_WithManagedInstalls_ShouldStorePackages()
    {
        var manifest = new Manifest
        {
            DisplayName = "Site Default",
            Catalogs = ["production"],
            ManagedInstalls = ["firefox", "chrome", "vlc"]
        };

        manifest.ManagedInstalls.Should().HaveCount(3);
        manifest.ManagedInstalls.Should().Contain("firefox");
    }

    [Fact]
    public void Manifest_WithConditionalItems_ShouldStoreConditions()
    {
        var manifest = new Manifest
        {
            DisplayName = "Engineering",
            ConditionalItems =
            [
                new ConditionalItem
                {
                    Condition = "hostname contains 'ENG-'",
                    ManagedInstalls = ["visual-studio", "git"]
                }
            ]
        };

        manifest.ConditionalItems.Should().HaveCount(1);
        manifest.ConditionalItems![0].ManagedInstalls.Should().Contain("visual-studio");
    }

    [Fact]
    public void ConditionalItem_WithNestedConditions_ShouldSupportNesting()
    {
        var condition = new ConditionalItem
        {
            Fact = "department",
            Operator = "==",
            Value = "Engineering",
            NestedConditionalItems =
            [
                new ConditionalItem
                {
                    Fact = "role",
                    Operator = "==",
                    Value = "Developer",
                    ManagedInstalls = ["vscode"]
                }
            ]
        };

        condition.NestedConditionalItems.Should().HaveCount(1);
        condition.NestedConditionalItems![0].ManagedInstalls.Should().Contain("vscode");
    }
}
