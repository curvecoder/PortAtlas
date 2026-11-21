> **Language / Langue**: [English](output-formats.md) · **Français**

# Référence des formats de sortie

## Fichiers écrits

| Fichier | Écrit par | Encodage | Remplacé |
| :--- | :--- | :--- | :--- |
| Journal d'audit CSV | `audit` | UTF-8 avec marque d'ordre des octets (BOM) | Une fois complet (voir [Cycle de réécriture](#cycle-de-réécriture)) |
| Journal d'audit NDJSON | `audit --ndjson` | UTF-8 sans BOM | Une fois complet, comme le journal CSV |
| Export NDJSON | `export ndjson` | UTF-8 sans BOM | Écrit sur place : un export interrompu laisse un fichier partiel |
| Matrices de règles, des sockets en écoute, de flux, par paire et de règles par hôte | `export rules` | UTF-8 avec BOM | Une fois complètes |
| Cache DNS | `audit`, `resolve` | UTF-8 sans BOM | Une fois complet ; quand il ne peut pas être écrit, le fichier précédent reste |
| Copie enrichie | `resolve` | BOM du journal qu'elle copie | Une fois complète |
| Carte HTML | `map --html`, `audit --html` | UTF-8 avec BOM | Écrite sur place |
| Diagramme Draw.io | `map --drawio`, `audit --drawio` | UTF-8 sans BOM | Écrit sur place |

Les lignes se terminent par la fin de ligne du système (CRLF sous Windows, LF sous Linux et macOS), sauf dans le diagramme Draw.io, toujours en LF, et dans le script inclus dans la carte HTML, qui garde CRLF. Un fichier remplacé une fois complet est d'abord écrit sous `<fichier>.tmp`, renommé `<fichier>.pending` une fois complet, puis déplacé sur `<fichier>`. Pendant l'écriture, `<fichier>.lock` est tenu ouvert sans partage ; Windows le supprime ensuite, Linux et macOS le laissent à côté du fichier.

Les noms de fichiers utilisés quand aucun chemin n'est fixé sont listés dans la [référence de la ligne de commande](cli.fr.md).

## Journal d'audit CSV

Écrit par `audit` (`-o`, `OutputCsvPath`). Les champs suivent les règles de guillemets de la RFC 4180 ; le séparateur est `CsvDelimiter` (`,` par défaut). Un enregistrement par flux dédupliqué. `audit` écrit chaque retour à la ligne d'un champ (une ligne de commande, par exemple) comme une espace : les journaux qu'il écrit ont donc une ligne par flux ; un journal écrit par une version antérieure peut contenir un champ entre guillemets avec des retours à la ligne, si bien qu'un enregistrement peut s'étendre sur plusieurs lignes, et chaque commande le lit en entier.

Un flux est identifié par son protocole, son sens, son application (`TargetExecutable`, ou `ExecutablePath` en l'absence de cible, ou `ProcessName` quand le chemin est vide lui aussi) et :

| Sens | Identifié aussi par | Ignoré |
| :--- | :--- | :--- |
| `Outbound` | adresse et port distants | port local (client) |
| `Inbound` | port local et adresse distante | port distant (client) |
| `Listening` | port local | adresses |

Les connexions répétées vers un même service mettent donc à jour une seule ligne au lieu d'en ajouter.

`--columns` et `CsvColumns` choisissent et ordonnent les colonnes ; sans eux, les 20 colonnes ci-dessous sont écrites dans cet ordre. Neuf colonnes ne peuvent pas être retirées, car elles identifient un flux quand le journal est relu : `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath` et `TargetExecutable` (voir [`--columns`](cli.fr.md)).

| Colonne | Catégorie | Contenu |
| :--- | :--- | :--- |
| `FirstSeenUtc` | Horodatage | Date et heure UTC de la première observation du flux |
| `LastSeenUtc` | Horodatage | Date et heure UTC de la dernière observation du flux |
| `ObservationCount` | Mesure | Nombre de balayages où le flux a été vu |
| `ServerName` | Serveur | Nom de la machine auditée |
| `ServerOsVersion` | Serveur | Description du système de la machine auditée |
| `ServerPrimaryIp` | Serveur | Adresse IP principale de la machine auditée : la première adresse IPv4 d'une interface active qui a une passerelle, sinon de n'importe quelle interface active, sinon `127.0.0.1` ; les interfaces sont relues toutes les minutes |
| `Protocol` | Réseau | `Tcp` ou `Udp` |
| `Direction` | Réseau | `Listening`, `Inbound` ou `Outbound` |
| `TcpState` | Réseau | Voir [États des sockets](#états-des-sockets) |
| `LocalIp` | Réseau | Adresse locale du socket |
| `LocalPort` | Réseau | Port local du socket |
| `RemoteIp` | Réseau | Adresse distante ; pour une écoute ou un socket UDP, `0.0.0.0` (IPv4) ou `::` (IPv6) |
| `RemotePort` | Réseau | Port distant ; `0` pour une écoute ou un socket UDP |
| `RemoteHostName` | Réseau | Nom issu d'un alias, du DNS inverse ou du propriétaire RDAP |
| `ProcessId` | Processus | Identifiant du processus |
| `ProcessName` | Processus | Nom de l'exécutable du processus |
| `ExecutablePath` | Processus | Chemin complet de l'exécutable ; vide pour les processus sans fichier exécutable (System Idle, System, Secure System, Registry) et, sous Linux, pour le processus d'un autre utilisateur quand l'audit ne tourne pas en root |
| `CommandLine` | Processus | Ligne de commande du processus |
| `TargetExecutable` | Processus | Application lancée par un lanceur (`.jar`, `.py`, `.ps1`, `.sh`, `.dll`…) |
| `UserName` | Processus | Compte du processus : `DOMAINE\compte` sous Windows (le SID quand le compte n'a plus de nom), le nom d'utilisateur sous Linux (l'UID numérique quand il n'est pas dans `/etc/passwd`) ; vide quand le processus ne peut pas être ouvert |

Les dates sont écrites au format aller-retour (`2026-03-14T08:30:00.0000000+00:00`) ; les nombres sont écrits sans séparateur de milliers. Un socket dont l'identifiant de processus vaut 0 ou moins a `SYSTEM` comme `UserName`, `System Idle` (identifiant 0) ou `Unknown` comme `ProcessName`, et ni chemin, ni ligne de commande, ni cible.

### États des sockets

| Valeur | Quand |
| :--- | :--- |
| `Closed`, `Listen`, `SynSent`, `SynReceived`, `Established`, `FinWait1`, `FinWait2`, `CloseWait`, `Closing`, `LastAck`, `TimeWait`, `DeleteTCB` | Socket TCP sous Windows |
| `UDP` | Socket UDP sous Windows |
| `Listen`, `Established`, `SynSent`, `SynRecv`, `FinWait1`, `FinWait2`, `TimeWait`, `Close`, `CloseWait`, `LastAck` | Socket TCP sous Linux |
| `Close`, `Established` | Socket UDP sous Linux, non connecté ou connecté |
| `Unknown(<n>)` (Windows), `State(<xx>)` (Linux) | Code d'état inconnu de PortAtlas |

### Cycle de réécriture

`audit` garde les flux en mémoire et réécrit tout le journal à partir d'eux, une ligne par flux avec son dernier compteur, sa date de dernière observation et son nom : au démarrage, toutes les `--rewrite-interval` minutes (`LogRewriteIntervalMinutes`, 5 par défaut) et à l'arrêt. Le journal NDJSON (`--ndjson`) est réécrit aux mêmes moments, à partir des mêmes flux. Un arrêt brutal (coupure de courant, processus tué) perd donc au plus les observations de la dernière période. Le journal est écrit avec les colonnes de l'exécution en cours : une colonne retirée de `--columns` depuis l'exécution précédente disparaît de tout le journal.

Chaque réécriture écrit `<journal>.tmp`, le renomme `<journal>.pending` une fois complet, puis remplace le journal : un programme qui lit le journal obtient l'ancienne version ou la nouvelle, jamais un mélange des deux. Windows refuse le remplacement tant qu'un autre programme tient le journal ouvert : l'audit continue, `<journal>.pending` reste, et la période suivante réessaie ; le tableau de bord (ou, avec `--no-tui`, la console) affiche un avertissement qui nomme le journal et la cause, répété à chaque période en échec. Quand la réécriture échoue à l'arrêt, `audit` se termine avec le code 1 et un message qui nomme le journal. Au démarrage, un `<journal>.pending` laissé par une réécriture interrompue remplace le journal et un `<journal>.tmp` est supprimé ; un journal qui ne peut pas être remplacé au démarrage arrête `audit` avec le code 1 et un message qui le nomme.

### Relecture d'un journal

Au démarrage, `audit` relit tout le journal CSV existant, pour que les compteurs et les dates de première observation se poursuivent d'un redémarrage à l'autre. Le journal est lu avec le séparateur trouvé sur sa première ligne, comme décrit plus bas : un journal écrit avant un changement de `CsvDelimiter` est donc repris. Le journal est ensuite réécrit à partir de ce qui a été lu, avec `CsvDelimiter` : un journal qui ne peut pas être lu en entier arrête `audit` avec une erreur qui nomme les colonnes ou la ligne, et le code de sortie 1 :

- un journal sans l'une des neuf colonnes qui identifient un flux, listées plus haut : ses flux ne correspondraient à aucun flux observé ;
- une ligne dont `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp` ou `RemotePort` est vide ou illisible, ou dont `LastSeenUtc`, `ObservationCount` ou `LocalIp` est illisible : la réécriture remplacerait la valeur par une valeur par défaut ;
- un guillemet jamais refermé avant la fin du journal : le message nomme le journal et la ligne où le guillemet s'ouvre ;
- un journal CSV absent ou sans flux alors que le journal NDJSON donné par `--ndjson` contient des flux : la réécriture les effacerait.

Corriger ou supprimer la ligne, ou écrire dans un nouveau journal avec `--output`.

`audit` (à la reprise de son journal), `map`, `export rules`, `export ndjson` et `resolve` trouvent le séparateur de chaque journal d'après sa première ligne : des journaux écrits avec des séparateurs différents peuvent donc être lus ensemble. Les candidats sont `,`, `;`, tabulation, `|` et `CsvDelimiter` (`,` pour `resolve`) : sur un en-tête, celui qui sépare le plus de noms de colonnes d'audit l'emporte ; sur une ligne sans en-tête, celui qui apparaît le plus souvent hors guillemets. `CsvDelimiter` (`,` pour `resolve`) l'emporte en cas d'égalité et sert pour une première ligne vide. Un séparateur hors de ces quatre ne peut être lu que par `CsvDelimiter`, et pas par `resolve`.

La première ligne est prise pour un en-tête quand elle nomme une colonne d'audit ; sinon le journal est lu par position, dans l'ordre du tableau ci-dessus ; `resolve` copie tel quel un journal sans en-tête de moins de 20 champs. `map` ne prend la première ligne pour un en-tête que si elle nomme `ServerName` ou `LocalPort`.

Dans `export rules` et `export ndjson`, une valeur illisible prend une valeur par défaut au lieu d'arrêter la commande : l'heure de lecture pour `FirstSeenUtc`, `FirstSeenUtc` pour `LastSeenUtc`, `1` pour `ObservationCount`, `Tcp`, `Outbound`, `127.0.0.1` pour `LocalIp`, `0.0.0.0` pour `RemoteIp`, `0` pour un port ou l'identifiant de processus ; un `TcpState` vide devient `Established`, et un `ServerName` vide le nom de la machine qui lit le journal. Une ligne sans `ServerName` ni `LocalPort`, ou avec un port hors de 0-65535, est écartée. `map` ne lit que ce dont la carte a besoin : une `Direction` illisible devient `Listening` quand le port distant vaut 0, `Outbound` sinon.

`map` écarte chaque ligne sans port local et distant valides et la nomme dans la console. Un guillemet jamais refermé arrête `export` et `resolve` avec le code 1 et un message qui nomme le journal et la ligne ; `map` garde les flux d'avant cette ligne et signale le reste du journal comme une seule ligne écartée.

## NDJSON

Écrit par `audit --ndjson` et par `export ndjson`. Un objet JSON par ligne, toujours avec les 20 valeurs du journal CSV sous des noms en camel case, quoi que dise `--columns` :

`firstSeenUtc`, `lastSeenUtc`, `observationCount`, `serverName`, `serverOsVersion`, `serverPrimaryIp`, `protocol`, `direction`, `tcpState`, `localIp`, `localPort`, `remoteIp`, `remotePort`, `remoteHostName`, `processId`, `processName`, `executablePath`, `commandLine`, `targetExecutable`, `userName`.

`observationCount`, `localPort`, `remotePort` et `processId` sont des nombres ; les autres valeurs sont des chaînes, vides quand elles sont inconnues, jamais `null`. Les dates sont au format ISO 8601 avec leur décalage (`2026-03-14T08:30:00.1234567+00:00`). Les caractères accentués sont écrits tels quels. PortAtlas lit chaque retour à la ligne d'une ligne de commande comme une espace ; un retour à la ligne d'un ancien journal CSV converti par `export ndjson` est écrit sous la forme `\n`.

`export rules`, `export ndjson` et `resolve` lisent les fichiers NDJSON (`.ndjson`, `.jsonl`) comme les fichiers CSV. Les trois lisent les noms de propriétés sans tenir compte de la casse. `export rules` et `export ndjson` ignorent sans message une ligne qui n'est pas un objet JSON valide ; `resolve` copie une telle ligne telle quelle, comme une ligne qui porte deux clés ne différant que par la casse, et écrit un nom d'hôte sous la clé telle que la ligne l'orthographie déjà.

## Matrice de règles de pare-feu

Écrite par `export rules`. Une ligne par sens, adresse source, adresse de destination, port de destination, protocole et processus ; les noms de processus sont comparés en tenant compte de la casse. Par défaut, les écoutes et les ports de destination éphémères (49152 et au-delà) sont écartés : `--listeners-only` n'exporte que les écoutes, et `--include-ephemeral-ports` garde les ports éphémères (voir la [référence de la ligne de commande](cli.fr.md)).

Les lignes sont triées par sens, zone, port, adresse de destination, adresse source, protocole et processus : la numérotation ne dépend pas de l'ordre des journaux donnés à `--input`. Les adresses sont triées comme du texte : `192.168.1.10` vient avant `192.168.1.2`. Le séparateur est `CsvDelimiter`. L'`ExecutablePath`, l'`UserName` et les noms d'hôte d'une ligne viennent de la ligne de journal vue le plus récemment qui a une valeur.

Une valeur qui commence par `=`, `+`, `-`, `@` (ou leurs formes pleine chasse), une tabulation ou un retour chariot est écrite précédée d'une apostrophe, pour qu'un tableur l'affiche comme du texte, apostrophe comprise, au lieu de l'exécuter comme une formule : un nom de processus ou d'hôte peut être choisi par un tiers. Le journal d'audit garde ses valeurs telles quelles, puisque PortAtlas le relit.

| Colonne | Contenu |
| :--- | :--- |
| `RuleId` | `RULE-001`, `RULE-002`… dans l'ordre du tri |
| `Direction` | `Outbound` ou `Inbound` |
| `SourceIp` | Adresse du client ; `0.0.0.0` ou `::` pour une écoute |
| `SourceHost` | Nom du client : le serveur audité pour un flux sortant, le nom distant (ou l'adresse, quand aucun nom n'est connu) pour un flux entrant, `Any` pour une écoute |
| `DestinationIp` | Adresse du serveur |
| `DestinationHost` | Nom du serveur, ou son adresse quand aucun nom n'est connu |
| `DestinationZone` | `Internal` (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`, `169.254.0.0/16`, `::1`, `fc00::/7`, `fe80::/10`, `0.0.0.0`, `::`) ou `Public` (toute autre adresse) |
| `Protocol` | `TCP` ou `UDP` |
| `DestinationPort` | Port du service |
| `ServiceName` | Nom déduit du port et du processus (par exemple `Oracle-TNS` pour 1521) |
| `ProcessName` | Processus propriétaire du socket |
| `ExecutablePath` | Chemin de ce processus |
| `UserName` | Compte de ce processus, comme dans le journal |
| `ObservationCount` | Somme des compteurs des flux regroupés |
| `FirstSeenUtc` | Première observation la plus ancienne |
| `LastSeenUtc` | Observation la plus récente |

### Matrice des sockets en écoute

Écrite par `export rules --listeners-output`, avec les colonnes de la matrice de règles de pare-feu : les lignes qu'écrirait `--listeners-only`, à partir des mêmes journaux et des mêmes filtres, pendant que la matrice de règles garde les flux.

## Matrice de flux

Écrite par `export rules --flows-output`, à partir des lignes de la matrice de règles après filtrage. Une ligne par adresse source, adresse de destination, protocole et port de destination : une connexion relevée par le journal de sa source (une règle `Outbound`) et par celui de sa destination (une règle `Inbound`) ne fait qu'une ligne, de même qu'un flux ouvert par plusieurs processus. Une destination de bouclage ou `0.0.0.0` / `::` ne désigne aucune machine : le même flux local sur deux serveurs reste sur deux lignes.

Les lignes sont triées par zone, port, adresse de destination, adresse source, protocole et serveur, comme du texte : la numérotation ne dépend pas de l'ordre des journaux. Une adresse qui appartient à un serveur audité, trouvée comme adresse locale dans son propre journal, porte le nom de ce serveur, quel que soit le journal qui a vu le flux ; toute autre adresse prend le nom donné par le journal de son propre bout, sinon celui donné par l'autre bout, sinon l'adresse. Une valeur qui pourrait commencer une formule reçoit la même apostrophe que dans la matrice de règles de pare-feu.

| Colonne | Contenu |
| :--- | :--- |
| `FlowId` | `FLOW-001`, `FLOW-002`… dans l'ordre du tri |
| `SourceIp`, `SourceHost` | Adresse et nom du client |
| `DestinationIp`, `DestinationHost` | Adresse et nom du serveur |
| `DestinationZone` | `Internal` ou `Public`, comme dans la matrice de règles |
| `Protocol` | `TCP` ou `UDP` |
| `DestinationPort` | Port du service |
| `ServiceName` | Service nommé par les règles de la destination (par celles de la source quand seule la source a vu le flux) ; `port/protocole` quand elles nomment plusieurs services |
| `SourceProcesses` | Processus qui ont ouvert le flux, triés et séparés par `, ` ; vide quand seule la destination l'a vu |
| `DestinationProcesses` | Processus qui ont accepté le flux ; vide quand seule la source l'a vu |
| `SeenFrom` | `Source`, `Destination` ou `Both` : le bout dont le journal a relevé le flux |
| `SourceObservationCount` | Observations relevées par le journal de la source |
| `DestinationObservationCount` | Observations relevées par le journal de la destination |
| `FirstSeenUtc` | Première observation la plus ancienne, aux deux bouts |
| `LastSeenUtc` | Observation la plus récente, aux deux bouts |

`SeenFrom` montre un trou dans les journaux : entre deux serveurs audités, un flux vu d'un seul bout signifie que les journaux ne couvrent pas la même période, ou qu'une traduction d'adresse ou une deuxième carte réseau cache un bout derrière une autre adresse.

## Matrice par paire

Écrite par `export rules --pairs-output`, à partir des lignes de la matrice de flux. Une ligne par adresse source et adresse de destination : les deux sens entre deux serveurs restent sur deux lignes, comme des règles de pare-feu. Triée par adresse source puis de destination, comme du texte. Les noms d'hôte sont ceux du premier flux de la paire. Une valeur qui pourrait commencer une formule reçoit la même apostrophe que dans la matrice de règles de pare-feu.

| Colonne | Contenu |
| :--- | :--- |
| `PairId` | `PAIR-001`, `PAIR-002`… dans l'ordre du tri |
| `SourceIp`, `SourceHost` | Adresse et nom du client, comme dans la matrice de flux |
| `DestinationIp`, `DestinationHost` | Adresse et nom du serveur, comme dans la matrice de flux |
| `FlowCount` | Nombre de lignes de la matrice de flux regroupées dans la paire |
| `Ports` | Ports de destination distincts, `port/protocole`, triés par port (`443/TCP, 873/TCP`) |
| `ObservationCount` | Somme des observations des flux regroupés, chaque flux compté une fois : le plus grand de ses deux bouts |
| `FirstSeenUtc` | Première observation la plus ancienne |
| `LastSeenUtc` | Observation la plus récente |
| `SeenFrom` | Valeurs distinctes de `SeenFrom` des flux regroupés, dans l'ordre `Source`, `Destination`, `Both` |

## Matrice de règles par hôte

Écrite par `export rules --host-rules-output`, à partir des lignes de la matrice de flux, pour les groupes de sécurité réseau (NSG) et les listes de sécurité, qui se posent par ressource et par sens. Chaque flux donne une règle `Outbound` à sa source et une règle `Inbound` à sa destination. Seule une adresse privée (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `fc00::/7`, `fe80::/10`) porte des règles : une adresse publique n'apparaît que comme pair d'un hôte privé, et un flux de bouclage ou `0.0.0.0` / `::` ne traverse jamais le réseau.

Les lignes sont triées par nom d'hôte, adresse de l'hôte, sens (`Inbound` d'abord), port, protocole et adresse du pair, comme du texte. Une valeur qui pourrait commencer une formule reçoit la même apostrophe que dans la matrice de règles de pare-feu.

| Colonne | Contenu |
| :--- | :--- |
| `HostRuleId` | `HOST-001`, `HOST-002`… dans l'ordre du tri |
| `Host`, `HostIp` | Hôte qui porte la règle, nommé comme dans la matrice de flux |
| `Audited` | `Yes` quand un journal de cette adresse a été lu, sinon `No` |
| `Direction` | `Inbound` quand l'hôte accepte la connexion, `Outbound` quand il l'ouvre |
| `PeerIp`, `PeerHost` | Autre bout du flux |
| `PeerZone` | `Internal` ou `Public`, comme `DestinationZone` |
| `Protocol` | `TCP` ou `UDP` |
| `Port` | Port de destination du flux : sur l'hôte pour une règle entrante, sur le pair pour une règle sortante |
| `ServiceName` | Comme dans la matrice de flux |
| `LocalProcesses` | Processus de l'hôte qui utilisent le flux ; vide quand le journal de l'hôte ne l'a pas relevé |
| `Evidence` | `Observed` quand le journal de l'hôte a relevé le flux, `Inferred` quand seul le journal du pair l'a fait |
| `ObservationCount` | Observations relevées par le journal de l'hôte, ou par celui du pair pour une règle `Inferred` |
| `FirstSeenUtc` | Première observation la plus ancienne du flux |
| `LastSeenUtc` | Observation la plus récente du flux |
| `FlowId` | Ligne de la matrice de flux d'où vient la règle |

## Carte topologique

`map --html` et `audit --html` écrivent un seul fichier HTML qui s'ouvre sans accès réseau. La page propose des choix de disposition, de tri, de routage et d'espacement, des filtres texte appliqués par le bouton `Apply Filters` ou `Enter`, la pagination des serveurs, un export SVG, et une section `CLI Command` qui affiche la commande `PortAtlas map` reproduisant les filtres et choix de disposition en cours. La commande copiée ne contient pas d'option `-i` : ajoutez les journaux à lire.

`map --drawio` écrit un fichier Draw.io non compressé (`.drawio`) pour Draw.io Desktop ou diagrams.net. Les serveurs cibles sont regroupés en zones : une zone par sous-réseau /24 pour les adresses IPv4, une seule zone `Internet` pour toutes les adresses publiques (IPv4 publiques, et IPv6 dans `2000::/3` hors `2001:db8::/32`), une zone `Other` pour les autres adresses IPv6, et une zone `Unknown` pour un serveur sans adresse. Les plages IPv4 qui ne sont pas publiques, comme les plages de documentation (`192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24`) et `100.64.0.0/10`, reçoivent une zone /24. Les zones passent à une nouvelle rangée au-delà d'une largeur fixe, pour que le diagramme reste lisible sur un grand réseau. Avec `--drawio-subnets off` (`DrawioSubnets`), aucune zone n'est dessinée et les serveurs sont disposés en simple grille. `--sort` (`MapSortMethod`) fixe l'ordre des serveurs et, pour les méthodes par sous-réseau, celui des zones.

## Cache DNS

`portatlas_dns_cache.json` (`DnsCacheFilePath`) associe chaque adresse résolue à son nom et à l'origine de ce nom. C'est un objet JSON indenté dont les noms de propriétés sont en Pascal case (le journal NDJSON est en camel case) :

```json
{
  "192.168.1.20": {
    "HostName": "proxmox.home.arpa",
    "Organization": null,
    "Country": null,
    "NetworkRange": null,
    "Source": "DNS",
    "ResolvedAtUtc": "2026-03-14T08:30:00.1234567+00:00"
  }
}
```

| Propriété | Contenu |
| :--- | :--- |
| `HostName` | Nom de l'adresse ; vide quand aucun n'a été trouvé |
| `Organization`, `Country`, `NetworkRange` | Propriétaire de l'adresse, d'après RDAP ; `null` quand il est inconnu |
| `Source` | `DNS`, `RDAP` ou `None` (aucun nom trouvé), écrits par `audit` et `resolve` ; `Local` (bouclage), écrit par `resolve` seulement |
| `ResolvedAtUtc` | Date de la recherche |

Un alias de `CustomHostAliases` n'est jamais écrit dans le cache. Un cache sous la forme de la 1.x, `{ "192.168.1.20": "proxmox.home.arpa" }`, est toujours lu. Une entrée illisible est écartée et son adresse est demandée à nouveau.

`audit` le lit et le met à jour ; `resolve` en fusionne plusieurs ; `lookup` le lit.

## Copie enrichie

`resolve` écrit, à côté de chaque journal d'audit `<nom>.<ext>`, une copie `<nom>_enriched.<ext>` dont `RemoteHostName` est rempli d'après le cache et les recherches ; `--in-place` réécrit plutôt le journal lui-même. Une copie CSV garde le séparateur du journal et sa marque d'ordre des octets, ou son absence ; dans une copie NDJSON, seules les lignes dont le nom a changé sont réécrites. Voir [`resolve`](cli.fr.md) et [Auditer des serveurs isolés](../how-to/audit-isolated-servers.fr.md).
