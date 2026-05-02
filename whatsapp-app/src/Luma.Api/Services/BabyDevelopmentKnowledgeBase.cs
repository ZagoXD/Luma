namespace Luma.Api.Services;

public sealed record BabyDevelopmentInfo(
    int Week,
    string SizeRange,
    string WeightRange,
    string Comparison,
    string Summary,
    string SafeNote);

public static class BabyDevelopmentKnowledgeBase
{
    public static BabyDevelopmentInfo? GetByWeek(int week)
    {
        if (week is < 4 or > 42)
        {
            return null;
        }

        return week switch
        {
            <= 5 => Create(week, "cerca de 1 a 2 mm", "muito pequeno para estimar com precisão", "uma sementinha", "Nesta fase inicial, as estruturas básicas começam a se formar e a confirmação/acompanhar por pré-natal é essencial."),
            6 => Create(week, "cerca de 4 a 6 mm", "menos de 1 g", "uma lentilha", "O coração pode começar a ser visualizado em alguns exames, dependendo da datação e do ultrassom."),
            7 => Create(week, "cerca de 8 a 10 mm", "menos de 1 g", "um mirtilo pequeno", "O desenvolvimento ainda é muito inicial, com crescimento rápido semana a semana."),
            8 => Create(week, "cerca de 1,5 a 2 cm", "cerca de 1 g", "uma framboesa", "Braços, pernas e primeiras estruturas do rosto continuam se diferenciando."),
            9 => Create(week, "cerca de 2 a 2,5 cm", "cerca de 2 g", "uma uva", "O bebê segue crescendo rápido e os principais sistemas continuam em formação."),
            10 => Create(week, "cerca de 3 a 3,5 cm", "cerca de 4 g", "um morango pequeno", "Dedinhos e traços iniciais ficam mais definidos ao longo das próximas semanas."),
            11 => Create(week, "cerca de 4 a 4,5 cm", "cerca de 7 g", "um figo pequeno", "A cabeça ainda é proporcionalmente maior e o crescimento corporal acelera aos poucos."),
            12 => Create(week, "cerca de 5 a 6 cm", "cerca de 14 g", "uma ameixa pequena", "O bebê já tem várias estruturas formadas e seguirá amadurecendo durante a gravidez."),
            13 => Create(week, "cerca de 7 a 8 cm", "cerca de 20 a 25 g", "um limão pequeno", "O segundo trimestre se aproxima e o crescimento passa a ficar mais perceptível nos exames."),
            14 => Create(week, "cerca de 8 a 9 cm", "cerca de 40 g", "um pêssego pequeno", "O bebê continua amadurecendo, com movimentos que podem aparecer no ultrassom."),
            15 => Create(week, "cerca de 10 cm", "cerca de 70 g", "uma maçã pequena", "O crescimento corporal segue, e pele e ossos continuam se desenvolvendo."),
            16 => Create(week, "cerca de 11 a 12 cm", "cerca de 100 g", "um abacate pequeno", "Muitas gestantes começam a notar mudanças corporais mais claras nesta fase."),
            17 => Create(week, "cerca de 13 cm", "cerca de 140 g", "uma pera", "O bebê ganha peso aos poucos e os movimentos continuam evoluindo."),
            18 => Create(week, "cerca de 14 cm", "cerca de 190 g", "um pimentão", "A fase costuma ser marcada por crescimento e amadurecimento dos sentidos."),
            19 => Create(week, "cerca de 15 cm", "cerca de 240 g", "uma manga pequena", "O corpo fica mais proporcional e a movimentação pode ficar mais perceptível com o tempo."),
            20 => Create(week, "cerca de 16 a 17 cm", "cerca de 300 g", "uma banana", "Metade da gestação se aproxima, considerando uma gravidez em torno de 40 semanas."),
            21 => Create(week, "cerca de 26 cm da cabeça aos pés", "cerca de 360 g", "uma cenoura", "A partir daqui, muitas referências passam a medir da cabeça aos pés."),
            22 => Create(week, "cerca de 27 a 28 cm", "cerca de 430 g", "um mamão papaia pequeno", "O bebê segue ganhando peso e amadurecendo órgãos e pele."),
            23 => Create(week, "cerca de 28 a 29 cm", "cerca de 500 g", "uma berinjela pequena", "O acompanhamento pré-natal ajuda a avaliar crescimento, placenta e bem-estar."),
            24 => Create(week, "cerca de 30 cm", "cerca de 600 g", "uma espiga de milho", "Pulmões e sistema nervoso continuam amadurecendo."),
            25 => Create(week, "cerca de 34 cm", "cerca de 660 a 700 g", "uma couve-flor pequena", "O bebê segue acumulando gordura e ganhando força nos movimentos."),
            26 => Create(week, "cerca de 35 cm", "cerca de 760 g", "uma abobrinha", "Audição e movimentos ficam mais evidentes para muitas gestantes."),
            27 => Create(week, "cerca de 36 cm", "cerca de 875 g", "um alface pequeno", "O fim do segundo trimestre se aproxima e o ganho de peso aumenta."),
            28 => Create(week, "cerca de 37 a 38 cm", "cerca de 1 kg", "uma berinjela grande", "O terceiro trimestre começa para muitas referências obstétricas."),
            29 => Create(week, "cerca de 38 a 39 cm", "cerca de 1,15 kg", "uma abóbora pequena", "O bebê continua acumulando gordura e amadurecendo o cérebro."),
            30 => Create(week, "cerca de 39 a 40 cm", "cerca de 1,3 kg", "um repolho", "O espaço vai ficando menor e os movimentos podem mudar de padrão."),
            31 => Create(week, "cerca de 41 cm", "cerca de 1,5 kg", "um coco", "O bebê segue ganhando peso, e o pré-natal acompanha pressão, crescimento e sintomas."),
            32 => Create(week, "cerca de 42 cm", "cerca de 1,7 kg", "uma jaca pequena", "O amadurecimento pulmonar e cerebral continua avançando."),
            33 => Create(week, "cerca de 43 a 44 cm", "cerca de 1,9 kg", "um abacaxi", "O bebê pode começar a se posicionar para o nascimento, mas isso varia bastante."),
            34 => Create(week, "cerca de 45 cm", "cerca de 2,1 kg", "um melão pequeno", "O ganho de peso segue importante nas próximas semanas."),
            35 => Create(week, "cerca de 46 cm", "cerca de 2,4 kg", "um melão", "O bebê continua amadurecendo e acumulando gordura."),
            36 => Create(week, "cerca de 47 cm", "cerca de 2,6 kg", "um mamão grande", "A reta final se aproxima, com consultas geralmente mais frequentes."),
            37 => Create(week, "cerca de 48 a 49 cm", "cerca de 2,9 kg", "uma acelga", "A gravidez já entra em uma fase considerada a termo inicial por muitas referências."),
            38 => Create(week, "cerca de 49 a 50 cm", "cerca de 3,1 kg", "um alho-poró grande", "O bebê segue pronto para ganhar peso e finalizar amadurecimentos."),
            39 => Create(week, "cerca de 50 cm", "cerca de 3,3 kg", "uma melancia pequena", "A data provável do parto é uma estimativa, não uma data exata."),
            40 => Create(week, "cerca de 50 a 52 cm", "cerca de 3,4 kg", "uma abóbora grande", "A data provável do parto costuma ser calculada em torno desta semana."),
            _ => Create(week, "varia bastante nesta fase", "varia conforme o bebê", "um recém-nascido a termo", "Depois de 40 semanas, o acompanhamento com obstetra ajuda a decidir os próximos cuidados.")
        };
    }

    private static BabyDevelopmentInfo Create(int week, string sizeRange, string weightRange, string comparison, string summary)
    {
        return new BabyDevelopmentInfo(
            week,
            sizeRange,
            weightRange,
            comparison,
            summary,
            "Esses dados são uma estimativa geral de desenvolvimento fetal e não substituem ultrassom, pré-natal ou orientação médica.");
    }
}
