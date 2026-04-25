import { ChatMessage, PhoneFrame } from "@/components/molecules/PhoneFrame";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const points = [
  {
    icon: "🌙",
    title: "Linguagem natural",
    text: 'Escreva como você fala: "menstruei hoje", "tô com cólica" ou "quando foi minha última menstruação?"',
  },
  {
    icon: "📊",
    title: "Histórico organizado",
    text: "Tudo fica registrado e você pode consultar quando quiser, sem precisar lembrar onde salvou.",
  },
  {
    icon: "🔔",
    title: "Lembretes opcionais",
    text: "Ative avisos sobre próxima menstruação ou anticoncepcional — só se quiser.",
  },
];

export function SolutionSection() {
  return (
    <section className="solucao">
      <div className="section-inner">
        <div className="solucao-grid">
          <div className="fade-up">
            <SectionHeading
              tag="A solução"
              title={
                <>
                  Registre tudo do jeito que você <em>já conversa</em>.
                </>
              }
              lead="A Luma é uma assistente de ciclo que você acessa por mensagem. Sem instalar nada novo, sem aprender interface nova — só mandar mensagem como você já faz todo dia."
            />
            <div className="solucao-points">
              {points.map((point) => (
                <div className="point-row" key={point.title}>
                  <div className="point-dot">{point.icon}</div>
                  <div>
                    <h4>{point.title}</h4>
                    <p>{point.text}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
          <div className="fade-up solucao-visual" style={{ transitionDelay: "0.15s" }}>
            <PhoneFrame>
              <ChatMessage type="bot" time="10:32" text="Oi! Tô aqui quando precisar registrar algo 🌙" />
              <ChatMessage type="user" time="10:33" text="tô com cólica forte hoje" />
              <ChatMessage
                type="bot"
                time="10:33"
                text={
                  <>
                    Registrei cólica forte para hoje ✅
                    <br />
                    Espero que melhore logo 💛
                  </>
                }
              />
              <div className="convo-divider">— mais tarde —</div>
              <ChatMessage type="user" time="18:10" text="quando é minha próxima menstruação?" />
              <ChatMessage
                type="bot"
                time="18:10"
                text="Pela sua média atual, ela está prevista para perto de 22/05. Mas pode variar um pouco — cada ciclo tem seu ritmo 🌿"
              />
            </PhoneFrame>
          </div>
        </div>
      </div>
    </section>
  );
}
