import { SectionHeading } from "@/components/molecules/SectionHeading";
import { Info } from "lucide-react";

const privacyItems = [
  ["Consentimento explícito.", "Antes de qualquer uso, você autoriza o que pode ser armazenado — sem letras miúdas."],
  ["Controle dos seus dados.", "Você poderá solicitar exclusão ou exportação dos seus registros a qualquer momento."],
  ["Sem diagnósticos.", "A Luma organiza registros pessoais. Ela não faz diagnóstico de nenhum tipo."],
  ["Só para você.", "Seus registros são usados apenas para organizar o seu próprio histórico."],
];

export function PrivacySection() {
  return (
    <section className="privacidade">
      <div className="section-inner">
        <SectionHeading
          tag="Confiança e privacidade"
          darkTitle
          title={
            <>
              Seus dados de ciclo
              <br />
              <em style={{ color: "var(--lavender)" }}>são íntimos.</em>
            </>
          }
          lead="Por isso, a Luma está sendo pensada desde o início com consentimento, controle da usuária e respeito à privacidade."
          centered
        />
        <div className="privacy-points fade-up" style={{ transitionDelay: "0.1s" }}>
          {privacyItems.map(([title, text]) => (
            <div className="privacy-item" key={title}>
              <div className="privacy-check">✓</div>
              <p>
                <strong>{title}</strong> {text}
              </p>
            </div>
          ))}
        </div>
        <div className="privacy-disclaimer fade-up" style={{ transitionDelay: "0.2s" }}>
          <Info size={16} strokeWidth={1.75} style={{ flexShrink: 0, marginTop: "0.15rem", color: "var(--lavender)" }} />
          <span>A Luma é um assistente de organização pessoal e <strong>não substitui orientação médica</strong>. Para questões de saúde, procure sempre um profissional de saúde.</span>
        </div>
      </div>
    </section>
  );
}
