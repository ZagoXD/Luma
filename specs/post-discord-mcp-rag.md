# Apresentação para a comunidade

Pessoal, comecei recentemente a pós em Engenharia de Software com IA Aplicada e isso me animou a retomar um projeto pessoal: a Luma!

A ideia nasceu em uma conversa com minha esposa: uma assistente pelo WhatsApp para ajudar no acompanhamento do ciclo menstrual e da gravidez. Hoje ela já tem cadastro, registros de ciclo e sintomas, estimativas de datas, calendário web, lembretes e transcrição de áudio.

O backend é C# com ASP.NET Core, Entity Framework Core e PostgreSQL, com Redis para controles de entrada e Docker para o ambiente. A interface web usa Next.js, e a integração conversacional usa OpenAI e Twilio.

Na evolução mais recente, integrei MCP de verdade: cliente e servidor com o SDK oficial, descoberta de ferramentas e chamadas pelo protocolo. A OpenAI escolhe as ferramentas via function calling, mas quem autoriza e executa as leituras e escritas é o backend. As sessões são privadas e vinculadas à usuária, sem um endpoint MCP público.

Também implementei RAG com embeddings e pgvector, combinado com busca textual em português. O acervo inicial é pequeno e versionado, com fontes; dados pessoais continuam nas tabelas do produto e não viram documentos vetoriais. Ainda quero ampliar esse acervo com revisão profissional, especialmente por ser um domínio sensível.

Um dos desafios mais interessantes é lidar com conversas fora da ordem: a usuária pode contar que menstruou enquanto ainda está no cadastro. A Luma guarda a intenção após consentimento e pede confirmação antes de efetivar o registro, em vez de tratar aquilo como resposta à pergunta anterior.

Estou trabalhando com TDD e testes de integração, incluindo idempotência, concorrência e falhas de ferramentas. Ela não faz diagnósticos nem substitui orientação médica.

Queria ouvir quem já trabalha com isso: como vocês evoluiriam as avaliações de agentes, a qualidade da recuperação e os controles contra respostas sem evidência? Experiências com MCP e RAG em .NET também seriam muito bem-vindas!
