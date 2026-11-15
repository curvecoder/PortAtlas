> **Language / Langue**: [English](upgrade-to-4.0.md) · **Français**

# Passer de la 3.x à la 4.0

PortAtlas 4.0 réécrit le journal d'audit à partir des flux tenus en mémoire et arrête une commande sur un fichier ou une valeur qu'il ne sait pas lire, là où la 3.x devinait ou écrasait. Les noms des commandes et du fichier de configuration ne changent pas, mais certains fichiers de configuration, lignes de commande et journaux d'audit de la 3.x sont refusés. Vérifiez chaque section ci-dessous avant de déployer la 4.0 dans un service ou une tâche planifiée.

## Avant la mise à jour

1. Arrêtez le service ou la tâche planifiée qui lance `audit`, pour qu'il écrive ses journaux et son cache DNS.
2. Copiez les journaux d'audit, le cache DNS et `portatlas.json` dans un dossier de sauvegarde. L'audit 4.0 réécrit le journal en place dès son premier démarrage.
3. Remplacez l'exécutable, puis faites les contrôles des sections ci-dessous avant de redémarrer le service.

## Le journal d'audit est réécrit, une ligne par flux

**Ce qui change.** En 3.x, l'audit ajoutait les nouveaux flux au journal et laissait les lignes déjà écrites telles quelles. En 4.0, `audit` charge tout le journal existant au démarrage, puis réécrit le journal CSV, et le journal NDJSON donné par `--ndjson`, au démarrage, toutes les `--rewrite-interval` minutes (clé `LogRewriteIntervalMinutes`, `5` par défaut) et à l'arrêt. Les nombres d'observations, dates de dernière observation et noms d'hôtes sur disque restent à jour. Un saut de ligne dans un champ est écrit comme une espace. Une réécriture interrompue par une coupure de courant est achevée au démarrage suivant à partir de `<journal>.pending`.

**Ce qu'il faut faire.** Rien pour un journal écrit par `audit`. Un programme qui lit le journal pendant l'audit doit accepter que le fichier soit remplacé, et pas seulement complété. Un script qui modifiait le journal à la main doit d'abord arrêter l'audit : la réécriture suivante remplace le fichier par les flux tenus en mémoire.

**Comment le vérifier.** Lancez `audit`, attendez la première réécriture et comparez les colonnes `ObservationCount` et `LastSeenUtc` d'un flux avec le tableau de bord.

## `--reload-history` et `ReloadHistoryLimit` sont retirés

**Ce qui change.** Tout le journal est toujours rechargé. `audit --reload-history` est une option inconnue, et un fichier de configuration qui contient encore `ReloadHistoryLimit` arrête toutes les commandes avec le code de sortie 1 :

```text
portatlas.json: "ReloadHistoryLimit" is not a configuration key. Correct its name or remove it.
```

**Ce qu'il faut faire.** Retirez `--reload-history` des définitions de services, des tâches planifiées et des scripts, et retirez la clé `ReloadHistoryLimit` de `portatlas.json`.

**Comment le vérifier.** Lancez une commande qui lit la configuration, par exemple `PortAtlas lookup 192.168.1.10 --config portatlas.json`. Elle doit afficher le diagnostic et non la ligne ci-dessus.

## Un journal d'audit n'est repris que s'il contient la clé des flux

**Ce qui change.** `audit` s'arrête avec le code de sortie 1 quand le journal existant n'a pas l'une des colonnes `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath` et `TargetExecutable`, ou contient dans l'une d'elles une valeur qu'il ne sait pas lire. `ProcessName` est nouvelle dans cette liste : elle identifie un flux dont la cible et le chemin de l'exécutable sont vides. `--columns` et `CsvColumns` sans ces colonnes sont refusés aussi. Le message nomme le journal et les colonnes manquantes ou la ligne :

```text
The audit log 'D:\audits\homelab.csv' cannot be resumed: it has no ProcessName column, so the flows it holds would match none of the observed ones. Write to a new log with --output.
```

**Ce qu'il faut faire.** Un journal écrit avec les colonnes par défaut ne demande aucun changement. Si `--columns` ou `CsvColumns` omet l'une des colonnes ci-dessus, ajoutez-la. Pour garder un journal qui ne les a pas, commencez un nouveau journal avec `--output` et gardez l'ancien comme archive : `map` et `export` le lisent toujours.

**Comment le vérifier.** Lancez `audit` avec vos options habituelles. Il doit ouvrir le tableau de bord, ou afficher son premier scan avec `--no-tui`, sans le message ci-dessus.

