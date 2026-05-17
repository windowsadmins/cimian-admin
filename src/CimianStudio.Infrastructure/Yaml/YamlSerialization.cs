namespace CimianStudio.Infrastructure.Yaml;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

/// <summary>
/// Shared YamlDotNet serializer/deserializer for Cimian repository files.
/// Naming convention is underscored (display_name, managed_installs, ...);
/// <see cref="YamlMemberAttribute"/> on the model takes precedence when present.
/// </summary>
public static class YamlSerialization
{
    public static IDeserializer Deserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ISerializer Serializer { get; } = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
        .WithEventEmitter(next => new ForceLiteralScalarEmitter(next))
        .Build();

    /// <summary>
    /// Forces multi-line strings to use the <c>|</c> literal block scalar style so that
    /// shell/PowerShell scripts round-trip without folding.
    /// </summary>
    private sealed class ForceLiteralScalarEmitter : ChainedEventEmitter
    {
        public ForceLiteralScalarEmitter(IEventEmitter next) : base(next) { }

        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string s && s.Contains('\n', StringComparison.Ordinal))
            {
                eventInfo.Style = ScalarStyle.Literal;
            }
            base.Emit(eventInfo, emitter);
        }
    }
}
