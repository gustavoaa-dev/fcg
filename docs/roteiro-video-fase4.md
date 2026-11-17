# Roteiro do vídeo — Fase 4 (14 min)

Texto para falar, na ordem. Fale devagar; as frases já estão curtas.
O que aparece na tela está marcado como **Na tela**. Os comandos estão prontos para colar.

---

## Preparo (fora da gravação)

Rode este script na janela que vai aparecer no vídeo. Ele checa cluster, contexto do kubectl, pods, IP público, login, cache, Elasticsearch e Mongo — e grava as variáveis.

```powershell
cd C:\Users\Gustavo\fcg
.\preparar-gravacao.ps1
. C:\Users\Gustavo\fcg\.video\ambiente.ps1
$ip
```

O terceiro comando tem um **ponto e espaço** no começo — e caminho absoluto, para funcionar mesmo se o seu prompt estiver em `C:\WINDOWS\system32`. Sem o ponto, as variáveis `$ip`, `$token` e `$gameId` não ficam disponíveis na sessão e os comandos do roteiro falham.

A linha seguinte, `$ip`, tem que imprimir `48.216.131.164`. Se imprimir vazio, o ambiente não carregou nessa janela — e não adianta seguir.

Espere a linha verde `PRONTO PARA GRAVAR`. Se aparecer `[FALHA]`, o script diz qual item e você não grava.

Cuidado com uma armadilha real: quando o Docker Desktop inicia, ele troca o contexto do `kubectl` para `docker-desktop`. O script força `fcg-aks` de volta. Se você reiniciar o Docker no meio do dia, rode o script outra vez.

Confira antes de gravar: 14 pods `Running`, token com ~377 caracteres, e cada comando abaixo já testado uma vez.

---

## Bloco 1 — Abertura e desenho (0:00 a 1:30)

**Na tela:** o diagrama `docs\arquitetura\fcg-arquitetura.html` aberto no navegador (tecla `F` para tela cheia). Se preferir imagem estática, use `docs\arquitetura\fcg-arquitetura.png`.

**Fala:**

> Esse é o FCG, uma plataforma de jogos. Nesta última fase eu coloquei ela na nuvem, automatizei a entrega e troquei a busca.
>
> Em quinze minutos eu mostro cinco coisas: o cluster na Azure, a busca nova, o cache, o banco de avaliações e um deploy ao vivo.
>
> Primeiro, o desenho. O usuário entra pelo Kong, que é o portão de entrada. Atrás dele ficam quatro serviços: usuários, catálogo, pagamentos e notificações. Eles conversam por filas, usando RabbitMQ.
>
> Cada serviço tem o banco que faz sentido para ele. SQL Server para os dados de negócio. MongoDB para as avaliações. Redis como cache. E Elasticsearch para a busca.
>
> Tudo isso roda em Kubernetes, na Azure.

---

## Bloco 2 — Nuvem e cluster (1:30 a 3:30)

**Na tela:** portal da Azure, grupo de recursos `fcg-rg`. Depois o terminal.

**Fala (no portal):**

> Esse é o grupo de recursos do projeto. Aqui está o cluster Kubernetes gerenciado, então eu não administro máquina nenhuma.
>
> Ele tem dois nós, cada um com duas CPUs e oito gigabytes de memória.
>
> Aqui do lado está o registro de imagens, que é onde ficam as imagens Docker que os serviços usam. Ele é privado, só o cluster acessa.

**Na tela:** terminal.

```powershell
kubectl get nodes
kubectl get pods
kubectl get svc kong
```

**Fala:**

> No terminal: dois nós prontos, quatorze pods rodando e um endereço IP público na última coluna. É por esse IP que a aplicação é acessada de fora.
>
> Quatorze pods é o seguinte: quatro serviços, um gateway, o banco relacional, o MongoDB, o Redis, o Elasticsearch e a parte de observabilidade.

---

## Bloco 3 — Busca avançada (3:30 a 5:30)

**Na tela:** terminal, com as duas chamadas.

