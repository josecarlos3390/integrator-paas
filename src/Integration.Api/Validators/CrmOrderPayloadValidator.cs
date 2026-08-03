using FluentValidation;
using Integration.Shared.Dtos;

namespace Integration.Api.Validators;

public class CrmOrderPayloadValidator : AbstractValidator<CrmOrderPayload>
{
    public CrmOrderPayloadValidator()
    {
        RuleFor(x => x.CrmOrderId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.CustomerId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrderDate).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count > 0).WithMessage("At least one line is required");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Sku).NotEmpty().MaximumLength(128);
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.Price).GreaterThanOrEqualTo(0);
        });
    }
}
