namespace CimianAdmin.Core.Models.Predicates;

/// <summary>
/// Definition of one predicate keypath (a fact the runtime exposes to manifest
/// conditionals). The UI builds the keypath dropdown from <see cref="PredicateKeypaths.All"/>.
/// </summary>
/// <param name="Key">Identifier as it appears in the serialized predicate (e.g. <c>hostname</c>).</param>
/// <param name="Label">Human-friendly label shown in the dropdown (e.g. <c>Hostname</c>).</param>
/// <param name="ValueType">Drives the operator list and the value editor.</param>
/// <param name="Suggestions">Optional fixed value suggestions (e.g. arch = x64 / ARM64).</param>
public sealed record PredicateKeypath(
    string Key,
    string Label,
    PredicateValueType ValueType,
    IReadOnlyList<string>? Suggestions = null);