```powershell
curl.exe -s -w "`nhttp=%{http_code} tempo=%{time_total}s`n" "http://$ip`:8000/api/jogos/search?q=jgoo" -H "Authorization: Bearer $token"
curl.exe -s -w "`nhttp=%{http_code} tempo=%{time_total}s`n" "http://$ip`:8000/api/jogos/search?q=zumbi" -H "Authorization: Bearer $token"
```

**Fala:**

> A busca antiga consultava o SQL e não tolerava erro de digitação. Agora existe um índice separado, no Elasticsearch.
>
> Repare que eu escrevi "jgoo", faltando a letra o. Ele achou "Jogo de Ação" mesmo assim. Isso se chama busca aproximada: ele aceita erro de digitação.
>
> O resultado vem ordenado por relevância. O nome do jogo pesa três vezes mais que a descrição.
>
> E o índice não fica velho: quando um jogo é criado, editado ou removido, o índice é atualizado na mesma hora.

**Na tela:** segunda janela do terminal, com port-forward aberto.

```powershell
# numa segunda janela, antes:
kubectl port-forward svc/elasticsearch 9200:9200

# nesta:
curl.exe -s "http://localhost:9200/games/_count"
```

**Fala:**

> Aqui está a prova de que quem responde é o Elasticsearch e não o banco relacional. Esse é o índice, com a quantidade de jogos indexados.
>
> A busca é independente do SQL. Se o SQL estiver lento, a busca continua rápida.

---

## Bloco 4 — Cache (5:30 a 8:00)

**Na tela:** terminal.

```powershell
kubectl exec deploy/redis -- redis-cli --scan --pattern 'catalog:*'
kubectl exec deploy/redis -- redis-cli TTL catalog:games:all
```

**Fala:**

> Cache é uma cópia temporária da resposta. A lista de jogos é a consulta mais cara do catálogo, porque traz tudo.
>
> Então ela fica guardada no Redis por sessenta segundos. Aqui está a chave e o tempo que falta para expirar.

```powershell
kubectl exec deploy/redis -- redis-cli DEL catalog:games:all
curl.exe -s -o NUL -w "sem cache: %{time_total}s`n" "http://$ip`:8000/api/jogos" -H "Authorization: Bearer $token"
curl.exe -s -o NUL -w "com cache: %{time_total}s`n" "http://$ip`:8000/api/jogos" -H "Authorization: Bearer $token"
```

**Fala:**

> Eu apaguei a chave. Essa primeira chamada foi direto no banco: [diga o tempo].
>
> A segunda veio do cache: [diga o tempo].
>
> Com poucos jogos a diferença é de milissegundos. Com volume, essa diferença é a que segura o banco em pé.
>
> Um detalhe importante de projeto: se o Redis cair, a API não cai. Ela só vai no banco e fica mais lenta. O cache nunca é motivo para a aplicação parar.

**Se quiser número mais forte:** insira uns 2000 jogos antes de gravar. A diferença passa a ser de centenas de milissegundos.

---

## Bloco 5 — Banco de documentos (8:00 a 9:30)

**Na tela:** terminal.

```powershell
if (-not $ip -or -not $token) { throw "Ambiente nao carregado. Rode: . C:\Users\Gustavo\fcg\.video\ambiente.ps1" }

$jogos = curl.exe -s "http://$ip`:8000/api/jogos" -H "Authorization: Bearer $token" | ConvertFrom-Json
if (-not $jogos) { throw "Sem jogos na resposta: confira `$ip e `$token." }
$gameId = $jogos[0].id
"gameId: $gameId"

