import { ButtonLink } from "@/components/atoms/Button";
import { isWaitlist } from "@/lib/site-mode";

export function DevelopmentSection() {
  return (
    <section className="desenvolvimento">
      <div className="section-inner fade-up">
        <span className="dev-badge">{isWaitlist ? "Em breve" : "Lançamento"}</span>
        <h2 className="section-h2">
          {isWaitlist ? "Uma conversa pode mudar sua rotina." : "Comece hoje."}
          <br />
          <em>A Luma acompanha você.</em>
        </h2>
        <p className="section-lead">
          {isWaitlist ? "Entre na lista de espera e receba o aviso quando as assinaturas forem abertas. Enquanto isso, conheça os recursos e o calendário demonstrativo." : "Ative seu plano, cadastre seu número e converse com a Luma pelo WhatsApp para registrar ciclo, sintomas, gravidez, lembretes e histórico."}
        </p>
        <div className="dev-counter">
          <div className="dev-stat">
            <strong id="counter-num">WhatsApp</strong>
            <span>atendimento direto</span>
          </div>
          <div className="dev-stat">
            <strong>V1</strong>
            <span>{isWaitlist ? "em preparação para o lançamento" : "pronta para uso"}</span>
          </div>
        </div>
        <ButtonLink variant="primary" href={isWaitlist ? "#lista" : "#precos"}>
          {isWaitlist ? "Quero entrar na lista" : "Quero meu plano"}
        </ButtonLink>
      </div>
    </section>
  );
}
