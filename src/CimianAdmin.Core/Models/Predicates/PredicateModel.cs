namespace CimianAdmin.Core.Models.Predicates;

/// <summary>
/// The compound row's join semantics (top of the predicate editor).
/// </summary>
public enum CompoundType
{
    /// <summary>AND — all rows must match.</summary>
    All,
    /// <summary>OR — any single row matching is enough.</summary>
    Any,
    /// <summary>NOT(all of …) — none of the rows match.</summary>
    None,
}

/// <summary>One condition row in the predicate editor.</summary>
public sealed class PredicateRow
{
    public string Keypath { get; set; } = string.Empty;
    public string OperatorToken { get; set; } = "==";
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Builder model behind the Predicate Editor tab. Round-trips to/from a serialized
/// predicate string via <see cref="PredicateSerializer"/> and <see cref="PredicateParser"/>.
/// </summary>
public sealed class PredicateBuilder
{
    public CompoundType Compound { get; set; } = CompoundType.All;
    public List<PredicateRow> Rows { get; set; } = [];
}
