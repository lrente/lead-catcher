namespace WaLeads.Core;

public static class LeadStageMapper
{
    public static string Map(string currentStage, string intent, int score, int hotThreshold)
    {
        if (currentStage == "booked")
        {
            return currentStage;
        }

        if (intent == "spam")
        {
            return "lost";
        }

        return score >= hotThreshold ? "hot" : "qualifying";
    }
}
