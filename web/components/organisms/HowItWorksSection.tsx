import { SectionHeading } from "@/components/molecules/SectionHeading";

const steps = [
  ["1", "Crie sua conta", "Cadastre seus dados e o celular que será usado no WhatsApp."],
  ["2", "Escolha seu plano", "Ative o plano que combina com a sua rotina e libere a Luma."],
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
