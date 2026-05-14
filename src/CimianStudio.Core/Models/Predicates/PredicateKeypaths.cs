namespace CimianStudio.Core.Models.Predicates;

/// <summary>
/// Catalog of the Cimian runtime facts that can appear on the left-hand side of a
/// manifest predicate. Sourced from <c>packages/CimianTools/pkg/predicates</c>
/// (SystemFacts + PredicateEngine).
/// </summary>
public static class PredicateKeypaths
{
    public static readonly IReadOnlyList<PredicateKeypath> All = new[]
    {
        new PredicateKeypath("hostname", "Hostname", PredicateValueType.String),
        new PredicateKeypath("arch", "Processor architecture", PredicateValueType.String,
            ["x64", "x86", "ARM64"]),
        new PredicateKeypath("os_version", "Full OS version", PredicateValueType.String),
        new PredicateKeypath("os_vers_major", "OS major version", PredicateValueType.Integer),
        new PredicateKeypath("os_vers_minor", "OS minor version", PredicateValueType.Integer),
        new PredicateKeypath("os_build_number", "OS build number", PredicateValueType.Integer),
        new PredicateKeypath("domain", "AD domain", PredicateValueType.String),
        new PredicateKeypath("username", "Logged-in user", PredicateValueType.String),
        new PredicateKeypath("machine_type", "Machine type", PredicateValueType.String,
            ["laptop", "desktop", "virtual", "server"]),
        new PredicateKeypath("machine_model", "Machine model", PredicateValueType.String),
        new PredicateKeypath("model_version", "Model friendly name", PredicateValueType.String),
        new PredicateKeypath("joined_type", "Join method", PredicateValueType.String,
            ["domain", "workgroup", "entra", "hybrid"]),
        new PredicateKeypath("battery_state", "Battery state", PredicateValueType.String,
            ["connected", "disconnected", "unknown"]),
        new PredicateKeypath("date", "Current date", PredicateValueType.Date),
        new PredicateKeypath("catalogs", "Catalogs", PredicateValueType.StringList),
        new PredicateKeypath("gpu_names", "GPU names", PredicateValueType.StringList),
        new PredicateKeypath("gpu_driver_version", "GPU driver version", PredicateValueType.String),
        new PredicateKeypath("gpu_vram_gb", "GPU VRAM (GB)", PredicateValueType.Integer),
        new PredicateKeypath("cpu_name", "CPU model", PredicateValueType.String),
        new PredicateKeypath("cpu_manufacturer", "CPU manufacturer", PredicateValueType.String,
            ["Intel", "AMD", "Qualcomm", "ARM"]),
        new PredicateKeypath("cpu_cores", "CPU cores", PredicateValueType.Integer),
        new PredicateKeypath("cpu_logical_cores", "CPU logical cores", PredicateValueType.Integer),
        new PredicateKeypath("npu_name", "NPU model", PredicateValueType.String),
        new PredicateKeypath("npu_available", "NPU present", PredicateValueType.Boolean),
        new PredicateKeypath("ram_total_gb", "Total RAM (GB)", PredicateValueType.Integer),
        new PredicateKeypath("ram_type", "RAM type", PredicateValueType.String,
            ["DDR3", "DDR4", "DDR5", "LPDDR4", "LPDDR5"]),
        new PredicateKeypath("storage_type", "Primary storage type", PredicateValueType.String,
            ["NVMe", "SSD", "HDD"]),
        new PredicateKeypath("storage_capacity_gb", "Primary storage size (GB)", PredicateValueType.Integer),
    };

    public static PredicateKeypath? Find(string key) =>
        All.FirstOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
}
