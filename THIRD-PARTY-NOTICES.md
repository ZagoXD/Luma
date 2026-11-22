# Dependências adicionadas para leitura de áudio

TagLibSharp 2.3.0 é utilizado, sem modificações, como assembly separado para ler
a duração de arquivos de áudio antes da transcrição. Licença: LGPL-2.1-only.

- Projeto e código-fonte: https://github.com/mono/taglib-sharp
- Versão distribuída: https://www.nuget.org/packages/TagLibSharp/2.3.0
- Texto da licença: https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html
- Copyright (c) 2006–2007 Brian Nickel; (c) 2009–2020 demais contribuidores.

A biblioteca não foi incorporada estaticamente nem modificada. Sua DLL pode ser
substituída por uma versão compatível. A referência a essa licença se aplica à
biblioteca, não altera automaticamente a licença do restante da aplicação.

FFmpeg é executado como processo externo para decodificar e limitar amostras de
áudio. O Docker instala o pacote da distribuição Linux, que mantém seus avisos
de licença. Código e termos: https://ffmpeg.org/legal.html.