Set-Content "$tmp\aval.json" -Value '{"nota":5,"comentario":"Muito bom","tags":["acao","top"]}' -Encoding ascii -NoNewline
curl.exe -s -w "`nhttp=%{http_code}`n" -X PUT "http://$ip`:8000/api/jogos/$gameId/avaliacoes" -H "Authorization: Bearer $token" -H "Content-Type: application/json" --data-binary "@$tmp\aval.json"
curl.exe -s -w "`nhttp=%{http_code}`n" "http://$ip`:8000/api/jogos/$gameId/avaliacoes/resumo" -H "Authorization: Bearer $token"
```

**Fala:**

> Avaliação de jogo tem nota, comentário e tags. O problema é que as tags mudam de jogo para jogo: um tem "ação", outro tem "corrida", outro não tem nenhuma.
>
> Guardar isso em tabela rígida dá trabalho, com tabela extra e vários joins. Por isso avaliação vive no MongoDB, que é um banco de documentos.
>
> Enviei uma avaliação com duas tags. E aqui está o resumo: total e média.

**Na tela:** outro comando, mostrando o documento. Este lê a credencial do Secret do cluster, então funciona em qualquer janela do PowerShell, com ou sem o ambiente carregado.

```powershell
$u = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((kubectl get secret mongo-secret -o jsonpath='{.data.root-username}')))
$p = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((kubectl get secret mongo-secret -o jsonpath='{.data.root-password}')))
kubectl exec deploy/mongo -- mongosh -u $u -p $p --authenticationDatabase admin fcg_catalog --quiet --eval "db.avaliacoes.find().toArray()"
```

**Fala:**

> No banco, o documento aparece assim: um JSON solto, com as tags dentro dele. Não tem esquema fixo. Se amanhã eu quiser uma tag nova, não preciso alterar banco nem escrever migração.

---

## Bloco 6 — Deploy ao vivo (9:30 a 12:30)

### Entenda antes de gravar (não narre isto)

A aba **Actions** é onde o GitHub mostra cada execução do arquivo `.github/workflows/ci-cd.yml`. Um `git push` na branch `master` cria uma linha nova no topo da lista. Cada linha é uma "run", e cada run tem dois blocos (jobs): `build-test` e `image-deploy`. Clicando no job, você vê os steps com o log ao vivo.

Nomes que vão aparecer na tela, na ordem: `Restore`, `Build`, `Testes unitarios`, `Build e push da imagem no ACR`, `Rolling update no AKS`.

Pré-requisitos: repositório limpo, última run verde, e esta URL aberta numa aba:

`https://github.com/gustavoaa-dev/fcg-catalog-api/actions`

### Passo 1 — a mudança (9:30)

**Na tela:** VS Code com o arquivo aberto.

```powershell
cd C:\Users\Gustavo\fcg\.fase2-repos\fcg-catalog-api
code FCG.CatalogAPI.API\Controllers\GamesController.cs
```

Vá até a linha 137. Ela está assim:

```csharp
return BadRequest(new { mensagem = "Informe o termo de busca." });
```

Troque por:

```csharp
return BadRequest(new { mensagem = "Informe o termo de busca (ex.: jogo)." });
```

**Fala:**

> Agora o deploy. Eu vou mudar uma linha do código, mandar para o GitHub e mostrar a esteira levando isso até produção sozinha.
>
> Essa é a mensagem que aparece quando alguém busca sem digitar nada. Vou deixar ela mais clara, dizendo que pode buscar por "jogo".

Salve o arquivo com `Ctrl+S`.

### Passo 2 — commit e push (9:50)

**Na tela:** terminal.

```powershell
git status --short
```

Esperado: exatamente uma linha, `M FCG.CatalogAPI.API/Controllers/GamesController.cs`. Se aparecer mais coisa, pare: tem alteração que não é sua.

```powershell
git commit -am "chore: mensagem de busca vazia"
git push origin master
```

**Fala:**

> Uma linha alterada, um commit, um push. Daqui para frente eu não toco mais em servidor.

O push responde com algo como `1 file changed` e `master -> master`. Esse push é o gatilho.

### Passo 3 — a esteira rodando (10:10)

**Na tela:** navegador na aba Actions. Aperte `F5` se a lista não atualizar sozinha.

O que você vê: uma linha nova no topo, com o nome `ci-cd`, a mensagem `chore: mensagem de busca vazia` e um círculo amarelo girando. Clique nela.

Dentro da run aparecem dois blocos. Clique no primeiro, `build-test`, para abrir o log.

**Fala:**

> A esteira começou sozinha. São quatro coisas, em duas etapas.
>
> Primeira: compilar e rodar os testes. Se um teste falhar, para aqui e nada vai para produção.
>
> Segunda: construir a imagem Docker. Repare que a etiqueta é o número do commit, então cada versão fica rastreável.
>
> Terceira: enviar a imagem para o registro na Azure.
>
> Quarta: avisar o cluster para usar a imagem nova.

Enquanto fala, os steps vão ficando com check verde, um a um. Não precisa esperar parado: narre o que já apareceu.

### Passo 4 — o cluster trocando o pod (11:10)

**Na tela:** terminal, segunda janela se preferir.

```powershell
kubectl get pods -l app=catalog-api -w
```

