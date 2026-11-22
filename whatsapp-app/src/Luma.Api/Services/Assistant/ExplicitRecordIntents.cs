namespace Luma.Api.Services.Assistant;

public static class ExplicitRecordIntents
{
    public static IReadOnlySet<string> Detect(string message)
    {
        var text = MessageText.Normalize(message);
        var found = new HashSet<string>();
        // This only guards obvious compound registrations; it does not replace semantic interpretation.
        if (text.Contains("nao ", StringComparison.Ordinal)) return found;
        if (new[] { "menstruei", "menstruacao desceu", "menstruacao comecou" }.Any(text.Contains)) found.Add("record_period_start");
        if (new[] { "menstruacao acabou", "menstruacao terminou" }.Any(text.Contains)) found.Add("record_period_end");
        if (new[] { "tive relacao", "transei", "fiz sexo" }.Any(text.Contains)) found.Add("record_sexual_activity");
        if (new[] { "estou com colica", "to com colica", "tive colica" }.Any(text.Contains)) found.Add("record_symptom");
        if (new[] { "estou triste", "estou irritada", "estou ansiosa" }.Any(text.Contains)) found.Add("record_mood");
        return found;
    }
}
