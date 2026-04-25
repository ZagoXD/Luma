import { InfoCard } from "@/components/molecules/Cards";
import { SectionHeading } from "@/components/molecules/SectionHeading";

const items = [
  ["🔴", "Início da menstruação", 'Só falar "menstruei hoje" — ela registra e inicia o ciclo.'],
  ["✅", "Fim da menstruação", '"Acabou ontem" já é suficiente para fechar o ciclo.'],
  ["💧", "Intensidade do fluxo", "Leve, médio ou intenso — você atualiza quando quiser."],
  ["😣", "Cólicas e sintomas", "Dor de cabeça, náusea, cansaço, inchaço e muito mais."],
  ["💭", "Humor e bem-estar", "Registre como você está se sentindo ao longo do ciclo."],
  ["🔔", "Lembretes", "Aviso antes da próxima menstruação ou para anticoncepcional."],
  ["📅", "Histórico do ciclo", "Consulte duração média, sintomas frequentes e padrões."],
  ["🔒", "Registros íntimos", "Dados opcionais e privados, armazenados com cuidado."],
];

export function RegisterSection() {
  return (
    <section className="registrar">
      <div className="section-inner">
        <SectionHeading
          tag="Funcionalidades"
          title={
            <>
              O que você poderá
              <br />
              <em>registrar e consultar</em>
            </>
          }
          lead="Tudo em linguagem simples, sem formulários complicados."
        />
        <div className="register-grid">
          {items.map(([icon, title, text], index) => (
            <InfoCard
              key={title}
              className="reg-card"
              delay={`${0.05 * (index + 1)}s`}
              icon={<span className="reg-emoji">{icon}</span>}
              title={title}
              text={text}
            />
          ))}
        </div>
      </div>
    </section>
  );
}
