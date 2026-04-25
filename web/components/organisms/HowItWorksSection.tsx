import { SectionHeading } from "@/components/molecules/SectionHeading";

const steps = [
  ["1", "Entre na lista", "Cadastre seu interesse aqui na página. É rápido e gratuito."],
  ["2", "Receba o acesso", "Quando a Luma estiver disponível, você recebe um convite no WhatsApp."],
  ["3", "Converse normalmente", "Mande mensagens como você faz com qualquer contato."],
  ["4", "Tudo organizado", "A Luma registra, calcula previsões e responde quando você perguntar."],
];

export function HowItWorksSection() {
  return (
    <section className="como" id="como">
      <div className="section-inner">
        <SectionHeading tag="Passo a passo" title="Como funciona" lead="Simples assim." centered />
        <div className="steps">
          {steps.map(([num, title, text], index) => (
            <div className="step fade-up" style={{ transitionDelay: `${0.1 * (index + 1)}s` }} key={title}>
              <div className="step-num">{num}</div>
              <h4>{title}</h4>
              <p>{text}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
