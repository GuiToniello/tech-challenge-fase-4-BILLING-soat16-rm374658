namespace TechChallenge.Oficina.Billing.Controllers.Features.Pagamentos
{
    public sealed class PagamentosResult<TValue, TError>
    {
        public TValue? Value { get; set; }
        public TError? Error { get; set; }

        public PagamentosResult() { }

        public PagamentosResult(TValue result)
        {
            Value = result;
            Error = default;
        }

        public PagamentosResult(TError error)
        {
            Value = default;
            Error = error;
        }
    }

    public static class PagamentosResult
    {
        public static PagamentosResult<TValue, Exception> From<TValue>(TValue result)
            => new(result);

        public static PagamentosResult<TValue, Exception> FromError<TValue>(Exception error)
            => new(error);
    }
}
