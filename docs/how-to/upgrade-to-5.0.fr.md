> **Language / Langue**: [English](upgrade-to-5.0.md) · **Français**

# Passer de la 4.x à la 5.0

PortAtlas 5.0 donne aux fichiers qu'il écrit par défaut le nom du produit, change la façon dont `export rules` écrit ses matrices, et refuse des entrées que la 4.x ignorait sans message. Les noms des commandes et les clés de configuration ne changent pas. Une installation dont le `portatlas.json` fixe les chemins des fichiers garde ses fichiers ; une installation qui comptait sur les noms par défaut commence de nouveaux fichiers. Vérifiez chaque section ci-dessous avant de déployer la 5.0 sur un service ou une tâche planifiée.

## Avant la mise à jour

1. Arrêtez le service ou la tâche planifiée qui lance `audit`, pour qu'il écrive ses journaux et son cache DNS.
2. Copiez les journaux d'audit, le cache DNS et `portatlas.json` dans un dossier de sauvegarde.
3. Remplacez l'exécutable, puis suivez les sections ci-dessous avant de redémarrer le service.

## Les noms de fichiers par défaut commencent par `portatlas_`

**Ce qui change.** Un fichier dont le chemin n'est pas fixé est écrit, et lu, sous un nouveau nom :

| 4.x | 5.0 | Fixé par |
| --- | --- | --- |
| `netflow_audit.csv` | `portatlas_audit.csv` | `OutputCsvPath`, `audit -o` |
| `netflow_audit.ndjson` | `portatlas_audit.ndjson` | `export ndjson -o` |
| `netflow_topology.html` | `portatlas_topology.html` | `OutputHtmlMapPath`, `map --html` |
| `netflow_dns_cache.json` | `portatlas_dns_cache.json` | `DnsCacheFilePath`, `resolve --cache` et `--output-cache` |

`audit` ne cherche pas les fichiers de la 4.x : sans chemin configuré, il commence un nouveau journal d'audit et un cache DNS vide. Par défaut, `map`, `export` et `resolve` lisent le nouveau journal, et `resolve` et `lookup` le nouveau cache. Le `portatlas.json` livré avec la 5.0 contient les nouveaux noms. Un `portatlas.json` copié du modèle de la 4.x fixe `OutputCsvPath` et `DnsCacheFilePath` aux noms de la 4.x : il continue de lire et d'écrire les fichiers de la 4.x.

**Ce qu'il faut faire.** Pour chaque serveur, choisissez l'une des solutions suivantes avant le premier démarrage :

- renommer les fichiers avec leur nom de la 5.0, dans le dossier où tourne l'audit ;
- ou garder les noms de la 4.x en fixant `OutputCsvPath`, `DnsCacheFilePath` et, pour la carte, `OutputHtmlMapPath` dans `portatlas.json`.

Mettez à jour les scripts qui lisent ces fichiers par leur nom par défaut.

**Comment le vérifier.** Lancez `audit --no-tui`. Les réglages affichés au démarrage nomment le journal d'audit sur la ligne `CSV output` et le cache DNS sur la ligne `Reverse DNS`. Si vous avez renommé les fichiers ou gardé les noms de la 4.x, le journal contient toujours les flux enregistrés par la 4.x après la première réécriture.

## Matrices de pare-feu

**Ce qui change.**

- La matrice de règles et la matrice de paires d'`export rules` sont écrites en UTF-8 avec marque d'ordre des octets (BOM), comme le journal d'audit CSV, pour qu'un tableur affiche les noms accentués.
- Chaque matrice ne remplace le fichier précédent qu'une fois complète : un export interrompu laisse la matrice précédente, là où la 4.x pouvait la laisser vide ou tronquée. Un chemin de sortie qui est un dossier est refusé avant toute écriture.
- Les colonnes `ExecutablePath` et `UserName` d'une règle viennent de la ligne de journal vue le plus récemment qui porte une valeur, comme les noms d'hôte. En 4.x elles venaient de la première ligne lue, et pouvaient donc changer avec l'ordre des journaux donnés à `--input`.

**Ce qu'il faut faire.** Un programme qui lit une matrice comme du texte doit sauter la marque d'ordre des octets au début du fichier. Rien d'autre.

**Comment le vérifier.** Ouvrez une matrice dans un afficheur hexadécimal : elle commence par `EF BB BF`. Exportez les mêmes journaux deux fois, avec `--input` dans deux ordres : les deux matrices sont identiques.

