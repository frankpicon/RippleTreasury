namespace EventTicketing.ServiceDefaults.Correlation;

public interface ICorrelationContext
{
    Guid CorrelationId { get; }
}

internal sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public Guid CorrelationId =>
        accessor.HttpContext?.Items[CorrelationIdMiddleware.ItemName] is Guid value
            ? value
            : Guid.NewGuid();
}
