// Validation rules (G7). A stand-in for FluentValidation: the detector matches it by namespace.
using System.ComponentModel.DataAnnotations;

namespace FluentValidation
{
    public interface IRuleBuilder<T, TProperty>;
    public interface IRuleBuilderOptions<T, TProperty> : IRuleBuilder<T, TProperty>;

    public abstract class AbstractValidator<T>
    {
        public IRuleBuilder<T, TProperty> RuleFor<TProperty>(System.Linq.Expressions.Expression<Func<T, TProperty>> selector) => null!;
    }

    public static class RuleBuilderExtensions
    {
        // As FluentValidation has it: rules on the rule builder, modifiers on the rule's options (T12).
        public static IRuleBuilderOptions<T, P> NotEmpty<T, P>(this IRuleBuilder<T, P> rule) => null!;
        public static IRuleBuilderOptions<T, P> GreaterThan<T, P>(this IRuleBuilder<T, P> rule, P value) => null!;
        public static IRuleBuilderOptions<T, P> WithMessage<T, P>(this IRuleBuilderOptions<T, P> rule, string message) => rule;
        public static IRuleBuilderOptions<T, P> When<T, P>(this IRuleBuilderOptions<T, P> rule, Func<T, bool> condition) => rule;
    }
}

namespace Shop.Core
{
    using FluentValidation;

    // N4: an app's own message modifier, the way nopCommerce writes one; a modifier, not a rule.
    public static class Texts
    {
        public static Task<string> Get(string key) => Task.FromResult(key);

        public static IRuleBuilderOptions<T, P> WithMessageAwait<T, P>(this IRuleBuilderOptions<T, P> rule, Task<string> message) => rule;

        // T12: an app's own rule whose name starts like a modifier's; it's on the rule builder, so it's a rule.
        public static IRuleBuilderOptions<T, P> Within<T, P>(this IRuleBuilder<T, P> rule, int most) => null!;
    }

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
            RuleFor(o => o.Customer).NotEmpty().WithMessageAwait(Texts.Get("Order.Customer.Required")).Within(40);
        }
    }
}
