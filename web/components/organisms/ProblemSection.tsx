import { PainCard } from "@/components/molecules/Cards";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const cards = [
  {
    icon: "📱",
    title: "O app fica esquecido",
    text: "Depois de alguns dias sem abrir, o histórico fica incompleto e as previsões perdem sentido.",
    delay: "0.1s",
  },
  {
    icon: "🗓",
    title: "Registro manual é chato",
    text: "Abrir, navegar até a tela certa, marcar sintomas um por um. Pouca gente faz isso no dia a dia.",
    delay: "0.2s",
  },
  {
    icon: "💭",
    title: "A memória falha",
    text: "Quando você finalmente abre o app, já não lembra quando exatamente a menstruação começou ou acabou.",
    delay: "0.3s",
  },
];

export function ProblemSection() {
  return (
    <section className="problema">
      <div className="section-inner">
        <SectionHeading
          tag="O problema"
          title={
            <>
              Você baixa o app.
              <br />
              <em>Mas esquece de abrir.</em>
            </>
          }
          lead="Apps de ciclo dependem de você lembrar de acessá-los, preencher campos e manter tudo atualizado. Na vida real, isso raramente acontece."
        />
        <div className="problema-cards">
          {cards.map((card) => (
            <PainCard key={card.title} {...card} />
          ))}
        </div>
      </div>
    </section>
  );
}
