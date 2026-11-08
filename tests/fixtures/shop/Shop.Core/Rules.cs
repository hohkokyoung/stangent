// Validation rules (G7). A stand-in for FluentValidation: the detector matches it by namespace.
using System.ComponentModel.DataAnnotations;

namespace FluentValidation
{
    public interface IRuleBuilder<T, TProperty>;

    public abstract class AbstractValidator<T>
    {
        public IRuleBuilder<T, TProperty> RuleFor<TProperty>(System.Linq.Expressions.Expression<Func<T, TProperty>> selector) => null!;
    }

    public static class RuleBuilderExtensions
    {
        public static IRuleBuilder<T, P> NotEmpty<T, P>(this IRuleBuilder<T, P> rule) => rule;
        public static IRuleBuilder<T, P> GreaterThan<T, P>(this IRuleBuilder<T, P> rule, P value) => rule;
        public static IRuleBuilder<T, P> WithMessage<T, P>(this IRuleBuilder<T, P> rule, string message) => rule;
        public static IRuleBuilder<T, P> When<T, P>(this IRuleBuilder<T, P> rule, Func<T, bool> condition) => rule;
    }
}

namespace Shop.Core
{
    using FluentValidation;

    public class NewOrder
    {
        [Required, StringLength(40, MinimumLength = 2)]
        public string Customer { get; set; } = "";

        public int Quantity { get; set; }
    }

    public class NewOrderValidator : AbstractValidator<NewOrder>
    {
        const int Smallest = 0;

        public NewOrderValidator()
        {
            RuleFor(o => o.Quantity).GreaterThan(Smallest).WithMessage("Order at least one").When(o => o.Customer != "")
                .NotEmpty();
        }
    }
}
