> **Language / Langue**: [English](cli.md) · **Français**

# Référence de la ligne de commande

PortAtlas est un ensemble de commandes. Chaque commande ne lit que ses propres options : une option donnée à une commande qui ne la lit pas, une option inconnue ou une valeur hors de la liste admise arrêtent le programme avec une erreur et le code de sortie 1.

```text
PortAtlas <command> [options]
```

| Commande | Rôle |
| :--- | :--- |
| [`audit`](#audit) | Balayer les sockets en continu et enregistrer les flux dédupliqués |
| [`map`](#map) | Produire une carte topologique HTML et/ou un diagramme Draw.io à partir des journaux |
| [`export rules`](#export-rules) | Consolider les journaux en matrice de règles de pare-feu |
| [`export ndjson`](#export-ndjson) | Convertir des journaux CSV et NDJSON en un fichier NDJSON |
| [`resolve`](#resolve) | Fusionner des caches DNS et écrire les noms d'hôtes dans les journaux |
| [`lookup`](#lookup) | Diagnostiquer la résolution de nom d'une adresse IP |
| [`config init`](#config-init) | Écrire un fichier de configuration avec les valeurs par défaut |

Sans commande, PortAtlas affiche l'aide et sort avec le code 0. `-h` ou `--help` après une commande affiche l'aide de cette commande.

## Origine des valeurs

Une valeur provient, par ordre de priorité :

1. de la ligne de commande ;
2. du fichier de configuration donné par `--config`, sinon de `portatlas.json` dans le dossier de travail, sinon de `portatlas.json` à côté de l'exécutable ;
3. de la valeur par défaut intégrée au programme.

La clé du fichier de configuration correspondant à chaque option figure dans les tableaux ci-dessous et est décrite dans la [référence de la configuration](configuration.fr.md).

## Option globale

| Option | Valeur | Défaut | Effet |
| :--- | :--- | :--- | :--- |
| `--config` | `<path>` | `portatlas.json` | Fichier de configuration lu par toutes les commandes |
| `--version` | aucune | | Affiche la version et quitte (commande racine uniquement) |

## Jetons de nom de fichier

Les chemins de sortie, et les chemins du fichier de configuration, acceptent ces jetons :

| Jeton | Remplacé par |
| :--- | :--- |
| `{ServerName}` | Nom de la machine |
| `{HostName}` | Nom de la machine (même valeur que `{ServerName}`) |
| `{Date}` | Date UTC, `yyyy-MM-dd` |
| `{Timestamp}` | Date et heure UTC, `yyyyMMdd_HHmmss` |

## Familles d'options partagées

### Entrées

Lues par `map`, `export rules`, `export ndjson` et `resolve`.

| Option | Valeur | Défaut | Effet |
| :--- | :--- | :--- | :--- |
| `-i`, `--input` | `<path>`, répétable | `OutputCsvPath` de la configuration | Un fichier, un dossier ou un motif sur le nom de fichier (`audits/nas-*.csv`) |

Un dossier ou un motif apporte ses fichiers portant les extensions lues par la commande (`.csv` pour `map` ; `.csv`, `.ndjson`, `.jsonl` pour `export rules`, `export ndjson` et `resolve`). Les sous-dossiers ne sont pas lus. Quand aucun fichier ne correspond, la commande affiche `No audit log found for: …` et sort avec le code 1.

Chaque journal CSV est lu avec le séparateur trouvé sur sa propre première ligne (`,`, `;`, tabulation ou `|`) : des journaux écrits avec des valeurs différentes de `CsvDelimiter` peuvent donc être lus ensemble ; `CsvDelimiter` n'est retenu que lorsque la première ligne ne tranche pas (fichier vide, ou égalité).

Parmi les fichiers d'un dossier ou d'un motif, la commande ne lit que les journaux d'audit : un CSV dont la première ligne nomme la colonne `ServerName` ou `LocalPort`, un NDJSON dont le premier enregistrement porte l'une de ces deux propriétés. Une matrice écrite par `export rules`, un journal sans ligne d'en-tête, un fichier vide ou un fichier qui ne peut pas être ouvert (tenu par un autre programme, accès refusé) est écarté. Quand plusieurs fichiers d'un dossier portent le même audit, la commande n'en garde qu'un : `map`, `export rules` et `export ndjson` lisent la copie écrite par `resolve` (`<name>_enriched.<ext>`) plutôt que l'original, et `export rules` et `export ndjson` lisent le CSV plutôt que le NDJSON de même nom ; `resolve` enrichit chaque original, CSV comme NDJSON, et écarte une copie enrichie dont l'original est présent. Chaque fichier écarté est nommé à l'écran avec sa raison. Un fichier dont le chemin est donné tel quel à `--input` est toujours lu, quel que soit son contenu ; s'il ne peut pas être ouvert, la commande s'arrête avec `The audit log '<path>' cannot be read: …` et le code de sortie 1. La copie enrichie ne reçoit pas les flux observés après le passage de `resolve` : relancez `resolve`, ou nommez l'original, pour les lire.

### Filtres de flux

Lus par `audit` (pour sa carte), `map` et `export rules`. Les valeurs de liste sont séparées par des virgules ; une option de liste peut être répétée et les valeurs s'additionnent.

| Option | Valeur | Clé de configuration | Effet |
| :--- | :--- | :--- | :--- |
| `--include-ports` | ports ou plages | `MapIncludedPorts` | Ne garder que ces ports de service (`80,443,8000-8080`) |
| `--exclude-ports` | ports ou plages | `MapExcludedPorts` | Retirer ces ports de service |
| `--include-hosts` | motifs | `MapIncludedHosts` | Ne garder que les serveurs dont le nom correspond (`nas*,*.home.arpa`) |
| `--exclude-hosts` | motifs | `MapExcludedHosts` | Retirer les serveurs dont le nom correspond |
| `--include-ips` | adresses ou CIDR | `MapIncludedIps` | Ne garder que ces adresses (`10.0.0.0/8`) |
| `--exclude-ips` | adresses ou CIDR | `MapExcludedIps` | Retirer ces adresses |
| `--include-processes` | noms | `MapIncludedProcesses` | Ne garder que ces noms de processus ou cibles applicatives |
| `--exclude-processes` | noms | `MapExcludedProcesses` | Retirer ces noms de processus ou cibles applicatives |
| `--direction` | `all`, `in`, `out` | `MapDirection` | Sens des flux à garder ; défaut `all` |
| `--listeners-only` | aucune | `MapListenersOnly` | Ne garder que les sockets en écoute |
| `--include-ephemeral-ports` | aucune | `MapExcludeEphemeralPorts` | Garder les ports 49152-65535, retirés par défaut |

Un port est un nombre entre 0 et 65535, une plage s'écrit dans l'ordre croissant (`8000-8080`). Un filtre d'adresses est une adresse (`192.168.1.50`, une adresse IPv4 s'écrivant avec ses quatre octets en décimal) ou un sous-réseau CIDR ; un sous-réseau écrit avec l'adresse d'un de ses hôtes (`192.168.1.10/24`) est lu comme le sous-réseau de cet hôte (`192.168.1.0/24`). Toute autre valeur donnée à `--include-ports`, `--exclude-ports`, `--include-ips` ou `--exclude-ips`, ou à leurs clés de configuration, arrête la commande avant qu'elle ne commence : le message nomme l'option, la clé et la valeur, et le code de sortie est 1.

### Rendu de la carte

Lues par `audit` et `map`. Les valeurs admises sont comparées sans tenir compte de la casse.

| Option | Valeur | Clé de configuration | Défaut |
| :--- | :--- | :--- | :--- |
| `--html` | `<path>` | `OutputHtmlMapPath` | aucun |
| `--drawio` | `<path>` | `OutputDrawioPath` | aucun |
| `--layout` | `layered`, `tree`, `horizontal-tree`, `organic`, `circular`, `grid` | `MapLayoutMode` | `layered` |
| `--sort` | `barycentric`, `tiers`, `subnet`, `degree`, `alpha`, `ip` | `MapSortMethod` | `barycentric` |
| `--routing` | `bezier`, `orthogonal`, `straight`, `bus` | `MapRoutingMode` | `bezier` |
| `--spacing` | `compact`, `balanced`, `spacious` | `MapSpacingProfile` | `balanced` |
| `--edge-labels` | `ports`, `none` | `MapEdgeLabels` | `ports` |
| `--drawio-edges` | `adaptive`, `direction`, `pair`, `none` | `DrawioEdgeMode` | `adaptive` |
| `--drawio-subnets` | `on`, `off` | `DrawioSubnets` | `on` |
| `--drawio-theme` | `dark`, `light` | `DrawioTheme` | `dark` |

`--layout`, `--sort`, `--routing`, `--spacing` et `--edge-labels` fixent l'état initial de la carte HTML ; la page permet de les changer. `--sort` ordonne aussi les serveurs cibles, et les zones de sous-réseau, du schéma Draw.io.

- Routage `bus` : les flèches d'un serveur partagent un tronc par sens, puis se séparent en une branche par cible. Les branches qui atteignent un serveur par le même bord et dans le même sens aboutissent sous une seule pointe. Pensé pour la mise en page `tree`.
- `--edge-labels none` : décoche *Show Ports*. Les flèches perdent leurs étiquettes de ports et les flux d'une paire de serveurs sont fusionnés en une seule flèche, avec une pointe à chaque extrémité qui reçoit un flux. L'inspecteur du serveur liste toujours les ports.
- `--drawio-edges pair` : une arête par paire de serveurs dans le diagramme Draw.io, avec deux pointes quand les flux vont dans les deux sens.

## audit

Balaye à intervalle fixe les sockets TCP et UDP de la machine, rattache chaque socket à son processus et écrit une ligne par flux dédupliqué dans le journal CSV. Tourne jusqu'à `Q`, `Ctrl+C` ou `SIGTERM`.

```text
PortAtlas audit [options]
```

| Option | Valeur | Clé de configuration | Défaut | Effet |
| :--- | :--- | :--- | :--- | :--- |
| `-o`, `--output` | `<path>` | `OutputCsvPath` | `netflow_audit.csv` | Journal d'audit CSV |
| `--ndjson` | `<path>` | `OutputNdjsonPath` | aucun | Journal NDJSON écrit en plus du CSV |
| `--interval` | secondes, au moins 1 | `ScanIntervalSeconds` | `5` | Délai entre deux balayages |
| `--columns` | noms de colonnes | `CsvColumns` | les 20 | Colonnes CSV, dans l'ordre ([liste](output-formats.fr.md#journal-daudit-csv)) |
| `--rewrite-interval` | minutes, au moins 1 | `LogRewriteIntervalMinutes` | `5` | Temps entre deux réécritures des journaux, écrits aussi au démarrage et à l'arrêt ([détails](output-formats.fr.md#journal-daudit-csv)) |
| `--no-tui` | aucune | `EnableDashboard` | tableau de bord actif | Tourner sans le tableau de bord |
| `--no-dns` | aucune | `ResolveRemoteHostNames` | DNS actif | Aucune résolution de nom : ni DNS inverse, ni RDAP, ni alias |
| `--no-rdap` | aucune | `ResolveIpOwners` | RDAP actif | Aucune requête RDAP vers les registres Internet ; le DNS inverse reste actif |
| `--dns-timeout-ms` | millisecondes, au moins 1 | `DnsResolutionTimeoutMs` | `3000` | Délai d'attente d'une réponse DNS inverse |
| `--exclude-remote-ips` | adresses ou CIDR | `ExcludedRemoteIps` | aucune | Adresses distantes non enregistrées |
| `--alias` | `ip=host` (IPv4 ou IPv6), ou `ip:host` pour IPv4, répétable | `CustomHostAliases` | aucun | Nom d'hôte utilisé à la place du DNS inverse ; une paire sans adresse IP ou sans nom est refusée |
| `--mask-server` | `<name>` | `MaskServerName` | aucun | Nom de serveur affiché par le tableau de bord |
| `--mask-ip` | `<address>` | `MaskLocalIp` | aucune | Adresse locale affichée par le tableau de bord |
| `--obfuscate` | aucune | aucune | inactif | Affiche `HOMELAB-01` et `192.168.1.10`, sauf si `--mask-server` ou `--mask-ip` est donné |

Familles lues : filtres de flux et rendu de la carte. Quand `--html` (ou `OutputHtmlMapPath`) est renseigné, la carte HTML est réécrite après chaque balayage ; la touche `M` l'écrit à la demande, avec le diagramme Draw.io quand `--drawio` (ou `OutputDrawioPath`) est renseigné. Une seule carte est écrite à la fois : les balayages terminés pendant son écriture donnent une seule réécriture ensuite, et une carte impossible à écrire (fichier verrouillé ou en lecture seule) affiche un avertissement sans arrêter l'audit.

Le tableau de bord n'est pas affiché quand l'entrée ou la sortie du processus est redirigée, même sans `--no-tui`.

Un scan qui échoue (table des sockets illisible, fichier retenu un instant) n'arrête pas l'audit : le tableau de bord affiche `Scan failed (n of 10 in a row): …` dans le titre de la table des flux, ou la ligne est écrite avec son heure sans tableau de bord, et le scan suivant a lieu à l'intervalle habituel. Après 10 scans en échec à la suite, l'audit s'arrête avec `The audit stopped after 10 failed scans in a row: …` et le code de sortie 1, après écriture des journaux.

Un nom de colonne invalide dans `--columns` ou `CsvColumns`, ou une liste sans `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath` et `TargetExecutable` (le journal est relu au démarrage suivant, voir les [formats de sortie](output-formats.fr.md#journal-daudit-csv)), arrête la commande avec `Column configuration error: …` et le code de sortie 1.

Quand la résolution de noms est active, le cache DNS désigné par `DnsCacheFilePath` est lu au démarrage et enregistré pendant l'audit. S'il existe mais est illisible (JSON tronqué ou invalide, fichier tenu par un autre programme ou non lisible par le compte), l'audit s'arrête avant son premier scan avec `The DNS cache '<path>' cannot be read: …` et le code de sortie 1, sans écrire ni les journaux ni le cache : le cache n'est pas remplacé par les seuls noms de cette exécution.

La commande `audit` ne reprend que depuis le journal CSV. Si le journal NDJSON (`--ndjson`, `OutputNdjsonPath`) existe et contient au moins un enregistrement alors que le journal CSV est absent ou ne contient aucun flux, l'audit s'arrête avant son premier scan avec `The NDJSON log '<path>' holds flows but the CSV log '<path>' is missing or holds none: …` et le code de sortie 1, sans rien écrire : démarrer remplacerait le journal NDJSON par les seuls flux de cette exécution. Restaurez le journal CSV ou déplacez le journal NDJSON. Un journal NDJSON vide ou absent n'arrête pas l'audit.

## map

Construit le graphe topologique à partir des journaux CSV et écrit les sorties demandées.

```text
PortAtlas map [-i <path>]... [--html <path>] [--drawio <path>] [options]
```

Sorties :

- `--html` écrit la carte HTML, `--drawio` le diagramme Draw.io ; les deux peuvent être donnés.
- Sans `--html` ni `--drawio`, la commande écrit la carte HTML dans `OutputHtmlMapPath` (ou `netflow_topology.html` si la clé est vide), et le diagramme Draw.io quand `OutputDrawioPath` est renseigné.

Une ligne de journal sans port local et distant valide est écartée de la carte ; la console nomme le journal et les numéros de ces lignes (les dix premiers).

Familles lues : entrées (`.csv`), filtres de flux, rendu de la carte.

## export rules

Consolide les journaux en une matrice d'une ligne par source, destination, port, protocole et processus.

```text
PortAtlas export rules [-i <path>]... [-o <path>] [options]
```

| Option | Valeur | Défaut | Effet |
| :--- | :--- | :--- | :--- |
| `-o`, `--output` | `<path>` | `firewall_rules.csv` | Fichier de la matrice ([colonnes](output-formats.fr.md#matrice-de-règles-de-pare-feu)) |
| `--min-observations` | nombre, au moins 1 | `1` | Garder les flux observés au moins ce nombre de fois |
| `--active-within-days` | nombre, au moins 1 | sans limite | Garder les flux vus pendant les N derniers jours |
| `--zone` | `all`, `internal`, `public` | `all` | Zone de destination à garder |
| `--pairs-output` | `<chemin>` | non écrite | Écrit aussi la matrice par paire, une ligne par source et destination ([colonnes](output-formats.fr.md#matrice-par-paire)) ; mêmes jetons que `--output` |

Familles lues : entrées (`.csv`, `.ndjson`, `.jsonl`) et filtres de flux, avec deux différences :

- les ports de destination éphémères (49152 et au-delà) sont retirés sauf avec `--include-ephemeral-ports` ; la clé `MapExcludeEphemeralPorts` n'est pas lue ;
- les sockets en écoute ne sont exportés qu'avec `--listeners-only` (ou `MapListenersOnly`), sous forme de règles entrantes depuis n'importe quelle source (`0.0.0.0`, hôte `Any`). Un socket est en écoute quand sa `Direction` vaut `Listening`, quel que soit son état : écoutes TCP et sockets UDP liés à un port.

Une destination est `Internal` si c'est une adresse de bouclage, une adresse d'une plage privée, ou `0.0.0.0` / `::` ; sinon elle est `Public`.

Une règle porte le nom du serveur, le nom distant, le chemin de l'exécutable et le compte trouvés sur n'importe laquelle de ses lignes de journal, le plus récemment vu quand plusieurs lignes diffèrent, quel que soit l'ordre des entrées. `--include-hosts` et `--exclude-hosts` sont testés sur la règle consolidée, contre ses deux extrémités, par le nom et par l'adresse que la règle affiche : une correspondance suffit, pour garder la règle comme pour la retirer. La source `Any` d'une écoute n'est pas testée.

`--include-ips` et `--exclude-ips` suivent la même règle sur les deux adresses que la règle affiche, quel que soit le sens du flux : une correspondance suffit, pour garder la règle comme pour la retirer. Exclure une adresse distante retire les flux qu'elle reçoit et ceux qu'elle ouvre ; inclure le sous-réseau du serveur garde tous ses flux, l'exclure les retire tous. Une écoute n'est testée que sur l'adresse à laquelle elle est liée : une écoute liée à `0.0.0.0` ou `::` n'est pas dans le sous-réseau de son serveur.

Un journal CSV est lu avec le séparateur trouvé sur sa propre première ligne (`,`, `;`, tabulation ou `|`) : des journaux écrits avec des valeurs différentes de `CsvDelimiter` peuvent donc être consolidés ensemble. C'est le séparateur placé entre les noms de colonnes de l'en-tête ou, dans un journal sans en-tête, celui qui revient le plus souvent hors des champs entre guillemets. La matrice est écrite avec `CsvDelimiter`.

## export ndjson

Convertit des journaux CSV et NDJSON en un seul fichier NDJSON, un objet JSON par flux.

```text
PortAtlas export ndjson [-i <path>]... [-o <path>]
```

| Option | Valeur | Défaut | Effet |
| :--- | :--- | :--- | :--- |
| `-o`, `--output` | `<path>` | `netflow_audit.ndjson` | Fichier NDJSON ([champs](output-formats.fr.md#ndjson)) |

Famille lue : entrées (`.csv`, `.ndjson`, `.jsonl`). Un fichier `.ndjson` ou `.jsonl` est lu comme NDJSON, tout autre fichier comme CSV. Les fichiers sont lus l'un après l'autre et seul le fichier en cours est gardé en mémoire. Chaque flux lu donne une ligne : un flux présent dans deux journaux, comme les journaux de deux jours, ou un journal et sa copie enrichie tous deux donnés par leur chemin à `--input`, est écrit deux fois. Une sortie donnée par son chemin à `--input` arrête la commande avec `The output '<path>' is also an audit log to read: choose another --output.` et le code de sortie 1. La sortie d'une exécution précédente trouvée dans un dossier ou un motif n'est pas lue, et la commande affiche `Skipped <path>: it is the output of this command.`

## resolve

Fusionne des caches DNS, résout les adresses encore sans nom et écrit des copies enrichies des journaux.

```text
PortAtlas resolve [-i <path>]... [--cache <path>]... [options]
```

| Option | Valeur | Défaut | Effet |
| :--- | :--- | :--- | :--- |
| `--cache` | `<path>`, répétable | `DnsCacheFilePath` | Cache DNS à fusionner : un fichier, un dossier (ses fichiers `.json`) ou un motif |
| `--output-cache` | `<path>` | `DnsCacheFilePath` | Cache fusionné à écrire |
| `--in-place` | aucune | inactif | Remplacer les journaux au lieu d'écrire `<name>_enriched.<ext>` |
| `--force-resolve` | aucune | inactif | Résoudre de nouveau toutes les adresses, y compris celles qui ont déjà un nom, et remplacer les noms déjà présents dans les journaux. Une recherche qui ne trouve rien n'efface jamais un nom connu |
| `--rdap-delay-ms` | millisecondes, au moins 0 | `200` | Délai entre deux requêtes RDAP |
| `--cache-only` | aucune | inactif | N'envoyer aucune requête DNS ni RDAP ; n'utiliser que les caches fusionnés |
| `--dns-timeout-ms` | millisecondes, au moins 1 | `DnsResolutionTimeoutMs` | Délai d'attente d'une réponse DNS inverse |

Famille lue : entrées (`.csv`, `.ndjson`, `.jsonl`). Quand deux caches donnent des noms différents pour une même adresse, l'entrée la plus récente est gardée et le conflit est affiché. Une entrée sans nom n'entre jamais en conflit avec une entrée nommée : l'entrée nommée est gardée.

Les journaux d'audit sont facultatifs. Quand `--cache` est donné sans `-i`, les journaux d'audit sont laissés intacts et seuls les caches sont résolus : les adresses du cache fusionné sont celles à résoudre. Sans `--force-resolve`, seules les entrées sans nom sont résolues ; avec, toutes le sont. Quand des journaux d'audit sont donnés, les adresses à résoudre sont celles des journaux. Le cache désigné par `--output-cache` est toujours fusionné avant d'être écrit, même si `--cache` ne le liste pas : lancer `resolve` depuis plusieurs machines complète donc le cache au lieu de le remplacer. Si ce cache existe mais est illisible (JSON tronqué ou invalide, fichier tenu par un autre programme ou non lisible par le compte), la commande s'arrête avant toute résolution avec `The DNS cache '<path>' cannot be read: …` et le code de sortie 1, sans rien écrire : le cache n'est pas remplacé par les seuls noms de cette exécution. Les autres caches listés dans `--cache` sont seulement lus : un cache illisible n'apporte aucun nom et l'exécution continue.

`Ctrl+C`, et `SIGTERM` sous Linux et macOS, arrêtent les résolutions : les adresses résolues avant l'arrêt sont enregistrées dans le cache nommé par `--output-cache`, les journaux pas encore traités restent tels quels et la commande se termine avec le code de sortie `130`. Une nouvelle exécution reprend à partir de ce cache.

## lookup

Affiche un diagnostic en six étapes de la résolution d'une adresse : classification, cache local, réglages DNS du système, requête DNS inverse, requête RDAP, entrée de cache résultante.

```text
PortAtlas lookup <ip> [--dns-timeout-ms <milliseconds>]
```

`<ip>` doit être une adresse IPv4 ou IPv6. La commande lit `CustomHostAliases`, `DnsExcludedNetworks`, `ExcludedRemoteIps`, `DnsCacheFilePath` et `DnsResolutionTimeoutMs` dans la configuration. Comme dans l'audit, une adresse nommée par `CustomHostAliases` prend ce nom, et une adresse de `DnsExcludedNetworks` ne reçoit aucun nom : pour les deux, les étapes DNS inverse et RDAP affichent `SKIPPED` et n'envoient aucune requête. L'étape 1 dit aussi si `ExcludedRemoteIps` fait écarter par l'audit les flux de l'adresse. Sinon, la commande envoie la requête DNS inverse avec le même délai que `audit`, si bien qu'un `SUCCESS` ici signifie que l'audit nomme aussi l'adresse, et la requête RDAP pour les adresses publiques uniquement ; `ResolveIpOwners` et `--no-rdap` ne s'y appliquent pas.

## config init

Écrit un fichier de configuration contenant la valeur par défaut de chaque clé, avec les 20 colonnes CSV listées.

```text
PortAtlas config init [<path>]
```

`<path>` est un fichier ou un dossier ; par défaut `portatlas.json` dans le dossier de travail. Un fichier existant est remplacé.

## Matrice des options

| Option | `audit` | `map` | `export rules` | `export ndjson` | `resolve` | `lookup` | `config init` |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `--config` | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| `-i`, `--input` | | ✔ | ✔ | ✔ | ✔ | | |
| `-o`, `--output` | ✔ | | ✔ | ✔ | | | |
| Filtres de flux | ✔ | ✔ | ✔ | | | | |
| Rendu de la carte, `--html`, `--drawio` | ✔ | ✔ | | | | | |
| `--ndjson`, `--interval`, `--columns`, `--rewrite-interval` | ✔ | | | | | | |
| `--no-tui`, `--no-dns`, `--no-rdap` | ✔ | | | | | | |
| `--dns-timeout-ms` | ✔ | | | | ✔ | ✔ | |
| `--exclude-remote-ips`, `--alias` | ✔ | | | | | | |
| `--mask-server`, `--mask-ip`, `--obfuscate` | ✔ | | | | | | |
| `--min-observations`, `--active-within-days`, `--zone`, `--pairs-output` | | | ✔ | | | | |
| `--cache`, `--output-cache`, `--in-place`, `--force-resolve`, `--rdap-delay-ms`, `--cache-only` | | | | | ✔ | | |

`config init` accepte `--config` parce que l'option est globale, mais ne la lit pas.

## Arrêt par l'entrée standard

Lorsque la variable d'environnement `PORTATLAS_STOP_ON_INPUT_END` vaut `1` et que l'entrée standard est redirigée, la fin de cette entrée arrête la commande comme `Ctrl+C` : `audit` écrit ses journaux et enregistre son cache DNS, `resolve` enregistre les adresses déjà résolues et se termine avec le code `130`, et les autres commandes se terminent aussitôt avec le code `130`. La fin de l'entrée est prise en compte dès qu'elle arrive, même quand l'entrée est vide dès le départ. L'interface graphique définit la variable et ferme l'entrée pour arrêter une commande. Sans la variable, l'entrée n'est pas lue : un service lancé avec une entrée vide continue de tourner.

## Codes de sortie

| Code | Signification |
| :--- | :--- |
| `0` | Succès, ou aide affichée |
| `1` | Ligne de commande invalide, aucun journal trouvé, nom de colonne CSV invalide ou liste de colonnes sans les colonnes de la clé d'un flux, journal d'`audit` illisible ou impossible à remplacer au démarrage ou à écrire à l'arrêt ([détails](output-formats.fr.md#journal-daudit-csv)), `audit` arrêté par 10 scans en échec à la suite, port ou adresse illisible dans un [filtre de flux](#filtres-de-flux), chemin donné par `--config` vide ou introuvable, ou fichier de configuration illisible, invalide (voir la [référence de configuration](configuration.fr.md)) ou qui fixe `"IsolatedMode": true`, cache DNS d'`audit` ou cache de sortie de `resolve` illisible, journal NDJSON d'`audit` qui contient des flux alors que son journal CSV est absent, fichier de sortie de `map`, `export rules`, `export ndjson` ou `config init` (ou son dossier) qui ne peut être ni créé ni écrit : la commande affiche `The output '<path>' cannot be written: …` |
| `130` | `resolve` arrêtée par `Ctrl+C` ou `SIGTERM`, après enregistrement des adresses déjà résolues ; commande autre qu'`audit` [arrêtée par son entrée](#arrêt-par-lentrée-standard) |
