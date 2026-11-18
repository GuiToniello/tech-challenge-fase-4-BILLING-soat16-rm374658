namespace TechChallenge.Oficina.Billing.Entities.Exceptions;

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