## Les flux lancés par un interpréteur changent de clé

**Ce qui change.** Sous Windows, la cible d'un script lancé par un interpréteur est désormais lue et remplit `TargetExecutable`. La clé d'un flux contient la cible : un tel flux est donc un nouveau flux au premier lancement de la 4.0. Il reçoit une nouvelle ligne, et sa ligne 3.x, sans cible, reste dans le journal.

**Ce qu'il faut faire.** Rien n'est exigé, et aucune conversion des anciennes lignes n'est possible. Si les lignes en double gênent, par exemple dans une matrice de règles de pare-feu, commencez un nouveau journal avec `--output` après la mise à jour.

**Comment le vérifier.** Après les premiers scans, les lignes de ces flux ont une valeur `TargetExecutable` ; les lignes 3.x des mêmes flux n'en ont pas.

## Un cache DNS illisible est refusé

**Ce qui change.** `resolve` s'arrête avant toute résolution quand le cache désigné par `--output-cache` existe mais est illisible (JSON tronqué ou invalide, fichier tenu par un autre programme ou non lisible par le compte). `audit` fait de même pour le cache désigné par `DnsCacheFilePath`. Les deux s'arrêtent avec le code de sortie 1 sans rien écrire :

```text
The DNS cache 'D:\audits\dns_cache.json' cannot be read: …
```

En 3.x, un tel cache était lu comme vide, puis remplacé par les noms de l'exécution en cours. Un cache listé dans `--cache` et seulement lu est toujours ignoré quand il est illisible.

**Ce qu'il faut faire.** Restaurez le cache depuis une sauvegarde, donnez au compte l'accès en lecture, ou supprimez le fichier pour repartir d'un cache vide.

**Comment le vérifier.** Lancez `PortAtlas lookup 192.168.1.10` avec la configuration de l'audit : l'étape 2 ne doit pas afficher `Cache access error`.

## Le fichier de configuration est contrôlé

**Ce qui change.** Une clé inconnue, une valeur `null` ou une entrée de liste `null` arrête toutes les commandes avec le code de sortie 1 et nomme la clé. Les clés respectent la casse, et une clé libre comme `Comment` est inconnue elle aussi. Un fichier de configuration illisible ou dont le JSON est invalide arrête la commande au lieu de revenir aux valeurs par défaut avec un avertissement. Un chemin `--config` vide ou inexistant arrête la commande. Sans `--config`, un `portatlas.json` absent donne toujours les valeurs par défaut.

**Ce qu'il faut faire.** Retirez les clés de commentaire et les clés laissées par d'anciennes versions, corrigez la casse de chaque clé d'après la [référence de la configuration](../reference/configuration.fr.md), et remplacez `null` par une valeur ou retirez la clé. `PortAtlas config init` écrit un fichier avec chaque clé et sa valeur par défaut, à comparer avec le vôtre.

**Comment le vérifier.** Lancez `PortAtlas lookup 192.168.1.10 --config portatlas.json`. Un fichier refusé affiche son chemin et la clé, puis sort avec le code 1.

## Les filtres de flux refusent ce qu'ils ne savent pas lire

**Ce qui change.** Un port ou une adresse de `--include-ports`, `--exclude-ports`, `--include-ips` ou `--exclude-ips` (clés `MapIncludedPorts`, `MapExcludedPorts`, `MapIncludedIps`, `MapExcludedIps`) illisible arrête `audit`, `map` et `export rules` avec le code de sortie 1. Le message nomme l'option, la clé et la valeur. En 3.x, une adresse illisible était ignorée, et un port illisible arrêtait `map` avec une trace de pile. De plus :

- Une adresse IPv4 s'écrit avec quatre octets décimaux : `192.168.1/24` et `010.0.0.1` sont refusés.
- Un sous-réseau dont les bits d'hôte sont positionnés est lu comme son réseau : `192.168.1.10/24` vaut `192.168.1.0/24`.
- Une adresse IPv6 entre crochets (`[2001:db8::1]`, `[2001:db8::1]:443`) est refusée. Dans les exclusions du tableau de bord et du DNS, elle est ignorée.

**Ce qu'il faut faire.** Écrivez chaque port sous la forme `443` ou `8000-8080`, chaque adresse IPv4 avec quatre octets, et chaque adresse IPv6 sans crochets.

**Comment le vérifier.** Lancez `PortAtlas map -i <journal> --html map.html --config portatlas.json`. La carte doit être écrite sans message sur un filtre.

## Les règles IPv6 ne correspondent plus aux pairs IPv4

