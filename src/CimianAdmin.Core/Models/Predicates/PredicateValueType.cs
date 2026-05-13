namespace CimianAdmin.Core.Models.Predicates;

/// <summary>
/// How a predicate keypath's right-hand value should be edited and serialized.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "These names describe predicate value categories, not CLR types.")]
public enum PredicateValueType
{
    String,
    Integer,
    Boolean,
    Date,
    /// <summary>
    /// Collection-valued fact (e.g. <c>catalogs</c>, <c>gpu_names</c>). Comparisons
    /// resolve to <c>ANY</c>-style membership checks during serialization.
    /// </summary>
    StringList,
}