## Chemins des exécutables sous Windows

**Ce qui change.** `ExecutablePath` est lu sans ouvrir le processus. En 4.x, il restait vide pour un service exécuté sous un autre compte que l'audit et pour les processus protégés, même avec l'audit lancé en administrateur. Il ne reste plus vide que pour les processus sans fichier exécutable : System Idle, System, Secure System et Registry. Un chemin est aussi écrit sans segment `..`, tel que Windows le conserve.

**Ce qu'il faut faire.** Rien pour reprendre un audit. Un flux enregistré par la 4.x sans chemin est identifié par le nom de son processus ; le même flux, relevé par la 5.0 avec son chemin, est un nouveau flux. La ligne de la 4.x reste dans le journal avec ses compteurs, et une nouvelle ligne commence. Pour repartir de compteurs propres, donnez à `audit` un nouveau fichier de sortie.

**Comment le vérifier.** Lancez `audit` en administrateur sur un serveur où un service tourne sous son propre compte, puis ouvrez le journal : les lignes de ce service portent son chemin.

## Réglages de la carte contrôlés dans le fichier de configuration

**Ce qui change.** Une valeur inconnue de `MapDirection`, `MapEdgeLabels`, `MapLayoutMode`, `MapSortMethod`, `MapRoutingMode`, `MapSpacingProfile`, `DrawioSubnets`, `DrawioEdgeMode` ou `DrawioTheme` était remplacée par la valeur par défaut sans message en 4.x. Elle arrête désormais toute commande qui lit le fichier, avec le code de sortie 1 et `<file>: "<key>" is "<value>", which the setting does not take. Accepted values: …`. Les valeurs sont reconnues sans tenir compte de la casse, et les orthographes lues par les versions précédentes restent acceptées.

**Que faire.** Corrigez la valeur nommée dans le message, ou retirez la clé pour garder la valeur par défaut.

**Comment vérifier.** Lancez `map -i <journal>` avec le fichier de configuration de chaque serveur : une valeur qui n'est pas acceptée l'arrête avec le message ci-dessus.

## Paires `--alias` contrôlées

**Ce qui change.** Une paire qu'`audit --alias` ne sait pas lire (`nas`, `=nas`, `192.168.1.5=`) était ignorée sans message en 4.x. C'est désormais une erreur de ligne de commande qui nomme la paire, avec le code de sortie 1. Une paire est coupée à son premier `=` : un alias IPv6 s'écrit donc `fe80::10=nas.home.arpa` ; la forme `ip:hôte` n'est acceptée que pour une adresse IPv4, avec ses quatre parties.

**Que faire.** Vérifiez les paires `--alias` des services et des tâches planifiées qui lancent `audit`.

**Comment vérifier.** Lancez `audit` avec les mêmes arguments : il démarre au lieu d'afficher `'<pair>' is not accepted by --alias: …`.

## `resolve` et `lookup` suivent les clés de résolution de noms

**Ce qui change.** En 4.x, `resolve` ignorait les clés de résolution de noms du fichier de configuration. Il lit désormais `ResolveRemoteHostNames`, `ResolveIpOwners`, `DnsExcludedNetworks` et `CustomHostAliases` : `"ResolveIpOwners": false` arrête ses requêtes RDAP, `"ResolveRemoteHostNames": false` arrête ses requêtes DNS inverses, les adresses de `DnsExcludedNetworks` et celles qui ont un alias ne reçoivent aucune requête, et un alias nomme son adresse. `lookup` applique désormais `CustomHostAliases` et `DnsExcludedNetworks` comme l'audit, au lieu d'`ExcludedRemoteIps`.

**Que faire.** Sur la machine qui lance `resolve` pour les [serveurs isolés](audit-isolated-servers.fr.md), vérifiez son `portatlas.json` : un fichier copié d'un serveur isolé, avec `"ResolveIpOwners": false`, empêche désormais les requêtes RDAP pour lesquelles vous lancez `resolve`.

**Comment vérifier.** Lancez `resolve` sur un journal qui contient une adresse publique : son propriétaire RDAP apparaît dans la copie enrichie.

## Voir aussi

- [Référence de la ligne de commande](../reference/cli.fr.md)
- [Référence du fichier de configuration](../reference/configuration.fr.md)
- [Formats de sortie](../reference/output-formats.fr.md)
