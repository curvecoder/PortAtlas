> **Language / Langue**: [English](upgrade-to-5.0.md) · **Français**

# Passer de la 4.x à la 5.0

PortAtlas 5.0 donne aux fichiers qu'il écrit par défaut le nom du produit, et change la façon dont `export rules` écrit ses matrices. Les noms des commandes et les clés de configuration ne changent pas. Une installation dont le `portatlas.json` fixe les chemins des fichiers garde ses fichiers ; une installation qui comptait sur les noms par défaut commence de nouveaux fichiers. Vérifiez chaque section ci-dessous avant de déployer la 5.0 sur un service ou une tâche planifiée.

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

## Voir aussi

- [Référence de la ligne de commande](../reference/cli.fr.md)
- [Référence du fichier de configuration](../reference/configuration.fr.md)
- [Formats de sortie](../reference/output-formats.fr.md)
