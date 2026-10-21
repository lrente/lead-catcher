namespace WaLeads.Evals;

public sealed class EvalFactAttribute : FactAttribute
{
    public EvalFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_LLM_EVALS"), "1", StringComparison.Ordinal))
        {
            Skip = "Set RUN_LLM_EVALS=1 to opt in to the real-LLM evaluation.";
        }
    }
}