**Ce qui change.** `::/0` ne correspond qu'aux adresses IPv6. Une règle IPv4 mappée de préfixe inférieur à 96, comme `::ffff:0:0/80`, est une règle IPv6. En 3.x, les deux correspondaient aussi aux pairs IPv4.

**Ce qu'il faut faire.** Pour filtrer toutes les adresses, listez `0.0.0.0/0` à côté de `::/0`. Pour filtrer des pairs IPv4, écrivez des sous-réseaux IPv4 comme `192.168.1.0/24`.

**Comment le vérifier.** Lancez `map` ou `export rules` avec le filtre et cherchez dans le résultat les pairs IPv4 attendus.

## Les journaux d'audit sont lus comme du CSV RFC 4180

**Ce qui change.** Un champ entre guillemets peut s'étendre sur plusieurs lignes : une ligne de commande qui contient des sauts de ligne est un seul flux. Un guillemet jamais fermé arrête `audit`, `export` et `resolve` avec le code de sortie 1 et un message qui nomme le journal et la ligne où le champ commence. En 3.x, chaque ligne physique était lue comme un flux.

**Ce qu'il faut faire.** Ouvrez le journal dans un éditeur de texte à la ligne donnée par le message, puis fermez le guillemet ou retirez la ligne. Un journal coupé pendant son écriture, par exemple par un disque plein, se termine par un tel champ.

**Comment le vérifier.** Lancez `PortAtlas export ndjson -i <journal> --output check.ndjson`. La commande doit se terminer sans message sur le journal.

## Les identifiants des règles de pare-feu sont `RULE-001`, `RULE-002`…

**Ce qui change.** `export rules` numérote ses règles `RULE-001`, `RULE-002`… au lieu de `FLUX-001`. Les règles sont triées sur toute leur clé de groupe, protocole et processus compris : leurs numéros ne dépendent plus de l'ordre des journaux et peuvent différer de la 3.7.0 pour les mêmes journaux.

**Ce qu'il faut faire.** Ne reportez pas les numéros de règles 3.x dans une demande d'ouverture de flux ou un tableur : exportez de nouveau la matrice avec la 4.0 et rapprochez les règles sur leur contenu (protocole, adresses, port, processus).

**Comment le vérifier.** Exportez deux fois les mêmes journaux dans un ordre différent : les numéros sont les mêmes.

## Les dossiers et motifs donnés à `--input` ne gardent que les journaux d'audit

**Ce qui change.** Un dossier ou un motif donné à `--input` ne fournit plus que les fichiers reconnus comme journaux d'audit, un fichier par audit. `map`, `export rules` et `export ndjson` préfèrent la copie `_enriched` à son original, et un journal CSV au journal NDJSON de même nom. `resolve` garde les deux formats et écarte une copie `_enriched` dont l'original est présent. Les fichiers écartés sont nommés sur la console. Un fichier donné par son chemin est toujours lu.

**Ce qu'il faut faire.** Rien, sauf si un script comptait lire toutes les copies d'un dossier. Les comptes peuvent changer pour les dossiers qui contenaient un journal et ses copies : donnez les fichiers par leur chemin pour tous les lire.

**Comment le vérifier.** Lancez la commande sur le dossier et lisez sur la console les noms des fichiers écartés.

## Formats de sortie

**Ce qui change.**

- Le journal d'audit NDJSON est écrit en UTF-8 sans marque d'ordre des octets. Le journal CSV la garde.
- Dans les matrices de règles et de paires d'`export rules`, une valeur qui commence par `=`, `+`, `-`, `@`, une tabulation ou un retour chariot est précédée d'une apostrophe, pour qu'un tableur ne l'exécute pas comme une formule.
- Avec `--listeners-only`, une ligne `Listen` dont la direction n'est pas `Listening` n'est plus exportée, et les sockets UDP liés sont gardés.

**Ce qu'il faut faire.** Un programme qui sautait une marque d'ordre des octets au début du journal NDJSON n'a rien à changer. Un programme qui lit les matrices comme du texte doit retirer l'apostrophe en tête de ces valeurs.

**Comment le vérifier.** Ouvrez le journal NDJSON dans un visualiseur hexadécimal : il commence par `{`. Ouvrez la matrice dans un tableur : aucune cellule n'est exécutée comme une formule.

## Voir aussi

- [Notes de version de la 4.0.0](../../release-notes/v4.0.0.md)
- [Référence de la ligne de commande](../reference/cli.fr.md)
- [Référence du fichier de configuration](../reference/configuration.fr.md)
- [Formats de sortie](../reference/output-formats.fr.md)
