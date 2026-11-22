> **Language / Langue**: [English](export-firewall-rules.md) · **Français**

# Exporter une matrice de règles de pare-feu

Objectif : une liste CSV des flux à autoriser, une ligne par source, destination, port et processus, pour préparer des règles de pare-feu.

Tous les résultats de cette page viennent du [homelab d'exemple](../examples/homelab/README.fr.md) : trois machines auditées, `nas` (`192.168.1.10`), `pve` (`192.168.1.20`) et `pihole` (`192.168.1.2`), et un portable (`192.168.1.50`) sans journal d'audit. Lancez les commandes dans `docs/examples/homelab` du dépôt pour obtenir les mêmes fichiers ; les tableaux ci-dessous montrent quelques colonnes de chaque fichier, la [référence des formats de sortie](../reference/output-formats.fr.md#matrice-de-règles-de-pare-feu) les liste toutes.

## Exporter les flux observés

```bash
PortAtlas export rules -i logs -o rules.csv
```

`-i` lit les journaux CSV et NDJSON (un dossier, un motif ou des fichiers). Les trois journaux donnent `rules.csv` :

<!-- example-output: rules/rules.csv complete -->

| RuleId | Direction | SourceHost | DestinationHost | Protocol | DestinationPort | ProcessName | ObservationCount |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | ---: |
| RULE-001 | Inbound | laptop.home.arpa | nas | TCP | 22 | sshd | 1 |
| RULE-002 | Inbound | laptop.home.arpa | pihole | TCP | 80 | lighttpd | 35 |
| RULE-003 | Inbound | laptop.home.arpa | nas | TCP | 445 | smbd | 240 |
| RULE-004 | Inbound | pve.home.arpa | nas | TCP | 873 | rsync | 96 |
| RULE-005 | Inbound | laptop.home.arpa | pve | TCP | 8006 | pveproxy | 310 |
| RULE-006 | Outbound | nas | pihole.home.arpa | UDP | 53 | systemd-resolved | 2016 |
| RULE-007 | Outbound | pve | pihole.home.arpa | UDP | 53 | systemd-resolved | 2016 |
| RULE-008 | Outbound | pve | localhost | TCP | 85 | pveproxy | 620 |
| RULE-009 | Outbound | pve | nas.home.arpa | TCP | 873 | rsync | 96 |
| RULE-010 | Outbound | pve | 0.debian.pool.ntp.org | UDP | 123 | chronyd | 48 |
| RULE-011 | Outbound | pihole | 198.51.100.53 | TCP | 853 | unbound | 2016 |

Comment le lire :

- Chaque journal décrit sa propre machine. La sauvegarde nocturne de `pve` vers `nas` figure dans les deux journaux, elle donne donc deux règles : `RULE-004`, la connexion que `nas` a acceptée, et `RULE-009`, la connexion que `pve` a ouverte.
- Le portable n'a pas de journal : ses connexions n'apparaissent que comme règles `Inbound` sur les machines qu'il joint (`RULE-001`, `RULE-002`, `RULE-003`, `RULE-005`).
- Une machine auditée est nommée par son nom de machine (`nas`) ; l'autre extrémité par le nom donné par son DNS (`nas.home.arpa`), ou par son adresse s'il n'y en a pas (`198.51.100.53`).
- `RULE-008` est une connexion interne à `pve`, vers `127.0.0.1`.
- Deux choses manquent volontairement : les sockets en écoute ([Lister les services offerts](#lister-les-services-offerts)) et une connexion de `pve` vers le port 51234 de `nas` ([Garder les ports éphémères](#garder-les-ports-éphémères)).

## Réduire le bruit

Garder les flux vus au moins cinq fois, sans DNS ni NTP :

```bash
PortAtlas export rules -i logs -o rules.csv --min-observations 5 --exclude-ports 53,123
```

<!-- example-output: rules-noise/rules.csv complete -->

| RuleId | Direction | SourceHost | DestinationHost | DestinationPort | ObservationCount |
| :--- | :--- | :--- | :--- | ---: | ---: |
| RULE-001 | Inbound | laptop.home.arpa | pihole | 80 | 35 |
| RULE-002 | Inbound | laptop.home.arpa | nas | 445 | 240 |
| RULE-003 | Inbound | pve.home.arpa | nas | 873 | 96 |
| RULE-004 | Inbound | laptop.home.arpa | pve | 8006 | 310 |
| RULE-005 | Outbound | pve | localhost | 85 | 620 |
| RULE-006 | Outbound | pve | nas.home.arpa | 873 | 96 |
| RULE-007 | Outbound | pihole | 198.51.100.53 | 853 | 2016 |

La session SSH vue une seule fois, les deux règles DNS et la règle NTP ont disparu. Les règles sont renumérotées après filtrage : `RULE-001` n'est pas la même règle que dans le premier export, comparez donc deux exports sur leurs colonnes, pas sur `RuleId`.

`--active-within-days 14` garde les flux vus pendant les 14 derniers jours, comptés depuis le jour où la commande s'exécute : sur ces journaux, écrits en novembre 2026, son résultat dépend de la date à laquelle vous la lancez.

## Garder une zone

```bash
PortAtlas export rules -i logs -o rules.csv --zone public
```

<!-- example-output: rules-public/rules.csv complete -->

| RuleId | SourceHost | DestinationIp | DestinationHost | DestinationZone | Protocol | DestinationPort |
| :--- | :--- | :--- | :--- | :--- | :--- | ---: |
| RULE-001 | pve | 203.0.113.10 | 0.debian.pool.ntp.org | Public | UDP | 123 |
| RULE-002 | pihole | 198.51.100.53 | 198.51.100.53 | Public | TCP | 853 |

Ce sont les services Internet dont le homelab a besoin : l'heure pour `pve`, le DNS sur TLS pour `pihole`. `--zone internal` garde les autres règles, celles vers des adresses privées et de bouclage :

```bash
PortAtlas export rules -i logs -o rules.csv --zone internal
```

## Garder un serveur

```bash
PortAtlas export rules -i logs -o rules.csv --include-hosts "nas*"
```

<!-- example-output: rules-nas/rules.csv complete -->

| RuleId | Direction | SourceHost | DestinationHost | DestinationPort |
| :--- | :--- | :--- | :--- | ---: |
| RULE-001 | Inbound | laptop.home.arpa | nas | 22 |
| RULE-002 | Inbound | laptop.home.arpa | nas | 445 |
| RULE-003 | Inbound | pve.home.arpa | nas | 873 |
| RULE-004 | Outbound | nas | pihole.home.arpa | 53 |
| RULE-005 | Outbound | pve | nas.home.arpa | 873 |

Le motif est testé sur les deux extrémités de chaque règle : `RULE-005` vient du journal de `pve`, et elle est gardée parce que sa destination, `nas.home.arpa`, correspond à `nas*`. `--include-ips 192.168.1.10` sélectionne la machine par son adresse plutôt que par son nom.

## Garder un sens

```bash
PortAtlas export rules -i logs -o rules.csv --direction out
```

<!-- example-output: rules-outbound/rules.csv complete -->

| RuleId | SourceHost | DestinationHost | Protocol | DestinationPort |
| :--- | :--- | :--- | :--- | ---: |
| RULE-001 | nas | pihole.home.arpa | UDP | 53 |
| RULE-002 | pve | pihole.home.arpa | UDP | 53 |
| RULE-003 | pve | localhost | TCP | 85 |
| RULE-004 | pve | nas.home.arpa | TCP | 873 |
| RULE-005 | pve | 0.debian.pool.ntp.org | UDP | 123 |
| RULE-006 | pihole | 198.51.100.53 | TCP | 853 |

Il ne reste que les connexions ouvertes par les machines auditées : les règles de sortie de chaque serveur. `--direction in` garde les connexions qu'elles ont acceptées.

## Garder les ports éphémères

Les ports de destination 49152 et au-delà sont écartés par défaut : un serveur choisit un tel port au hasard, et une règle écrite pour lui ne tiendrait pas après un redémarrage. `--include-ephemeral-ports` les garde :

```bash
PortAtlas export rules -i logs -o rules.csv --include-ephemeral-ports
```

<!-- example-output: rules-ephemeral/rules.csv -->

| RuleId | Direction | SourceHost | DestinationHost | Protocol | DestinationPort | ProcessName | ObservationCount |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | ---: |
| RULE-010 | Outbound | pve | nas.home.arpa | TCP | 51234 | mount.nfs | 3 |

Cette connexion est le montage NFS de `pve` qui demande un partage à `nas`, sur le port que `rpc.mountd` a choisi au démarrage. Une règle de pare-feu pour elle demande d'abord un port `mountd` fixe sur le NAS.

## Lister les services offerts

```bash
PortAtlas export rules -i logs -o services.csv --listeners-only
```

<!-- example-output: listeners/services.csv complete -->

| RuleId | SourceHost | DestinationIp | DestinationHost | Protocol | DestinationPort | ProcessName |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- |
| RULE-001 | Any | 0.0.0.0 | nas | TCP | 22 | sshd |
| RULE-002 | Any | 0.0.0.0 | pve | TCP | 22 | sshd |
| RULE-003 | Any | 0.0.0.0 | pihole | TCP | 53 | pihole-FTL |
| RULE-004 | Any | 0.0.0.0 | pihole | UDP | 53 | pihole-FTL |
| RULE-005 | Any | 0.0.0.0 | pihole | TCP | 80 | lighttpd |
| RULE-006 | Any | 127.0.0.1 | pve | TCP | 85 | pvedaemon |
| RULE-007 | Any | 0.0.0.0 | nas | TCP | 445 | smbd |
| RULE-008 | Any | 0.0.0.0 | nas | TCP | 873 | rsync |
| RULE-009 | Any | 0.0.0.0 | pve | TCP | 8006 | pveproxy |

Chaque socket en écoute, écouteur TCP ou socket UDP liée, devient une règle entrante depuis n'importe quelle source (`Any`) vers son adresse et son port : le point de départ d'un jeu de règles entrantes. `0.0.0.0` désigne toutes les adresses de la machine ; `pvedaemon` (`RULE-006`) n'écoute que sur `127.0.0.1` et n'a besoin d'aucune règle de pare-feu.

## Écrire toutes les matrices en une passe

La matrice détaillée répond à « quel processus utilise quel port ». D'autres questions demandent d'autres vues des mêmes flux ; chaque option `--…-output` ajoute un fichier, avec les mêmes filtres, et les journaux ne sont lus qu'une fois :

```bash
PortAtlas export rules -i logs -o rules.csv --flows-output flows.csv --pairs-output pairs.csv --host-rules-output host_rules.csv --listeners-output services.csv --group-rules-output group_rules.csv
```

| Question | Fichier | Option |
| :--- | :--- | :--- |
| Quel processus ouvre ou accepte quelle connexion ? | `rules.csv` | `-o` |
| Quelles connexions traversent le réseau, comptées une fois ? | `flows.csv` | `--flows-output` |
| Quelle machine parle à quelle autre, sur quels ports ? | `pairs.csv` | `--pairs-output` |
| Quelles règles porte chaque machine (groupes de sécurité réseau) ? | `host_rules.csv` | `--host-rules-output` |
| Quels services offre chaque machine ? | `services.csv` | `--listeners-output` |
| Quelles règles entre groupes d'adresses (VLAN, groupes applicatifs) ? | `group_rules.csv` | `--group-rules-output` |

Les sections suivantes suivent la sauvegarde de `pve` vers `nas` (TCP 873) dans chaque fichier.

### Matrice de flux : une ligne par connexion

Dans `rules.csv`, la sauvegarde fait deux règles, une par journal :

<!-- example-output: all-matrices/rules.csv -->

| RuleId | Direction | SourceHost | DestinationHost | DestinationPort | ProcessName |
| :--- | :--- | :--- | :--- | ---: | :--- |
| RULE-004 | Inbound | pve.home.arpa | nas | 873 | rsync |
| RULE-009 | Outbound | pve | nas.home.arpa | 873 | rsync |

`flows.csv` les fusionne en une ligne, `FLOW-007`, avec le processus de chaque extrémité :

<!-- example-output: all-matrices/flows.csv complete -->

| FlowId | SourceHost | DestinationHost | Protocol | DestinationPort | SourceProcesses | DestinationProcesses | SeenFrom |
| :--- | :--- | :--- | :--- | ---: | :--- | :--- | :--- |
| FLOW-001 | laptop.home.arpa | nas | TCP | 22 | | sshd | Destination |
| FLOW-002 | nas | pihole | UDP | 53 | systemd-resolved | | Source |
| FLOW-003 | pve | pihole | UDP | 53 | systemd-resolved | | Source |
| FLOW-004 | laptop.home.arpa | pihole | TCP | 80 | | lighttpd | Destination |
| FLOW-005 | pve | localhost | TCP | 85 | pveproxy | | Source |
| FLOW-006 | laptop.home.arpa | nas | TCP | 445 | | smbd | Destination |
| FLOW-007 | pve | nas | TCP | 873 | rsync | rsync | Both |
| FLOW-008 | laptop.home.arpa | pve | TCP | 8006 | | pveproxy | Destination |
| FLOW-009 | pve | 0.debian.pool.ntp.org | UDP | 123 | chronyd | | Source |
| FLOW-010 | pihole | 198.51.100.53 | TCP | 853 | unbound | | Source |

`SeenFrom` nomme l'extrémité dont le journal a enregistré le flux. `Both` est attendu entre deux machines auditées. Les requêtes DNS vers `pihole` (`FLOW-002`, `FLOW-003`) sont `Source` bien que `pihole` soit audité : une socket serveur UDP n'a pas d'adresse distante, le journal de `pihole` contient donc l'écouteur DNS, pas les clients. Entre deux machines auditées, un flux TCP vu d'une seule extrémité signifie que les journaux ne couvrent pas la même période.

La matrice des flux nomme une machine auditée d'après elle-même aux deux extrémités (`nas`, pas `nas.home.arpa`).

### Matrice par paire : qui parle à qui

`pairs.csv` garde une ligne par source et destination, avec leurs ports :

<!-- example-output: all-matrices/pairs.csv -->

| PairId | SourceHost | DestinationHost | FlowCount | Ports | ObservationCount | SeenFrom |
| :--- | :--- | :--- | ---: | :--- | ---: | :--- |
| PAIR-005 | pve | nas | 1 | 873/TCP | 96 | Both |
| PAIR-008 | laptop.home.arpa | nas | 2 | 22/TCP, 445/TCP | 241 | Destination |

`PAIR-008` réunit la session SSH et les partages de fichiers du portable sur le NAS : une vue d'ensemble avant de lire les règles une par une. Les deux sens entre deux machines restent sur deux lignes, comme les règles de pare-feu.

### Matrice de règles par hôte : les règles que porte chaque machine

Les groupes de sécurité réseau et les pare-feu d'hôte portent des règles par machine et par sens. `host_rules.csv` donne à chaque flux une règle `Outbound` sur sa source et une règle `Inbound` sur sa destination, pour chaque adresse privée, portable compris :

<!-- example-output: all-matrices/host_rules.csv -->

| HostRuleId | Host | Audited | Direction | PeerHost | Port | LocalProcesses | Evidence |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | :--- |
| HOST-001 | laptop.home.arpa | No | Outbound | nas | 22 | | Inferred |
| HOST-003 | laptop.home.arpa | No | Outbound | nas | 445 | | Inferred |
| HOST-007 | nas | Yes | Inbound | pve | 873 | rsync | Observed |
| HOST-009 | pihole | Yes | Inbound | nas | 53 | | Inferred |
| HOST-016 | pve | Yes | Outbound | nas | 873 | rsync | Observed |

Filtrez la colonne `Host` pour obtenir les règles d'une machine. `Evidence` vaut `Observed` quand le journal de la machine elle-même a enregistré le flux, `Inferred` quand seule l'autre extrémité l'a fait : le portable n'a pas de journal, et `pihole` ne voit pas ses clients DNS (voir plus haut).

### Matrice de règles par groupe : les règles entre groupes d'adresses

Les groupes d'adresses viennent de la clé `AddressGroups` de la configuration. Le homelab fournit ce `portatlas.json` :

```json
{
  "AddressGroups": {
    "dns": [ "192.168.1.2" ],
    "servers": [ "192.168.1.10", "192.168.1.20" ],
    "clients": [ "192.168.1.48/28" ]
  }
}
```

`group_rules.csv` a une ligne par groupe source, groupe destination, protocole et port :

<!-- example-output: all-matrices/group_rules.csv -->

| GroupRuleId | SourceGroup | DestinationGroup | Protocol | DestinationPort | RuleCount | Processes |
| :--- | :--- | :--- | :--- | ---: | ---: | :--- |
| GROUP-004 | clients | servers | TCP | 445 | 1 | smbd |
| GROUP-007 | servers | 203.0.113.10 | UDP | 123 | 1 | chronyd |
| GROUP-008 | servers | dns | UDP | 53 | 2 | systemd-resolved |
| GROUP-009 | servers | servers | TCP | 873 | 2 | rsync |

`GROUP-008` réunit les règles DNS de `nas` et de `pve` (`RuleCount` 2) ; `GROUP-009` réunit les deux règles de la sauvegarde. Une adresse hors de tout groupe se représente elle-même (`203.0.113.10`).

`--address-group` ajoute des groupes le temps d'une exécution, sans modifier le fichier. Pour regrouper les deux services Internet :

```bash
PortAtlas export rules -i logs -o rules.csv --group-rules-output group_rules.csv --address-group upstream=198.51.100.0/24 --address-group upstream=203.0.113.0/24
```

<!-- example-output: groups-option/group_rules.csv -->

| GroupRuleId | SourceGroup | DestinationGroup | Protocol | DestinationPort | Processes |
| :--- | :--- | :--- | :--- | ---: | :--- |
| GROUP-006 | dns | upstream | TCP | 853 | unbound |
| GROUP-009 | servers | upstream | UDP | 123 | chronyd |

## Lire le résultat

La commande affiche le nombre de fichiers lus, de flux parcourus et de règles écrites. Chaque ligne porte le processus, son chemin et les dates d'observation, pour que chaque règle puisse être justifiée ([liste des colonnes](../reference/output-formats.fr.md#matrice-de-règles-de-pare-feu)).
