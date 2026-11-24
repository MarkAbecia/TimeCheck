using Serilog.Core;
using Serilog.Events;

namespace TimeCheck.Api.Infrastructure;

/// <summary>Masks PII and secrets in destructured log objects (§8.4). Callers still must not log PII directly.</summary>
public sealed class PiiMaskingPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "mobile", "address", "birthdate", "guardianContact", "guestContact", "guardianName", "guestName",
        "password", "token", "accessToken", "refreshToken", "authorization", "textContent", "content",
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue result)
    {
        result = null!;
        var type = value.GetType();
        if (type.IsPrimitive || value is string || type.IsEnum || !type.IsClass) return false;
        var props = type.GetProperties().Where(p => p.GetIndexParameters().Length == 0).ToList();
        if (!props.Any(p => Sensitive.Contains(p.Name))) return false;

        result = new StructureValue(props.Select(p => new LogEventProperty(p.Name,
            Sensitive.Contains(p.Name) ? new ScalarValue("***") : factory.CreatePropertyValue(p.GetValue(value), true))), type.Name);
        return true;
    }
}
