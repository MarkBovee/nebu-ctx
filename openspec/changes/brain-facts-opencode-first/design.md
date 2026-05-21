## Context

`nebu-ctx` heeft nu drie memory-lagen op papier, maar de praktijk klopt niet met de naamgeving. `docs/MEMORY.md` beschrijft brain als episodisch logboek voor raw prompts, assistant outputs en session summaries, terwijl knowledge de wake-up en recalllaag is. In code gebeurt dat ook zo: hooks en de OpenCode plugin sturen ruwe tekst of een simpele sessiesummary naar `ctx_brain`, en startup/compact flows lezen daarna vooral knowledge terug.

Dat model is te zwak voor langdurige project-memory. Het heeft geen typed facts, geen provenance die verder gaat dan string-prefixes, geen temporal state, geen correctiemodel en geen duidelijk onderscheid tussen raw transcript en canonical memory. Omdat OpenCode nu de primaire IDE is, is parity met Claude-hooks niet meer genoeg; OpenCode moet de referentie-lifecycle worden waar de rest op aansluit.

Tegelijk willen we de publieke 5-tool MCP surface behouden, offline-safe sync niet verliezen en geen server-side transcriptstore toevoegen. Raw journal data blijft daarom client-local. Hosted brain wordt fact-only en knowledge wordt een projection/public retrievallaag bovenop brain.

## Goals / Non-Goals

**Goals:**
- brain omzetten naar een fact-only, server-owned canonical memorylaag
- raw prompts, assistant output en andere lifecycle events lokaal journaliseren in plaats van server-side als brain-log op te slaan
- een gedeelde client lifecycle core invoeren waarop OpenCode, Claude en Copilot kunnen aansluiten
- OpenCode startup, continuation, compaction, idle flush en stop flows laten werken op verse brain-backed wake-up context
- typed fact ingest met provenance, confidence, temporal state, supersession en invalidation invoeren
- publieke `ctx(domain="memory", ...)` retrieval- en wakeupacties op een brain-backed projection laten werken zonder contractbreuk
- dashboard memory inspectie semantisch maken met actieve facts, superseded facts, corrections en wake-up composition
- offline replay idempotent houden voor journal-afgeleide fact batches

**Non-Goals:**
- raw transcript server-side bewaren of nieuwe hosted journal/end-to-end chat history introduceren
- de publieke MCP surface uitbreiden met aparte transcript- of journaling-tools
- in deze change direct een volledige graph-native memory engine of embeddings-first redesign bouwen
- legacy brain logentries automatisch naar nieuwe facts migreren op basis van heuristische transcript parsing

## Decisions

### 1. Brain wordt canonical fact store; journal blijft client-local

Hosted brain krijgt typed fact entries met semantische metadata in plaats van generieke key/value logregels. Raw lifecycle data blijft in een lokale journalstore per project/session, bijvoorbeeld JSONL-gebaseerd onder de client data dir.

Waarom:
- transcript en facts zijn andere datatypen met andere retention- en correctness-eisen
- raw transcript op server bewaren maakt privacy, volume en cleanup onnodig zwaar
- fact-only brain dwingt discipline af in wakeup, recall en dashboardweergave

Alternatieven:
- brain als mixed transcript+facts store houden: afgewezen, omdat retrieval en semantiek dan troebel blijven
- transcript ook server-side bewaren: afgewezen, omdat gebruiker expliciet alleen afgeleide facts in brain wil en lokale journal voldoende is voor debug/replay

### 2. Client extraheert fact candidates; server canonicaliseert en projecteert

De client verzamelt lifecycle events, bewaart ze lokaal in journalvorm en leidt er fact candidates uit af. De server blijft eigenaar van canonicalization: dedup, logical identity, supersession, invalidation, lifecycle status en public projection.

Waarom:
- client ziet editor- en hook-specifieke context het eerst
- server is juiste plek voor projectbrede consistentie, operators en canonical retrieval
- deze verdeling houdt offline fallback haalbaar zonder server-semantiek te dupliceren

Alternatieven:
- alle extractie server-side doen: afgewezen, omdat raw transcript dan toch server-visible moet worden
- alles client-side canonical maken: afgewezen, omdat cross-device consistency en dashboard-ownership dan wegvallen

### 3. Gedeelde lifecycle core vervangt editor-specifieke memory-logica

We introduceren een editor-onafhankelijke lifecycle core in de client met gestandaardiseerde events zoals `session_start`, `user_turn`, `assistant_turn_complete`, `tool_activity`, `pre_compact`, `post_compact`, `idle_flush` en `session_stop`. Claude-hooks en OpenCode plugin worden adapters op deze kern.

Waarom:
- huidige memorylogica zit verspreid tussen hook handlers en OpenCode-plugin met deels dubbele betekenis
- OpenCode-first vraagt één normatieve flow die andere editors volgen
- gedeelde eventmodellen maken tests en offline replay beter beheersbaar

