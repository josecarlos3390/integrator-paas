using FluentValidation;
using Integration.Shared.Dtos;

namespace Integration.Api.Validators;

public class IngestionPayloadValidator : AbstractValidator<IngestionPayload>
{
    private static readonly HashSet<string> ValidObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "account", "vendor", "product", "order", "invoice", "price_list", "price_list_header"
    };

    public IngestionPayloadValidator()
    {
        RuleFor(x => x.Object)
            .NotEmpty()
            .MaximumLength(64)
            .Must(o => ValidObjects.Contains(o))
            .WithMessage($"Object must be one of: {string.Join(", ", ValidObjects)}");

        RuleFor(x => x.Entry).NotNull();

        RuleFor(x => x.Entry.Id)
            .NotEmpty()
            .MaximumLength(64)
            .When(x => x.Entry != null);

        RuleFor(x => x.Entry.Context)
            .NotNull()
            .When(x => x.Entry != null);

        RuleFor(x => x.Entry.Context.TenantId)
            .NotEmpty()
            .MaximumLength(64)
            .When(x => x.Entry?.Context != null);

        RuleFor(x => x.Entry.Messages)
            .NotNull()
            .Must(m => m.Count > 0)
            .WithMessage("At least one message is required")
            .When(x => x.Entry != null);

        RuleFor(x => x.Entry.Metadata.SourceSystem)
            .NotEmpty()
            .MaximumLength(64)
            .When(x => x.Entry?.Metadata != null);

        RuleFor(x => x.Entry.Metadata.TargetSystem)
            .NotEmpty()
            .MaximumLength(64)
            .When(x => x.Entry?.Metadata != null);
    }
}
