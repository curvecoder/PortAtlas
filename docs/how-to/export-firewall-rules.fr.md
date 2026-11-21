> **Language / Langue**: [English](export-firewall-rules.md) · **Français**

# Exporter une matrice de règles de pare-feu

Objectif : une liste CSV des flux à autoriser, une ligne par source, destination, port et processus, pour préparer des règles de pare-feu.

## Exporter les flux observés

```bash
PortAtlas export rules -i collected -o firewall_rules.csv
```

`-i` lit les journaux CSV et NDJSON (un dossier, un motif ou des fichiers). Les ports de destination éphémères (49152 et au-delà) sont écartés.

## Réduire le bruit

Garder les flux vus au moins cinq fois pendant les 14 derniers jours, vers des adresses internes, sans DNS ni NTP :

```bash
PortAtlas export rules -i collected -o internal_rules.csv --min-observations 5 --active-within-days 14 --zone internal --exclude-ports 53,123
```

`--zone public` ne garde que les destinations hors des plages privées, par exemple pour lister les services Internet dont un serveur a besoin.

## Résumer par paire de serveurs

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --pairs-output firewall_pairs.csv
```

`--pairs-output` écrit un second fichier à côté de la matrice détaillée, avec les mêmes filtres : une ligne par source et destination, avec le nombre de flux et leurs ports ([liste des colonnes](../reference/output-formats.fr.md#matrice-par-paire)). Elle donne une vue d'ensemble de qui parle à qui avant de lire les règles une à une.

## Donner une matrice de flux à une équipe réseau

Quand plusieurs serveurs sont audités, une connexion entre deux d'entre eux figure dans les deux journaux : la matrice détaillée la montre deux fois, en règle `Outbound` avec le processus client et en règle `Inbound` avec le processus serveur.

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --flows-output firewall_flows.csv
```

`--flows-output` écrit une ligne par flux, avec les processus de chaque bout et le bout dont le journal l'a vu (`SeenFrom` : `Source`, `Destination` ou `Both`) ([liste des colonnes](../reference/output-formats.fr.md#matrice-de-flux)). Entre deux serveurs audités, un flux vu d'un seul bout signale des journaux qui ne couvrent pas la même période.

## Préparer les règles des groupes de sécurité réseau

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --host-rules-output firewall_host_rules.csv
```

`--host-rules-output` écrit les règles que porte chaque hôte : une règle `Outbound` sur la source et une règle `Inbound` sur la destination de chaque flux, pour toute adresse privée ([liste des colonnes](../reference/output-formats.fr.md#matrice-de-règles-par-hôte)). Filtrer la colonne `Host` donne les règles d'un serveur. `Evidence` vaut `Inferred` quand seul le journal de l'autre bout a vu le flux, par exemple pour un hôte sans journal.

## Lister les services proposés

```bash
PortAtlas export rules -i collected -o services.csv --listeners-only
```

Chaque socket en écoute, écoute TCP ou socket UDP lié à un port, devient une règle entrante depuis n'importe quelle source (`0.0.0.0`, hôte `Any`) vers son adresse et son port : le point de départ d'un jeu de règles entrantes.

`--listeners-output services.csv` écrit le même fichier pendant que `-o` garde les flux : une seule exécution lit les journaux une fois pour tous les fichiers.

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --flows-output firewall_flows.csv --pairs-output firewall_pairs.csv --host-rules-output firewall_host_rules.csv --listeners-output services.csv
```

## Lire le résultat

La commande affiche le nombre de fichiers lus, de flux examinés et de règles écrites. Chaque ligne porte le processus, son chemin et les dates d'observation, pour justifier chaque règle ([liste des colonnes](../reference/output-formats.fr.md#matrice-de-règles-de-pare-feu)).
