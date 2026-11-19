using FluentValidation;
using FluentValidation.Results;

namespace Shop.Core;

// D208: validators run outside the request body's own validator: rules a controller-like class writes on a validator it holds and
// runs before each action, and a validator class run through an interface.

public class ReturnRequest
{
    public int OrderId { get; set; }
    public string Reason { get; set; } = "";
}

public abstract class ReturnEndpointBase
{
    protected InlineValidator<ReturnRequest> Shared { get; } = new();

    public void OnActionExecuting(ReturnRequest request) => Check(request);

    void Check(ReturnRequest request) => Shared.Validate(request);
}

public class ReturnEndpoint : ReturnEndpointBase
{
    public ReturnEndpoint() => Shared.RuleFor(r => r.Reason).NotEmpty();
}

public interface IReturnValidator
{
    ValidationResult Validate(ReturnRequest request);
}

public class ReturnValidator : AbstractValidator<ReturnRequest>, IReturnValidator
{
    public ReturnValidator()
    {
        RuleFor(r => r.OrderId).GreaterThan(0);
        RuleFor(r => r).NotEmpty();
    }
}

public class ReturnService(IReturnValidator validator)
{
    public bool Open(ReturnRequest request) => validator.Validate(request) is not null;
}
