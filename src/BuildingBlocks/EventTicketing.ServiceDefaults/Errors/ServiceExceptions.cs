namespace EventTicketing.ServiceDefaults.Errors;

public abstract class ServiceException(string message) : Exception(message)
{
}

public sealed class ResourceNotFoundException(string message) : ServiceException(message)
{
}

public sealed class ResourceConflictException(string message) : ServiceException(message)
{
}

public sealed class RequestValidationException(string message) : ServiceException(message)
{
}
