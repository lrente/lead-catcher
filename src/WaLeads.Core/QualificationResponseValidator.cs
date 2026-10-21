using FluentValidation;

namespace WaLeads.Core;

public sealed class QualificationResponseValidator : AbstractValidator<QualificationResponse>
{
    public static readonly string[] Intents = ["pricing", "booking", "info", "support", "spam", "other"];
    public static readonly string[] NextActions = ["ask_question", "propose_booking", "handoff_human", "ignore"];

    public QualificationResponseValidator()
    {
        RuleFor(response => response.Intent).Must(Intents.Contains)
            .WithMessage("intent must be one of: pricing, booking, info, support, spam, other.");
        RuleFor(response => response.Score).InclusiveBetween(0, 100);
        RuleFor(response => response.Confidence).InclusiveBetween(0m, 1m);
        RuleFor(response => response.NextAction).Must(NextActions.Contains)
            .WithMessage("next_action must be one of: ask_question, propose_booking, handoff_human, ignore.");
        RuleFor(response => response.MissingInfo).NotNull();
        RuleForEach(response => response.MissingInfo).NotEmpty();
        RuleFor(response => response.SuggestedReply).MaximumLength(600);
        RuleFor(response => response.SuggestedReply)
            .NotEmpty()
            .When(response => response.NextAction is "ask_question" or "propose_booking");
    }
}