O que aparece: o pod antigo continua `Running`; um segundo pod entra como `ContainerCreating`, depois `Running`; aí o antigo vira `Terminating` e some. `Ctrl+C` para encerrar o watch.

**Fala:**

> Enquanto ele trabalha, o pod antigo continua atendendo. O Kubernetes só tira o antigo quando o novo estiver pronto. Isso é o que garante que ninguém veja erro durante o deploy.

### Passo 5 — a prova (11:50)

**Na tela:** de volta à aba Actions, quando o círculo ficar verde.

**Fala:**

> A run ficou verde: build, teste, imagem publicada e cluster atualizado.

**Na tela:** terminal.

```powershell
curl.exe -s -w "`nhttp=%{http_code}`n" "http://$ip`:8000/api/jogos/search?q=" -H "Authorization: Bearer $token"
```

**Fala:**

> E aqui está a mudança no ar: o texto agora cita "ex.: jogo". Sem eu ter tocado em servidor. Um commit, um push, um minuto e meio.

Se a resposta ainda vier com o texto antigo, espere 10 segundos e repita: o pod novo pode estar no fim do rollout.

### Se algo falhar

- **Run vermelha:** clique no step vermelho, leia a mensagem e diga em voz alta o que falhou. Uma run vermelha explicada vale mais que uma verde sem explicação.
- **Nenhuma run apareceu:** confira se o push foi para `master` (`git branch --show-current`) e recarregue a página.
- **Pod não muda:** `kubectl describe deployment catalog-api | Select-String -Pattern 'Image|Events' -Context 0,2`.
- **Mudou o código e não mudou nada na tela:** o pod pode ter subido com a imagem antiga por cache. `kubectl rollout restart deployment/catalog-api` e repita.

---

## Bloco 7 — Repositórios e fechamento (12:30 a 14:00)

**Na tela:** GitHub, três arquivos.

**Fala:**

> Três arquivos que provam o que eu mostrei.
>
> Esse é o arquivo da esteira, no próprio repositório. Ela está versionada como o código.
>
> Esses são os manifestos do Kubernetes: os serviços, os volumes, o Elasticsearch e o segredo do broker.
>
> E esse é o código do índice de busca: ele monta a consulta com busca aproximada e o peso no nome.
>
> O que ficou de fora, e eu prefiro dizer: infraestrutura como código com Terraform, um cofre gerenciado de segredos e o scanner de vulnerabilidade na imagem. Nenhum dos três era obrigatório, os dois primeiros estão no próximo passo natural.
>
> O cluster pode ser desligado com um comando e o custo fixo fica em torno de cento e quarenta reais por mês. Obrigado.

---

## Se algo falhar no meio

- **Comando não imprimiu nada:** quase sempre é variável vazia naquela janela — o `curl -s` esconde o erro. Rode `$ip` e `$token.Length`. Se vier vazio, a janela não recebeu o ambiente: `. C:\Users\Gustavo\fcg\.video\ambiente.ps1`. Vale principalmente para a segunda janela, a do port-forward.
- **`Não é possível indexar em uma matriz nula`:** o `$ip` estava vazio, então o `/api/jogos` não respondeu e o resultado veio nulo. Mesma cura: carregar o ambiente com caminho absoluto.
- **`MongoServerError: Command aggregate requires authentication`:** credencial vazia. Use o comando do bloco 5 que lê o Secret, ou carregue o ambiente.
- **`http=000`:** não conectou. Confira `$ip` e se o cluster está `Running`.
- **`http=401`:** token vencido. Rode `.\preparar-gravacao.ps1` e carregue o ambiente de novo.
- **Comando errado:** repita o comando, não reinicie a fala.
- **Busca vazia:** o Elasticsearch tem atualização de um segundo. Espere e repita.
- **Pipeline vermelha:** leia o log, diga o que falhou e siga para o fechamento. Mostrar diagnóstico vale mais que fingir que passou.
- **Pod não sobe:** `kubectl describe pod` e `kubectl logs`. Não corte a gravação por isso.

## Depois de gravar

Reverta a mudança da mensagem, se quiser o código original, e desligue o cluster:

```powershell
git -C C:\Users\Gustavo\fcg\.fase2-repos\fcg-catalog-api checkout -- FCG.CatalogAPI.API/Controllers/GamesController.cs
az aks stop -n fcg-aks -g fcg-rg
```
