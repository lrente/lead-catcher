using System.Globalization;
using System.Text.RegularExpressions;

namespace WaLeads.Core;

public sealed class QualificationGuard
{
    private static readonly Regex CurrencyAmountRegex = new(
        @"(?:(?:€|EUR|euros?)\s*(?<amount>\d[\d\s.,]*)|(?<amount>\d[\d\s.,]*)\s*(?:€|EUR|euros?))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public GuardedQualification Apply(
        QualificationResponse response,
        string businessContext,
        bool containsNonTextMessage,
        decimal minimumConfidence)
    {
        if (response.Intent == "spam" || response.NextAction == "ignore")
        {
            return new GuardedQualification(response with { SuggestedReply = null }, false, null);
        }

        string? forcedReason = null;
        if (response.Confidence < minimumConfidence)
        {
            forcedReason = "low_confidence";
        }
        else if (ContainsUnverifiedPrice(response.SuggestedReply, businessContext))
        {
            forcedReason = "unverified_price";
        }
        else if (containsNonTextMessage)
        {
            forcedReason = "non_text_message";
        }

        return forcedReason is null
            ? new GuardedQualification(response, false, null)
            : new GuardedQualification(
                response with { NextAction = "handoff_human", SuggestedReply = null },
                true,
                forcedReason);
    }

    private static bool ContainsUnverifiedPrice(string? reply, string businessContext)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        var knownAmounts = CurrencyAmountRegex.Matches(businessContext)
            .Select(match => NormalizeAmount(match.Groups["amount"].Value))
            .Where(amount => amount.HasValue)
            .Select(amount => amount!.Value)
            .ToHashSet();

        return CurrencyAmountRegex.Matches(reply)
            .Select(match => NormalizeAmount(match.Groups["amount"].Value))
            .Any(amount => amount.HasValue && !knownAmounts.Contains(amount.Value));
    }

    private static decimal? NormalizeAmount(string value)
    {
        var normalized = Regex.Replace(value, @"\s", string.Empty);
        var lastComma = normalized.LastIndexOf(',');
        var lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            var decimalSeparator = lastComma > lastDot ? ',' : '.';
            var thousandsSeparator = decimalSeparator == ',' ? '.' : ',';
            normalized = normalized.Replace(thousandsSeparator.ToString(), string.Empty, StringComparison.Ordinal);
            if (decimalSeparator == ',')
            {
                normalized = normalized.Replace(',', '.');
            }
        }
        else if (lastComma >= 0)
        {
            normalized = normalized.Replace(',', '.');
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }
}

public sealed record GuardedQualification(
    QualificationResponse Response,
    bool ForcedHandoff,
    string? ForcedReason);
