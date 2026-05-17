namespace CimianStudio.Core.Models.Predicates;

/// <summary>
/// One predicate operator: the serialized token (<c>BEGINSWITH</c>, <c>==</c>, …)
/// plus a MunkiAdmin-style friendly label (<c>begins with</c>, <c>is</c>, …).
/// </summary>
public sealed record PredicateOperator(string Token, string Label);

public static class PredicateOperators
{
    // Friendly labels mirror MunkiAdmin's predicate editor wording.
    public static readonly PredicateOperator Equal = new("==", "is");
    public static readonly PredicateOperator NotEqual = new("!=", "is not");
    public static readonly PredicateOperator Contains = new("CONTAINS", "contains");
    public static readonly PredicateOperator DoesNotContain = new("NOT CONTAINS", "does not contain");
    public static readonly PredicateOperator BeginsWith = new("BEGINSWITH", "begins with");
    public static readonly PredicateOperator EndsWith = new("ENDSWITH", "ends with");
    public static readonly PredicateOperator Like = new("LIKE", "is like");
    public static readonly PredicateOperator Matches = new("MATCHES", "matches");
    public static readonly PredicateOperator GreaterThan = new(">", "is greater than");
    public static readonly PredicateOperator GreaterOrEqual = new(">=", "is at least");
    public static readonly PredicateOperator LessThan = new("<", "is less than");
    public static readonly PredicateOperator LessOrEqual = new("<=", "is at most");

    public static IReadOnlyList<PredicateOperator> For(PredicateValueType type) => type switch
    {
        PredicateValueType.Integer => [Equal, NotEqual, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual],
        PredicateValueType.Date => [Equal, NotEqual, BeginsWith, GreaterThan, LessThan],
        PredicateValueType.Boolean => [Equal, NotEqual],
        PredicateValueType.StringList => [Contains, DoesNotContain, Equal, NotEqual],
        _ => [Equal, NotEqual, Contains, BeginsWith, EndsWith, Like, Matches],
    };

    public static PredicateOperator FindByToken(string token)
    {
        // Case-insensitive lookup; falls back to Equal for unknown tokens so the UI
        // doesn't blow up on round-tripping a hand-written predicate.
        foreach (var op in All)
        {
            if (string.Equals(op.Token, token, StringComparison.OrdinalIgnoreCase)) return op;
        }
        return Equal;
    }

    public static readonly IReadOnlyList<PredicateOperator> All =
    [
        Equal, NotEqual, Contains, DoesNotContain, BeginsWith, EndsWith, Like, Matches,
        GreaterThan, GreaterOrEqual, LessThan, LessOrEqual,
    ];
}
