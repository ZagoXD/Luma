namespace Luma.Api.Services;

public enum IncomingContentKind { Text, Audio, Unsupported, UnsupportedWithCaption, Empty }

public static class IncomingContentPolicy
{
    public const string UnsupportedReply = "Por enquanto, não consigo interpretar imagens, vídeos, documentos, contatos ou localização. Pode escrever o que deseja registrar? Áudios curtos estão disponíveis no Essencial.";
    public const string CaptionReply = "Não consigo analisar o anexo. Para evitar registrar algo fora de contexto, envie sua descrição em uma mensagem de texto separada.";

    public static IncomingContentKind Classify(IFormCollection form)
    {
        var text = !string.IsNullOrWhiteSpace(form["Body"]);
        var type = form["MessageType"].ToString().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(form["Latitude"]) || !string.IsNullOrWhiteSpace(form["Longitude"])
            || type is "contacts" or "contact" or "location" or "sticker" or "reaction"
            || !string.IsNullOrWhiteSpace(form["Contacts"])) return IncomingContentKind.Unsupported;
        if (int.TryParse(form["NumMedia"], out var count) && count > 0)
        {
            if (count == 1 && WhatsAppAudioTranscriptionService.HasAudioMedia(form) && !text) return IncomingContentKind.Audio;
            return text ? IncomingContentKind.UnsupportedWithCaption : IncomingContentKind.Unsupported;
        }
        if (type.Length > 0 && type is not ("text" or "button" or "interactive")) return IncomingContentKind.Unsupported;
        return text ? IncomingContentKind.Text : IncomingContentKind.Empty;
    }
}

public static class ConversationTopicPolicy
{
    public const string Reply = "Posso ajudar com ciclo menstrual, sintomas, gravidez e funcionamento da Luma. Para outros assuntos, não consigo continuar a conversa por aqui. O que você quer acompanhar?";
    public static bool IsClearlyExternal(string message)
    {
        var text = MessageText.Normalize(message);
        if (new[] { "menstru", "ciclo", "gravidez", "gravida", "gesta", "camisinha", "pilula", "anticoncep", "sintoma", "humor", "plano", "suporte" }.Any(text.Contains)) return false;
        return new[]{"previsao do tempo", "cotacao do dolar", "resultado do jogo", "receita de bolo", "codigo em python", "codigo em javascript", "quem ganhou o jogo"}.Any(text.Contains);
    }
}