Alternatieven:
- OpenCode apart verbeteren en Claude ongemoeid laten: afgewezen, omdat divergent gedrag snel terugkeert
- alleen bestaande hooks uitbreiden: afgewezen, omdat dat oude Claude-naamgeving tot architectuur maakt

### 4. OpenCode wordt primaire lifecycle-adapter

De OpenCode plugin gebruikt de lifecycle core direct en levert expliciete oorzaken mee zoals `startup`, `resume`, `compact`, `continuation`, `idle` en `stop`. `experimental.chat.system.transform` en `experimental.session.compacting` gebruiken brain-backed wake-up/continuation output als bron van waarheid.

Waarom:
- OpenCode is nu dagelijkse hoofdrouting
- plugin is al actief en heeft lifecycle-oppervlak voor startup, compacting, idle en continuation
- hierdoor testen we de echte hoofdflow in plaats van parity-gedrag aan de rand

Alternatieven:
- OpenCode op Claude parity laten steunen: afgewezen, omdat startup/continuation dan conceptueel second-class blijft

### 5. Knowledge wordt brain-backed projection in plaats van owner

De publieke memory surface blijft `ctx(domain="memory", action=...)`, maar knowledge recall/wakeup/status haalt zijn effectieve data uit brain-backed projectionlogica. Knowledge blijft bestaan als public retrieval/categorization view, niet als primaire eigenaar van de memorywaarheid.

Waarom:
- publieke contracten hoeven dan niet te breken
- brain kan semantisch owner worden zonder directe clientbreuk
- projection laat bestaande wakeup/recall flows gefaseerd meebewegen

Alternatieven:
- knowledge volledig verwijderen: afgewezen, te veel contract- en dashboardimpact voor één change
- knowledge owner laten: afgewezen, dan blijft brain secundair en mislukt kern-doel

### 6. Legacy brain logentries worden niet automatisch gemigreerd

Oude `ctx_brain` data blijft legacy/read-only of dashboard-historie, maar we proberen die niet automatisch naar facts om te zetten. Nieuwe brain fact storage krijgt aparte schema- en contractpaden.

Waarom:
- transcriptstring naar betrouwbare fact omzetten is te heuristisch en te riskant
- foute memorymigratie schaadt vertrouwen meer dan tijdelijke legacy-silo

Alternatieven:
- transcript salvage tijdens migratie: afgewezen voor kernchange; eventueel later handmatige tooling

## Risks / Trade-offs

- [Fact extractie mist belangrijke signalen] -> begin met hoge-signal categorieen zoals task, decision, constraint, preference en verified finding; breid later uit met tests per eventtype
- [OpenCode-first flow veroorzaakt tijdelijk gedragverschil met Claude/Copilot] -> definieer lifecycle core eerst en laat bestaande adapters daarop aansluiten in dezelfde change
- [Knowledge projection en brain owner lopen tijdelijk dubbel] -> houd duidelijke ownershipregels aan: brain is canonical, knowledge is derived/public only
- [Client-local journal groeit te hard] -> voeg retention, rotatie en session-scoped cleanup direct toe
- [Offline replay produceert duplicate facts] -> batch ingest krijgt deterministische identities per source scope, logical key en event batch
- [Dashboard krijgt mixed legacy en nieuwe brain data] -> voeg expliciete scheiding tussen legacy brain entries en semantic brain facts toe of verberg legacy standaard

## Migration Plan

1. Voeg nieuwe brain fact contracts, storage en services additief toe zonder oude publieke memoryacties direct te breken.
2. Introduceer client-local journal en lifecycle core; laat bestaande hook/plugin paden events daarin schrijven.
3. Schakel brain ingest om van raw transcript/session-summary writes naar fact-candidate batches.
4. Laat OpenCode plugin startup, continuation, compacting en idle flows brain-backed wake-up output gebruiken.
5. Laat knowledge recall/wakeup/status stap voor stap over brain-backed projection lopen.
6. Werk dashboard memory inspectie om naar semantic brain facts en lifecycle state.
7. Laat Claude/Copilot adapters op dezelfde lifecycle core landen zodat parity terugkeert op het nieuwe model.
8. Markeer legacy brain logentries als niet-canonical; rollback blijft mogelijk door nieuwe brain fact read paths uit te schakelen en bestaande knowledge flows te blijven gebruiken.

## Open Questions

- Welke minimale fact kinds gaan in v1 mee: alleen `task`, `decision`, `constraint`, `preference`, `fact`, `correction`, of ook `hypothesis` en `question` direct?
- Moet knowledge projection fysiek persisted blijven of mag een deel van wake-up selectie on-demand uit brain facts worden opgebouwd?
- Willen we legacy brain logentries nog zichtbaar houden in dashboard onder een aparte history-tab, of volledig uit operator-defaults halen?
