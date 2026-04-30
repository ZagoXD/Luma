using Microsoft.AspNetCore.Http;

namespace Luma.Api.Services;

public sealed record ConversationScopeResult(bool IsGroup, string Reason);

public sealed class ConversationScopeDetector
{
    private static readonly string[] GroupFieldNames =
    [
        "GroupSid",
        "GroupId",
        "GroupName",
        "ChannelSid",
        "ConversationSid",
        "ParticipantSid"
    ];

    public ConversationScopeResult DetectTwilio(IFormCollection form)
    {
        var from = form["From"].ToString();
        if (from.Contains("@g.us", StringComparison.OrdinalIgnoreCase))
        {
            return new ConversationScopeResult(true, "whatsapp_group_jid");
        }

        foreach (var field in GroupFieldNames)
        {
            if (!string.IsNullOrWhiteSpace(form[field].ToString()))
            {
                return new ConversationScopeResult(true, $"twilio_group_field:{field}");
            }
        }

        if (!from.StartsWith("whatsapp:+", StringComparison.OrdinalIgnoreCase))
        {
            return new ConversationScopeResult(true, "non_individual_whatsapp_sender");
        }

        var normalized = PhoneNumber.Normalize(from);
        var digits = AccountInputNormalizer.OnlyDigits(normalized);
        if (digits.Length < 10)
        {
            return new ConversationScopeResult(true, "invalid_individual_phone");
        }

        return new ConversationScopeResult(false, "individual");
    }
}
