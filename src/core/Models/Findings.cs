namespace AutoPortal.Guardian.Core.Models;

public enum ThermalLevel { Normal, Elevated, High, Critical }
public enum Priority { Critical, High, Recommended, Informational }

/// <summary>Something observed. Evidence is the recorded data that backs it.</summary>
public sealed record Finding(string Code, ComponentKind Component, ThermalLevel Level, string Title, string Explanation, IReadOnlyList<string> Evidence);

/// <summary>Advice derived from findings. IsPossibility=true means a hypothesis, not a confirmed diagnosis.</summary>
public sealed record Recommendation(Priority Priority, string Title, string Detail, IReadOnlyList<string> Evidence, bool IsPossibility = true);

public sealed record StabilityEvent(DateTime Time, string Source, int EventId, string Category, string Summary);
