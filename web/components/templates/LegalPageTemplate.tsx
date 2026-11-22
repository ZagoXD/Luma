import Link from "next/link";
import { legalVersion } from "@/lib/legal";

export function LegalPageTemplate({ privacy = false }: { privacy?: boolean }) {
  return <main className="account-page"><article className="legal-document">
    <Link href="/">Voltar à Luma</Link>
    <h1>{privacy ? "Política de Privacidade" : "Termos de Uso"}</h1>
    <p>Versão {legalVersion}. Aplicável à Luma, assistente de acompanhamento do ciclo menstrual e gravidez.</p>
    <h2>Responsável e contato</h2>
    <p>Gustavo Luiz Conceição Zago, CNPJ 64.406.839/0001-07. R. João e Guiomar Soeira, 155, Reserva Real, Ribeirão Preto, SP, Brasil, CEP 14094-645. Contato e solicitações de privacidade: <a href="mailto:skyziinxd@gmail.com">skyziinxd@gmail.com</a>.</p>
    {privacy ? <>
      <h2>Dados e finalidades</h2>
      <p>Tratamos nome, e-mail, CPF, celular, credenciais protegidas, verificações e registros de aceite para criar, autenticar e proteger a conta. Dados de plano e transações servem para cobrança e controle de acesso. O tratamento necessário ao contrato e às obrigações legais não depende de consentimento para publicidade.</p>
      <p>Mediante consentimento específico e destacado, tratamos os dados sensíveis que você decidir informar: ciclo, menstruação, sintomas, humor, relações sexuais, contracepção e gravidez. Eles permitem organizar seu histórico, calendário, estimativas e respostas. Você pode retirar esse consentimento pelo contato acima ou excluir a conta; os recursos que dependem desses dados deixarão de funcionar.</p>
      <h2>Inteligência artificial e fornecedores</h2>
      <p>Mensagens e o contexto necessário são processados pela OpenAI para interpretação e respostas. Áudios podem ser transcritos pela ElevenLabs. A Twilio fornece mensagens WhatsApp e verificação por SMS; a Resend envia e-mails; o Mercado Pago processa cobranças; AWS e Cloudflare fornecem infraestrutura e armazenamento. Esses serviços podem envolver processamento fora do Brasil, sujeito às medidas e instrumentos exigidos pela LGPD.</p>
      <p>Não enviamos histórico de saúde como descrição de cobrança. Número completo do cartão e código de segurança são capturados pelos campos seguros do provedor de pagamento, não pelo backend da Luma. Não vendemos dados de saúde e não criamos uma base compartilhada de conhecimento com relatos pessoais das usuárias.</p>
      <h2>Segurança e retenção</h2>
      <p>Contabilizamos mensagens admitidas, duração de áudios, solicitações de imagem e unidades técnicas de processamento para aplicar as franquias e prevenir abuso. Esse controle técnico não armazena o conteúdo das conversas de saúde.</p>
      <p>Usamos controles de acesso, conexão protegida e criptografia de dados pessoais no banco. Nenhum sistema é imune a incidentes. A conta mantém os registros necessários ao serviço até a exclusão. Imagens educativas temporárias usam uma regra de expiração de um dia; a remoção pelo armazenamento é assíncrona.</p>
      <p>Na exclusão, interrompemos cobranças e eliminamos dados de contato e de saúde do ambiente ativo. Enquanto a interrupção externa estiver pendente, conservamos apenas o necessário para concluir a solicitação. Backups possuem retenção de sete dias e podem conter dados até expirarem; uma restauração deve reaplicar as exclusões. Registros financeiros mínimos podem permanecer, separados dos dados de saúde, para obrigações legais e exercício de direitos, sem permitir reativação da conta.</p>
      <h2>Seus direitos</h2>
      <p>Você pode solicitar confirmação de tratamento, acesso, correção, informações de compartilhamento, portabilidade quando aplicável, revogação do consentimento e eliminação, observadas as hipóteses legais de conservação. Escreva ao contato indicado; podemos verificar sua identidade para proteger seus dados.</p>
      <h2>Notificações, lista de espera e cookies</h2>
      <p>Lembretes são opcionais, dependem de plano e configuração, e podem ser desativados. Na lista de espera, coletamos apenas os dados informados para avisar sobre o lançamento; participar não cria assinatura nem autoriza cobrança. Você pode pedir a remoção pelo mesmo contato. O site usa cookie necessário de autenticação; rastreamento publicitário, se adicionado, precisará de informação e controles próprios.</p>
    </> : <>
      <h2>Serviço e limites</h2>
      <p>A Luma é destinada a pessoas com 18 anos ou mais. Organiza registros e fornece estimativas educativas por WhatsApp e pelo painel web. Não substitui consulta, pré-natal, exame ou atendimento de emergência; não diagnostica gravidez ou doenças e não determina se um sangramento é normal. Previsões de ovulação e janela fértil não são método contraceptivo. Respostas de IA podem conter erros; procure um profissional para decisões de saúde.</p>
      <h2>Conta e dados</h2>
      <p>Informe dados corretos, mantenha suas credenciais protegidas e confirme o celular. O tratamento dos dados sensíveis depende de consentimento específico, separado do aceite destes termos. Consulte a <Link href="/privacidade">Política de Privacidade</Link>. Não envie dados de terceiros sem autorização.</p>
      <h2>Planos e cobrança</h2>
      <p>O Básico inclui até 10 mensagens processadas pela IA por dia e o Essencial, até 20, sem cota mensal de mensagens. A contagem renova à meia-noite no horário de São Paulo. No Essencial, cada áudio usa uma mensagem e sua duração no saldo de 10 minutos por mês, com até 60 segundos por áudio. São permitidas até duas solicitações de imagem por mês. Tentativas admitidas podem consumir a cota mesmo se houver falha do provedor; consulte o suporte em caso de divergência.</p>
      <p>Após a cota de IA, o menu registrar do WhatsApp permite continuar registrando dados e corrigir a última menstruação, sem IA. O painel, calendário, suporte e configurações permanecem disponíveis. Os controles contra abuso e envio simultâneo continuam aplicáveis. O cadastro inicial possui uma cota separada de até 12 mensagens por dia.</p>
      <p>Básico: R$ 11,00 por mês ou R$ 70,80 por ano. Essencial: R$ 20,00 por mês ou R$ 118,80 por ano. O valor equivalente de R$ 5,90 ou R$ 9,90 mensais refere-se ao anual cobrado integralmente à vista, não a parcelas mensais. O checkout apresenta o valor total e o período antes da confirmação.</p>
      <p>O Essencial inclui recursos adicionais como áudios, imagens educativas e lembretes. O acesso depende de pagamento aprovado; autorização do cartão ou abertura do checkout não significam pagamento. A primeira cobrança pode ser assíncrona. Os dados de cartão são processados pelo Mercado Pago.</p>
      <h2>Renovação e cancelamento</h2>
      <p>A contratação autoriza cobranças recorrentes no período escolhido, informadas no checkout. A renovação automática pode ser desativada no painel: isso impede novas renovações e mantém o período já pago. Alterar plano ou período pode exigir nova autorização, sempre indicada antes de confirmar.</p>
      <p>Cancelar imediatamente é uma ação diferente: encerra o acesso e interrompe futuras cobranças. Mostramos uma confirmação antes dessa ação. O cancelamento, por si só, não representa estorno automático; os direitos legais de reembolso permanecem preservados.</p>
      <h2>Arrependimento e reembolso</h2>
      <p>Nas contratações online, você pode exercer o direito legal de arrependimento em sete dias, contados conforme o art. 49 do CDC. Em Configurações da conta, a área de Arrependimento e reembolso registra a solicitação e permite acompanhar o processamento. Você também pode solicitar pelo suporte. Não descontamos taxas de processamento do reembolso integral devido nesse caso.</p>
      <p>O estorno retorna pelo meio original. A solicitação e a confirmação serão registradas; a visualização na fatura depende do emissor. O fim da janela automática de sete dias não exclui outros direitos, como pedidos por cobrança indevida ou falha do serviço, que podem ser tratados pelo suporte.</p>
      <h2>Exclusão e suporte</h2>
      <p>Em Configurações, você pode pedir a exclusão confirmando sua senha. Interrompemos as cobranças e eliminamos os dados conforme a política de privacidade. Exclusão da conta e reembolso são solicitações distintas. Em caso de falha de comunicação com o provedor, registramos a pendência para concluir a interrupção e informamos que a operação ainda não foi confirmada.</p>
      <p>O suporte está disponível no painel e pelo e-mail informado acima. Recursos podem depender da disponibilidade de terceiros. Não limitamos os direitos do consumidor nem afastamos responsabilidades previstas em lei. Mudanças relevantes nestes documentos serão informadas; um novo consentimento será solicitado quando necessário.</p>
    </>}
    <p><Link href={privacy ? "/termos" : "/privacidade"}>{privacy ? "Termos de Uso" : "Política de Privacidade"}</Link></p>
  </article></main>;
}
